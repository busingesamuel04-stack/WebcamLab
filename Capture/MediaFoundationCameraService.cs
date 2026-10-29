using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WebcamLab.Core.Models;
using WebcamLab.Core.Native;

namespace WebcamLab.Capture;

public partial class MediaFoundationCameraService : ICameraService
{
    private readonly object _stateLock = new();
    private Thread? _captureThread;
    private CancellationTokenSource? _cts;
    private bool _isCapturing;
    private IntPtr _bgr32Buffer = IntPtr.Zero;
    private int _bgr32BufferSize;
    private IntPtr _zebraBuffer = IntPtr.Zero;
    private int _zebraBufferSize;
    private IntPtr _processedBuffer = IntPtr.Zero;
    private int _processedBufferSize;
    private IntPtr _falseColorBuffer = IntPtr.Zero;
    private int _falseColorBufferSize;
    private readonly ImageAnalyzer _imageAnalyzer = new();

    private RecordingBackendMode _selectedRecordingBackend = RecordingBackendMode.MediaFoundation_Default;
    private readonly FfmpegRecorderBackend _ffmpegBackend = new();
    private readonly NativeMediaFoundationRecorder _nativeBackend = new();

    public RecordingBackendMode SelectedRecordingBackend
    {
        get => _selectedRecordingBackend;
        set
        {
            if (IsRecording) throw new InvalidOperationException("Cannot change recording backend while recording is active.");
            _selectedRecordingBackend = value;
        }
    }

    public IRecorderBackend ActiveRecorder => (_selectedRecordingBackend == RecordingBackendMode.FFmpeg_Legacy)
        ? _ffmpegBackend
        : _nativeBackend;

    private IRecorderBackend _recorder => ActiveRecorder;
    private CameraDevice? _activeDevice;
    private CameraFormat? _activeFormat;

    private readonly Queue<long> _frameTimestamps = new(64);
    private long _totalFrames;
    private long _droppedFrames;
    private Stopwatch _fpsTimer = new();

    // Snapshot request coordination (Pristine original camera payload)
    private volatile bool _snapshotRequested;
    private string? _snapshotOutputDir;
    private string? _snapshotFormat;
    private TaskCompletionSource<string>? _snapshotTcs;

    // Processed Snapshot request coordination (Graded presentation layer export)
    private volatile bool _processedSnapshotRequested;
    private string? _processedSnapshotOutputDir;
    private SoftwareLookSettings? _processedSnapshotSettings;
    private TaskCompletionSource<string>? _processedSnapshotTcs;

    // Multi-frame burst snapshot coordination (Phase 23)
    private volatile bool _burstRequested;
    private int _burstTargetCount;
    private string? _burstOutputDir;
    private MultiFrameBurstSettings? _burstSettings;
    private TaskCompletionSource<MultiFrameEnhancementSidecar>? _burstTcs;
    private List<BurstFrameData> _burstFramesCollected = [];

    // Raw burst frame collection coordination (Phase 26 Super-Resolution Research)
    private volatile bool _rawBurstRequested;
    private int _rawBurstTargetCount;
    private TaskCompletionSource<List<BurstFrameData>>? _rawBurstTcs;
    private List<BurstFrameData> _rawBurstFramesCollected = [];

    public bool IsCapturing => _isCapturing;
    public bool IsRecording => _recorder.IsRecording;
    public bool IsFinalizing => _recorder.IsFinalizing;
    public bool IsBurstCapturing => _burstRequested || (_burstFramesCollected.Count > 0);
    public CaptureStatistics Statistics { get; } = new();
    public CameraControlService Controls { get; } = new();
    public RealtekIspProcessingService RealtekIsp { get; } = new();
    public WebcamLab.Capture.Audio.WasapiCaptureService AudioCapture => _recorder.AudioService;
    public WebcamLab.Capture.Audio.AudioVideoSyncReport? LastSyncReport => _recorder.LastSyncReport;
    public RecordingTelemetry GetRecordingTelemetry() => _recorder.GetTelemetry();
    public void WriteRecordingSidecarV3(string mp4Path, bool isOriginalMode = false) => _recorder.WriteSidecarV3(mp4Path, isOriginalMode);
    public IntPtr RawBgr32Buffer => _bgr32Buffer;
    public int Bgr32BufferSize => _bgr32BufferSize;

    private RecordingMode _selectedRecordingMode = RecordingMode.OriginalCamera;
    public RecordingMode SelectedRecordingMode
    {
        get => _selectedRecordingMode;
        set => _selectedRecordingMode = value;
    }

    private RecordingCadenceMode _selectedCadenceMode = RecordingCadenceMode.Capture_Faithful_VFR;
    public RecordingCadenceMode SelectedCadenceMode
    {
        get => _selectedCadenceMode;
        set => _selectedCadenceMode = value;
    }

    public bool EnableAnalysis { get; set; } = true;
    public bool EnableZebra { get; set; } = false;
    public int ZebraThreshold { get; set; } = 235;
    public bool IsFullRange { get; set; } = true;

