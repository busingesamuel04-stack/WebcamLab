using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using WebcamLab.Core.Audio;
using WebcamLab.Core.Native;

namespace WebcamLab.Capture.Audio;

/// <summary>
/// High-fidelity Windows Core Audio (WASAPI) capture engine operating in shared mode.
/// Captures unadulterated microphone audio, extracts hardware QPC and device-position timestamps,
/// converts IEEE Float samples to standard 16-bit PCM, computes live audio levels,
/// and streams directly to disk or registered subscribers.
/// </summary>
public class WasapiCaptureService : IDisposable
{
    private AudioNative.IMMDeviceEnumerator? _enumerator;
    private AudioNative.IMMDevice? _device;
    private AudioNative.IAudioClient? _audioClient;
    private AudioNative.IAudioCaptureClient? _captureClient;

    private Thread? _captureThread;
    private AutoResetEvent? _audioEvent;
    private ManualResetEvent? _stopEvent;
    private volatile bool _isCapturing;
    private readonly object _lock = new();

    private AudioDeviceInfo? _activeDeviceInfo;
    private int _channels = 2;
    private int _sampleRate = 48000;
    private int _bitsPerSample = 16;
    private bool _isFloat = true;

    // Telemetry & counters
    private long _totalFramesRead;
    private long _totalBytesRecorded;
    private int _discontinuitiesCount;
    private int _silencePacketsCount;
    private ulong _initialQpcPosition;
    private ulong _finalQpcPosition;
    private ulong _initialDevicePosition;
    private ulong _finalDevicePosition;

    private float _peakLevel;
    private float _peakDb = -60f;
    private float _rmsLevel;
    private float _rmsDb = -60f;
    private volatile bool _isClipping;
    private long _lastClipTimestampTicks;
    private string _endpointStatus = "Idle";

    // Optional destination WAV writer
    private WaveFileWriter? _waveWriter;

    public event Action<AudioChunk>? AudioChunkAvailable;
    public event EventHandler<AudioLevelEventArgs>? AudioLevelUpdated;

    public bool IsCapturing => _isCapturing;
    public AudioDeviceInfo? ActiveDeviceInfo => _activeDeviceInfo;
    public int Channels => _channels;
    public int SampleRate => _sampleRate;
    public float PeakLevel => _peakLevel;
    public float PeakDb => _peakDb;
    public float RmsLevel => _rmsLevel;
    public float RmsDb => _rmsDb;
    public bool IsClipping => _isClipping;
    public string EndpointStatus => _endpointStatus;
    public long TotalFramesRead => _totalFramesRead;
    public int DiscontinuitiesCount => _discontinuitiesCount;
    public int SilencePacketsCount => _silencePacketsCount;
    public ulong InitialQpcPosition => _initialQpcPosition;
    public ulong FinalQpcPosition => _finalQpcPosition;

    public AudioCaptureStats GetStats()
    {
        lock (_lock)
        {
            double durationSec = (_sampleRate > 0) ? (double)_totalFramesRead / _sampleRate : 0.0;
            return new AudioCaptureStats
            {
                TotalFramesRead = _totalFramesRead,
                TotalBytesRecorded = _totalBytesRecorded,
                DiscontinuitiesCount = _discontinuitiesCount,
                SilencePacketsCount = _silencePacketsCount,
                InitialQpcPosition = _initialQpcPosition,
                FinalQpcPosition = _finalQpcPosition,
                InitialDevicePosition = _initialDevicePosition,
                FinalDevicePosition = _finalDevicePosition,
                DurationSeconds = durationSec,
                EstimatedSampleRate = _sampleRate
            };
        }
    }

