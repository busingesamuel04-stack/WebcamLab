using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using WebcamLab.Capture.Audio;
using WebcamLab.Core.Audio;
using WebcamLab.Core.Models;
using WebcamLab.Core.Native;
using WebcamLab.Core.Session;

namespace WebcamLab.Capture;

/// <summary>
/// Native Windows Media Foundation Recording Engine.
/// Provides zero-dependency in-process H.264 video and AAC audio encoding directly
/// to MP4 container via IMFSinkWriter without spawning external ffmpeg.exe processes.
/// </summary>
public class NativeMediaFoundationRecorder : IRecorderBackend, IDisposable
{
    private class VideoFrameItem
    {
        public byte[] Buffer { get; set; } = [];
        public int Length { get; set; }
        public int RepeatCount { get; set; } = 1;
        public long SourceTimestampHns { get; set; }
        public double NormalizedTimeSec { get; set; }
        public long QpcTicks { get; set; }
    }

    private class AudioSampleItem
    {
        public byte[] PcmData { get; set; } = [];
        public int SampleCount { get; set; }
        public int Channels { get; set; }
        public int SampleRate { get; set; }
        public long TimestampHns { get; set; }
        public ulong QpcPosition { get; set; }
    }

    // Direct MFT Interface for AAC encoder negotiation
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("bf94c121-5b05-4e6f-8000-ba598961414d")]
    private interface IMFTransform
    {
        [PreserveSig] int GetStreamLimits(out uint pdwInputMinimum, out uint pdwInputMaximum, out uint pdwOutputMinimum, out uint pdwOutputMaximum);
        [PreserveSig] int GetStreamCount(out uint pcInputStreams, out uint pcOutputStreams);
        [PreserveSig] int GetStreamIDs(uint dwInputIDArraySize, [Out] uint[] pdwInputIDs, uint dwOutputIDArraySize, [Out] uint[] pdwOutputIDs);
        [PreserveSig] int GetInputStreamInfo(uint dwInputStreamID, IntPtr pStreamInfo);
        [PreserveSig] int GetOutputStreamInfo(uint dwOutputStreamID, IntPtr pStreamInfo);
        [PreserveSig] int GetAttributes(out MediaFoundationNative.IMFAttributes pAttributes);
        [PreserveSig] int GetInputStreamAttributes(uint dwInputStreamID, out MediaFoundationNative.IMFAttributes pAttributes);
        [PreserveSig] int GetOutputStreamAttributes(uint dwOutputStreamID, out MediaFoundationNative.IMFAttributes pAttributes);
        [PreserveSig] int DeleteInputStream(uint dwStreamID);
        [PreserveSig] int AddInputStreams(uint cStreams, [In] uint[] adwStreamIDs);
        [PreserveSig] int GetInputAvailableType(uint dwInputStreamID, uint dwTypeIndex, out MediaFoundationNative.IMFMediaType ppType);
        [PreserveSig] int GetOutputAvailableType(uint dwOutputStreamID, uint dwTypeIndex, out MediaFoundationNative.IMFMediaType ppType);
        [PreserveSig] int SetInputType(uint dwInputStreamID, [In] MediaFoundationNative.IMFMediaType pType, uint dwFlags);
        [PreserveSig] int SetOutputType(uint dwOutputStreamID, [In] MediaFoundationNative.IMFMediaType pType, uint dwFlags);
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        ref Guid clsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    private static readonly Guid CLSID_AACMFTEncoder = new("93af0c51-2275-45d2-a35b-f2ba21caed00");
    private static readonly Guid IID_IMFTransform = new("bf94c121-5b05-4e6f-8000-ba598961414d");

    // Canonical GUIDs from Windows SDK mfapi.h (Verified via Phase30 probe)
    private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFMediaType_Audio = new("73647561-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFAudioFormat_AAC = new("00001610-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFAudioFormat_PCM = new("00000001-0000-0010-8000-00aa00389b71");

    private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");

    private static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    private static readonly Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    private static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

    private static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
    private static readonly Guid MF_SINK_WRITER_DISABLE_THROTTLING = new("08b845d8-2b74-4afe-9d53-be16d2d5ae8c");

    public string BackendName => "Windows Media Foundation (Native / Default)";
    public RecordingBackendMode BackendMode => RecordingBackendMode.MediaFoundation_Default;

    private MediaFoundationNative.IMFSinkWriter? _sinkWriter;
    private uint _videoStreamIndex;
    private uint _audioStreamIndex;
    private readonly object _lock = new();
    private volatile bool _isRecording;
    private volatile bool _isFinalizing;
    private DateTime _recordStartTime;
    private long _startStopwatchTick;

    // Asynchronous encoder queue
    private BlockingCollection<VideoFrameItem>? _videoQueue;
    private BlockingCollection<AudioSampleItem>? _audioQueue;
    private readonly ConcurrentQueue<byte[]> _bufferPool = new();
    private Thread? _writerThread;
    private CancellationTokenSource? _writerCts;
    private readonly ManualResetEventSlim _initDone = new(false);
    private Exception? _initException;

    // Configuration
    private RecordingCadenceMode _cadenceMode = RecordingCadenceMode.CFR_Compatibility;
    private RecordingMode _recordingMode = RecordingMode.OriginalCamera;
    private SoftwareLookSettings? _activeLookSettings;
    private List<PresetControlValue>? _activeHardwareSettings;
    private string? _currentFilePath;
    private int _frameWidth;
    private int _frameHeight;
    private double _targetFps = 30.0;
    private string _captureFormat = "MJPG";
    private TimeSpan _finalDuration = TimeSpan.Zero;
    private bool _isHardwareAccelerated = true;

    // Statistics
    private int _framesCaptured;
    private int _framesEncoded;
    private int _framesDropped;
    private long _scheduledCfrFrames;
    private long _initialSourceTimestampHns = -1;
    private long _lastSourceTimestampHns = -1;
    private long _finalSourceTimestampHns = -1;
    private double _lastNormalizedTimeSec = 0.0;
    private double _minFrameDeltaMs = double.MaxValue;
    private double _maxFrameDeltaMs = 0.0;
    private double _totalFrameDeltaMs = 0.0;
    private int _deltaCount = 0;
    private int _monotonicityCorrectionsCount;

    private readonly object _auditLock = new();
    private readonly List<FrameTimingAuditRecord> _timingAuditTrail = [];
    private readonly List<double> _vfrNormalizedSampleTimes = [];

    // Audio Capture
    private readonly WasapiCaptureService _audioCaptureService = new();
    private readonly AudioVideoSynchronizer _avSynchronizer = new();
    private bool _isAudioEnabled;
    private AudioDeviceInfo? _activeAudioDevice;
    private AudioVideoSyncReport? _lastSyncReport;
    private long _audioSamplesWritten;
    private long _firstVideoArrivalQpcTicks = -1;
    private ulong _firstAudioQpcPosition = 0;
    private bool _leadSilenceInjected = false;

    public bool IsRecording => _isRecording;
    public bool IsFinalizing => _isFinalizing;
    public string? CurrentFilePath => _currentFilePath;
    public TimeSpan Duration => _isRecording ? DateTime.UtcNow - _recordStartTime : _finalDuration;
    public int FramesCaptured => _framesCaptured;
    public int FramesEncoded => _framesEncoded;
    public int DroppedFrames => _framesDropped;
    public int QueueBacklog => (_videoQueue?.Count ?? 0) + (_audioQueue?.Count ?? 0);
    public double MinFrameDeltaMs => _deltaCount > 0 ? _minFrameDeltaMs : 0.0;
    public double MaxFrameDeltaMs => _deltaCount > 0 ? _maxFrameDeltaMs : 0.0;
    public double MeanFrameDeltaMs => _deltaCount > 0 ? _totalFrameDeltaMs / _deltaCount : 0.0;
    public int MonotonicityCorrectionsCount => _monotonicityCorrectionsCount;
    public WasapiCaptureService AudioService => _audioCaptureService;
    public AudioVideoSyncReport? LastSyncReport => _lastSyncReport;
    public AudioDeviceInfo? ActiveAudioDevice => _activeAudioDevice;
    public bool IsHardwareAccelerated => _isHardwareAccelerated;

    public void Start(
        string outputFilePath,
        int width,
        int height,
        double targetFps,
        RecordingMode recordingMode,
        SoftwareLookSettings? lookSettings,
        IReadOnlyList<PresetControlValue>? hardwareSettings,
        string captureFormat = "MJPG",
        RecordingCadenceMode cadenceMode = RecordingCadenceMode.CFR_Compatibility,
        bool enableAudio = true,
        AudioDeviceInfo? audioDevice = null)
    {
        lock (_lock)
        {
            if (_isRecording)
            {
                throw new InvalidOperationException("Native Media Foundation Recorder is already active.");
            }

            _currentFilePath = outputFilePath;
            _frameWidth = width;
            _frameHeight = height;
            _targetFps = targetFps > 0 ? targetFps : 30.0;
            _recordingMode = recordingMode;
            _activeLookSettings = lookSettings?.Clone();
            _activeHardwareSettings = hardwareSettings != null ? new List<PresetControlValue>(hardwareSettings) : null;
            _captureFormat = captureFormat;
            _cadenceMode = cadenceMode;
            _isAudioEnabled = enableAudio;
            _activeAudioDevice = audioDevice;

            _framesCaptured = 0;
            _framesEncoded = 0;
            _framesDropped = 0;
            _scheduledCfrFrames = 0;
            _initialSourceTimestampHns = -1;
            _lastSourceTimestampHns = -1;
            _finalSourceTimestampHns = -1;
            _lastNormalizedTimeSec = 0.0;
            _minFrameDeltaMs = double.MaxValue;
            _maxFrameDeltaMs = 0.0;
            _totalFrameDeltaMs = 0.0;
            _deltaCount = 0;
            _monotonicityCorrectionsCount = 0;
            _audioSamplesWritten = 0;
            _firstVideoArrivalQpcTicks = -1;
            _firstAudioQpcPosition = 0;
            _leadSilenceInjected = false;
            _isHardwareAccelerated = true;
            _avSynchronizer.Reset();

            lock (_auditLock)
            {
                _timingAuditTrail.Clear();
                _vfrNormalizedSampleTimes.Clear();
            }

            string? dir = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            if (File.Exists(outputFilePath))
            {
                File.Delete(outputFilePath);
            }

            // 1. Start async queues and background writer (which initializes IMFSinkWriter on its dedicated MTA thread)
            _videoQueue = new BlockingCollection<VideoFrameItem>(new ConcurrentQueue<VideoFrameItem>(), 120);
            _audioQueue = new BlockingCollection<AudioSampleItem>(new ConcurrentQueue<AudioSampleItem>(), 300);
            _writerCts = new CancellationTokenSource();
            _initDone.Reset();
            _initException = null;

            _writerThread = new Thread(ProcessEncodingQueues)
            {
                Name = "WebcamLab_NativeMF_EncoderWorker",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _writerThread.SetApartmentState(ApartmentState.MTA);
            _writerThread.Start();

            // Wait for IMFSinkWriter initialization to complete on worker thread
            if (!_initDone.Wait(10000))
            {
                throw new TimeoutException("Timed out waiting for Native Media Foundation encoder initialization.");
            }

            if (_initException != null)
            {
                throw new InvalidOperationException($"Native Media Foundation encoder initialization failed: {_initException.Message}", _initException);
            }

            // 2. Start WASAPI Audio Capture if enabled
            if (_isAudioEnabled)
            {
                _audioCaptureService.AudioChunkAvailable += OnAudioChunkCaptured;
                try
                {
                    _audioCaptureService.Start(audioDevice?.Id);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NativeMediaFoundationRecorder] WASAPI start failed: {ex.Message}");
                    _audioCaptureService.AudioChunkAvailable -= OnAudioChunkCaptured;
                    _isAudioEnabled = false;
                }
            }

            _recordStartTime = DateTime.UtcNow;
            _startStopwatchTick = Stopwatch.GetTimestamp();
            _isRecording = true;
        }
    }

    private void InitializeSinkWriter(string outputFilePath, int width, int height)
    {
        // Attempt hardware-accelerated MFTs first
        bool initSuccess = TryInitializeSinkWriter(outputFilePath, width, height, enableHardware: true);
        if (!initSuccess)
        {
            Debug.WriteLine("[NativeMediaFoundationRecorder] Hardware MFT initialization failed. Falling back to Microsoft Software H.264 Encoder MFT.");
            _isHardwareAccelerated = false;
            initSuccess = TryInitializeSinkWriter(outputFilePath, width, height, enableHardware: false);
            if (!initSuccess)
            {
                throw new InvalidOperationException("Failed to initialize Windows Media Foundation Sink Writer with both Hardware and Software H.264 encoders.");
            }
        }
    }

    private bool TryInitializeSinkWriter(string outputFilePath, int width, int height, bool enableHardware)
    {
        try
        {
            int hr = MediaFoundationNative.MFCreateAttributes(out var attrs, 2);
            Guid gHw = MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS;
            Guid gTh = MF_SINK_WRITER_DISABLE_THROTTLING;
            attrs.SetUINT32(ref gHw, enableHardware ? 1u : 0u);
            attrs.SetUINT32(ref gTh, 1);

            hr = MediaFoundationNative.MFCreateSinkWriterFromURL(outputFilePath, IntPtr.Zero, attrs, out var writer);
            if (hr != 0 || writer == null) return false;

            // Configure Video Stream (H.264 1080p)
            hr = MediaFoundationNative.MFCreateMediaType(out var videoTarget);
            Guid gMaj = MF_MT_MAJOR_TYPE;
            Guid gSub = MF_MT_SUBTYPE;
            Guid gMedVid = MFMediaType_Video;
            Guid gH264 = MFVideoFormat_H264;
            Guid gFs = MF_MT_FRAME_SIZE;
            Guid gFr = MF_MT_FRAME_RATE;
            Guid gBr = MF_MT_AVG_BITRATE;
            Guid gIm = MF_MT_INTERLACE_MODE;
            Guid gPar = MF_MT_PIXEL_ASPECT_RATIO;

            videoTarget.SetGUID(ref gMaj, ref gMedVid);
            videoTarget.SetGUID(ref gSub, ref gH264);
            videoTarget.SetUINT64(ref gFs, MediaFoundationNative.PackSize((uint)width, (uint)height));
            videoTarget.SetUINT64(ref gFr, MediaFoundationNative.PackRatio((uint)Math.Round(_targetFps), 1));
            videoTarget.SetUINT32(ref gBr, 5000000); // 5 Mbps target
            videoTarget.SetUINT32(ref gIm, 2); // Progressive
            videoTarget.SetUINT64(ref gPar, MediaFoundationNative.PackRatio(1, 1));

            hr = writer.AddStream(videoTarget, out uint vIdx);
            if (hr != 0)
            {
                Marshal.ReleaseComObject(writer);
                return false;
            }

            // Input: RGB32
            hr = MediaFoundationNative.MFCreateMediaType(out var videoInput);
            Guid gRgb32 = MFVideoFormat_RGB32;
            videoInput.SetGUID(ref gMaj, ref gMedVid);
            videoInput.SetGUID(ref gSub, ref gRgb32);
            videoInput.SetUINT64(ref gFs, MediaFoundationNative.PackSize((uint)width, (uint)height));
            videoInput.SetUINT64(ref gFr, MediaFoundationNative.PackRatio((uint)Math.Round(_targetFps), 1));
            videoInput.SetUINT32(ref gIm, 2);
            videoInput.SetUINT64(ref gPar, MediaFoundationNative.PackRatio(1, 1));

            hr = writer.SetInputMediaType(vIdx, videoInput, null);
            if (hr != 0)
            {
                Marshal.ReleaseComObject(writer);
                return false;
            }

            _sinkWriter = writer;
            _videoStreamIndex = vIdx;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ConfigureAudioStream()
    {
        Guid clsid = CLSID_AACMFTEncoder;
        Guid iid = IID_IMFTransform;
        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out object obj);
        if (hr != 0 || obj == null) throw new InvalidOperationException("Failed to activate AAC MFT");

        IMFTransform mft = (IMFTransform)obj;

        Guid gSr = MF_MT_AUDIO_SAMPLES_PER_SECOND;
        Guid gAvg = MF_MT_AUDIO_AVG_BYTES_PER_SECOND;
        Guid gSub = MF_MT_SUBTYPE;
        Guid gPcm = MFAudioFormat_PCM;

        MediaFoundationNative.IMFMediaType? aacOutType = null;
        for (uint i = 0; i < 40; i++)
        {
            hr = mft.GetOutputAvailableType(0, i, out var outType);
            if (hr != 0 || outType == null) break;
            outType.GetUINT32(ref gSr, out uint sr);
            outType.GetUINT32(ref gAvg, out uint avg);
            if (sr == 48000 && avg == 24000)
            {
                aacOutType = outType;
                break;
            }
        }
        if (aacOutType == null) throw new InvalidOperationException("AAC 48kHz 192kbps type not found");

        mft.SetOutputType(0, aacOutType, 0);

        MediaFoundationNative.IMFMediaType? aacInType = null;
        for (uint i = 0; i < 20; i++)
        {
            hr = mft.GetInputAvailableType(0, i, out var inType);
            if (hr != 0 || inType == null) break;
            inType.GetGUID(ref gSub, out Guid sub);
            inType.GetUINT32(ref gSr, out uint sr);
            inType.GetUINT32(ref gAvg, out uint avg);
            if (sub == gPcm && sr == 48000 && avg == 192000)
            {
                aacInType = inType;
                break;
            }
        }
        if (aacInType == null) throw new InvalidOperationException("PCM 48kHz stereo input type not found");

        hr = _sinkWriter!.AddStream(aacOutType, out _audioStreamIndex);
        if (hr != 0) throw new InvalidOperationException($"AddStream(AAC) failed: hr=0x{hr:X8}");

        hr = _sinkWriter.SetInputMediaType(_audioStreamIndex, aacInType, null);
        if (hr != 0) throw new InvalidOperationException($"SetInputMediaType(PCM) failed: hr=0x{hr:X8}");
    }

    public void WriteFrame(IntPtr pRgb32, int bufferSize, long sourceTimestampHns, long qpcTicks)
    {
        if (!_isRecording || _videoQueue == null || _videoQueue.IsAddingCompleted)
        {
            return;
        }

        int frameIdx = Interlocked.Increment(ref _framesCaptured) - 1;

        if (_initialSourceTimestampHns < 0)
        {
            _initialSourceTimestampHns = sourceTimestampHns;
            _firstVideoArrivalQpcTicks = qpcTicks;
        }

        if (_isAudioEnabled)
        {
            _avSynchronizer.RegisterVideoFrame(sourceTimestampHns, qpcTicks);
        }

        // Monotonicity verification
        long effectiveHns = sourceTimestampHns;
        bool corrected = false;
        double correctionDeltaMs = 0.0;
        if (effectiveHns <= _lastSourceTimestampHns)
        {
            _monotonicityCorrectionsCount++;
            corrected = true;
            correctionDeltaMs = (_lastSourceTimestampHns + 10_000 - sourceTimestampHns) / 10_000.0;
            effectiveHns = _lastSourceTimestampHns + 10_000; // +1 ms
        }

        double normalizedSec = (effectiveHns - _initialSourceTimestampHns) / 10_000_000.0;
        double rawSec = sourceTimestampHns / 10_000_000.0;
        double deltaMs = (_lastSourceTimestampHns >= 0) ? (effectiveHns - _lastSourceTimestampHns) / 10_000.0 : 0.0;
        if (_lastSourceTimestampHns >= 0)
        {
            _minFrameDeltaMs = Math.Min(_minFrameDeltaMs, deltaMs);
            _maxFrameDeltaMs = Math.Max(_maxFrameDeltaMs, deltaMs);
            _totalFrameDeltaMs += deltaMs;
            _deltaCount++;
        }

        _lastSourceTimestampHns = effectiveHns;
        _finalSourceTimestampHns = effectiveHns;
        _lastNormalizedTimeSec = normalizedSec;

        int repeatCount = 1;
        if (_cadenceMode == RecordingCadenceMode.CFR_Compatibility)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - _startStopwatchTick;
            double elapsedSec = (double)elapsedTicks / Stopwatch.Frequency;
            long targetTotalFrames = (long)Math.Round(elapsedSec * _targetFps);

            if (frameIdx == 0)
            {
                repeatCount = 1;
                _scheduledCfrFrames = 1;
            }
            else
            {
                long needed = targetTotalFrames - _scheduledCfrFrames;
                repeatCount = (int)Math.Clamp(needed, 1, 300);
                _scheduledCfrFrames += repeatCount;
            }
        }

        lock (_auditLock)
        {
            _timingAuditTrail.Add(new FrameTimingAuditRecord
            {
                FrameIndex = frameIdx,
                OriginalSourceTimestampHns = sourceTimestampHns,
                RawSourceTimeSec = rawSec,
                NormalizedTimeSec = normalizedSec,
                MonotonicityCorrected = corrected,
                CorrectionDeltaMs = correctionDeltaMs,
                InterFrameDeltaMs = deltaMs,
                RepeatCount = repeatCount
            });

            _vfrNormalizedSampleTimes.Add(normalizedSec);
        }

        // Rent buffer
        if (!_bufferPool.TryDequeue(out byte[]? frameBuf) || frameBuf.Length < bufferSize)
        {
            frameBuf = new byte[bufferSize];
        }
        Marshal.Copy(pRgb32, frameBuf, 0, bufferSize);

        var item = new VideoFrameItem
        {
            Buffer = frameBuf,
            Length = bufferSize,
            RepeatCount = repeatCount,
            SourceTimestampHns = effectiveHns,
            NormalizedTimeSec = normalizedSec,
            QpcTicks = qpcTicks
        };

        if (!_videoQueue.TryAdd(item))
        {
            Interlocked.Increment(ref _framesDropped);
            _bufferPool.Enqueue(frameBuf);
        }
    }

    private void OnAudioChunkCaptured(AudioChunk chunk)
    {
        // Admit samples while recording is active or during drain
        if (_audioQueue == null || _audioQueue.IsAddingCompleted) return;

        _avSynchronizer.RegisterAudioPacket(
            chunk.QPCPosition,
            chunk.DevicePosition,
            chunk.SampleCount,
            chunk.SampleRate,
            chunk.Channels,
            chunk.Flags);

        if (_firstAudioQpcPosition == 0)
        {
            _firstAudioQpcPosition = chunk.QPCPosition;
        }

        long sampleTimeHns = (long)(chunk.DevicePosition * 10_000_000.0 / chunk.SampleRate);

        var item = new AudioSampleItem
        {
            PcmData = chunk.Pcm16Data,
            SampleCount = chunk.SampleCount,
            Channels = chunk.Channels,
            SampleRate = chunk.SampleRate,
            TimestampHns = sampleTimeHns,
            QpcPosition = chunk.QPCPosition
        };

        _audioQueue.TryAdd(item);
    }

    private void ProcessEncodingQueues()
    {
        MediaFoundationNative.MFStartup(0x00020070, 0);

        try
        {
            // 1. Initialize IMFSinkWriter with Hardware MFT support (and software fallback)
            InitializeSinkWriter(_currentFilePath!, _frameWidth, _frameHeight);

            // 2. Configure Audio Stream (AAC 48kHz Stereo) if enabled
            if (_isAudioEnabled)
            {
                try
                {
                    ConfigureAudioStream();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NativeMediaFoundationRecorder] Audio configuration failed: {ex.Message}. Falling back to video-only.");
                    _isAudioEnabled = false;
                }
            }

            // 3. Begin Writing
            int hr = _sinkWriter!.BeginWriting();
            if (hr != 0) throw new InvalidOperationException($"_sinkWriter.BeginWriting failed: hr=0x{hr:X8}");
        }
        catch (Exception ex)
        {
            _initException = ex;
            _initDone.Set();
            return;
        }

        _initDone.Set();

        long videoFrameDurationHns = (long)(10_000_000.0 / _targetFps);
        long writtenVideoFrames = 0;
        VideoFrameItem? lastVideoItem = null;

        // Drain barrier: Run until both queues are completed and completely empty
        while (!(_videoQueue?.IsCompleted ?? true) || (_isAudioEnabled && !(_audioQueue?.IsCompleted ?? true)))
        {
            bool hadWork = false;

            // 1. Process Video
            if (_videoQueue != null && _videoQueue.TryTake(out var vItem, 5))
            {
                hadWork = true;
                lastVideoItem = vItem;
                int count = Math.Max(1, vItem.RepeatCount);
                for (int r = 0; r < count; r++)
                {
                    WriteVideoSample(vItem, writtenVideoFrames, videoFrameDurationHns);
                    writtenVideoFrames++;
                    _framesEncoded++;
                }
                _bufferPool.Enqueue(vItem.Buffer);
            }

            // 2. Process Audio (wait for video frame 0 to establish timeline epoch if video stream active)
            if (_isAudioEnabled && _audioQueue != null)
            {
                if (_framesCaptured == 0 && !(_videoQueue?.IsCompleted ?? true) && !(_writerCts?.IsCancellationRequested ?? false))
                {
                    // Yield briefly to let initial video frame arrive
                }
                else if (_audioQueue.TryTake(out var aItem, 5))
                {
                    hadWork = true;
                    WriteAudioSample(aItem);
                }
            }

            if (!hadWork)
            {
                if (_writerCts != null && _writerCts.IsCancellationRequested)
                {
                    break;
                }
                Thread.Sleep(1);
            }
        }

        // Post-drain tail duration alignment:
        if (_isAudioEnabled && _framesEncoded > 0 && _sinkWriter != null)
        {
            long videoDurHns = (_cadenceMode == RecordingCadenceMode.CFR_Compatibility)
                ? (writtenVideoFrames * videoFrameDurationHns)
                : ((_finalSourceTimestampHns >= _initialSourceTimestampHns) ? (_finalSourceTimestampHns - _initialSourceTimestampHns + videoFrameDurationHns) : 0);

            long audioDurHns = (long)(_audioSamplesWritten * 10_000_000.0 / 48000.0);
            long deltaHns = videoDurHns - audioDurHns;
            if (deltaHns > 50_000L) // Video outlasts audio by > 5 ms
            {
                long tailSilenceFrames = (long)Math.Round((double)deltaHns * 48000.0 / 10_000_000.0);
                WriteSilenceAudioFrames(tailSilenceFrames, 48000, 2);
            }
            else if (deltaHns < -50_000L && lastVideoItem != null)
            {
                // Audio outlasts video by > 5 ms: add needed duplicate frames to balance timeline
                long extraFrames = (long)Math.Round((double)(-deltaHns) / videoFrameDurationHns);
                if (extraFrames < 1) extraFrames = 1;
                for (int e = 0; e < extraFrames; e++)
                {
                    if (_cadenceMode == RecordingCadenceMode.Capture_Faithful_VFR)
                    {
                        long paddedHns = _finalSourceTimestampHns + videoFrameDurationHns;
                        var paddedItem = new VideoFrameItem
                        {
                            Buffer = lastVideoItem.Buffer,
                            Length = lastVideoItem.Length,
                            SourceTimestampHns = paddedHns,
                            QpcTicks = Stopwatch.GetTimestamp()
                        };
                        WriteVideoSample(paddedItem, writtenVideoFrames, videoFrameDurationHns);
                        _finalSourceTimestampHns = paddedHns;
                        lock (_auditLock)
                        {
                            _vfrNormalizedSampleTimes.Add((paddedHns - _initialSourceTimestampHns) / 10_000_000.0);
                        }
                    }
                    else
                    {
                        WriteVideoSample(lastVideoItem, writtenVideoFrames, videoFrameDurationHns);
                    }
                    writtenVideoFrames++;
                    _framesEncoded++;
                }
            }
        }

        // Finalize and release IMFSinkWriter on this dedicated encoder thread
        if (_sinkWriter != null)
        {
            try
            {
                if (_framesEncoded > 0)
                {
                    _sinkWriter.Finalize();
                }
                else
                {
                    Debug.WriteLine("[NativeMediaFoundationRecorder] Stop called with 0 encoded frames. Skipping SinkWriter.Finalize().");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NativeMediaFoundationRecorder] Finalize exception: {ex.Message}");
            }
            finally
            {
                Marshal.ReleaseComObject(_sinkWriter);
                _sinkWriter = null;
            }
        }
    }

    private void WriteVideoSample(VideoFrameItem item, long frameIndex, long nominalDurationHns)
    {
        if (_sinkWriter == null) return;

        int hr = MediaFoundationNative.MFCreateMemoryBuffer((uint)item.Length, out var vBuf);
        if (hr != 0) return;

        vBuf.Lock(out IntPtr pData, out uint maxLen, out uint curLen);
        Marshal.Copy(item.Buffer, 0, pData, item.Length);
        vBuf.Unlock();
        vBuf.SetCurrentLength((uint)item.Length);

        hr = MediaFoundationNative.MFCreateSample(out var sample);
        if (hr != 0)
        {
            Marshal.ReleaseComObject(vBuf);
            return;
        }

        sample.AddBuffer(vBuf);

        long ptsHns = (_cadenceMode == RecordingCadenceMode.CFR_Compatibility)
            ? (frameIndex * nominalDurationHns)
            : (item.SourceTimestampHns - _initialSourceTimestampHns);

        sample.SetSampleTime(ptsHns);
        sample.SetSampleDuration(nominalDurationHns);

        _sinkWriter.WriteSample(_videoStreamIndex, sample);

        Marshal.ReleaseComObject(sample);
        Marshal.ReleaseComObject(vBuf);
    }

    private void WriteAudioSample(AudioSampleItem item)
    {
        if (_sinkWriter == null) return;

        // Perform precision hardware timeline calibration on the very first audio sample
        if (!_leadSilenceInjected)
        {
            _leadSilenceInjected = true;
            ApplyAudioStartCalibration(item);
        }

        uint byteCount = (uint)item.PcmData.Length;
        int hr = MediaFoundationNative.MFCreateMemoryBuffer(byteCount, out var aBuf);
        if (hr != 0) return;

        aBuf.Lock(out IntPtr pData, out uint maxLen, out uint curLen);
        Marshal.Copy(item.PcmData, 0, pData, (int)byteCount);
        aBuf.Unlock();
        aBuf.SetCurrentLength(byteCount);

        hr = MediaFoundationNative.MFCreateSample(out var sample);
        if (hr != 0)
        {
            Marshal.ReleaseComObject(aBuf);
            return;
        }

        sample.AddBuffer(aBuf);
        long durHns = (long)(item.SampleCount * 10_000_000.0 / item.SampleRate);
        long ptsHns = (long)(_audioSamplesWritten * 10_000_000.0 / item.SampleRate);

        sample.SetSampleTime(ptsHns);
        sample.SetSampleDuration(durHns);

        _sinkWriter.WriteSample(_audioStreamIndex, sample);

        _audioSamplesWritten += item.SampleCount;

        Marshal.ReleaseComObject(sample);
        Marshal.ReleaseComObject(aBuf);
    }

    private void ApplyAudioStartCalibration(AudioSampleItem firstItem)
    {
        // 1. Determine video hardware start time in 100ns units
        long videoStartHns;
        if (_initialSourceTimestampHns > 10_000_000_000L)
        {
            // Camera UVC driver provides true system QPC timestamp in 100ns units
            videoStartHns = _initialSourceTimestampHns;
        }
        else if (_firstVideoArrivalQpcTicks > 0)
        {
            videoStartHns = (long)(_firstVideoArrivalQpcTicks * 10_000_000.0 / Stopwatch.Frequency);
        }
        else
        {
            videoStartHns = (long)(_startStopwatchTick * 10_000_000.0 / Stopwatch.Frequency);
        }

        // 2. Determine audio hardware start time (WASAPI QPC is always in 100ns units)
        long audioStartHns = (firstItem.QpcPosition > 0) ? (long)firstItem.QpcPosition : videoStartHns;

        // 3. Compute measured start delay between audio and video
        long offsetHns = audioStartHns - videoStartHns;
        double offsetMs = offsetHns / 10_000.0;
        Debug.WriteLine($"[NativeMediaFoundationRecorder] Calibrated A/V start offset: {offsetMs:F2} ms ({offsetHns} hns)");

        // 4. Prepend exact measured silence if audio started after video
        if (offsetHns > 5000L) // > 0.5 ms
        {
            long silenceFrames = (long)Math.Round((double)offsetHns * firstItem.SampleRate / 10_000_000.0);
            WriteSilenceAudioFrames(silenceFrames, firstItem.SampleRate, firstItem.Channels);
        }
    }

    private void WriteSilenceAudioFrames(long totalFrames, int sampleRate, int channels)
    {
        if (_sinkWriter == null || totalFrames <= 0) return;

        long remainingFrames = totalFrames;
        while (remainingFrames > 0)
        {
            int chunkFrames = (int)Math.Min(1024, remainingFrames);
            int byteCount = chunkFrames * channels * 2; // 16-bit PCM = 2 bytes per sample
            byte[] silenceData = new byte[byteCount];

            int hr = MediaFoundationNative.MFCreateMemoryBuffer((uint)byteCount, out var aBuf);
            if (hr != 0) break;

            aBuf.Lock(out IntPtr pData, out _, out _);
            Marshal.Copy(silenceData, 0, pData, byteCount);
            aBuf.Unlock();
            aBuf.SetCurrentLength((uint)byteCount);

            hr = MediaFoundationNative.MFCreateSample(out var sample);
            if (hr != 0)
            {
                Marshal.ReleaseComObject(aBuf);
                break;
            }

            sample.AddBuffer(aBuf);
            long durHns = (long)(chunkFrames * 10_000_000.0 / sampleRate);
            long ptsHns = (long)(_audioSamplesWritten * 10_000_000.0 / sampleRate);

            sample.SetSampleTime(ptsHns);
            sample.SetSampleDuration(durHns);

            _sinkWriter.WriteSample(_audioStreamIndex, sample);
            _audioSamplesWritten += chunkFrames;

            Marshal.ReleaseComObject(sample);
            Marshal.ReleaseComObject(aBuf);

            remainingFrames -= chunkFrames;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRecording || _isFinalizing) return;

            _isFinalizing = true;
            _isRecording = false;

            double stopElapsedSec = (Stopwatch.GetTimestamp() - _startStopwatchTick) / (double)Stopwatch.Frequency;
            _finalDuration = TimeSpan.FromSeconds(stopElapsedSec);

            // Gate 2 Audio Buffer Drain Barrier:
            // 1. Keep OnAudioChunkCaptured subscribed while WasapiCaptureService.Stop() runs.
            //    Stop() signals the capture worker thread and joins it, ensuring all pending
            //    WASAPI hardware buffers are fully read and emitted via AudioChunkAvailable.
            if (_isAudioEnabled)
            {
                try
                {
                    _audioCaptureService.Stop();
                }
                finally
                {
                    _audioCaptureService.AudioChunkAvailable -= OnAudioChunkCaptured;
                }
                _lastSyncReport = _avSynchronizer.GenerateReport(stopElapsedSec);
            }

            // 2. Seal the queues so no further items can be added
            _videoQueue?.CompleteAdding();
            _audioQueue?.CompleteAdding();

            // 3. Wait for the encoder worker thread to drain every single queued frame and packet, and finalize SinkWriter
            _writerThread?.Join(10000);
            _writerThread = null;

            // 4. Post-Finalization VFR hardware timestamp injection
            if (_cadenceMode == RecordingCadenceMode.Capture_Faithful_VFR &&
                _framesEncoded > 0 &&
                !string.IsNullOrEmpty(_currentFilePath) &&
                File.Exists(_currentFilePath))
            {
                List<double> normalizedPts;
                lock (_auditLock)
                {
                    normalizedPts = new List<double>(_vfrNormalizedSampleTimes);
                }

                if (normalizedPts.Count > 0)
                {
                    bool injected = Mp4TimestampInjector.InjectTrueSourceTimestamps(_currentFilePath, normalizedPts);
                    Debug.WriteLine($"[NativeMediaFoundationRecorder] VFR Hardware Timestamp Injection result: {injected}");
                }
            }

            // 6. Write Companion Sidecar Metadata V3 (for Graded recordings to document applied looks)
            if (_recordingMode == RecordingMode.GradedVideo && _framesEncoded > 0 && !string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
            {
                WriteSidecarV3(_currentFilePath, isOriginalMode: false);
            }

            _isFinalizing = false;
        }
    }

    public IReadOnlyList<FrameTimingAuditRecord> GetTimingAuditRecords()
    {
        lock (_auditLock)
        {
            return new List<FrameTimingAuditRecord>(_timingAuditTrail);
        }
    }

    public RecordingTelemetry GetTelemetry()
    {
        return new RecordingTelemetry
        {
            IsRecording = _isRecording,
            IsFinalizing = _isFinalizing,
            Mode = _recordingMode,
            CadenceMode = _cadenceMode,
            CurrentFilePath = _currentFilePath,
            Duration = Duration,
            FramesCaptured = _framesCaptured,
            FramesEncoded = _framesEncoded,
            PaddedDuplicateFrames = Math.Max(0, _framesEncoded - _framesCaptured),
            DroppedFrames = _framesDropped,
            QueueBacklog = QueueBacklog,
            EncodingFps = Duration.TotalSeconds > 0 ? (double)_framesEncoded / Duration.TotalSeconds : 0,
            IsAudioActive = _isAudioEnabled && _audioCaptureService.IsCapturing,
            AudioPeakLevel = _audioCaptureService.PeakLevel,
            AudioPeakDb = _audioCaptureService.PeakDb,
            AudioRmsDb = _audioCaptureService.RmsDb,
            IsAudioClipping = _audioCaptureService.IsClipping,
            AudioDiscontinuitiesCount = _audioCaptureService.DiscontinuitiesCount,
            AudioVideoOffsetMs = _lastSyncReport?.InitialOffsetMs ?? 0.0
        };
    }

    public void WriteSidecarV3(string mp4Path, bool isOriginalMode = false)
    {
        try
        {
            string sidecarPath = Path.ChangeExtension(mp4Path, ".json");
            var metadata = new GradedVideoMetadata
            {
                SchemaVersion = RecordingSidecarV3.CurrentSchemaVersion,
                RecordingMode = isOriginalMode ? "Original" : "Graded",
                CadenceMode = _cadenceMode.ToString(),
                VideoFileName = Path.GetFileName(mp4Path),
                VideoFilePath = mp4Path,
                RecordedAtUtc = _recordStartTime,
                CompletedAtUtc = DateTime.UtcNow,
                CameraDeviceName = "Dell Monitor Integrated Webcam",
                Width = _frameWidth,
                Height = _frameHeight,
                CaptureFormat = _captureFormat,
                TargetFps = _targetFps,
                AchievedFps = Duration.TotalSeconds > 0 ? (double)_framesCaptured / Duration.TotalSeconds : 0,
                DurationSeconds = Duration.TotalSeconds,
                TotalFramesCaptured = _framesCaptured,
                TotalFramesEncoded = _framesEncoded,
                DroppedFrames = _framesDropped,
                PaddedDuplicateFrames = Math.Max(0, _framesEncoded - _framesCaptured),
                SourceTimestampsPreserved = (_cadenceMode == RecordingCadenceMode.Capture_Faithful_VFR),
                MinFrameDeltaMs = MinFrameDeltaMs,
                MaxFrameDeltaMs = MaxFrameDeltaMs,
                MeanFrameDeltaMs = MeanFrameDeltaMs,
                MonotonicityCorrectionsCount = _monotonicityCorrectionsCount,
                InitialSourceTimestampHns = _initialSourceTimestampHns,
                FinalSourceTimestampHns = _finalSourceTimestampHns,
                SourceDurationSeconds = (_initialSourceTimestampHns >= 0 && _finalSourceTimestampHns >= _initialSourceTimestampHns)
                    ? (_finalSourceTimestampHns - _initialSourceTimestampHns) / 10_000_000.0
                    : 0.0,
                TimingPolicy = (_cadenceMode == RecordingCadenceMode.Capture_Faithful_VFR)
                    ? "Capture-Faithful Variable Frame Rate (Source-Timestamp Preserved, Zero Padding)"
                    : "Constant Frame Rate (CFR Compatibility with Duplicate Padding)",
                EncoderBackend = "Native Windows Media Foundation (IMFSinkWriter, H.264 MFT, AAC MFT)",
                FrameTimingAuditTrail = new List<FrameTimingAuditRecord>(GetTimingAuditRecords()),
                SoftwareLook = isOriginalMode
                    ? new SoftwareLookSettings { LookName = "Original Camera Passthrough", Enabled = false }
                    : (_activeLookSettings ?? new SoftwareLookSettings()),
                HardwareControls = _activeHardwareSettings ?? [],

                AudioEnabled = _isAudioEnabled,
                AudioDeviceName = _activeAudioDevice?.FriendlyName,
                AudioDeviceId = _activeAudioDevice?.Id,
                AudioSampleRate = _lastSyncReport?.AudioSampleRate ?? 48000,
                AudioChannels = _lastSyncReport?.AudioChannels ?? 2,
                AudioCodec = _isAudioEnabled ? "AAC-LC (192 kbps, 48 kHz)" : null,
                AudioDurationSeconds = (_audioSamplesWritten > 0) ? (double)_audioSamplesWritten / 48000.0 : _lastSyncReport?.AudioDurationSeconds,
                InitialAudioVideoOffsetMs = _lastSyncReport?.InitialOffsetMs,
                MiddleAudioVideoOffsetMs = _lastSyncReport?.MiddleOffsetMs,
                FinalAudioVideoOffsetMs = _lastSyncReport?.FinalOffsetMs,
                AudioVideoDriftMs = (_audioSamplesWritten > 0 && Duration.TotalSeconds > 0) ? Math.Round(((double)_audioSamplesWritten / 48000.0 - Duration.TotalSeconds) * 1000.0, 2) : _lastSyncReport?.DurationDriftMs,
                DriftRateMsPerMin = _lastSyncReport?.DriftRateMsPerMin,
                ClockPpm = _lastSyncReport?.ClockPpm,
                AudioDiscontinuitiesCount = _lastSyncReport?.AudioDiscontinuitiesCount,
                SilencePacketsCount = _audioCaptureService.SilencePacketsCount,

                SessionInterrupted = WorkstationSessionManager.Instance.CumulativeLockDurationSec > 0.05,
                CumulativeSessionLockDurationSec = WorkstationSessionManager.Instance.CumulativeLockDurationSec,
                SessionEvents = new List<SessionInterruptionEvent>(WorkstationSessionManager.Instance.GetRecordingEvents())
            };

            string json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sidecarPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NativeMediaFoundationRecorder] Failed to write sidecar V3: {ex.Message}");
        }
    }

    public void WriteTimingAuditSidecar(string sidecarPath)
    {
        try
        {
            var auditData = new
            {
                RecordingMode = _recordingMode.ToString(),
                CadenceMode = _cadenceMode.ToString(),
                VideoFilePath = CurrentFilePath ?? string.Empty,
                CaptureFormat = _captureFormat,
                TargetFps = _targetFps,
                DurationSeconds = Duration.TotalSeconds,
                SourceDurationSeconds = (_initialSourceTimestampHns >= 0 && _finalSourceTimestampHns >= _initialSourceTimestampHns)
                    ? (_finalSourceTimestampHns - _initialSourceTimestampHns) / 10_000_000.0
                    : 0.0,
                TotalFramesCaptured = _framesCaptured,
                TotalFramesEncoded = _framesEncoded,
                DroppedFrames = _framesDropped,
                PaddedDuplicateFrames = Math.Max(0, _framesEncoded - _framesCaptured),
                MonotonicityCorrectionsCount = _monotonicityCorrectionsCount,
                MinFrameDeltaMs = MinFrameDeltaMs,
                MaxFrameDeltaMs = MaxFrameDeltaMs,
                MeanFrameDeltaMs = MeanFrameDeltaMs,
                AudioEnabled = _isAudioEnabled,
                AudioSync = _lastSyncReport,
                EncoderBackend = "Native Windows Media Foundation (IMFSinkWriter, H.264 MFT, AAC MFT)",
                TimingPolicy = (_cadenceMode == RecordingCadenceMode.Capture_Faithful_VFR)
                    ? "Capture-Faithful Variable Frame Rate (Source-Timestamp Preserved, Zero Padding)"
                    : "Constant Frame Rate (CFR Compatibility with Duplicate Padding)",
                FrameTimingAuditTrail = GetTimingAuditRecords()
            };

            string json = JsonSerializer.Serialize(auditData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sidecarPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NativeMediaFoundationRecorder] Failed to write timing audit sidecar: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _audioCaptureService.Dispose();
        _videoQueue?.Dispose();
        _audioQueue?.Dispose();
        _writerCts?.Dispose();
        _initDone.Dispose();
        GC.SuppressFinalize(this);
    }
}