    public SoftwareLookPipeline LookPipeline { get; } = new();
    public SoftwareLookSettings LookSettings { get; set; } = new();
    public CubeLut? ActiveLut { get; set; }
    public bool IsGradedPreviewActive { get; set; } = true;

    public FalseColorEngine FalseColorEngine { get; } = new();
    public FalseColorSettings FalseColorSettings { get; set; } = new();

    public void UpdateFalseColorSettings(FalseColorSettings settings)
    {
        FalseColorSettings = settings.Clone();
        FalseColorEngine.SynchronizeLut(FalseColorSettings);
    }

    public event Action<IntPtr, int, int, int>? FrameReady;
    public event Action<HistogramData>? HistogramReady;
    public event Action<CaptureStatistics>? StatisticsUpdated;
    public event Action<string>? ErrorOccurred;
    public event Action<int, int>? BurstProgressUpdated;

    public MediaFoundationCameraService()
    {
        int hr = MediaFoundationNative.MFStartup(MediaFoundationNative.MF_VERSION);
        if (hr != 0)
        {
            throw new InvalidOperationException($"MFStartup failed with error code 0x{hr:X8}");
        }
    }

    public Task<IReadOnlyList<CameraDevice>> EnumerateCamerasAsync()
    {
        return Task.Run<IReadOnlyList<CameraDevice>>(() =>
        {
            var result = new List<CameraDevice>();
            int hr = MediaFoundationNative.MFCreateAttributes(out var attr, 1);
            if (hr != 0) return result;

            var sourceType = MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE;
            var vidCap = MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID;
            hr = attr.SetGUID(ref sourceType, ref vidCap);
            if (hr != 0) return result;

            hr = MediaFoundationNative.MFEnumDeviceSources(attr, out IntPtr ppDevices, out uint count);
            if (hr != 0 || count == 0) return result;

            IntPtr[] activates = new IntPtr[count];
            Marshal.Copy(ppDevices, activates, 0, (int)count);

            for (uint i = 0; i < count; i++)
            {
                var activate = (MediaFoundationNative.IMFActivate)Marshal.GetObjectForIUnknown(activates[i]);
                uint cch = 0;
                var friendlyNameKey = MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME;
                activate.GetAllocatedString(ref friendlyNameKey, out string name, ref cch);

                cch = 0;
                var symLinkKey = MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK;
                activate.GetAllocatedString(ref symLinkKey, out string symLink, ref cch);

                string? vid = null;
                string? pid = null;
                if (!string.IsNullOrEmpty(symLink))
                {
                    var vidMatch = VidRegex().Match(symLink);
                    if (vidMatch.Success) vid = vidMatch.Groups[1].Value.ToUpperInvariant();

                    var pidMatch = PidRegex().Match(symLink);
                    if (pidMatch.Success) pid = pidMatch.Groups[1].Value.ToUpperInvariant();
                }

                bool isDell = (vid == "0BDA" && pid == "58BA") ||
                              (!string.IsNullOrEmpty(name) && name.Contains("Dell", StringComparison.OrdinalIgnoreCase) && name.Contains("UZ2315H", StringComparison.OrdinalIgnoreCase));

                result.Add(new CameraDevice
                {
                    Index = (int)i,
                    Name = name ?? $"Camera {i}",
                    SymbolicLink = symLink ?? string.Empty,
                    IsDellUZ2315H = isDell,
                    VendorId = vid ?? (isDell ? "0BDA" : null),
                    ProductId = pid ?? (isDell ? "58BA" : null)
                });

                Marshal.ReleaseComObject(activate);
            }

            MediaFoundationNative.CoTaskMemFree(ppDevices);
            return result;
        });
    }