    public void Start(string? deviceId = null, string? outputWavPath = null)
    {
        lock (_lock)
        {
            if (_isCapturing) return;

            _enumerator = (AudioNative.IMMDeviceEnumerator)new AudioNative.MMDeviceEnumeratorCom();

            if (!string.IsNullOrEmpty(deviceId))
            {
                int hrGet = _enumerator.GetDevice(deviceId, out _device);
                if (hrGet != 0 || _device == null)
                {
                    // Fall back to default
                    _enumerator.GetDefaultAudioEndpoint(AudioNative.EDataFlow.eCapture, AudioNative.ERole.eConsole, out _device);
                }
            }
            else
            {
                _enumerator.GetDefaultAudioEndpoint(AudioNative.EDataFlow.eCapture, AudioNative.ERole.eConsole, out _device);
            }

            if (_device == null)
            {
                throw new InvalidOperationException("No audio capture device available.");
            }

            var iidAudioClient = AudioNative.IID_IAudioClient;
            int hr = _device.Activate(ref iidAudioClient, AudioNative.CLSCTX_ALL, IntPtr.Zero, out var audioClientObj);
            if (hr != 0 || audioClientObj is not AudioNative.IAudioClient client)
            {
                throw new InvalidOperationException($"Failed to activate IAudioClient (HRESULT 0x{hr:X8}).");
            }
            _audioClient = client;

            // Query native shared mix format
            hr = _audioClient.GetMixFormat(out IntPtr pFormat);
            if (hr != 0 || pFormat == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to get audio mix format (HRESULT 0x{hr:X8}).");
            }

            try
            {
                var waveFormat = Marshal.PtrToStructure<AudioNative.WAVEFORMATEX>(pFormat);
                _channels = waveFormat.nChannels;
                _sampleRate = (int)waveFormat.nSamplesPerSec;
                _bitsPerSample = waveFormat.wBitsPerSample;

                _activeDeviceInfo = new AudioDeviceInfo
                {
                    Id = deviceId ?? "default",
                    FriendlyName = "Active Audio Endpoint",
                    Channels = _channels,
                    SampleRate = _sampleRate,
                    BitsPerSample = _bitsPerSample,
                    IsFloat = _isFloat
                };

                if (waveFormat.wFormatTag == AudioNative.WAVE_FORMAT_EXTENSIBLE)
                {
                    var ext = Marshal.PtrToStructure<AudioNative.WAVEFORMATEXTENSIBLE>(pFormat);
                    _isFloat = ext.SubFormat == AudioNative.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
                    if (ext.Format.wBitsPerSample > 0)
                    {
                        _bitsPerSample = ext.Format.wBitsPerSample;
                    }
                }
                else if (waveFormat.wFormatTag == AudioNative.WAVE_FORMAT_IEEE_FLOAT)
                {
                    _isFloat = true;
                }
                else
                {
                    _isFloat = false;
                }

                // 200ms buffer (2,000,000 hns)
                long bufferDurationHns = 2_000_000;

                _audioEvent = new AutoResetEvent(false);
                uint streamFlags = AudioNative.AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AudioNative.AUDCLNT_STREAMFLAGS_NOPERSIST;

                hr = _audioClient.Initialize(
                    AudioNative.AUDCLNT_SHAREMODE_SHARED,
                    streamFlags,
                    bufferDurationHns,
                    0,
                    pFormat,
                    IntPtr.Zero);

                int initHr = hr;

                if (hr != 0)
                {
                    // Fall back to polling without event flag
                    // When Initialize fails on an IAudioClient instance, a fresh instance must be activated
                    _audioEvent.Dispose();
                    _audioEvent = null;

                    Marshal.ReleaseComObject(_audioClient);
                    int hrRe = _device.Activate(ref iidAudioClient, AudioNative.CLSCTX_ALL, IntPtr.Zero, out audioClientObj);
                    if (hrRe != 0 || audioClientObj is not AudioNative.IAudioClient reClient)
                    {
                        throw new InvalidOperationException($"Failed to re-activate IAudioClient for polling fallback (HRESULT 0x{hrRe:X8}). Initial Init hr was 0x{initHr:X8}.");
                    }
                    _audioClient = reClient;

                    streamFlags = AudioNative.AUDCLNT_STREAMFLAGS_NOPERSIST;
                    hr = _audioClient.Initialize(
                        AudioNative.AUDCLNT_SHAREMODE_SHARED,
                        streamFlags,
                        bufferDurationHns,
                        0,
                        pFormat,
                        IntPtr.Zero);

                    if (hr != 0)
                    {
                        throw new InvalidOperationException($"Failed to initialize WASAPI AudioClient (HRESULT 0x{hr:X8}). Initial Init hr was 0x{initHr:X8}.");
                    }
                }
                else
                {
                    _audioClient.SetEventHandle(_audioEvent.SafeWaitHandle.DangerousGetHandle());
                }
            }
            finally
            {
                AudioNative.CoTaskMemFree(pFormat);
            }

            // Get IAudioCaptureClient service
            var iidCapture = AudioNative.IID_IAudioCaptureClient;
            hr = _audioClient.GetService(ref iidCapture, out IntPtr pCapture);
            if (hr != 0 || pCapture == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to get IAudioCaptureClient (HRESULT 0x{hr:X8}). AudioClient Init hr was 0x{hr:X8}.");
            }
            _captureClient = (AudioNative.IAudioCaptureClient)Marshal.GetObjectForIUnknown(pCapture);
            Marshal.Release(pCapture);

            // Reset stats
            _totalFramesRead = 0;
            _totalBytesRecorded = 0;
            _discontinuitiesCount = 0;
            _silencePacketsCount = 0;
            _initialQpcPosition = 0;
            _finalQpcPosition = 0;
            _initialDevicePosition = 0;
            _finalDevicePosition = 0;
            _peakLevel = 0f;
            _peakDb = -60f;
            _rmsLevel = 0f;

            if (!string.IsNullOrEmpty(outputWavPath))
            {
                // Always write standard 16-bit PCM for universal compatibility
                _waveWriter = new WaveFileWriter(outputWavPath, _channels, _sampleRate, 16);
            }

            _stopEvent = new ManualResetEvent(false);
            _isCapturing = true;

            hr = _audioClient.Start();
            if (hr != 0)
            {
                _isCapturing = false;
                throw new InvalidOperationException($"Failed to start audio client (HRESULT 0x{hr:X8}).");
            }

            _captureThread = new Thread(CaptureLoop)
            {
                Name = "WebcamLab.WasapiCaptureWorker",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _captureThread.Start();
        }
    }

    private void CaptureLoop()
    {
        var waitHandles = (_audioEvent != null)
            ? new WaitHandle[] { _stopEvent!, _audioEvent }
            : new WaitHandle[] { _stopEvent! };

        // Intermediate conversion buffer (reusable)
        byte[] pcmConversionBuffer = new byte[8192];

        while (_isCapturing)
        {
            int waitIdx = WaitHandle.WaitAny(waitHandles, _audioEvent != null ? 50 : 10);
            bool shouldExit = (waitIdx == 0);

            if (_captureClient == null) break;

            while (true)
            {
                int hr = _captureClient.GetNextPacketSize(out uint packetSize);
                if (hr != 0 || packetSize == 0) break;

                hr = _captureClient.GetBuffer(
                    out IntPtr pData,
                    out uint numFramesRead,
                    out uint flags,
                    out ulong devicePosition,
                    out ulong qpcPosition);

                if (hr != 0) break;

                try
                {
                    if (numFramesRead > 0)
                    {
                        if ((flags & AudioNative.AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) != 0)
                        {
                            Interlocked.Increment(ref _discontinuitiesCount);
                        }

                        if ((flags & AudioNative.AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                        {
                            Interlocked.Increment(ref _silencePacketsCount);
                        }

                        if (_initialQpcPosition == 0)
                        {
                            _initialQpcPosition = qpcPosition;
                            _initialDevicePosition = devicePosition;
                        }
                        _finalQpcPosition = qpcPosition;
                        _finalDevicePosition = devicePosition + numFramesRead;
                        Interlocked.Add(ref _totalFramesRead, numFramesRead);

                        int totalSamples = (int)numFramesRead * _channels;
                        int requiredBytes = totalSamples * 2; // 16-bit PCM = 2 bytes per sample

                        if (pcmConversionBuffer.Length < requiredBytes)
                        {
                            pcmConversionBuffer = new byte[requiredBytes * 2];
                        }

                        float currentPeak = 0f;
                        float sumSquare = 0f;

                        if ((flags & AudioNative.AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                        {
                            Array.Clear(pcmConversionBuffer, 0, requiredBytes);
                        }
                        else if (_isFloat)
                        {
                            // Convert 32-bit float to 16-bit signed PCM
                            unsafe
                            {
                                float* pFloat = (float*)pData.ToPointer();
                                for (int s = 0; s < totalSamples; s++)
                                {
                                    float sample = pFloat[s];
                                    float absSample = MathF.Abs(sample);
                                    if (absSample > currentPeak) currentPeak = absSample;
                                    sumSquare += sample * sample;

                                    // Clamp sample to [-1.0, 1.0] and scale to Int16
                                    float clamped = Math.Clamp(sample, -1.0f, 1.0f);
                                    short pcm16 = (short)(clamped * 32767.0f);

                                    pcmConversionBuffer[s * 2] = (byte)(pcm16 & 0xFF);
                                    pcmConversionBuffer[s * 2 + 1] = (byte)((pcm16 >> 8) & 0xFF);
                                }
                            }
                        }
                        else if (_bitsPerSample == 16)
                        {
                            // Already 16-bit PCM: copy directly and calculate peak
                            Marshal.Copy(pData, pcmConversionBuffer, 0, requiredBytes);
                            unsafe
                            {
                                short* pShort = (short*)pData.ToPointer();
                                for (int s = 0; s < totalSamples; s++)
                                {
                                    float sample = pShort[s] / 32768.0f;
                                    float absSample = MathF.Abs(sample);
                                    if (absSample > currentPeak) currentPeak = absSample;
                                    sumSquare += sample * sample;
                                }
                            }
                        }
                        else
                        {
                            // Fallback
                            Marshal.Copy(pData, pcmConversionBuffer, 0, Math.Min(requiredBytes, pcmConversionBuffer.Length));
                        }

                        // Write to WAV file if active
                        if (_waveWriter != null)
                        {
                            _waveWriter.WriteSamples(pcmConversionBuffer, 0, requiredBytes);
                            Interlocked.Add(ref _totalBytesRecorded, requiredBytes);
                        }

                        // Compute metrics
                        _peakLevel = Math.Clamp(currentPeak, 0f, 1f);
                        _peakDb = _peakLevel > 0.0001f ? MathF.Max(-60f, 20f * MathF.Log10(_peakLevel)) : -60f;
                        _rmsLevel = totalSamples > 0 ? MathF.Sqrt(sumSquare / totalSamples) : 0f;
                        _rmsDb = _rmsLevel > 0.0001f ? MathF.Max(-60f, 20f * MathF.Log10(_rmsLevel)) : -60f;

                        // Clipping detection: samples reaching within -0.05 dBFS of full scale (>= 0.994f)
                        if (currentPeak >= 0.994f)
                        {
                            _isClipping = true;
                            _lastClipTimestampTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                        }
                        else if (_isClipping)
                        {
                            // Hold clip indicator for 1.2s for visual noticeability
                            long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - _lastClipTimestampTicks;
                            if ((double)elapsedTicks / System.Diagnostics.Stopwatch.Frequency > 1.2)
                            {
                                _isClipping = false;
                            }
                        }

                        _endpointStatus = _discontinuitiesCount > 0 ? $"Warning: {_discontinuitiesCount} packet discontinuity" : "Active";

                        AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs
                        {
                            PeakLevel = _peakLevel,
                            PeakDb = _peakDb,
                            RmsLevel = _rmsLevel,
                            RmsDb = _rmsDb,
                            IsClipping = _isClipping,
                            DiscontinuitiesCount = _discontinuitiesCount
                        });

                        // Notify chunk subscribers
                        if (AudioChunkAvailable != null)
                        {
                            byte[] chunkCopy = new byte[requiredBytes];
                            Buffer.BlockCopy(pcmConversionBuffer, 0, chunkCopy, 0, requiredBytes);

                            var chunk = new AudioChunk(
                                chunkCopy,
                                (int)numFramesRead,
                                _channels,
                                _sampleRate,
                                devicePosition,
                                qpcPosition,
                                flags);

                            AudioChunkAvailable.Invoke(chunk);
                        }
                    }
                }
                finally
                {
                    _captureClient.ReleaseBuffer(numFramesRead);
                }
            }

            if (shouldExit)
            {
                break;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isCapturing) return;
            _isCapturing = false;

            _stopEvent?.Set();
            _captureThread?.Join(3000);

            try
            {
                _audioClient?.Stop();
            }
            catch { }

            _waveWriter?.Dispose();
            _waveWriter = null;

            CleanupNative();
        }
    }

    private void CleanupNative()
    {
        if (_captureClient != null && Marshal.IsComObject(_captureClient))
        {
            Marshal.ReleaseComObject(_captureClient);
            _captureClient = null;
        }

        if (_audioClient != null && Marshal.IsComObject(_audioClient))
        {
            Marshal.ReleaseComObject(_audioClient);
            _audioClient = null;
        }

        if (_device != null && Marshal.IsComObject(_device))
        {
            Marshal.ReleaseComObject(_device);
            _device = null;
        }

        if (_enumerator != null && Marshal.IsComObject(_enumerator))
        {
            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }

        _audioEvent?.Dispose();
        _audioEvent = null;

        _stopEvent?.Dispose();
        _stopEvent = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