    public Task<IReadOnlyList<CameraFormat>> GetSupportedFormatsAsync(CameraDevice device)
    {
        return Task.Run<IReadOnlyList<CameraFormat>>(() =>
        {
            var formats = new List<CameraFormat>();
            int hr = MediaFoundationNative.MFCreateAttributes(out var attr, 1);
            if (hr != 0) return formats;

            var sourceType = MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE;
            var vidCap = MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID;
            attr.SetGUID(ref sourceType, ref vidCap);

            hr = MediaFoundationNative.MFEnumDeviceSources(attr, out IntPtr ppDevices, out uint count);
            if (hr != 0 || device.Index >= count) return formats;

            IntPtr[] activates = new IntPtr[count];
            Marshal.Copy(ppDevices, activates, 0, (int)count);

            var activate = (MediaFoundationNative.IMFActivate)Marshal.GetObjectForIUnknown(activates[device.Index]);
            var iidSource = typeof(MediaFoundationNative.IMFMediaSource).GUID;
            hr = activate.ActivateObject(ref iidSource, out object sourceObj);
            if (hr != 0)
            {
                MediaFoundationNative.CoTaskMemFree(ppDevices);
                return formats;
            }

            var mediaSource = (MediaFoundationNative.IMFMediaSource)sourceObj;
            hr = MediaFoundationNative.MFCreateSourceReaderFromMediaSource(mediaSource, null, out var reader);
            if (hr != 0)
            {
                Marshal.ReleaseComObject(mediaSource);
                activate.ShutdownObject();
                Marshal.ReleaseComObject(activate);
                MediaFoundationNative.CoTaskMemFree(ppDevices);
                return formats;
            }

            uint typeIndex = 0;
            var frameSizeKey = MediaFoundationNative.MF_MT_FRAME_SIZE;
            var subtypeKey = MediaFoundationNative.MF_MT_SUBTYPE;
            var frameRateKey = MediaFoundationNative.MF_MT_FRAME_RATE;
            var rateMaxKey = MediaFoundationNative.MF_MT_FRAME_RATE_RANGE_MAX;
            var nominalRangeKey = MediaFoundationNative.MF_MT_VIDEO_NOMINAL_RANGE;

            while (true)
            {
                hr = reader.GetNativeMediaType(MediaFoundationNative.MFSOURCE_READER_FIRST_VIDEO_STREAM, typeIndex, out var mediaType);
                if (hr != 0) break;

                mediaType.GetGUID(ref subtypeKey, out Guid subtype);
                mediaType.GetUINT64(ref frameSizeKey, out ulong sizeVal);
                uint width = (uint)(sizeVal >> 32);
                uint height = (uint)(sizeVal & 0xFFFFFFFF);

                hr = mediaType.GetUINT64(ref frameRateKey, out ulong rateVal);
                if (hr != 0 || rateVal == 0)
                {
                    mediaType.GetUINT64(ref rateMaxKey, out rateVal);
                }
                uint num = (uint)(rateVal >> 32);
                uint den = (uint)(rateVal & 0xFFFFFFFF);
                double fps = den > 0 ? (double)num / den : 0;

                string fourcc = MediaFoundationNative.GetFourCCFromGuid(subtype);

                uint? rawNominal = null;
                NominalColorRange nominalRange = NominalColorRange.Unspecified;
                if (mediaType.GetUINT32(ref nominalRangeKey, out uint nomVal) == 0)
                {
                    rawNominal = nomVal;
                    nominalRange = nomVal switch
                    {
                        1 => NominalColorRange.FullRange_0_255,   // MFNominalRange_Normal / MFNominalRange_0_255
                        2 => NominalColorRange.VideoRange_16_235, // MFNominalRange_Wide / MFNominalRange_16_235
                        3 => NominalColorRange.Extended_48_208,
                        4 => NominalColorRange.Extended_64_127,
                        _ => NominalColorRange.Unspecified
                    };
                }
                else
                {
                    // Implicit standard fallback when driver omits the attribute
                    nominalRange = (fourcc is "MJPG" or "MJPEG" or "RGB24" or "RGB32")
                        ? NominalColorRange.FullRange_0_255
                        : NominalColorRange.VideoRange_16_235;
                }

                formats.Add(new CameraFormat
                {
                    StreamIndex = MediaFoundationNative.MFSOURCE_READER_FIRST_VIDEO_STREAM,
                    TypeIndex = typeIndex,
                    SubtypeGuid = subtype,
                    FourCC = fourcc,
                    Width = width,
                    Height = height,
                    FrameRate = fps,
                    FrameRateNumerator = num,
                    FrameRateDenominator = den,
                    RawNominalRangeValue = rawNominal,
                    NominalRange = nominalRange
                });

                Marshal.ReleaseComObject(mediaType);
                typeIndex++;
            }

            Marshal.ReleaseComObject(reader);
            Marshal.ReleaseComObject(mediaSource);
            activate.ShutdownObject();
            Marshal.ReleaseComObject(activate);

            for (uint i = 0; i < count; i++)
            {
                if (i != device.Index)
                {
                    Marshal.Release(activates[i]);
                }
            }

            MediaFoundationNative.CoTaskMemFree(ppDevices);
            return formats;
        });
    }

    public Task StartCaptureAsync(CameraDevice device, CameraFormat format)
    {
        lock (_stateLock)
        {
            if (_isCapturing)
            {
                StopCaptureInternal();
            }

            _activeDevice = device;
            _activeFormat = format;
            RealtekIsp.UpdateActiveFormat(format);
            _cts = new CancellationTokenSource();
            _isCapturing = true;
            IsFullRange = (format.NominalRange != NominalColorRange.VideoRange_16_235);

            int width = (int)format.Width;
            int height = (int)format.Height;
            _bgr32BufferSize = width * height * 4;
            _zebraBufferSize = _bgr32BufferSize;
            _processedBufferSize = _bgr32BufferSize;
            _falseColorBufferSize = _bgr32BufferSize;

            if (_bgr32Buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_bgr32Buffer);
            }
            _bgr32Buffer = Marshal.AllocHGlobal(_bgr32BufferSize);

            if (_zebraBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_zebraBuffer);
            }
            _zebraBuffer = Marshal.AllocHGlobal(_zebraBufferSize);

            if (_processedBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_processedBuffer);
            }
            _processedBuffer = Marshal.AllocHGlobal(_processedBufferSize);

            if (_falseColorBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_falseColorBuffer);
            }
            _falseColorBuffer = Marshal.AllocHGlobal(_falseColorBufferSize);

            _totalFrames = 0;
            _droppedFrames = 0;
            _frameTimestamps.Clear();
            _fpsTimer.Restart();

            Statistics.ActiveResolution = $"{width} × {height}";
            Statistics.ActivePixelFormat = format.FourCC;
            Statistics.TargetFps = format.FrameRate;
            Statistics.CurrentFps = 0;
            Statistics.TotalFrames = 0;
            Statistics.DroppedFrames = 0;
            Statistics.IsActive = true;
            Statistics.CaptureBackend = "Windows Media Foundation (SourceReader)";

            var token = _cts.Token;
            _captureThread = new Thread(() => CaptureLoop(device.Index, format.TypeIndex, width, height, format.FourCC, token))
            {
                IsBackground = true,
                Name = "WebcamLab.CaptureThread",
                Priority = ThreadPriority.AboveNormal
            };
            _captureThread.Start();
        }

        return Task.CompletedTask;
    }

    private void CaptureLoop(int deviceIndex, uint typeIndex, int width, int height, string fourcc, CancellationToken token)
    {
        MediaFoundationNative.IMFActivate? activate = null;
        MediaFoundationNative.IMFMediaSource? mediaSource = null;
        MediaFoundationNative.IMFSourceReader? reader = null;
        IntPtr ppDevices = IntPtr.Zero;

        try
        {
            int hr = MediaFoundationNative.MFCreateAttributes(out var attr, 1);
            if (hr != 0) throw new InvalidOperationException($"MFCreateAttributes failed: 0x{hr:X8}");

            var sourceType = MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE;
            var vidCap = MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID;
            attr.SetGUID(ref sourceType, ref vidCap);

            hr = MediaFoundationNative.MFEnumDeviceSources(attr, out ppDevices, out uint count);
            if (hr != 0 || deviceIndex >= count) throw new InvalidOperationException("Camera device not found or disconnected.");

            IntPtr[] activates = new IntPtr[count];
            Marshal.Copy(ppDevices, activates, 0, (int)count);
            activate = (MediaFoundationNative.IMFActivate)Marshal.GetObjectForIUnknown(activates[deviceIndex]);

            var iidSource = typeof(MediaFoundationNative.IMFMediaSource).GUID;
            hr = activate.ActivateObject(ref iidSource, out object sourceObj);
            if (hr != 0)
            {
                if (hr == unchecked((int)0x800700AA) || hr == unchecked((int)0x8007001F))
                {
                    throw new InvalidOperationException("The camera is currently in use by another application (e.g. Teams, Zoom, Windows Camera).");
                }
                throw new InvalidOperationException($"Failed to activate camera device: 0x{hr:X8}");
            }

            mediaSource = (MediaFoundationNative.IMFMediaSource)sourceObj;
            Controls.AttachLiveMediaSource(mediaSource);
            hr = MediaFoundationNative.MFCreateSourceReaderFromMediaSource(mediaSource, null, out reader);
            if (hr != 0) throw new InvalidOperationException($"MFCreateSourceReaderFromMediaSource failed: 0x{hr:X8}");

            // Configure native type
            hr = reader.GetNativeMediaType(MediaFoundationNative.MFSOURCE_READER_FIRST_VIDEO_STREAM, typeIndex, out var nativeType);
            if (hr != 0) throw new InvalidOperationException($"GetNativeMediaType failed: 0x{hr:X8}");

            hr = reader.SetCurrentMediaType(MediaFoundationNative.MFSOURCE_READER_FIRST_VIDEO_STREAM, IntPtr.Zero, nativeType);
            Marshal.ReleaseComObject(nativeType);
            if (hr != 0) throw new InvalidOperationException($"SetCurrentMediaType failed: 0x{hr:X8}");

            var lastStatsUpdate = Stopwatch.StartNew();
            long bytesProcessedInWindow = 0;

            while (!token.IsCancellationRequested)
            {
                hr = reader.ReadSample(MediaFoundationNative.MFSOURCE_READER_FIRST_VIDEO_STREAM, 0, out _, out uint flags, out ulong timestamp, out var sample);
                if (hr != 0)
                {
                    _droppedFrames++;
                    continue;
                }

                if (sample == null)
                {
                    continue;
                }

                sample.GetBufferByIndex(0, out var buffer);
                buffer.Lock(out IntPtr pData, out _, out uint curLen);

                try
                {
                    bytesProcessedInWindow += curLen;
                    _totalFrames++;

                    // Convert sample to BGR32 target buffer
                    if (fourcc is "MJPG" or "MJPEG")
                    {
                        ColorConverter.DecodeMjpegToBgr32(pData, curLen, _bgr32Buffer, width, height);
                    }
                    else if (fourcc is "YUY2" or "YUYV")
                    {
                        bool isStudio = _activeFormat?.NominalRange == NominalColorRange.VideoRange_16_235;
                        ColorConverter.ConvertYuy2ToBgr32(pData, _bgr32Buffer, width, height, isStudio);
                    }
                    else if (fourcc is "NV12")
                    {
                        bool isStudio = _activeFormat?.NominalRange == NominalColorRange.VideoRange_16_235;
                        ColorConverter.ConvertNv12ToBgr32(pData, _bgr32Buffer, width, height, isStudio);
                    }

                    // Process Software Look if active or needed for Graded recording
                    bool needGrading = (IsGradedPreviewActive && LookSettings.Enabled) ||
                                       (_recorder.IsRecording && _selectedRecordingMode == RecordingMode.GradedVideo);

                    IntPtr displaySourceBuffer = _bgr32Buffer;
                    if (needGrading && _processedBuffer != IntPtr.Zero)
                    {
                        LookPipeline.ProcessFrame(_bgr32Buffer, _processedBuffer, width, height, width * 4, LookSettings, ActiveLut);
                        if (IsGradedPreviewActive && LookSettings.Enabled)
                        {
                            displaySourceBuffer = _processedBuffer;
                        }
                    }

                    // Feed recorder if active (Strict routing: Original encodes _bgr32Buffer, Graded encodes _processedBuffer)
                    // Guarantee: False-color, zebras, and HUD overlays are rendered downstream, never touching these buffers
                    if (_recorder.IsRecording)
                    {
                        ulong sourceTimestampHns = timestamp;
                        if (sourceTimestampHns == 0)
                        {
                            sample.GetSampleTime(out long st);
                            if (st > 0) sourceTimestampHns = (ulong)st;
                        }
                        long arrivalQpcTicks = Stopwatch.GetTimestamp();

                        IntPtr recordBuffer = (_selectedRecordingMode == RecordingMode.GradedVideo) ? _processedBuffer : _bgr32Buffer;
                        _recorder.WriteFrame(recordBuffer, _bgr32BufferSize, (long)sourceTimestampHns, arrivalQpcTicks);
                    }

                    // Handle snapshot if requested (always pristine camera payload)
                    if (_snapshotRequested)
                    {
                        HandleSnapshot(pData, curLen, width, height, fourcc);
                    }

                    // Handle Processed Snapshot request if requested
                    if (_processedSnapshotRequested)
                    {
                        HandleProcessedSnapshot(displaySourceBuffer, width, height, width * 4);
                    }

                    // Handle multi-frame burst capture collection if active (Phase 23)
                    if (_burstRequested)
                    {
                        ulong sourceTimestampHns = timestamp;
                        if (sourceTimestampHns == 0)
                        {
                            sample.GetSampleTime(out long st);
                            if (st > 0) sourceTimestampHns = (ulong)st;
                        }

                        // Check for duplicate frame
                        bool isDuplicate = _burstFramesCollected.Count > 0 &&
                                           _burstFramesCollected[^1].TimestampHns == (long)sourceTimestampHns &&
                                           sourceTimestampHns != 0;

                        if (!isDuplicate)
                        {
                            byte[] frameBytes = new byte[width * height * 4];
                            Marshal.Copy(_bgr32Buffer, frameBytes, 0, frameBytes.Length);
                            var frameData = BurstFrameData.Create(
                                _burstFramesCollected.Count + 1,
                                (long)sourceTimestampHns,
                                Stopwatch.GetTimestamp(),
                                width,
                                height,
                                frameBytes);

                            _burstFramesCollected.Add(frameData);

                            BurstProgressUpdated?.Invoke(_burstFramesCollected.Count, _burstTargetCount);

                            if (_burstFramesCollected.Count >= _burstTargetCount)
                            {
                                _burstRequested = false;
                                var collected = _burstFramesCollected;
                                _burstFramesCollected = [];
                                var bDir = _burstOutputDir ?? AppDomain.CurrentDomain.BaseDirectory;
                                var bSettings = _burstSettings ?? new MultiFrameBurstSettings();
                                var bTcs = _burstTcs;
                                var camName = _activeDevice?.Name ?? "Dell Monitor Integrated Webcam";
                                var fmtName = fourcc;

                                // Process burst asynchronously on thread pool so capture loop is never blocked
                                Task.Run(() =>
                                {
                                    try
                                    {
                                        var report = MultiFrameCaptureService.ProcessBurst(collected, bDir, bSettings, camName, fmtName);
                                        bTcs?.TrySetResult(report);
                                    }
                                    catch (Exception ex)
                                    {
                                        bTcs?.TrySetException(ex);
                                    }
                                });
                            }
                        }
                    }

                    // Handle raw burst frame collection if active (Phase 26 Super-Resolution)
                    if (_rawBurstRequested)
                    {
                        ulong sourceTimestampHns = timestamp;
                        if (sourceTimestampHns == 0)
                        {
                            sample.GetSampleTime(out long st);
                            if (st > 0) sourceTimestampHns = (ulong)st;
                        }

                        bool isDuplicate = _rawBurstFramesCollected.Count > 0 &&
                                           _rawBurstFramesCollected[^1].TimestampHns == (long)sourceTimestampHns &&
                                           sourceTimestampHns != 0;

                        if (!isDuplicate)
                        {
                            byte[] frameBytes = new byte[width * height * 4];
                            Marshal.Copy(_bgr32Buffer, frameBytes, 0, frameBytes.Length);
                            var frameData = BurstFrameData.Create(
                                _rawBurstFramesCollected.Count + 1,
                                (long)sourceTimestampHns,
                                Stopwatch.GetTimestamp(),
                                width,
                                height,
                                frameBytes);

                            _rawBurstFramesCollected.Add(frameData);

                            if (_rawBurstFramesCollected.Count >= _rawBurstTargetCount)
                            {
                                _rawBurstRequested = false;
                                var collected = _rawBurstFramesCollected;
                                _rawBurstFramesCollected = [];
                                var tcs = _rawBurstTcs;
                                Task.Run(() => tcs?.TrySetResult(collected));
                            }
                        }
                    }

                    // Determine analysis source buffer (Original Camera Feed vs Graded Preview)
                    IntPtr analysisSourceBuffer = _bgr32Buffer;
                    if (FalseColorSettings.AnalysisSource == AnalysisSource.GradedPreview && IsGradedPreviewActive && LookSettings.Enabled)
                    {
                        analysisSourceBuffer = _processedBuffer;
                    }

                    // Viewport presentation composition (strictly non-destructive)
                    IntPtr previewBuffer = displaySourceBuffer;

                    // 1. False-color exposure scope overlay
                    if (FalseColorSettings.Enabled && _falseColorBuffer != IntPtr.Zero)
                    {
                        FalseColorEngine.RenderFalseColor(
                            analysisSourceBuffer,
                            displaySourceBuffer,
                            _falseColorBuffer,
                            width,
                            height,
                            width * 4,
                            FalseColorSettings);

                        previewBuffer = _falseColorBuffer;
                    }

                    // 2. Analyze frame & generate histogram/clipping metrics (and optional zebra overlay)
                    if (EnableAnalysis || EnableZebra)
                    {
                        var hist = _imageAnalyzer.AnalyzeFrame(
                            analysisSourceBuffer,
                            width,
                            height,
                            width * 4,
                            IsFullRange,
                            ZebraThreshold,
                            EnableZebra,
                            _zebraBuffer,
                            out bool zebraApplied,
                            previewBuffer);

                        if (EnableZebra && zebraApplied && _zebraBuffer != IntPtr.Zero)
                        {
                            previewBuffer = _zebraBuffer;
                        }

                        HistogramReady?.Invoke(hist);
                    }

                    // Dispatch frame ready to UI
                    FrameReady?.Invoke(previewBuffer, width, height, width * 4);

                    // Update FPS
                    long now = Stopwatch.GetTimestamp();
                    _frameTimestamps.Enqueue(now);
                    while (_frameTimestamps.Count > 30)
                    {
                        _frameTimestamps.Dequeue();
                    }

                    if (lastStatsUpdate.ElapsedMilliseconds >= 250)
                    {
                        double fps = 0;
                        if (_frameTimestamps.Count > 1)
                        {
                            long oldest = _frameTimestamps.Peek();
                            double elapsedSec = (double)(now - oldest) / Stopwatch.Frequency;
                            if (elapsedSec > 0)
                            {
                                fps = (_frameTimestamps.Count - 1) / elapsedSec;
                            }
                        }

                        double mbps = (bytesProcessedInWindow / (1024.0 * 1024.0)) / (lastStatsUpdate.ElapsedMilliseconds / 1000.0);
                        bytesProcessedInWindow = 0;
                        lastStatsUpdate.Restart();

                        Statistics.CurrentFps = fps;
                        Statistics.TotalFrames = _totalFrames;
                        Statistics.DroppedFrames = _droppedFrames;
                        Statistics.DataRateMBps = mbps;
                        Statistics.IsRecording = _recorder.IsRecording;
                        Statistics.RecordingDuration = _recorder.Duration;
                        Statistics.RecordingFilePath = _recorder.CurrentFilePath;

                        StatisticsUpdated?.Invoke(Statistics);
                    }
                }
                finally
                {
                    buffer.Unlock();
                    Marshal.ReleaseComObject(buffer);
                    Marshal.ReleaseComObject(sample);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean cancellation
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Capture error: {ex.Message}");
        }
        finally
        {
            Controls.DetachLiveMediaSource();
            if (reader != null) Marshal.ReleaseComObject(reader);
            if (mediaSource != null) Marshal.ReleaseComObject(mediaSource);
            if (activate != null)
            {
                activate.ShutdownObject();
                Marshal.ReleaseComObject(activate);
            }
            if (ppDevices != IntPtr.Zero)
            {
                MediaFoundationNative.CoTaskMemFree(ppDevices);
            }
        }
    }

    private void HandleSnapshot(IntPtr rawDataPtr, uint rawLen, int width, int height, string fourcc)
    {
        _snapshotRequested = false;
        var dir = _snapshotOutputDir ?? AppDomain.CurrentDomain.BaseDirectory;
        var format = _snapshotFormat ?? "png";
        var tcs = _snapshotTcs;

        try
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string filename = $"WebcamLab_{fourcc}_{width}x{height}_{timestamp}.{format.ToLowerInvariant()}";
            string filePath = Path.Combine(dir, filename);

            if ((fourcc is "MJPG" or "MJPEG") && format.Equals("jpg", StringComparison.OrdinalIgnoreCase))
            {
                // Direct zero-loss write of raw camera JPEG frame!
                byte[] jpegBytes = new byte[rawLen];
                Marshal.Copy(rawDataPtr, jpegBytes, 0, (int)rawLen);
                File.WriteAllBytes(filePath, jpegBytes);
            }
            else
            {
                // Write from converted Bgr32 buffer
                var bitmap = BitmapSource.Create(
                    width, height, 96, 96,
                    System.Windows.Media.PixelFormats.Bgr32, null,
                    _bgr32Buffer, _bgr32BufferSize, width * 4);

                using var fileStream = new FileStream(filePath, FileMode.Create);
                BitmapEncoder encoder = format.ToLowerInvariant() switch
                {
                    "jpg" or "jpeg" => new JpegBitmapEncoder { QualityLevel = 98 },
                    "bmp" => new BmpBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(fileStream);
            }

            tcs?.TrySetResult(filePath);
        }
        catch (Exception ex)
        {
            tcs?.TrySetException(ex);
        }
    }

    public Task<string> CaptureStillImageAsync(string outputDirectory, string format = "png")
    {
        if (!_isCapturing)
        {
            throw new InvalidOperationException("Cannot capture still image: Camera stream is not active.");
        }

        var tcs = new TaskCompletionSource<string>();
        _snapshotOutputDir = outputDirectory;
        _snapshotFormat = format;
        _snapshotTcs = tcs;
        _snapshotRequested = true;

        return tcs.Task;
    }

    public Task<string> ExportProcessedSnapshotAsync(string outputDirectory, SoftwareLookSettings settings, string format = "png")
    {
        if (!_isCapturing)
        {
            throw new InvalidOperationException("Cannot export processed snapshot: Camera stream is not active.");
        }

        var tcs = new TaskCompletionSource<string>();
        _processedSnapshotOutputDir = outputDirectory;
        _processedSnapshotSettings = settings.Clone();
        _processedSnapshotTcs = tcs;
        _processedSnapshotRequested = true;

        return tcs.Task;
    }

    public Task<MultiFrameEnhancementSidecar> CaptureEnhancedStillAsync(string outputDirectory, MultiFrameBurstSettings settings)
    {
        if (!_isCapturing)
        {
            throw new InvalidOperationException("Cannot capture enhanced still: Camera stream is not active.");
        }
        if (_burstRequested || _rawBurstRequested)
        {
            throw new InvalidOperationException("A multi-frame burst capture is already in progress.");
        }

        var tcs = new TaskCompletionSource<MultiFrameEnhancementSidecar>();
        _burstOutputDir = outputDirectory;
        _burstSettings = settings;
        _burstTargetCount = settings.BurstSize;
        _burstFramesCollected = [];
        _burstTcs = tcs;
        _burstRequested = true;

        return tcs.Task;
    }

    /// <summary>
    /// Asynchronously captures a burst of genuine raw camera frames directly from the live stream.
    /// Used for Phase 26 Super-Resolution research and subpixel motion analysis.
    /// </summary>
    public Task<List<BurstFrameData>> CaptureRawBurstFramesAsync(int burstSize)
    {
        if (!_isCapturing)
        {
            throw new InvalidOperationException("Cannot capture raw burst: Camera stream is not active.");
        }
        if (_burstRequested || _rawBurstRequested)
        {
            throw new InvalidOperationException("A multi-frame burst capture is already in progress.");
        }

        var tcs = new TaskCompletionSource<List<BurstFrameData>>();
        _rawBurstTargetCount = burstSize;
        _rawBurstFramesCollected = [];
        _rawBurstTcs = tcs;
        _rawBurstRequested = true;

        return tcs.Task;
    }

    private void HandleProcessedSnapshot(IntPtr displaySourceBuffer, int width, int height, int stride)
    {
        _processedSnapshotRequested = false;
        var dir = _processedSnapshotOutputDir ?? AppDomain.CurrentDomain.BaseDirectory;
        var settings = _processedSnapshotSettings ?? LookSettings;
        var tcs = _processedSnapshotTcs;

        try
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Ensure we export the graded frame even if preview was in bypass mode
            IntPtr exportBuffer = displaySourceBuffer;
            if (exportBuffer == _bgr32Buffer && (settings.Enabled || !settings.IsNeutral() || (settings.LutEnabled && ActiveLut != null)))
            {
                LookPipeline.ProcessFrame(_bgr32Buffer, _processedBuffer, width, height, stride, settings, ActiveLut);
                exportBuffer = _processedBuffer;
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string imageFileName = $"WebcamLab_Graded_{width}x{height}_{timestamp}.png";
            string imagePath = Path.Combine(dir, imageFileName);

            var bitmapSource = BitmapSource.Create(
                width,
                height,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgr32,
                null,
                exportBuffer,
                stride * height,
                stride);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            using (var fs = new FileStream(imagePath, FileMode.Create, FileAccess.Write))
            {
                encoder.Save(fs);
            }

            // Write companion metadata sidecar JSON
            string metaFileName = $"WebcamLab_Graded_{width}x{height}_{timestamp}_metadata.json";
            string metaPath = Path.Combine(dir, metaFileName);
            var metadata = new
            {
                CaptureType = "WebcamLab Processed Graded Snapshot",
                ExportedAtUtc = DateTime.UtcNow.ToString("o"),
                Resolution = $"{width}x{height}",
                Format = "Lossless PNG (BGR32 surface)",
                Note = "Processed from decoded camera feed on presentation layer. Camera raw payload and untouched recordings remain unaffected.",
                SoftwareLook = new
                {
                    settings.LookName,
                    settings.ToneCurveName,
                    ExposureOffsetEV = settings.ExposureOffset,
                    settings.Contrast,
                    settings.Saturation,
                    settings.Highlights,
                    settings.Shadows,
                    settings.SCurveStrength,
                    ActiveLut = settings.LutFileName ?? "None",
                    settings.LutEnabled,
                    settings.SharpenStrength,
                    settings.DenoiseStrength
                }
            };

            string metaJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metaPath, metaJson);

            tcs?.TrySetResult(imagePath);
        }
        catch (Exception ex)
        {
            tcs?.TrySetException(ex);
        }
    }

    public Task StartRecordingAsync(
        string outputFilePath,
        RecordingMode mode = RecordingMode.OriginalCamera,
        RecordingCadenceMode cadenceMode = RecordingCadenceMode.CFR_Compatibility,
        bool enableAudio = true,
        WebcamLab.Core.Audio.AudioDeviceInfo? audioDevice = null)
    {
        if (!_isCapturing || _activeFormat == null)
        {
            throw new InvalidOperationException("Cannot start recording: Camera stream is not active.");
        }

        _selectedRecordingMode = mode;
        _selectedCadenceMode = cadenceMode;
        var hwSettings = Controls.GetCurrentControlValues();
        _recorder.Start(
            outputFilePath,
            (int)_activeFormat.Width,
            (int)_activeFormat.Height,
            _activeFormat.FrameRate,
            mode,
            LookSettings,
            hwSettings,
            _activeFormat.FourCC,
            cadenceMode,
            enableAudio,
            audioDevice);

        Statistics.IsRecording = true;
        Statistics.RecordingFilePath = outputFilePath;
        StatisticsUpdated?.Invoke(Statistics);

        return Task.CompletedTask;
    }

    public Task StopRecordingAsync()
    {
        try
        {
            _recorder.Stop();
        }
        finally
        {
            Statistics.IsRecording = false;
            StatisticsUpdated?.Invoke(Statistics);
        }
        return Task.CompletedTask;
    }

    public IReadOnlyList<FrameTimingAuditRecord> GetLastRecordingAuditRecords() => _recorder.GetTimingAuditRecords();
    public void WriteTimingAuditSidecar(string sidecarPath) => _recorder.WriteTimingAuditSidecar(sidecarPath);
    public int LastMonotonicityCorrectionsCount => _recorder.MonotonicityCorrectionsCount;

    public Task StopCaptureAsync()
    {
        lock (_stateLock)
        {
            StopCaptureInternal();
        }
        return Task.CompletedTask;
    }

    private void StopCaptureInternal()
    {
        if (_recorder.IsRecording)
        {
            _recorder.Stop();
        }

        _cts?.Cancel();
        _captureThread?.Join(1000);
        _captureThread = null;
        _cts?.Dispose();
        _cts = null;
        _isCapturing = false;

        _burstRequested = false;
        _burstTcs?.TrySetCanceled();
        _burstFramesCollected.Clear();

        _rawBurstRequested = false;
        _rawBurstTcs?.TrySetCanceled();
        _rawBurstFramesCollected.Clear();

        if (_bgr32Buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_bgr32Buffer);
            _bgr32Buffer = IntPtr.Zero;
        }

        if (_zebraBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_zebraBuffer);
            _zebraBuffer = IntPtr.Zero;
        }

        if (_processedBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_processedBuffer);
            _processedBuffer = IntPtr.Zero;
            _processedBufferSize = 0;
        }

        if (_falseColorBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_falseColorBuffer);
            _falseColorBuffer = IntPtr.Zero;
            _falseColorBufferSize = 0;
        }

        Statistics.IsActive = false;
        Statistics.IsRecording = false;
        Statistics.CurrentFps = 0;
        StatisticsUpdated?.Invoke(Statistics);
    }

    public void Dispose()
    {
        StopCaptureInternal();
        RealtekIsp.Dispose();
        LookPipeline.Dispose();
        _ffmpegBackend.Dispose();
        _nativeBackend.Dispose();
        MediaFoundationNative.MFShutdown();
        GC.SuppressFinalize(this);
    }

    [GeneratedRegex(@"VID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VidRegex();

    [GeneratedRegex(@"PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex PidRegex();
}
