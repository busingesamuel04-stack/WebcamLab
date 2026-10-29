using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WebcamLab.Capture;
using WebcamLab.Core.Models;

namespace WebcamLab.ViewModels;

public class StarterLookItem
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SoftwareLookSettings Settings { get; set; } = new();

    public override string ToString() => Name;
}

public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly ICameraService _cameraService;
    private readonly PresetService _presetService;
    private readonly Dispatcher _dispatcher;
    private readonly HistogramRenderer _histogramRenderer = new();
    private readonly Dictionary<int, CancellationTokenSource> _propertyDebounceTokens = new();
    private readonly DispatcherTimer _uiTelemetryTimer;
    private long _lastHistogramRenderTick;
    private string? _activeRecordingFilePath;

    private ObservableCollection<CameraDevice> _cameras = [];
    private CameraDevice? _selectedCamera;
    private ObservableCollection<CameraFormat> _formats = [];
    private CameraFormat? _selectedFormat;
    private ObservableCollection<CapabilityItem> _capabilities = [];
    private ObservableCollection<string> _capturedMediaList = [];

    private ObservableCollection<CameraHardwareProperty> _supportedControls = [];
    private ObservableCollection<CameraHardwareProperty> _unsupportedControls = [];
    private ObservableCollection<CapturePreset> _presets = [];
    private CapturePreset? _selectedPreset;
    private string _presetStatusText = "Select a preset to apply or create a new custom preset.";
    private string _newPresetName = string.Empty;
    private string _newPresetDescription = string.Empty;

    private bool _enableZebra;
    private int _zebraThreshold = 235;
    private bool _isFullRange = true;
    private bool _showRgbHistogram;
    private string _highlightClipText = "0.0%";
    private string _shadowCrushText = "0.0%";
    private string _meanLumaText = "0.0";
    private string _observedRangeText = "--";
    private string _broadcastSuperWhiteText = "0.0%";
    private string _broadcastSubBlackText = "0.0%";
    private string _rangeStandardText = "Full-Range sRGB [0..255]";

    // Phase 8 False-Color Exposure Scopes
    private bool _enableFalseColor;
    private FalseColorProfile _selectedFalseColorProfile = FalseColorProfile.WebcamLabCustom10Zone;
    private AnalysisSource _analysisSource = AnalysisSource.OriginalCameraFeed;
    private ArriIntermediateMode _arriIntermediateMode = ArriIntermediateMode.DesaturatedGrayscale;
    private double _falseColorOpacity = 100.0;
    private double _skinToneTarget = 54.0;
    private double _skinToneTolerance = 3.0;
    private double _middleGrayTarget = 40.0;
    private double _middleGrayTolerance = 2.0;
    private string? _zoneValidationError;

    private bool _isGradedPreviewActive = true;
    private double _softwareExposureOffset = 0.0;
    private double _softwareContrast = 1.0;
    private double _softwareSaturation = 1.0;
    private double _softwareHighlights = 0.0;
    private double _softwareShadows = 0.0;
    private double _softwareSCurveStrength = 0.0;
    private double _softwareSharpenStrength = 0.0;
    private double _softwareDenoiseStrength = 0.0;
    private string _activeLutTitle = "None (Neutral)";
    private bool _isLutLoaded;
    private bool _isLutEnabled;
    private StarterLookItem? _selectedStarterLook;
    private ObservableCollection<StarterLookItem> _starterLooks = [];

    private WriteableBitmap? _previewBitmap;
    private int _isRenderingFrame;

    private bool _isCapturing;
    private bool _isRecording;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _statusMessage = "Ready. Select a camera to inspect capabilities.";

    private string _activeResolution = "--";
    private string _activePixelFormat = "--";
    private string _currentFps = "0.0";
    private string _targetFps = "--";
    private long _totalFrames;
    private long _droppedFrames;
    private string _dataRateMBps = "0.00";
    private string _recordingDuration = "00:00:00";
    private bool _isDellDetected;
    private string _dellBadgeText = string.Empty;

    // Phase 15: Experimental Realtek ISP YUY2 Processing Control
    private bool _isRealtekStandardModeSelected = true;
    private bool _isRealtekReducedDetailSelected = false;
    private string _realtekFeedbackMessage = string.Empty;
    private bool _hasRealtekFeedback;

    public ObservableCollection<CameraDevice> Cameras
    {
        get => _cameras;
        set => SetProperty(ref _cameras, value);
    }

    public CameraDevice? SelectedCamera
    {
        get => _selectedCamera;
        set
        {
            if (SetProperty(ref _selectedCamera, value))
            {
                OnCameraSelected(value);
            }
        }
    }

    public ObservableCollection<CameraFormat> Formats
    {
        get => _formats;
        set => SetProperty(ref _formats, value);
    }

    public CameraFormat? SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetProperty(ref _selectedFormat, value))
            {
                if (value != null)
                {
                    IsFullRange = (value.NominalRange != NominalColorRange.VideoRange_16_235);
                    OnPropertyChanged(nameof(ActiveNominalRangeText));
                    _cameraService.RealtekIsp.UpdateActiveFormat(value);
                    ApplyRealtekProcessingCommand?.RaiseCanExecuteChanged();
                }

                if (value != null && IsCapturing && SelectedCamera != null)
                {
                    _ = SwitchFormatAsync(SelectedCamera, value);
                }
            }
        }
    }

    public ObservableCollection<CapabilityItem> Capabilities
    {
        get => _capabilities;
        set => SetProperty(ref _capabilities, value);
    }

    public ObservableCollection<string> CapturedMediaList
    {
        get => _capturedMediaList;
        set => SetProperty(ref _capturedMediaList, value);
    }

    public WriteableBitmap? PreviewBitmap
    {
        get => _previewBitmap;
        set => SetProperty(ref _previewBitmap, value);
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set
        {
            if (SetProperty(ref _isCapturing, value))
            {
                OnPropertyChanged(nameof(IsNotCapturing));
                OnPropertyChanged(nameof(CaptureButtonText));
                OnPropertyChanged(nameof(RecordButtonText));
                OnPropertyChanged(nameof(GradedBadgeVisibility));
                ToggleCaptureCommand.RaiseCanExecuteChanged();
                CaptureSnapshotCommand.RaiseCanExecuteChanged();
                ToggleRecordingCommand.RaiseCanExecuteChanged();
                ExportProcessedSnapshotCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotCapturing => !IsCapturing;
    public string CaptureButtonText => IsCapturing ? "■ Stop Stream" : "▶ Start Stream";

    private RecordingMode _selectedRecordingMode = RecordingMode.OriginalCamera;
    public RecordingMode SelectedRecordingMode
    {
        get => _selectedRecordingMode;
        set
        {
            if (SetProperty(ref _selectedRecordingMode, value))
            {
                _cameraService.SelectedRecordingMode = value;
                OnPropertyChanged(nameof(IsOriginalRecordingSelected));
                OnPropertyChanged(nameof(IsGradedRecordingSelected));
                OnPropertyChanged(nameof(RecordButtonText));
                OnPropertyChanged(nameof(RecordingModeBadgeText));
            }
        }
    }

    private RecordingCadenceMode _selectedCadenceMode = RecordingCadenceMode.Capture_Faithful_VFR;
    public RecordingCadenceMode SelectedCadenceMode
    {
        get => _selectedCadenceMode;
        set
        {
            if (SetProperty(ref _selectedCadenceMode, value))
            {
                _cameraService.SelectedCadenceMode = value;
                OnPropertyChanged(nameof(IsCaptureFaithfulSelected));
                OnPropertyChanged(nameof(IsCfrCompatibilitySelected));
                OnPropertyChanged(nameof(CadenceModeBadgeText));
                OnPropertyChanged(nameof(RecordingModeBadgeText));
            }
        }
    }

    public bool IsCaptureFaithfulSelected
    {
        get => _selectedCadenceMode == RecordingCadenceMode.Capture_Faithful_VFR;
        set
        {
            if (value) SelectedCadenceMode = RecordingCadenceMode.Capture_Faithful_VFR;
        }
    }

    public bool IsCfrCompatibilitySelected
    {
        get => _selectedCadenceMode == RecordingCadenceMode.CFR_Compatibility;
        set
        {
            if (value) SelectedCadenceMode = RecordingCadenceMode.CFR_Compatibility;
        }
    }

    public string CadenceModeBadgeText => _selectedCadenceMode == RecordingCadenceMode.Capture_Faithful_VFR ? "FAITHFUL (VFR)" : "CFR (30 FPS)";

    // Recording Backend Engine Properties (Phase 33 Native Promotion)
    private RecordingBackendMode _selectedRecordingBackend = RecordingBackendMode.MediaFoundation_Default;
    public RecordingBackendMode SelectedRecordingBackend
    {
        get => _selectedRecordingBackend;
        set
        {
            if (SetProperty(ref _selectedRecordingBackend, value))
            {
                _cameraService.SelectedRecordingBackend = value;
                OnPropertyChanged(nameof(IsFfmpegBackendSelected));
                OnPropertyChanged(nameof(IsNativeMfBackendSelected));
                OnPropertyChanged(nameof(RecordingBackendBadgeText));
                OnPropertyChanged(nameof(RecordingModeBadgeText));
                OnPropertyChanged(nameof(RecordButtonText));
            }
        }
    }

    public bool IsFfmpegAvailable => File.Exists(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"));

    public bool IsFfmpegBackendSelected
    {
        get => _selectedRecordingBackend == RecordingBackendMode.FFmpeg_Legacy;
        set
        {
            if (value)
            {
                if (!IsFfmpegAvailable)
                {
                    ShowError("FFmpeg fallback is not available: ffmpeg.exe was not found in the application directory.");
                    return;
                }
                SelectedRecordingBackend = RecordingBackendMode.FFmpeg_Legacy;
            }
        }
    }

    public bool IsNativeMfBackendSelected
    {
        get => _selectedRecordingBackend == RecordingBackendMode.MediaFoundation_Default;
        set
        {
            if (value) SelectedRecordingBackend = RecordingBackendMode.MediaFoundation_Default;
        }
    }

    public string RecordingBackendBadgeText => _selectedRecordingBackend == RecordingBackendMode.MediaFoundation_Default
        ? "NATIVE MF (DEFAULT)"
        : "FFMPEG (FALLBACK)";

    public bool IsOriginalRecordingSelected
    {
        get => _selectedRecordingMode == RecordingMode.OriginalCamera;
        set
        {
            if (value) SelectedRecordingMode = RecordingMode.OriginalCamera;
        }
    }

    public bool IsGradedRecordingSelected
    {
        get => _selectedRecordingMode == RecordingMode.GradedVideo;
        set
        {
            if (value) SelectedRecordingMode = RecordingMode.GradedVideo;
        }
    }

    public string RecordButtonText
    {
        get
        {
            if (IsFinalizing) return "⏳ Finalizing...";
            if (IsRecording) return "■ Stop Record";
            string cadenceStr = SelectedCadenceMode == RecordingCadenceMode.Capture_Faithful_VFR ? "VFR" : "CFR";
            string backendSuffix = SelectedRecordingBackend == RecordingBackendMode.FFmpeg_Legacy ? " [FFmpeg]" : "";
            return SelectedRecordingMode == RecordingMode.GradedVideo
                ? $"● Record Graded ({cadenceStr}){backendSuffix}"
                : $"● Record Original ({cadenceStr}){backendSuffix}";
        }
    }

    // Audio Capture & Synchronization Properties (Phase 16)
    public System.Collections.ObjectModel.ObservableCollection<WebcamLab.Core.Audio.AudioDeviceInfo> AudioDevices { get; } = new();

    private WebcamLab.Core.Audio.AudioDeviceInfo? _selectedAudioDevice;
    public WebcamLab.Core.Audio.AudioDeviceInfo? SelectedAudioDevice
    {
        get => _selectedAudioDevice;
        set
        {
            if (SetProperty(ref _selectedAudioDevice, value))
            {
                OnPropertyChanged(nameof(AudioDeviceSummaryText));
                OnPropertyChanged(nameof(AudioStatusBadgeText));
            }
        }
    }

    private bool _isAudioRecordingEnabled = true;
    public bool IsAudioRecordingEnabled
    {
        get => _isAudioRecordingEnabled;
        set
        {
            if (SetProperty(ref _isAudioRecordingEnabled, value))
            {
                OnPropertyChanged(nameof(AudioStatusBadgeText));
            }
        }
    }

    private float _audioPeakLevel;
    public float AudioPeakLevel
    {
        get => _audioPeakLevel;
        private set => SetProperty(ref _audioPeakLevel, value);
    }

    private float _audioPeakDb = -60f;
    public float AudioPeakDb
    {
        get => _audioPeakDb;
        private set
        {
            if (SetProperty(ref _audioPeakDb, value))
            {
                OnPropertyChanged(nameof(AudioLevelText));
            }
        }
    }

    private float _audioRmsDb = -60f;
    public float AudioRmsDb
    {
        get => _audioRmsDb;
        private set
        {
            if (SetProperty(ref _audioRmsDb, value))
            {
                OnPropertyChanged(nameof(AudioRmsLevelText));
            }
        }
    }

    private double _audioPeakMeterValue = 0.0;
    public double AudioPeakMeterValue
    {
        get => _audioPeakMeterValue;
        private set => SetProperty(ref _audioPeakMeterValue, value);
    }

    private double _audioRmsMeterValue = 0.0;
    public double AudioRmsMeterValue
    {
        get => _audioRmsMeterValue;
        private set => SetProperty(ref _audioRmsMeterValue, value);
    }

    private bool _isAudioClipping;
    public bool IsAudioClipping
    {
        get => _isAudioClipping;
        private set => SetProperty(ref _isAudioClipping, value);
    }

    private int _audioDiscontinuitiesCount;
    public int AudioDiscontinuitiesCount
    {
        get => _audioDiscontinuitiesCount;
        private set => SetProperty(ref _audioDiscontinuitiesCount, value);
    }

    private bool _audioDiscontinuityWarning;
    public bool AudioDiscontinuityWarning
    {
        get => _audioDiscontinuityWarning;
        private set => SetProperty(ref _audioDiscontinuityWarning, value);
    }

    private string _audioFormatSummary = "--";
    public string AudioFormatSummary
    {
        get => _audioFormatSummary;
        private set => SetProperty(ref _audioFormatSummary, value);
    }

    private string _audioStatusDetails = "Standby";
    public string AudioStatusDetails
    {
        get => _audioStatusDetails;
        private set => SetProperty(ref _audioStatusDetails, value);
    }

    public string AudioLevelText => _audioPeakDb > -59.5f ? $"{_audioPeakDb:F1} dBFS" : "-∞ dBFS";
    public string AudioRmsLevelText => _audioRmsDb > -59.5f ? $"{_audioRmsDb:F1} dBFS" : "-∞ dBFS";

    public string AudioDeviceSummaryText => SelectedAudioDevice != null
        ? $"{SelectedAudioDevice.FriendlyName} [{SelectedAudioDevice.FormatSummary}]"
        : "No audio device selected";

    public string AudioStatusBadgeText => IsAudioRecordingEnabled
        ? (SelectedAudioDevice != null ? (SelectedAudioDevice.IsDellUZ2315H ? "DELL MIC (SYNC)" : "MIC ACTIVE") : "NO DEVICE")
        : "MIC MUTED";

    public string RecordingModeBadgeText
    {
        get
        {
            string cadence = SelectedCadenceMode == RecordingCadenceMode.Capture_Faithful_VFR ? "VFR" : "CFR";
            string backendTag = SelectedRecordingBackend == RecordingBackendMode.FFmpeg_Legacy ? " [FFMPEG]" : " [NATIVE MF]";
            if (SelectedRecordingMode == RecordingMode.GradedVideo)
            {
                var look = string.IsNullOrWhiteSpace(_cameraService.LookSettings.LookName) ? "Graded" : _cameraService.LookSettings.LookName;
                return $"GRADED: {look.ToUpperInvariant()} [{cadence}]{backendTag}";
            }
            return $"ORIGINAL [{cadence}]{backendTag}";
        }
    }

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (SetProperty(ref _isRecording, value))
            {
                OnPropertyChanged(nameof(RecordButtonText));
                ToggleRecordingCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _isFinalizing;
    public bool IsFinalizing
    {
        get => _isFinalizing;
        set
        {
            if (SetProperty(ref _isFinalizing, value))
            {
                OnPropertyChanged(nameof(RecordButtonText));
                ToggleRecordingCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    // Recording Telemetry Properties
    private bool _isTelemetryExpanded;
    public bool IsTelemetryExpanded
    {
        get => _isTelemetryExpanded;
        set => SetProperty(ref _isTelemetryExpanded, value);
    }

    private string _telemetryLiveFps = "-- FPS";
    public string TelemetryLiveFps
    {
        get => _telemetryLiveFps;
        set => SetProperty(ref _telemetryLiveFps, value);
    }

    private string _telemetryTargetFps = "-- FPS";
    public string TelemetryTargetFps
    {
        get => _telemetryTargetFps;
        set => SetProperty(ref _telemetryTargetFps, value);
    }

    private string _telemetryCapturedFrames = "--";
    public string TelemetryCapturedFrames
    {
        get => _telemetryCapturedFrames;
        set => SetProperty(ref _telemetryCapturedFrames, value);
    }

    private string _telemetryEncodedFrames = "--";
    public string TelemetryEncodedFrames
    {
        get => _telemetryEncodedFrames;
        set => SetProperty(ref _telemetryEncodedFrames, value);
    }

    private string _telemetryPaddedDuplicates = "--";
    public string TelemetryPaddedDuplicates
    {
        get => _telemetryPaddedDuplicates;
        set => SetProperty(ref _telemetryPaddedDuplicates, value);
    }

    private string _telemetryDroppedFrames = "--";
    public string TelemetryDroppedFrames
    {
        get => _telemetryDroppedFrames;
        set => SetProperty(ref _telemetryDroppedFrames, value);
    }

    private string _telemetryAudioState = "Standby";
    public string TelemetryAudioState
    {
        get => _telemetryAudioState;
        set => SetProperty(ref _telemetryAudioState, value);
    }

    private string _telemetryAudioDiscontinuities = "--";
    public string TelemetryAudioDiscontinuities
    {
        get => _telemetryAudioDiscontinuities;
        set => SetProperty(ref _telemetryAudioDiscontinuities, value);
    }

    private string _telemetryActiveLook = "Neutral Bypass";
    public string TelemetryActiveLook
    {
        get => _telemetryActiveLook;
        set => SetProperty(ref _telemetryActiveLook, value);
    }

    private string _telemetryLutStatus = "None";
    public string TelemetryLutStatus
    {
        get => _telemetryLutStatus;
        set => SetProperty(ref _telemetryLutStatus, value);
    }

    private string _telemetryRealtekState = "--";
    public string TelemetryRealtekState
    {
        get => _telemetryRealtekState;
        set => SetProperty(ref _telemetryRealtekState, value);
    }

    private string _telemetryAvSyncOffset = "N/A (Standby)";
    public string TelemetryAvSyncOffset
    {
        get => _telemetryAvSyncOffset;
        set => SetProperty(ref _telemetryAvSyncOffset, value);
    }

    private string _telemetryCadenceMode = "--";
    public string TelemetryCadenceMode
    {
        get => _telemetryCadenceMode;
        set => SetProperty(ref _telemetryCadenceMode, value);
    }

    private string _telemetryRecordingMode = "--";
    public string TelemetryRecordingMode
    {
        get => _telemetryRecordingMode;
        set => SetProperty(ref _telemetryRecordingMode, value);
    }

    private string _telemetryDuration = "00:00:00.00";
    public string TelemetryDuration
    {
        get => _telemetryDuration;
        set => SetProperty(ref _telemetryDuration, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ActiveResolution
    {
        get => _activeResolution;
        set => SetProperty(ref _activeResolution, value);
    }

    public string ActivePixelFormat
    {
        get => _activePixelFormat;
        set => SetProperty(ref _activePixelFormat, value);
    }

    public string CurrentFps
    {
        get => _currentFps;
        set => SetProperty(ref _currentFps, value);
    }

    public string TargetFps
    {
        get => _targetFps;
        set => SetProperty(ref _targetFps, value);
    }

    public long TotalFrames
    {
        get => _totalFrames;
        set => SetProperty(ref _totalFrames, value);
    }

    public long DroppedFrames
    {
        get => _droppedFrames;
        set => SetProperty(ref _droppedFrames, value);
    }

    public string DataRateMBps
    {
        get => _dataRateMBps;
        set => SetProperty(ref _dataRateMBps, value);
    }

    public string RecordingDuration
    {
        get => _recordingDuration;
        set => SetProperty(ref _recordingDuration, value);
    }

    public bool IsDellDetected
    {
        get => _isDellDetected;
        set => SetProperty(ref _isDellDetected, value);
    }

    public string DellBadgeText
    {
        get => _dellBadgeText;
        set => SetProperty(ref _dellBadgeText, value);
    }

    public ObservableCollection<CameraHardwareProperty> SupportedControls
    {
        get => _supportedControls;
        set => SetProperty(ref _supportedControls, value);
    }

    public ObservableCollection<CameraHardwareProperty> UnsupportedControls
    {
        get => _unsupportedControls;
        set => SetProperty(ref _unsupportedControls, value);
    }

    public WriteableBitmap HistogramBitmap => _histogramRenderer.Bitmap;

    public bool ShowRgbHistogram
    {
        get => _showRgbHistogram;
        set => SetProperty(ref _showRgbHistogram, value);
    }

    public bool EnableZebra
    {
        get => _enableZebra;
        set
        {
            if (SetProperty(ref _enableZebra, value))
            {
                _cameraService.EnableZebra = value;
            }
        }
    }

    public int ZebraThreshold
    {
        get => _zebraThreshold;
        set
        {
            if (SetProperty(ref _zebraThreshold, value))
            {
                _cameraService.ZebraThreshold = value;
            }
        }
    }

    public bool IsFullRange
    {
        get => _isFullRange;
        set
        {
            if (SetProperty(ref _isFullRange, value))
            {
                _cameraService.IsFullRange = value;
                RangeStandardText = value ? "Full-Range [0..255]" : "Video-Range [16..235]";
                OnPropertyChanged(nameof(HighlightThresholdHeader));
                OnPropertyChanged(nameof(ShadowCrushThresholdHeader));
            }
        }
    }

    public string HighlightThresholdHeader => IsFullRange ? "DECODED HIGHLIGHT CLIP (≥ 254)" : "PEAK REACHED (≥ 235)";
    public string ShadowCrushThresholdHeader => IsFullRange ? "DECODED SHADOW CRUSH (≤ 1)" : "FLOOR REACHED (≤ 16)";
    public string ActiveNominalRangeText => SelectedFormat?.NominalRangeText ?? "--";

    // Phase 15: Experimental Realtek ISP Processing Control Properties
    public bool IsRealtekProcessingSupported => _cameraService.RealtekIsp.IsSupported;
    public bool IsRealtekFormatCompatible => _cameraService.RealtekIsp.IsFormatCompatible;
    public string RealtekStatusDescription => _cameraService.RealtekIsp.StatusDescription;
    public string RealtekFormatNote => _cameraService.RealtekIsp.FormatCompatibilityNote;
    public string RealtekReadBackText => _cameraService.RealtekIsp.CurrentReadBackValue.HasValue
        ? $"0x{_cameraService.RealtekIsp.CurrentReadBackValue.Value:X2}"
        : "--";

    public bool IsRealtekStandardModeSelected
    {
        get => _isRealtekStandardModeSelected;
        set
        {
            if (SetProperty(ref _isRealtekStandardModeSelected, value))
            {
                if (value)
                {
                    _isRealtekReducedDetailSelected = false;
                    OnPropertyChanged(nameof(IsRealtekReducedDetailSelected));
                }
            }
        }
    }

    public bool IsRealtekReducedDetailSelected
    {
        get => _isRealtekReducedDetailSelected;
        set
        {
            if (SetProperty(ref _isRealtekReducedDetailSelected, value))
            {
                if (value)
                {
                    _isRealtekStandardModeSelected = false;
                    OnPropertyChanged(nameof(IsRealtekStandardModeSelected));
                }
            }
        }
    }

    public string RealtekFeedbackMessage
    {
        get => _realtekFeedbackMessage;
        set => SetProperty(ref _realtekFeedbackMessage, value);
    }

    public bool HasRealtekFeedback
    {
        get => _hasRealtekFeedback;
        set => SetProperty(ref _hasRealtekFeedback, value);
    }

    public RelayCommand ApplyRealtekProcessingCommand { get; }
    public RelayCommand RestoreRealtekBaselineCommand { get; }

    public string HighlightClipText
    {
        get => _highlightClipText;
        set => SetProperty(ref _highlightClipText, value);
    }

    public string ShadowCrushText
    {
        get => _shadowCrushText;
        set => SetProperty(ref _shadowCrushText, value);
    }

    public string MeanLumaText
    {
        get => _meanLumaText;
        set => SetProperty(ref _meanLumaText, value);
    }

    public string ObservedRangeText
    {
        get => _observedRangeText;
        set => SetProperty(ref _observedRangeText, value);
    }

    public string BroadcastSuperWhiteText
    {
        get => _broadcastSuperWhiteText;
        set => SetProperty(ref _broadcastSuperWhiteText, value);
    }

    public string BroadcastSubBlackText
    {
        get => _broadcastSubBlackText;
        set => SetProperty(ref _broadcastSubBlackText, value);
    }

    public string RangeStandardText
    {
        get => _rangeStandardText;
        set => SetProperty(ref _rangeStandardText, value);
    }

    public ObservableCollection<CapturePreset> Presets
    {
        get => _presets;
        set => SetProperty(ref _presets, value);
    }

    public CapturePreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value))
            {
                OnPropertyChanged(nameof(IsUserPresetSelected));
                OnPropertyChanged(nameof(PresetModifiedBadge));
                ApplySelectedPresetCommand?.RaiseCanExecuteChanged();
                DuplicatePresetCommand?.RaiseCanExecuteChanged();
                UpdateUserPresetCommand?.RaiseCanExecuteChanged();
                DeletePresetCommand?.RaiseCanExecuteChanged();
                ExportPresetCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsUserPresetSelected => SelectedPreset != null && !SelectedPreset.IsBuiltIn;

    public string PresetModifiedBadge => (SelectedPreset != null && _presetService.IsPresetModified(SelectedPreset, SupportedControls))
        ? "* (Modified)"
        : string.Empty;

    public string PresetStatusText
    {
        get => _presetStatusText;
        set => SetProperty(ref _presetStatusText, value);
    }

    public string NewPresetName
    {
        get => _newPresetName;
        set => SetProperty(ref _newPresetName, value);
    }

    public string NewPresetDescription
    {
        get => _newPresetDescription;
        set => SetProperty(ref _newPresetDescription, value);
    }

    public string CaptureBackend => "Windows Media Foundation (SourceReader)";

    // ==========================================
    // Phase 7: Non-Destructive Software Look
    // ==========================================

    public bool IsGradedPreviewActive
    {
        get => _isGradedPreviewActive;
        set
        {
            if (SetProperty(ref _isGradedPreviewActive, value))
            {
                _cameraService.IsGradedPreviewActive = value;
                OnPropertyChanged(nameof(ViewportModeBadge));
                OnPropertyChanged(nameof(ViewportModeBrush));
                OnPropertyChanged(nameof(ViewportModeTooltip));
            }
        }
    }

    public string ViewportModeBadge => IsGradedPreviewActive
        ? "[VIEWING: GRADED PREVIEW]"
        : "[VIEWING: ORIGINAL CAMERA FEED]";

    public Brush ViewportModeBrush => IsGradedPreviewActive
        ? new SolidColorBrush(Color.FromRgb(245, 158, 11)) // Amber / Gold
        : new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue / Native

    public string ViewportModeTooltip => IsGradedPreviewActive
        ? "Viewing real-time software graded look. The original camera feed remains 100% unaltered."
        : "Viewing untouched, pristine camera feed directly from Media Foundation source.";

    public Visibility GradedBadgeVisibility => IsCapturing ? Visibility.Visible : Visibility.Collapsed;

    public double SoftwareExposureOffset
    {
        get => _softwareExposureOffset;
        set
        {
            if (SetProperty(ref _softwareExposureOffset, Math.Round(value, 2)))
            {
                SyncSoftwareLookSettings();
            }
        }
    }

    public double SoftwareContrast
    {
        get => _softwareContrast;
        set
        {
            if (SetProperty(ref _softwareContrast, Math.Round(value, 2)))
            {
                SyncSoftwareLookSettings();
            }
        }
    }

    public double SoftwareSaturation
    {
        get => _softwareSaturation;
        set
        {
            if (SetProperty(ref _softwareSaturation, Math.Round(value, 2)))
            {
                SyncSoftwareLookSettings();
                OnPropertyChanged(nameof(SaturationDisplayLabel));
            }
        }
    }

    public string SaturationDisplayLabel => _softwareSaturation <= 0.001
        ? "0.00 (Software Monochrome)"
        : $"{_softwareSaturation:F2}x";

    public double SoftwareHighlights
    {
        get => _softwareHighlights;
        set
        {
            if (SetProperty(ref _softwareHighlights, Math.Round(value, 1)))
            {
                SyncSoftwareLookSettings();
            }
        }
    }

    public double SoftwareShadows
    {
        get => _softwareShadows;
        set
        {
            if (SetProperty(ref _softwareShadows, Math.Round(value, 1)))
            {
                SyncSoftwareLookSettings();
            }
        }
    }

    public double SoftwareSCurveStrength
    {
        get => _softwareSCurveStrength;
        set
        {
            if (SetProperty(ref _softwareSCurveStrength, Math.Round(value, 2)))
            {
                SyncSoftwareLookSettings();
            }
        }
    }

    public double SoftwareSharpenStrength
    {
        get => _softwareSharpenStrength;
        set
        {
            if (SetProperty(ref _softwareSharpenStrength, Math.Round(value, 2)))
            {
                SyncSoftwareLookSettings();
            }
        }
    }

    public double SoftwareDenoiseStrength
    {
        get => _softwareDenoiseStrength;
        set
        {
            if (SetProperty(ref _softwareDenoiseStrength, Math.Round(value, 2)))
            {
                SyncSoftwareLookSettings();
            }
        }
    }

    public string ActiveLutTitle
    {
        get => _activeLutTitle;
        set => SetProperty(ref _activeLutTitle, value);
    }

    public bool IsLutLoaded
    {
        get => _isLutLoaded;
        set
        {
            if (SetProperty(ref _isLutLoaded, value))
            {
                ClearLutCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsLutEnabled
    {
        get => _isLutEnabled;
        set
        {
            if (SetProperty(ref _isLutEnabled, value))
            {
                SyncSoftwareLookSettings();
            }
        }
    }

    public ObservableCollection<StarterLookItem> StarterLooks
    {
        get => _starterLooks;
        set => SetProperty(ref _starterLooks, value);
    }

    public StarterLookItem? SelectedStarterLook
    {
        get => _selectedStarterLook;
        set
        {
            if (SetProperty(ref _selectedStarterLook, value) && value != null)
            {
                ApplySoftwareLook(value.Settings);
            }
        }
    }

    // ==========================================
    // Phase 22: Cinema Look Manager
    // ==========================================
    private readonly CinemaLookService _lookService = new();
    private ObservableCollection<CinemaLook> _looks = [];
    public ObservableCollection<CinemaLook> Looks
    {
        get => _looks;
        set => SetProperty(ref _looks, value);
    }

    private CinemaLook? _selectedLook;
    public CinemaLook? SelectedLook
    {
        get => _selectedLook;
        set
        {
            if (SetProperty(ref _selectedLook, value))
            {
                OnPropertyChanged(nameof(IsUserLookSelected));
                if (value != null)
                {
                    NewLookName = value.Name;
                    SelectedLookShortcut = value.ShortcutKey ?? string.Empty;
                }
                ApplySelectedLookCommand?.RaiseCanExecuteChanged();
                DuplicateLookCommand?.RaiseCanExecuteChanged();
                RenameLookCommand?.RaiseCanExecuteChanged();
                DeleteLookCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    private CinemaLook? _activeLook;
    public CinemaLook? ActiveLook
    {
        get => _activeLook;
        set
        {
            if (SetProperty(ref _activeLook, value))
            {
                OnPropertyChanged(nameof(ActiveLookNameText));
                OnPropertyChanged(nameof(RecordingModeBadgeText));
                OnPropertyChanged(nameof(ActiveLookBadgeText));
            }
        }
    }

    public string ActiveLookNameText => ActiveLook?.Name ?? "Neutral Bypass";
    public string ActiveLookBadgeText => ActiveLook != null
        ? $"{ActiveLook.Name.ToUpperInvariant()}{(!string.IsNullOrEmpty(ActiveLook.ShortcutKey) ? $" [{ActiveLook.ShortcutKey}]" : "")}"
        : "NEUTRAL BYPASS";

    public bool IsUserLookSelected => SelectedLook != null && !SelectedLook.IsBuiltIn;

    private string _newLookName = string.Empty;
    public string NewLookName
    {
        get => _newLookName;
        set => SetProperty(ref _newLookName, value);
    }

    private string _newLookDescription = string.Empty;
    public string NewLookDescription
    {
        get => _newLookDescription;
        set => SetProperty(ref _newLookDescription, value);
    }

    private string _selectedLookShortcut = string.Empty;
    public string SelectedLookShortcut
    {
        get => _selectedLookShortcut;
        set => SetProperty(ref _selectedLookShortcut, value);
    }

    private string _lookStatusText = "Select or create a Cinema Look to apply.";
    public string LookStatusText
    {
        get => _lookStatusText;
        set => SetProperty(ref _lookStatusText, value);
    }

    private string _lutValidationWarning = string.Empty;
    public string LutValidationWarning
    {
        get => _lutValidationWarning;
        set
        {
            if (SetProperty(ref _lutValidationWarning, value))
            {
                HasLutWarning = !string.IsNullOrEmpty(value);
            }
        }
    }

    private bool _hasLutWarning;
    public bool HasLutWarning
    {
        get => _hasLutWarning;
        set => SetProperty(ref _hasLutWarning, value);
    }

    public RelayCommand ApplySelectedLookCommand { get; }
    public RelayCommand SaveCurrentAsLookCommand { get; }
    public RelayCommand DuplicateLookCommand { get; }
    public RelayCommand RenameLookCommand { get; }
    public RelayCommand DeleteLookCommand { get; }
    public RelayCommand ResetToNeutralLookCommand { get; }
    public RelayCommand ToggleTelemetryPanelCommand { get; }

    // ==========================================
    // Phase 23: Multi-Frame Still Enhancement
    // ==========================================
    private bool _isMultiFrameEnhancementEnabled;
    public bool IsMultiFrameEnhancementEnabled
    {
        get => _isMultiFrameEnhancementEnabled;
        set
        {
            if (SetProperty(ref _isMultiFrameEnhancementEnabled, value))
            {
                OnPropertyChanged(nameof(SnapshotButtonText));
            }
        }
    }

    public int[] BurstSizes { get; } = [4, 8, 12];

    private int _selectedBurstSize = 8;
    public int SelectedBurstSize
    {
        get => _selectedBurstSize;
        set
        {
            if (SetProperty(ref _selectedBurstSize, value))
            {
                OnPropertyChanged(nameof(SnapshotButtonText));
            }
        }
    }

    private bool _isBurstCapturing;
    public bool IsBurstCapturing
    {
        get => _isBurstCapturing;
        private set
        {
            if (SetProperty(ref _isBurstCapturing, value))
            {
                OnPropertyChanged(nameof(SnapshotButtonText));
                CaptureSnapshotCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _burstProgressText = string.Empty;
    public string BurstProgressText
    {
        get => _burstProgressText;
        set => SetProperty(ref _burstProgressText, value);
    }

    private string? _lastOriginalStillPath;
    public string? LastOriginalStillPath
    {
        get => _lastOriginalStillPath;
        set
        {
            if (SetProperty(ref _lastOriginalStillPath, value))
            {
                OnPropertyChanged(nameof(HasOriginalStill));
            }
        }
    }

    private string? _lastEnhancedStillPath;
    public string? LastEnhancedStillPath
    {
        get => _lastEnhancedStillPath;
        set
        {
            if (SetProperty(ref _lastEnhancedStillPath, value))
            {
                OnPropertyChanged(nameof(HasEnhancedComparison));
            }
        }
    }

    private string? _lastSidecarJsonPath;
    public string? LastSidecarJsonPath
    {
        get => _lastSidecarJsonPath;
        set => SetProperty(ref _lastSidecarJsonPath, value);
    }

    public bool HasOriginalStill => !string.IsNullOrEmpty(_lastOriginalStillPath) && File.Exists(_lastOriginalStillPath);
    public bool HasEnhancedComparison => !string.IsNullOrEmpty(_lastEnhancedStillPath) && File.Exists(_lastEnhancedStillPath);

    private string _enhancementSummaryText = string.Empty;
    public string EnhancementSummaryText
    {
        get => _enhancementSummaryText;
        set => SetProperty(ref _enhancementSummaryText, value);
    }

    public string SnapshotButtonText
    {
        get
        {
            if (IsBurstCapturing) return "Capturing Burst...";
            if (IsMultiFrameEnhancementEnabled) return $"📸 Multi-Frame ({SelectedBurstSize}f)";
            return "📸 Take Snapshot";
        }
    }

    public RelayCommand OpenOriginalStillCommand { get; }
    public RelayCommand OpenEnhancedStillCommand { get; }
    public RelayCommand OpenSidecarJsonCommand { get; }

    // ==========================================
    // Phase 8: False-Color Exposure Scopes
    // ==========================================

    public bool EnableFalseColor
    {
        get => _enableFalseColor;
        set
        {
            if (SetProperty(ref _enableFalseColor, value))
            {
                _cameraService.FalseColorSettings.Enabled = value;
                _cameraService.UpdateFalseColorSettings(_cameraService.FalseColorSettings);
                OnPropertyChanged(nameof(FalseColorBadgeVisibility));
                OnPropertyChanged(nameof(FalseColorBadgeText));
                OnPropertyChanged(nameof(FalseColorBadgeBrush));
            }
        }
    }

    public FalseColorProfile SelectedFalseColorProfile
    {
        get => _selectedFalseColorProfile;
        set
        {
            if (SetProperty(ref _selectedFalseColorProfile, value))
            {
                _cameraService.FalseColorSettings.SelectedProfile = value;
                _cameraService.UpdateFalseColorSettings(_cameraService.FalseColorSettings);
                RefreshLegendZones();
                OnPropertyChanged(nameof(FalseColorBadgeText));
                OnPropertyChanged(nameof(IsCustomProfileSelected));
                OnPropertyChanged(nameof(IsArriProfileSelected));
            }
        }
    }

    public bool IsCustomProfileSelected
    {
        get => _selectedFalseColorProfile == FalseColorProfile.WebcamLabCustom10Zone;
        set
        {
            if (value) SelectedFalseColorProfile = FalseColorProfile.WebcamLabCustom10Zone;
        }
    }

    public bool IsArriProfileSelected
    {
        get => _selectedFalseColorProfile == FalseColorProfile.ArriStyleReference;
        set
        {
            if (value) SelectedFalseColorProfile = FalseColorProfile.ArriStyleReference;
        }
    }

    public AnalysisSource AnalysisSource
    {
        get => _analysisSource;
        set
        {
            if (SetProperty(ref _analysisSource, value))
            {
                _cameraService.FalseColorSettings.AnalysisSource = value;
                _cameraService.UpdateFalseColorSettings(_cameraService.FalseColorSettings);
                OnPropertyChanged(nameof(AnalysisSourceText));
                OnPropertyChanged(nameof(IsAnalysisSourceOriginal));
                OnPropertyChanged(nameof(IsAnalysisSourceGraded));
                OnPropertyChanged(nameof(AnalysisSourceWarningVisibility));
                OnPropertyChanged(nameof(FalseColorBadgeText));
                OnPropertyChanged(nameof(FalseColorBadgeBrush));
            }
        }
    }

    public bool IsAnalysisSourceOriginal
    {
        get => _analysisSource == AnalysisSource.OriginalCameraFeed;
        set
        {
            if (value) AnalysisSource = AnalysisSource.OriginalCameraFeed;
        }
    }

    public bool IsAnalysisSourceGraded
    {
        get => _analysisSource == AnalysisSource.GradedPreview;
        set
        {
            if (value) AnalysisSource = AnalysisSource.GradedPreview;
        }
    }

    public ArriIntermediateMode ArriIntermediateMode
    {
        get => _arriIntermediateMode;
        set
        {
            if (SetProperty(ref _arriIntermediateMode, value))
            {
                _cameraService.FalseColorSettings.ArriIntermediateMode = value;
                _cameraService.UpdateFalseColorSettings(_cameraService.FalseColorSettings);
                OnPropertyChanged(nameof(IsArriGrayscaleIntermediate));
                OnPropertyChanged(nameof(IsArriPassThroughIntermediate));
            }
        }
    }

    public bool IsArriGrayscaleIntermediate
    {
        get => _arriIntermediateMode == ArriIntermediateMode.DesaturatedGrayscale;
        set
        {
            if (value) ArriIntermediateMode = ArriIntermediateMode.DesaturatedGrayscale;
        }
    }

    public bool IsArriPassThroughIntermediate
    {
        get => _arriIntermediateMode == ArriIntermediateMode.PassThroughColor;
        set
        {
            if (value) ArriIntermediateMode = ArriIntermediateMode.PassThroughColor;
        }
    }

    public double FalseColorOpacity
    {
        get => _falseColorOpacity;
        set
        {
            if (SetProperty(ref _falseColorOpacity, Math.Round(value, 0)))
            {
                _cameraService.FalseColorSettings.OverlayOpacity = _falseColorOpacity / 100.0;
                _cameraService.UpdateFalseColorSettings(_cameraService.FalseColorSettings);
                OnPropertyChanged(nameof(FalseColorOpacityText));
            }
        }
    }

    public string FalseColorOpacityText => $"{_falseColorOpacity:F0}%";

    public double SkinToneTarget
    {
        get => _skinToneTarget;
        set
        {
            if (SetProperty(ref _skinToneTarget, Math.Round(value, 1)))
            {
                _cameraService.FalseColorSettings.SkinToneTarget = _skinToneTarget;
                _cameraService.UpdateFalseColorSettings(_cameraService.FalseColorSettings);
                OnPropertyChanged(nameof(SkinToneTargetRangeText));
            }
        }
    }

    public double SkinToneTolerance
    {
        get => _skinToneTolerance;
        set
        {
            if (SetProperty(ref _skinToneTolerance, Math.Round(value, 1)))
            {
                _cameraService.FalseColorSettings.SkinToneTolerance = _skinToneTolerance;
                _cameraService.UpdateFalseColorSettings(_cameraService.FalseColorSettings);
                OnPropertyChanged(nameof(SkinToneTargetRangeText));
            }
        }
    }

    public double MiddleGrayTarget
    {
        get => _middleGrayTarget;
        set
        {
            if (SetProperty(ref _middleGrayTarget, Math.Round(value, 1)))
            {
                _cameraService.FalseColorSettings.MiddleGrayTarget = _middleGrayTarget;
                _cameraService.UpdateFalseColorSettings(_cameraService.FalseColorSettings);
                OnPropertyChanged(nameof(MiddleGrayTargetRangeText));
            }
        }
    }

    public double MiddleGrayTolerance
    {
        get => _middleGrayTolerance;
        set
        {
            if (SetProperty(ref _middleGrayTolerance, Math.Round(value, 1)))
            {
                _cameraService.FalseColorSettings.MiddleGrayTolerance = _middleGrayTolerance;
                _cameraService.UpdateFalseColorSettings(_cameraService.FalseColorSettings);
                OnPropertyChanged(nameof(MiddleGrayTargetRangeText));
            }
        }
    }

    public ObservableCollection<FalseColorZoneViewModel> CustomZones { get; } = [];
    public ObservableCollection<FalseColorZoneViewModel> ActiveLegendZones { get; } = [];

    public string? ZoneValidationError
    {
        get => _zoneValidationError;
        set
        {
            if (SetProperty(ref _zoneValidationError, value))
            {
                OnPropertyChanged(nameof(IsZoneValid));
                OnPropertyChanged(nameof(ZoneValidationErrorVisibility));
            }
        }
    }

    public bool IsZoneValid => string.IsNullOrEmpty(_zoneValidationError);
    public Visibility ZoneValidationErrorVisibility => !string.IsNullOrEmpty(_zoneValidationError) ? Visibility.Visible : Visibility.Collapsed;

    public string SkinToneTargetRangeText => $"Skin Target: {SkinToneTarget - SkinToneTolerance:F1} – {SkinToneTarget + SkinToneTolerance:F1} IRE ({SkinToneTarget:F1} ± {SkinToneTolerance:F1})";
    public string MiddleGrayTargetRangeText => $"Mid-Gray: {MiddleGrayTarget - MiddleGrayTolerance:F1} – {MiddleGrayTarget + MiddleGrayTolerance:F1} IRE ({MiddleGrayTarget:F1} ± {MiddleGrayTolerance:F1})";

    public Visibility FalseColorBadgeVisibility => EnableFalseColor ? Visibility.Visible : Visibility.Collapsed;

    public string FalseColorBadgeText
    {
        get
        {
            string prof = SelectedFalseColorProfile == FalseColorProfile.ArriStyleReference ? "ARRI REFERENCE" : "10-ZONE CUSTOM";
            string src = AnalysisSource == AnalysisSource.OriginalCameraFeed ? "SRC: ORIGINAL CAMERA" : "SRC: GRADED PREVIEW";
            return $"[FALSE COLOR: {prof} | {src}]";
        }
    }

    public Brush FalseColorBadgeBrush => AnalysisSource == AnalysisSource.OriginalCameraFeed
        ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) // Emerald Green
        : new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber Warning

    public string AnalysisSourceText => AnalysisSource == AnalysisSource.OriginalCameraFeed
        ? "Original Camera Feed (Pre-Look / Default)"
        : "Graded Preview (Post-Look)";

    public Visibility AnalysisSourceWarningVisibility => AnalysisSource == AnalysisSource.GradedPreview ? Visibility.Visible : Visibility.Collapsed;
    public string AnalysisSourceWarningText => "⚠️ Analyzing Graded Preview: Metrics and False Color reflect post-LUT/curve styling, not pristine camera exposure. For objective exposure assistance, switch to 'Original Camera Feed'.";

    public RelayCommand ToggleGradedPreviewCommand { get; }
    public RelayCommand ResetSoftwareLookCommand { get; }
    public RelayCommand OpenLutFileCommand { get; }
    public RelayCommand ClearLutCommand { get; }
    public RelayCommand ExportProcessedSnapshotCommand { get; }

    // Phase 8 False Color Commands
    public RelayCommand ToggleFalseColorCommand { get; }
    public RelayCommand ResetFalseColorDefaultsCommand { get; }
    public RelayCommand RestoreDefaultZonesCommand { get; }
    public RelayCommand ToggleAnalysisSourceCommand { get; }

    public RelayCommand RefreshCamerasCommand { get; }
    public RelayCommand RefreshAudioDevicesCommand { get; }
    public RelayCommand ToggleCaptureCommand { get; }
    public RelayCommand CaptureSnapshotCommand { get; }
    public RelayCommand ToggleRecordingCommand { get; }
    public RelayCommand OpenCapturesFolderCommand { get; }
    public RelayCommand OpenRecordingsFolderCommand { get; }
    public RelayCommand OpenStillsFolderCommand { get; }
    public RelayCommand DismissErrorCommand { get; }
    public RelayCommand ResetAllControlsCommand { get; }
    public RelayCommand ResetPropertyCommand { get; }
    public RelayCommand RestoreInitialSettingsCommand { get; }
    public RelayCommand RestoreInitialPropertyCommand { get; }

    public RelayCommand ApplySelectedPresetCommand { get; }
    public RelayCommand SaveCurrentAsPresetCommand { get; }
    public RelayCommand DuplicatePresetCommand { get; }
    public RelayCommand UpdateUserPresetCommand { get; }
    public RelayCommand DeletePresetCommand { get; }
    public RelayCommand ExportPresetCommand { get; }
    public RelayCommand ImportPresetCommand { get; }
    public RelayCommand RestoreBaselineSettingsCommand { get; }

    public MainViewModel() : this(null)
    {
    }

    public MainViewModel(ICameraService? cameraService)
    {
        _cameraService = cameraService ?? new MediaFoundationCameraService();
        _presetService = new PresetService();
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _cameraService.FrameReady += OnFrameReady;
        _cameraService.HistogramReady += OnHistogramReady;
        _cameraService.StatisticsUpdated += OnStatisticsUpdated;
        _cameraService.ErrorOccurred += OnErrorOccurred;

        LoadPresets();

        RefreshCamerasCommand = new RelayCommand(async () => await RefreshCamerasAsync());
        RefreshAudioDevicesCommand = new RelayCommand(async () => await RefreshAudioDevicesAsync());
        ToggleCaptureCommand = new RelayCommand(async () => await ToggleCaptureAsync(), () => SelectedCamera != null && SelectedFormat != null);
        CaptureSnapshotCommand = new RelayCommand(async () => await CaptureSnapshotAsync(), () => IsCapturing && !IsBurstCapturing);
        ToggleRecordingCommand = new RelayCommand(async () => await ToggleRecordingAsync(), () => IsCapturing);
        OpenCapturesFolderCommand = new RelayCommand(OpenCapturesFolder);
        OpenRecordingsFolderCommand = new RelayCommand(OpenRecordingsFolder);
        OpenStillsFolderCommand = new RelayCommand(OpenStillsFolder);
        DismissErrorCommand = new RelayCommand(() => HasError = false);
        ResetAllControlsCommand = new RelayCommand(ResetAllHardwareControls);
        RestoreInitialSettingsCommand = new RelayCommand(RestoreInitialHardwareControls);
        ResetPropertyCommand = new RelayCommand(param =>
        {
            if (param is CameraHardwareProperty prop)
            {
                ResetHardwareProperty(prop);
            }
        });
        RestoreInitialPropertyCommand = new RelayCommand(param =>
        {
            if (param is CameraHardwareProperty prop)
            {
                RestoreInitialHardwareProperty(prop);
            }
        });

        ApplySelectedPresetCommand = new RelayCommand(ApplySelectedPreset, () => SelectedPreset != null);
        SaveCurrentAsPresetCommand = new RelayCommand(SaveCurrentAsPreset, () => SupportedControls.Count > 0);
        DuplicatePresetCommand = new RelayCommand(DuplicatePreset, () => SelectedPreset != null);
        UpdateUserPresetCommand = new RelayCommand(UpdateUserPreset, () => IsUserPresetSelected);
        DeletePresetCommand = new RelayCommand(DeletePreset, () => IsUserPresetSelected);
        ExportPresetCommand = new RelayCommand(ExportPreset, () => SelectedPreset != null);
        ImportPresetCommand = new RelayCommand(ImportPreset);
        RestoreBaselineSettingsCommand = new RelayCommand(RestoreBaselineSettings, () => SupportedControls.Count > 0);

        ToggleGradedPreviewCommand = new RelayCommand(() => IsGradedPreviewActive = !IsGradedPreviewActive);
        ResetSoftwareLookCommand = new RelayCommand(ResetSoftwareLook);
        OpenLutFileCommand = new RelayCommand(OpenLutFile);
        ClearLutCommand = new RelayCommand(ClearLut, () => IsLutLoaded);
        ExportProcessedSnapshotCommand = new RelayCommand(async () => await ExportProcessedSnapshotAsync(), () => IsCapturing);

        ToggleFalseColorCommand = new RelayCommand(() => EnableFalseColor = !EnableFalseColor);
        ResetFalseColorDefaultsCommand = new RelayCommand(ResetFalseColorDefaults);
        RestoreDefaultZonesCommand = new RelayCommand(RestoreDefaultZones);
        ToggleAnalysisSourceCommand = new RelayCommand(() => AnalysisSource = (AnalysisSource == AnalysisSource.OriginalCameraFeed ? AnalysisSource.GradedPreview : AnalysisSource.OriginalCameraFeed));

        ApplyRealtekProcessingCommand = new RelayCommand(ApplyRealtekProcessing, () => IsRealtekProcessingSupported && IsRealtekFormatCompatible);
        RestoreRealtekBaselineCommand = new RelayCommand(RestoreRealtekBaseline, () => IsRealtekProcessingSupported);
        _cameraService.RealtekIsp.StateChanged += OnRealtekIspStateChanged;

        // Phase 22: Cinema Look Manager Commands
        ApplySelectedLookCommand = new RelayCommand(() => { if (SelectedLook != null) ApplyCinemaLook(SelectedLook); }, () => SelectedLook != null);
        SaveCurrentAsLookCommand = new RelayCommand(SaveCurrentAsLook);
        DuplicateLookCommand = new RelayCommand(DuplicateLook, () => SelectedLook != null);
        RenameLookCommand = new RelayCommand(RenameLook, () => IsUserLookSelected);
        DeleteLookCommand = new RelayCommand(DeleteLook, () => IsUserLookSelected);
        ResetToNeutralLookCommand = new RelayCommand(ResetToNeutralLook);
        ToggleTelemetryPanelCommand = new RelayCommand(() => IsTelemetryExpanded = !IsTelemetryExpanded);

        // Phase 23: Multi-Frame Still Enhancement Commands & Events
        _cameraService.BurstProgressUpdated += (current, total) =>
        {
            _dispatcher.Invoke(() =>
            {
                var text = $"Capturing burst frame {current}/{total}...";
                BurstProgressText = text;
                StatusMessage = text;
            });
        };

        OpenOriginalStillCommand = new RelayCommand(() =>
        {
            if (HasOriginalStill)
            {
                Process.Start(new ProcessStartInfo { FileName = LastOriginalStillPath!, UseShellExecute = true });
            }
        }, () => HasOriginalStill);

        OpenEnhancedStillCommand = new RelayCommand(() =>
        {
            if (HasEnhancedComparison)
            {
                Process.Start(new ProcessStartInfo { FileName = LastEnhancedStillPath!, UseShellExecute = true });
            }
        }, () => HasEnhancedComparison);

        OpenSidecarJsonCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrEmpty(LastSidecarJsonPath) && File.Exists(LastSidecarJsonPath))
            {
                Process.Start(new ProcessStartInfo { FileName = LastSidecarJsonPath!, UseShellExecute = true });
            }
        }, () => !string.IsNullOrEmpty(LastSidecarJsonPath) && File.Exists(LastSidecarJsonPath));

        var defaultZones = _cameraService.FalseColorSettings.CustomZones ?? FalseColorSettings.CreateDefault10Zones();
        foreach (var z in defaultZones)
        {
            CustomZones.Add(new FalseColorZoneViewModel(z.Clone(), OnZoneEdited));
        }
        RefreshLegendZones();

        InitializeStarterLooks();
        LoadLooks();

        // 25 Hz bounded telemetry and audio monitoring timer (decoupled from WASAPI thread)
        _uiTelemetryTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(40) };
        _uiTelemetryTimer.Tick += OnTelemetryTimerTick;
        _uiTelemetryTimer.Start();

        _ = RefreshCamerasAsync();
        _ = RefreshAudioDevicesAsync();
    }

    public async Task RefreshAudioDevicesAsync()
    {
        try
        {
            var devs = await WebcamLab.Core.Audio.AudioDeviceService.EnumerateCaptureDevicesAsync();
            _dispatcher.Invoke(() =>
            {
                AudioDevices.Clear();
                foreach (var d in devs)
                {
                    AudioDevices.Add(d);
                }

                // Prefer Dell UZ2315H audio device if present, else default, else first
                var preferred = devs.FirstOrDefault(d => d.IsDellUZ2315H) ??
                                devs.FirstOrDefault(d => d.IsDefault) ??
                                devs.FirstOrDefault();
                SelectedAudioDevice = preferred;
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainViewModel] Audio enumeration error: {ex.Message}");
        }
    }

    private void OnTelemetryTimerTick(object? sender, EventArgs e)
    {
        // 1. Audio Level Metering (bounded rate 25 Hz)
        if (_cameraService.AudioCapture.IsCapturing)
        {
            AudioPeakLevel = _cameraService.AudioCapture.PeakLevel;
            AudioPeakDb = _cameraService.AudioCapture.PeakDb;
            AudioRmsDb = _cameraService.AudioCapture.RmsDb;
            IsAudioClipping = _cameraService.AudioCapture.IsClipping;
            AudioPeakMeterValue = Math.Clamp((AudioPeakDb + 60f) / 60f, 0.0, 1.0);
            AudioRmsMeterValue = Math.Clamp((AudioRmsDb + 60f) / 60f, 0.0, 1.0);
            AudioDiscontinuitiesCount = _cameraService.AudioCapture.DiscontinuitiesCount;
            AudioDiscontinuityWarning = AudioDiscontinuitiesCount > 0;
            AudioFormatSummary = _cameraService.AudioCapture.ActiveDeviceInfo?.FormatSummary ?? "--";
            AudioStatusDetails = _cameraService.AudioCapture.EndpointStatus;
        }
        else
        {
            AudioPeakLevel = 0f;
            AudioPeakDb = -60f;
            AudioRmsDb = -60f;
            IsAudioClipping = false;
            AudioPeakMeterValue = 0.0;
            AudioRmsMeterValue = 0.0;
            AudioFormatSummary = SelectedAudioDevice?.FormatSummary ?? "--";
            AudioStatusDetails = IsAudioRecordingEnabled ? "Standby" : "Muted";
        }

        // 2. Recording Telemetry & Diagnostics
        var telemetry = _cameraService.GetRecordingTelemetry();
        IsFinalizing = telemetry.IsFinalizing;
        if (telemetry.IsRecording || telemetry.IsFinalizing)
        {
            TelemetryLiveFps = $"{CurrentFps} FPS (Actual)";
            TelemetryTargetFps = $"{TargetFps} FPS (Nominal)";
            TelemetryCapturedFrames = $"{telemetry.FramesCaptured:N0}";
            TelemetryEncodedFrames = $"{telemetry.FramesEncoded:N0}";
            TelemetryPaddedDuplicates = (telemetry.CadenceMode == RecordingCadenceMode.CFR_Compatibility)
                ? $"{telemetry.PaddedDuplicateFrames:N0} (CFR Grid)"
                : "0 (VFR Mode)";
            TelemetryDroppedFrames = $"{telemetry.DroppedFrames:N0}";
            TelemetryDuration = telemetry.Duration.ToString(@"hh\:mm\:ss\.ff");
            TelemetryAudioState = telemetry.IsAudioActive ? "Active" : "Inactive";
            TelemetryAudioDiscontinuities = $"{telemetry.AudioDiscontinuitiesCount}";
            TelemetryActiveLook = ActiveLook?.Name ?? "Neutral Bypass";
            TelemetryLutStatus = HasLutWarning ? "Bypassed (Missing)" : (IsLutEnabled && IsLutLoaded ? "Active" : "None");
            TelemetryRealtekState = RealtekReadBackText != "--" ? RealtekReadBackText : "N/A (Standard)";
            TelemetryAvSyncOffset = telemetry.IsAudioActive ? $"{telemetry.AudioVideoOffsetMs:+0.0;-0.0;0.0} ms" : "N/A (No Audio)";
            TelemetryCadenceMode = telemetry.CadenceMode == RecordingCadenceMode.Capture_Faithful_VFR ? "VFR (Faithful)" : "CFR (30 FPS)";
            TelemetryRecordingMode = telemetry.Mode == RecordingMode.GradedVideo ? "Graded Video" : "Original Camera";
        }
    }

    public async Task RefreshCamerasAsync()
    {
        try
        {
            StatusMessage = "Enumerating video capture devices via Media Foundation...";
            var devices = await _cameraService.EnumerateCamerasAsync();

            Cameras = new ObservableCollection<CameraDevice>(devices);

            var dell = devices.FirstOrDefault(d => d.IsDellUZ2315H);
            if (dell != null)
            {
                IsDellDetected = true;
                DellBadgeText = "Dell UZ2315H Detected (VID:0BDA, PID:58BA)";
                SelectedCamera = dell;
            }
            else if (devices.Count > 0)
            {
                IsDellDetected = false;
                DellBadgeText = "Generic Camera";
                SelectedCamera = devices[0];
            }
            else
            {
                IsDellDetected = false;
                DellBadgeText = "No Cameras Found";
                StatusMessage = "No camera devices detected on the system.";
            }
        }
        catch (Exception ex)
        {
            ShowError($"Failed to enumerate cameras: {ex.Message}");
        }
    }

    private async void OnCameraSelected(CameraDevice? camera)
    {
        if (camera == null)
        {
            Formats.Clear();
            SelectedFormat = null;
            Capabilities.Clear();
            SupportedControls.Clear();
            UnsupportedControls.Clear();
            return;
        }

        try
        {
            StatusMessage = $"Querying stream formats for {camera.Name}...";
            var formatsList = await _cameraService.GetSupportedFormatsAsync(camera);
            Formats = new ObservableCollection<CameraFormat>(formatsList);

            // Auto-select preferred format: MJPEG 1080p 30 FPS or highest resolution
            var preferred = formatsList.FirstOrDefault(f => f.FourCC == "MJPG" && f.Width == 1920 && f.Height == 1080 && f.FrameRate >= 29)
                         ?? formatsList.FirstOrDefault(f => f.FourCC == "MJPG" && f.Width == 1920 && f.Height == 1080)
                         ?? formatsList.FirstOrDefault(f => f.Width == 1920 && f.Height == 1080)
                         ?? formatsList.FirstOrDefault();

            SelectedFormat = preferred;

            // Generate capability analysis report
            Capabilities = new ObservableCollection<CapabilityItem>(
                CapabilityReportFactory.GenerateReportForDellUZ2315H(formatsList));

            // Query hardware control capabilities
            var props = await _cameraService.Controls.QueryPropertiesAsync(camera);
            SupportedControls = new ObservableCollection<CameraHardwareProperty>(props.Where(p => p.IsSupported));
            UnsupportedControls = new ObservableCollection<CameraHardwareProperty>(props.Where(p => !p.IsSupported));

            foreach (var prop in SupportedControls)
            {
                prop.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(CameraHardwareProperty.CurrentValue) || e.PropertyName == nameof(CameraHardwareProperty.IsAuto))
                    {
                        CommitPropertyChange(prop, prop.CurrentValue, prop.IsAuto);
                    }
                };
            }

            SaveCurrentAsPresetCommand.RaiseCanExecuteChanged();
            RestoreBaselineSettingsCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(PresetModifiedBadge));

            await _cameraService.RealtekIsp.InitializeAsync(camera);
            _cameraService.RealtekIsp.UpdateActiveFormat(preferred);
            ApplyRealtekProcessingCommand.RaiseCanExecuteChanged();
            RestoreRealtekBaselineCommand.RaiseCanExecuteChanged();

            bool shouldAutoStart = Environment.GetCommandLineArgs().Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
            if (shouldAutoStart && preferred != null)
            {
                await _cameraService.StartCaptureAsync(camera, preferred);
                IsCapturing = true;
                TargetFps = $"{preferred.FrameRate:F0} FPS";
                StatusMessage = $"Streaming from {camera.Name} [{preferred.FourCC} {preferred.Width}×{preferred.Height}]";
            }
            else
            {
                StatusMessage = $"Found {formatsList.Count} stream formats for {camera.Name}. Ready to capture.";
            }
        }
        catch (Exception ex)
        {
            ShowError($"Failed to query formats: {ex.Message}");
        }
    }

    private async Task SwitchFormatAsync(CameraDevice camera, CameraFormat format)
    {
        try
        {
            StatusMessage = $"Switching format to {format.DisplayString}...";
            await _cameraService.StartCaptureAsync(camera, format);
            TargetFps = $"{format.FrameRate:F0} FPS";
            StatusMessage = $"Streaming {format.FourCC} {format.Width}×{format.Height} @ {format.FrameRate:F0} FPS";
        }
        catch (Exception ex)
        {
            ShowError($"Failed to switch format: {ex.Message}");
        }
    }

    private async Task ToggleCaptureAsync()
    {
        if (SelectedCamera == null || SelectedFormat == null) return;

        if (IsCapturing)
        {
            try
            {
                StatusMessage = "Stopping capture...";
                await _cameraService.StopCaptureAsync();
                IsCapturing = false;
                StatusMessage = "Capture stopped.";
            }
            catch (Exception ex)
            {
                ShowError($"Failed to stop capture: {ex.Message}");
            }
        }
        else
        {
            try
            {
                StatusMessage = $"Starting stream {SelectedFormat.DisplayString}...";
                await _cameraService.StartCaptureAsync(SelectedCamera, SelectedFormat);
                IsCapturing = true;
                TargetFps = $"{SelectedFormat.FrameRate:F0} FPS";
                StatusMessage = $"Capturing from {SelectedCamera.Name} [{SelectedFormat.FourCC} {SelectedFormat.Width}×{SelectedFormat.Height}]";
            }
            catch (Exception ex)
            {
                ShowError($"Capture start failed: {ex.Message}");
                IsCapturing = false;
            }
        }
    }

    private async Task CaptureSnapshotAsync()
    {
        if (!IsCapturing || IsBurstCapturing) return;

        try
        {
            var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WebcamLab");
            if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);

            if (IsMultiFrameEnhancementEnabled)
            {
                IsBurstCapturing = true;
                BurstProgressText = $"Initiating multi-frame burst ({SelectedBurstSize} frames)...";
                StatusMessage = BurstProgressText;

                var settings = new MultiFrameBurstSettings
                {
                    BurstSize = SelectedBurstSize,
                    Algorithm = MultiFrameFusionAlgorithm.MotionAdaptiveWeightedMean,
                    MotionSensitivity = 1.0f,
                    MaxRegistrationShiftPx = 32.0,
                    MinRegistrationConfidence = 0.60
                };

                var sidecar = await _cameraService.CaptureEnhancedStillAsync(baseDir, settings);

                _dispatcher.Invoke(() =>
                {
                    LastOriginalStillPath = sidecar.OriginalImagePath;
                    LastEnhancedStillPath = sidecar.EnhancedImagePath;
                    LastSidecarJsonPath = sidecar.SidecarPath;

                    if (!string.IsNullOrEmpty(sidecar.EnhancedImagePath) && File.Exists(sidecar.EnhancedImagePath))
                    {
                        CapturedMediaList.Insert(0, sidecar.EnhancedImagePath);
                    }
                    if (!string.IsNullOrEmpty(sidecar.OriginalImagePath) && File.Exists(sidecar.OriginalImagePath))
                    {
                        CapturedMediaList.Insert(1, sidecar.OriginalImagePath);
                    }

                    EnhancementSummaryText = $"Fused {sidecar.FramesAcceptedForFusion}/{sidecar.BurstSizeRequested} frames. Noise: +{sidecar.MeasuredNoiseReductionDb:F1} dB. Edge retention: {sidecar.MeasuredEdgeRetentionRatio * 100:F1}%. Motion: {sidecar.MotionMaskPixelPercentage:F1}%.";
                    StatusMessage = $"Multi-frame still saved: {Path.GetFileName(sidecar.EnhancedImagePath)} (+{sidecar.MeasuredNoiseReductionDb:F1} dB noise reduction)";
                    OnPropertyChanged(nameof(HasEnhancedComparison));
                    OnPropertyChanged(nameof(HasOriginalStill));
                    OpenOriginalStillCommand.RaiseCanExecuteChanged();
                    OpenEnhancedStillCommand.RaiseCanExecuteChanged();
                    OpenSidecarJsonCommand.RaiseCanExecuteChanged();
                });
            }
            else
            {
                // Single-frame baseline snapshot
                string fileExt = (SelectedFormat?.FourCC is "MJPG" or "MJPEG") ? "jpg" : "png";
                StatusMessage = "Capturing still frame...";
                var savedPath = await _cameraService.CaptureStillImageAsync(baseDir, fileExt);

                _dispatcher.Invoke(() =>
                {
                    CapturedMediaList.Insert(0, savedPath);
                    LastOriginalStillPath = savedPath;
                    LastEnhancedStillPath = null;
                    LastSidecarJsonPath = null;
                    EnhancementSummaryText = "Single-frame standard capture (no multi-frame fusion).";
                    StatusMessage = $"Still image saved: {Path.GetFileName(savedPath)}";
                    OnPropertyChanged(nameof(HasEnhancedComparison));
                    OnPropertyChanged(nameof(HasOriginalStill));
                    OpenOriginalStillCommand.RaiseCanExecuteChanged();
                    OpenEnhancedStillCommand.RaiseCanExecuteChanged();
                    OpenSidecarJsonCommand.RaiseCanExecuteChanged();
                });
            }
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() =>
            {
                ShowError($"Snapshot failed: {ex.Message}");
            });
        }
        finally
        {
            _dispatcher.Invoke(() =>
            {
                IsBurstCapturing = false;
                BurstProgressText = string.Empty;
                CaptureSnapshotCommand.RaiseCanExecuteChanged();
            });
        }
    }

    private async Task ToggleRecordingAsync()
    {
        if (!IsCapturing) return;

        if (IsRecording)
        {
            try
            {
                IsFinalizing = true;
                StatusMessage = "Finalizing video recording & audio multiplexing... Please wait.";
                await _cameraService.StopRecordingAsync();
                IsRecording = false;

                // Ensure versioned JSON sidecar report (Schema 3.0) is written for finalized recording
                if (!string.IsNullOrEmpty(_activeRecordingFilePath) && File.Exists(_activeRecordingFilePath))
                {
                    _cameraService.WriteRecordingSidecarV3(_activeRecordingFilePath, SelectedRecordingMode == RecordingMode.OriginalCamera);
                }

                var report = _cameraService.LastSyncReport;
                string syncDetails = (report != null && IsAudioRecordingEnabled)
                    ? $" | A/V Offset: {report.InitialOffsetMs:+0.0;-0.0;0.0} ms, Drift: {report.DurationDriftMs:+0.0;-0.0;0.0} ms"
                    : "";

                long fileSizeBytes = File.Exists(_activeRecordingFilePath) ? new FileInfo(_activeRecordingFilePath!).Length : 0;
                StatusMessage = $"Recording saved ({Path.GetFileName(_activeRecordingFilePath)} - {fileSizeBytes:N0} bytes){syncDetails}. Versioned JSON sidecar generated.";
            }
            catch (Exception ex)
            {
                ShowError($"Failed to finalize recording: {ex.Message}");
            }
            finally
            {
                IsFinalizing = false;
            }
        }
        else
        {
            if (IsFinalizing)
            {
                ShowError("Recording cannot be started: previous session is still finalizing.");
                return;
            }

            try
            {
                // Pre-flight check: If graded recording with LUT is requested, ensure LUT file exists on disk
                if (SelectedRecordingMode == RecordingMode.GradedVideo && _cameraService.LookSettings.LutEnabled)
                {
                    var lutPath = _cameraService.LookSettings.LutFilePath;
                    if (string.IsNullOrWhiteSpace(lutPath) || !File.Exists(lutPath))
                    {
                        var lutName = _cameraService.LookSettings.LutFileName ?? "configured LUT";
                        ShowError($"Cannot start Graded Recording: 3D LUT '{lutName}' was not found on disk ({lutPath}). Recording aborted to prevent fallback substitution.");
                        return;
                    }
                }

                var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "WebcamLab");
                if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string modeTag = SelectedRecordingMode == RecordingMode.GradedVideo ? "Graded" : "Original";
                string cadenceTag = SelectedCadenceMode == RecordingCadenceMode.Capture_Faithful_VFR ? "VFR" : "CFR";
                string audioTag = IsAudioRecordingEnabled ? "AV" : "VideoOnly";
                string backendTag = SelectedRecordingBackend == RecordingBackendMode.MediaFoundation_Default ? "NativeMF" : "FFmpeg";
                string filename = $"WebcamLab_{backendTag}_{modeTag}_{cadenceTag}_{audioTag}_{timestamp}.mp4";
                string fullPath = Path.Combine(baseDir, filename);
                _activeRecordingFilePath = fullPath;

                StatusMessage = $"Recording ({modeTag} [{cadenceTag}] {(IsAudioRecordingEnabled ? "+ Audio" : "")}) to {filename}...";
                await _cameraService.StartRecordingAsync(
                    fullPath,
                    SelectedRecordingMode,
                    SelectedCadenceMode,
                    IsAudioRecordingEnabled,
                    SelectedAudioDevice);
                IsRecording = true;

                _dispatcher.Invoke(() =>
                {
                    CapturedMediaList.Insert(0, fullPath);
                });
            }
            catch (Exception ex)
            {
                ShowError($"Recording failed: {ex.Message}");
                IsRecording = false;
            }
        }
    }

    public void OpenRecordingsFolder()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "WebcamLab");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError($"Could not open recordings folder: {ex.Message}");
        }
    }

    public void OpenStillsFolder()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WebcamLab");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError($"Could not open stills folder: {ex.Message}");
        }
    }

    private void OpenCapturesFolder()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "WebcamLab");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError($"Could not open folder: {ex.Message}");
        }
    }

    private unsafe void OnFrameReady(IntPtr pBgr32, int width, int height, int stride)
    {
        // Prevent Dispatcher backlog: drop UI render frame if previous render is still in flight
        if (Interlocked.CompareExchange(ref _isRenderingFrame, 1, 0) != 0)
        {
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            try
            {
                if (_previewBitmap == null || _previewBitmap.PixelWidth != width || _previewBitmap.PixelHeight != height)
                {
                    PreviewBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                }

                if (_previewBitmap != null)
                {
                    _previewBitmap.Lock();
                    Buffer.MemoryCopy(
                        (void*)pBgr32,
                        (void*)_previewBitmap.BackBuffer,
                        _previewBitmap.BackBufferStride * height,
                        stride * height);
                    _previewBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                    _previewBitmap.Unlock();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isRenderingFrame, 0);
            }
        }));
    }

    private void OnStatisticsUpdated(CaptureStatistics stats)
    {
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            ActiveResolution = stats.ActiveResolution;
            ActivePixelFormat = stats.ActivePixelFormat;
            CurrentFps = $"{stats.CurrentFps:F1}";
            TotalFrames = stats.TotalFrames;
            DroppedFrames = stats.DroppedFrames;
            DataRateMBps = $"{stats.DataRateMBps:F2}";
            if (stats.IsRecording)
            {
                RecordingDuration = stats.RecordingDuration.ToString(@"hh\:mm\:ss");
            }
        }));
    }

    private void OnErrorOccurred(string error)
    {
        ShowError(error);
    }

    private void ShowError(string message)
    {
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            ErrorMessage = message;
            HasError = true;
            StatusMessage = $"Error: {message}";
        }));
    }

    public void CommitPropertyChange(CameraHardwareProperty prop, int value, bool isAuto)
    {
        if (_propertyDebounceTokens.TryGetValue(prop.PropertyId, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _propertyDebounceTokens[prop.PropertyId] = cts;

        Task.Delay(50, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                _cameraService.Controls.SetProperty(prop, value, isAuto);
                _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    OnPropertyChanged(nameof(PresetModifiedBadge));
                }));
            }
        }, TaskScheduler.Default);
    }

    private void ResetAllHardwareControls()
    {
        foreach (var prop in SupportedControls)
        {
            _cameraService.Controls.ResetProperty(prop);
        }
        _cameraService.RealtekIsp.RestoreInitialState();
        StatusMessage = "All supported hardware controls reset to factory defaults.";
        OnPropertyChanged(nameof(PresetModifiedBadge));
    }

    private void RestoreInitialHardwareControls()
    {
        foreach (var prop in SupportedControls)
        {
            _cameraService.Controls.RestoreInitialProperty(prop);
        }
        _cameraService.RealtekIsp.RestoreInitialState();
        RealtekFeedbackMessage = "Realtek ISP state restored to session baseline.";
        HasRealtekFeedback = true;
        StatusMessage = "All hardware controls restored to session initial values.";
        OnPropertyChanged(nameof(PresetModifiedBadge));
    }

    private void ResetHardwareProperty(CameraHardwareProperty prop)
    {
        _cameraService.Controls.ResetProperty(prop);
        StatusMessage = $"{prop.Name} reset to factory default ({prop.DefaultValue}).";
        OnPropertyChanged(nameof(PresetModifiedBadge));
    }

    private void RestoreInitialHardwareProperty(CameraHardwareProperty prop)
    {
        _cameraService.Controls.RestoreInitialProperty(prop);
        StatusMessage = $"{prop.Name} restored to initial value ({prop.InitialValue}).";
        OnPropertyChanged(nameof(PresetModifiedBadge));
    }

    private void LoadPresets()
    {
        var list = new List<CapturePreset>();
        list.AddRange(PresetService.GetBuiltInPresets());
        list.AddRange(_presetService.LoadUserPresets());
        Presets = new ObservableCollection<CapturePreset>(list);
        SelectedPreset = Presets.FirstOrDefault();
    }

    public void ApplySelectedPreset()
    {
        if (SelectedPreset == null) return;
        var result = _presetService.ApplyPreset(SelectedPreset, SupportedControls, _cameraService.Controls);
        if (SelectedPreset.SoftwareLook != null)
        {
            ApplySoftwareLook(SelectedPreset.SoftwareLook);
        }
        if (SelectedPreset.FalseColor != null)
        {
            ApplyFalseColorSettings(SelectedPreset.FalseColor);
        }
        if (SelectedPreset.VendorHardware != null && _cameraService.RealtekIsp.IsSupported)
        {
            var mode = (RealtekProcessingMode)SelectedPreset.VendorHardware.RealtekUnit7Control2Value;
            _cameraService.RealtekIsp.ApplyProcessingMode(mode);
        }
        PresetStatusText = result.Summary;
        StatusMessage = result.Summary;
        OnPropertyChanged(nameof(PresetModifiedBadge));
    }

    public void SaveCurrentAsPreset()
    {
        if (SupportedControls.Count == 0) return;

        string name = string.IsNullOrWhiteSpace(NewPresetName)
            ? $"Custom Look {DateTime.Now:yyyyMMdd_HHmm}"
            : NewPresetName.Trim();

        string desc = string.IsNullOrWhiteSpace(NewPresetDescription)
            ? $"Snapshot of {SupportedControls.Count} active camera controls and software look."
            : NewPresetDescription.Trim();

        var newPreset = _presetService.CreateFromCurrentSettings(name, desc, SupportedControls);
        newPreset.SoftwareLook = _cameraService.LookSettings.Clone();
        newPreset.FalseColor = _cameraService.FalseColorSettings.Clone();

        if (_cameraService.RealtekIsp.IsSupported && _cameraService.RealtekIsp.ActiveMode == RealtekProcessingMode.ReducedDetail)
        {
            newPreset.VendorHardware = new VendorHardwareSettings
            {
                ApplyExperimentalProcessing = true,
                RealtekUnit7Control2Value = (byte)_cameraService.RealtekIsp.ActiveMode
            };
        }

        _presetService.SaveUserPreset(newPreset);

        Presets.Add(newPreset);
        SelectedPreset = newPreset;
        NewPresetName = string.Empty;
        NewPresetDescription = string.Empty;
        PresetStatusText = $"Preset '{newPreset.Name}' created and saved to disk.";
        StatusMessage = PresetStatusText;
        OnPropertyChanged(nameof(PresetModifiedBadge));
    }

    public void DuplicatePreset()
    {
        if (SelectedPreset == null) return;
        var clone = SelectedPreset.Clone();
        _presetService.SaveUserPreset(clone);
        Presets.Add(clone);
        SelectedPreset = clone;
        PresetStatusText = $"Duplicated preset as '{clone.Name}'.";
        StatusMessage = PresetStatusText;
        OnPropertyChanged(nameof(PresetModifiedBadge));
    }

    public void UpdateUserPreset()
    {
        if (SelectedPreset == null || SelectedPreset.IsBuiltIn) return;
        SelectedPreset.HardwareControls.Clear();
        foreach (var p in SupportedControls.Where(p => p.IsSupported))
        {
            SelectedPreset.HardwareControls.Add(new PresetControlValue(p));
        }
        SelectedPreset.SoftwareLook = _cameraService.LookSettings.Clone();
        SelectedPreset.FalseColor = _cameraService.FalseColorSettings.Clone();
        _presetService.SaveUserPreset(SelectedPreset);
        PresetStatusText = $"Updated '{SelectedPreset.Name}' with current hardware and software values.";
        StatusMessage = PresetStatusText;
        OnPropertyChanged(nameof(PresetModifiedBadge));
    }

    public void DeletePreset()
    {
        if (SelectedPreset == null || SelectedPreset.IsBuiltIn) return;
        string name = SelectedPreset.Name;
        _presetService.DeleteUserPreset(SelectedPreset);
        Presets.Remove(SelectedPreset);
        SelectedPreset = Presets.FirstOrDefault();
        PresetStatusText = $"Deleted preset '{name}'.";
        StatusMessage = PresetStatusText;
        OnPropertyChanged(nameof(PresetModifiedBadge));
    }

    public void ExportPreset()
    {
        if (SelectedPreset == null) return;
        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "WebcamLab Preset (*.json)|*.json",
            FileName = $"{SelectedPreset.Name.Replace(" ", "_").Replace("/", "_")}.json",
            Title = $"Export Preset: {SelectedPreset.Name}"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                _presetService.ExportPreset(SelectedPreset, sfd.FileName);
                PresetStatusText = $"Exported '{SelectedPreset.Name}' to {Path.GetFileName(sfd.FileName)}.";
                StatusMessage = PresetStatusText;
            }
            catch (Exception ex)
            {
                ShowError($"Export failed: {ex.Message}");
            }
        }
    }

    public void ImportPreset()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "WebcamLab Preset (*.json)|*.json",
            Title = "Import WebcamLab Preset"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                var imported = _presetService.ImportPreset(ofd.FileName);
                _presetService.SaveUserPreset(imported);
                Presets.Add(imported);
                SelectedPreset = imported;
                PresetStatusText = $"Imported preset '{imported.Name}' successfully.";
                StatusMessage = PresetStatusText;
                OnPropertyChanged(nameof(PresetModifiedBadge));
            }
            catch (Exception ex)
            {
                ShowError($"Import failed: {ex.Message}");
            }
        }
    }

    public void RestoreBaselineSettings()
    {
        foreach (var prop in SupportedControls)
        {
            _cameraService.Controls.RestoreInitialProperty(prop);
        }
        PresetStatusText = "All camera controls restored to connection baseline values.";
        StatusMessage = PresetStatusText;
        OnPropertyChanged(nameof(PresetModifiedBadge));
    }

    // ==========================================
    // Phase 7: Software Look Pipeline Operations
    // ==========================================

    private void InitializeStarterLooks()
    {
        var looks = new List<StarterLookItem>
        {
            new StarterLookItem
            {
                Name = "Neutral Bypass",
                Description = "Untouched linear passthrough without software color grading or filtering.",
                Settings = new SoftwareLookSettings
                {
                    LookName = "Neutral Bypass",
                    ExposureOffset = 0.0,
                    Contrast = 1.0,
                    Saturation = 1.0,
                    Highlights = 0.0,
                    Shadows = 0.0,
                    SCurveStrength = 0.0,
                    SharpenStrength = 0.0,
                    DenoiseStrength = 0.0,
                    LutEnabled = false
                }
            },
            new StarterLookItem
            {
                Name = "Subtle Film Contrast",
                Description = "Gentle film curve with 15% contrast boost, smooth roll-off, and subtle unsharp masking.",
                Settings = new SoftwareLookSettings
                {
                    LookName = "Subtle Film Contrast",
                    ExposureOffset = 0.0,
                    Contrast = 1.15,
                    Saturation = 1.05,
                    Highlights = -5.0,
                    Shadows = -10.0,
                    SCurveStrength = 0.35,
                    SharpenStrength = 0.20,
                    DenoiseStrength = 0.0,
                    LutEnabled = false
                }
            },
            new StarterLookItem
            {
                Name = "Warm Amber Cinema",
                Description = "Golden highlights, warm shadow lift, and rich skin tone warmth using 3D warm film LUT.",
                Settings = new SoftwareLookSettings
                {
                    LookName = "Warm Amber Cinema",
                    ExposureOffset = 0.0,
                    Contrast = 1.10,
                    Saturation = 1.05,
                    Highlights = 0.0,
                    Shadows = -5.0,
                    SCurveStrength = 0.25,
                    SharpenStrength = 0.20,
                    DenoiseStrength = 0.0,
                    LutFileName = "film_warmth.cube",
                    LutEnabled = true
                }
            },
            new StarterLookItem
            {
                Name = "Cool Steel Cinema",
                Description = "Crisp steel cyan shadows and neutral highlights with edge enhancement.",
                Settings = new SoftwareLookSettings
                {
                    LookName = "Cool Steel Cinema",
                    ExposureOffset = 0.0,
                    Contrast = 1.15,
                    Saturation = 0.95,
                    Highlights = 0.0,
                    Shadows = -5.0,
                    SCurveStrength = 0.30,
                    SharpenStrength = 0.25,
                    DenoiseStrength = 0.0,
                    LutFileName = "cool_steel.cube",
                    LutEnabled = true
                }
            },
            new StarterLookItem
            {
                Name = "Punchy Commercial",
                Description = "Vibrant commercial grade with lifted shadows, controlled highlights, and 20% saturation boost.",
                Settings = new SoftwareLookSettings
                {
                    LookName = "Punchy Commercial",
                    ExposureOffset = 0.20,
                    Contrast = 1.25,
                    Saturation = 1.20,
                    Highlights = -10.0,
                    Shadows = 10.0,
                    SCurveStrength = 0.40,
                    SharpenStrength = 0.30,
                    DenoiseStrength = 0.0,
                    LutEnabled = false
                }
            },
            new StarterLookItem
            {
                Name = "Clean Black & White",
                Description = "Classic high-contrast monochrome with deep blacks and sharp micro-contrast.",
                Settings = new SoftwareLookSettings
                {
                    LookName = "Clean Black & White",
                    ExposureOffset = 0.0,
                    Contrast = 1.30,
                    Saturation = 0.0,
                    Highlights = -5.0,
                    Shadows = -15.0,
                    SCurveStrength = 0.50,
                    SharpenStrength = 0.30,
                    DenoiseStrength = 0.0,
                    LutEnabled = false
                }
            }
        };

        StarterLooks = new ObservableCollection<StarterLookItem>(looks);
        _selectedStarterLook = looks[0];
        OnPropertyChanged(nameof(SelectedStarterLook));
    }

    public void ApplySoftwareLook(SoftwareLookSettings look)
    {
        _softwareExposureOffset = look.ExposureOffset;
        _softwareContrast = look.Contrast;
        _softwareSaturation = look.Saturation;
        _softwareHighlights = look.Highlights;
        _softwareShadows = look.Shadows;
        _softwareSCurveStrength = look.SCurveStrength;
        _softwareSharpenStrength = look.SharpenStrength;
        _softwareDenoiseStrength = look.DenoiseStrength;
        _isLutEnabled = look.LutEnabled;

        OnPropertyChanged(nameof(SoftwareExposureOffset));
        OnPropertyChanged(nameof(SoftwareContrast));
        OnPropertyChanged(nameof(SoftwareSaturation));
        OnPropertyChanged(nameof(SaturationDisplayLabel));
        OnPropertyChanged(nameof(SoftwareHighlights));
        OnPropertyChanged(nameof(SoftwareShadows));
        OnPropertyChanged(nameof(SoftwareSCurveStrength));
        OnPropertyChanged(nameof(SoftwareSharpenStrength));
        OnPropertyChanged(nameof(SoftwareDenoiseStrength));
        OnPropertyChanged(nameof(IsLutEnabled));

        if (!string.IsNullOrEmpty(look.LutFilePath) && File.Exists(look.LutFilePath))
        {
            LoadLutFromFile(look.LutFilePath);
        }
        else if (!string.IsNullOrEmpty(look.LutFileName))
        {
            string candidate = Path.Combine(_presetService.LutStorageDirectory, look.LutFileName);
            if (File.Exists(candidate))
            {
                LoadLutFromFile(candidate);
            }
            else
            {
                string projectCandidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Presets", "LUTs", look.LutFileName);
                if (File.Exists(projectCandidate))
                {
                    LoadLutFromFile(Path.GetFullPath(projectCandidate));
                }
                else
                {
                    ClearLut();
                }
            }
        }
        else
        {
            ClearLut();
        }

        SyncSoftwareLookSettings();
    }

    private void SyncSoftwareLookSettings()
    {
        var s = _cameraService.LookSettings;
        s.ExposureOffset = _softwareExposureOffset;
        s.Contrast = _softwareContrast;
        s.Saturation = _softwareSaturation;
        s.Highlights = _softwareHighlights;
        s.Shadows = _softwareShadows;
        s.SCurveStrength = _softwareSCurveStrength;
        s.SharpenStrength = _softwareSharpenStrength;
        s.DenoiseStrength = _softwareDenoiseStrength;
        s.LutEnabled = _isLutEnabled;
        _cameraService.IsGradedPreviewActive = _isGradedPreviewActive;
        OnPropertyChanged(nameof(ViewportModeBadge));
        OnPropertyChanged(nameof(RecordingModeBadgeText));
    }

    public void OpenLutFile()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Cube LUT (*.cube)|*.cube|All Files (*.*)|*.*",
            Title = "Load .cube Look Up Table",
            InitialDirectory = _presetService.LutStorageDirectory
        };

        if (ofd.ShowDialog() == true)
        {
            LoadLutFromFile(ofd.FileName);
        }
    }

    public void LoadLutFromFile(string filePath)
    {
        try
        {
            var lut = CubeLut.LoadFromFile(filePath);
            _cameraService.ActiveLut = lut;
            _cameraService.LookSettings.LutFilePath = filePath;
            _cameraService.LookSettings.LutFileName = Path.GetFileName(filePath);
            _cameraService.LookSettings.LutEnabled = true;

            ActiveLutTitle = $"{lut.Title} ({lut.Type} {lut.Size}³)";
            IsLutLoaded = true;
            IsLutEnabled = true;
            OnPropertyChanged(nameof(IsLutEnabled));
            StatusMessage = $"Loaded LUT: {Path.GetFileName(filePath)} ({lut.Title})";
        }
        catch (Exception ex)
        {
            ShowError($"Failed to parse .cube LUT: {ex.Message}");
        }
    }

    public void ClearLut()
    {
        _cameraService.ActiveLut = null;
        _cameraService.LookSettings.LutFilePath = null;
        _cameraService.LookSettings.LutFileName = null;
        _cameraService.LookSettings.LutEnabled = false;
        ActiveLutTitle = "None (Neutral)";
        IsLutLoaded = false;
        IsLutEnabled = false;
        OnPropertyChanged(nameof(IsLutEnabled));
        StatusMessage = "LUT cleared. Reverted to neutral.";
    }

    public void ResetSoftwareLook()
    {
        _softwareExposureOffset = 0.0;
        _softwareContrast = 1.0;
        _softwareSaturation = 1.0;
        _softwareHighlights = 0.0;
        _softwareShadows = 0.0;
        _softwareSCurveStrength = 0.0;
        _softwareSharpenStrength = 0.0;
        _softwareDenoiseStrength = 0.0;
        ClearLut();

        OnPropertyChanged(nameof(SoftwareExposureOffset));
        OnPropertyChanged(nameof(SoftwareContrast));
        OnPropertyChanged(nameof(SoftwareSaturation));
        OnPropertyChanged(nameof(SaturationDisplayLabel));
        OnPropertyChanged(nameof(SoftwareHighlights));
        OnPropertyChanged(nameof(SoftwareShadows));
        OnPropertyChanged(nameof(SoftwareSCurveStrength));
        OnPropertyChanged(nameof(SoftwareSharpenStrength));
        OnPropertyChanged(nameof(SoftwareDenoiseStrength));

        SyncSoftwareLookSettings();
        StatusMessage = "Software look reset to neutral bypass.";
    }

    // ==========================================
    // Phase 22: Cinema Look Management Methods
    // ==========================================

    public void LoadLooks()
    {
        var all = _lookService.GetAllLooks();
        Looks = new ObservableCollection<CinemaLook>(all);
        SelectedLook = Looks.FirstOrDefault();
        ActiveLook = Looks.FirstOrDefault();

        // Keep StarterLooks backwards-compatible
        var starterList = all.Select(l => new StarterLookItem
        {
            Name = l.Name,
            Description = l.Description,
            Settings = l.Settings
        }).ToList();
        StarterLooks = new ObservableCollection<StarterLookItem>(starterList);
        SelectedStarterLook = StarterLooks.FirstOrDefault();
    }

    public void ApplyCinemaLook(CinemaLook look)
    {
        if (look == null) return;

        // 1. Resolve and validate LUT asset without silent substitution
        var (lutStatus, resolvedPath, message) = _lookService.ResolveAndValidateLut(look.Settings);
        if (lutStatus == LutStatus.Missing || lutStatus == LutStatus.Incompatible)
        {
            LutValidationWarning = message;
            HasLutWarning = true;
            // Bypass LUT in pipeline without crashing or substituting
            _cameraService.ActiveLut = null;
            _cameraService.LookSettings.LutEnabled = false;
            ActiveLutTitle = $"Bypassed ({look.Settings.LutFileName ?? "missing"})";
            IsLutLoaded = false;
            IsLutEnabled = false;
        }
        else if (lutStatus == LutStatus.Ready && !string.IsNullOrEmpty(resolvedPath))
        {
            LutValidationWarning = string.Empty;
            HasLutWarning = false;
            LoadLutFromFile(resolvedPath);
        }
        else
        {
            LutValidationWarning = string.Empty;
            HasLutWarning = false;
            ClearLut();
        }

        // 2. Apply software tone adjustments (keeping hardware controls completely untouched!)
        _softwareExposureOffset = look.Settings.ExposureOffset;
        _softwareContrast = look.Settings.Contrast;
        _softwareSaturation = look.Settings.Saturation;
        _softwareHighlights = look.Settings.Highlights;
        _softwareShadows = look.Settings.Shadows;
        _softwareSCurveStrength = look.Settings.SCurveStrength;
        _softwareSharpenStrength = look.Settings.SharpenStrength;
        _softwareDenoiseStrength = look.Settings.DenoiseStrength;
        _isLutEnabled = (lutStatus == LutStatus.Ready);

        OnPropertyChanged(nameof(SoftwareExposureOffset));
        OnPropertyChanged(nameof(SoftwareContrast));
        OnPropertyChanged(nameof(SoftwareSaturation));
        OnPropertyChanged(nameof(SaturationDisplayLabel));
        OnPropertyChanged(nameof(SoftwareHighlights));
        OnPropertyChanged(nameof(SoftwareShadows));
        OnPropertyChanged(nameof(SoftwareSCurveStrength));
        OnPropertyChanged(nameof(SoftwareSharpenStrength));
        OnPropertyChanged(nameof(SoftwareDenoiseStrength));
        OnPropertyChanged(nameof(IsLutEnabled));

        SyncSoftwareLookSettings();
        ActiveLook = look;
        string shortcutTag = !string.IsNullOrEmpty(look.ShortcutKey) ? $" [{look.ShortcutKey}]" : "";
        LookStatusText = $"Applied Look: '{look.Name}'{shortcutTag}.";
    }

    public void ResetToNeutralLook()
    {
        _softwareExposureOffset = 0.0;
        _softwareContrast = 1.0;
        _softwareSaturation = 1.0;
        _softwareHighlights = 0.0;
        _softwareShadows = 0.0;
        _softwareSCurveStrength = 0.0;
        _softwareSharpenStrength = 0.0;
        _softwareDenoiseStrength = 0.0;
        _isLutEnabled = false;

        OnPropertyChanged(nameof(SoftwareExposureOffset));
        OnPropertyChanged(nameof(SoftwareContrast));
        OnPropertyChanged(nameof(SoftwareSaturation));
        OnPropertyChanged(nameof(SaturationDisplayLabel));
        OnPropertyChanged(nameof(SoftwareHighlights));
        OnPropertyChanged(nameof(SoftwareShadows));
        OnPropertyChanged(nameof(SoftwareSCurveStrength));
        OnPropertyChanged(nameof(SoftwareSharpenStrength));
        OnPropertyChanged(nameof(SoftwareDenoiseStrength));
        OnPropertyChanged(nameof(IsLutEnabled));

        ClearLut();
        SyncSoftwareLookSettings();

        var neutral = Looks.FirstOrDefault(l => l.Id == "look-neutral-bypass") ?? Looks.FirstOrDefault();
        if (neutral != null)
        {
            ActiveLook = neutral;
            SelectedLook = neutral;
        }

        LutValidationWarning = string.Empty;
        HasLutWarning = false;
        LookStatusText = "Reset to Neutral Bypass (Linear Passthrough).";
    }

    public void SaveCurrentAsLook()
    {
        string name = string.IsNullOrWhiteSpace(NewLookName)
            ? $"Custom Look {Looks.Count(l => !l.IsBuiltIn) + 1}"
            : NewLookName.Trim();

        string? shortcut = string.IsNullOrWhiteSpace(SelectedLookShortcut)
            ? null
            : SelectedLookShortcut.Trim();

        if (!string.IsNullOrEmpty(shortcut))
        {
            if (!_lookService.ValidateShortcut(shortcut, null, Looks, out string conflictReason))
            {
                ShowError($"Invalid shortcut: {conflictReason}");
                return;
            }
        }

        var newLook = new CinemaLook
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Description = string.IsNullOrWhiteSpace(NewLookDescription) ? "Custom user-defined look profile." : NewLookDescription.Trim(),
            IsBuiltIn = false,
            ShortcutKey = shortcut,
            Settings = new SoftwareLookSettings
            {
                LookName = name,
                ExposureOffset = _softwareExposureOffset,
                Contrast = _softwareContrast,
                Saturation = _softwareSaturation,
                Highlights = _softwareHighlights,
                Shadows = _softwareShadows,
                SCurveStrength = _softwareSCurveStrength,
                SharpenStrength = _softwareSharpenStrength,
                DenoiseStrength = _softwareDenoiseStrength,
                LutEnabled = _isLutEnabled && IsLutLoaded,
                LutFilePath = _cameraService.LookSettings.LutFilePath,
                LutFileName = _cameraService.LookSettings.LutFileName
            }
        };

        _lookService.SaveUserLook(newLook);
        LoadLooks();
        SelectedLook = Looks.FirstOrDefault(l => l.Id == newLook.Id);
        ActiveLook = SelectedLook;
        LookStatusText = $"Saved new look: '{newLook.Name}'.";
    }

    public void DuplicateLook()
    {
        if (SelectedLook == null) return;
        var clone = _lookService.DuplicateLook(SelectedLook);
        LoadLooks();
        SelectedLook = Looks.FirstOrDefault(l => l.Id == clone.Id);
        LookStatusText = $"Duplicated look: '{clone.Name}'.";
    }

    public void RenameLook()
    {
        if (SelectedLook == null || SelectedLook.IsBuiltIn) return;
        if (string.IsNullOrWhiteSpace(NewLookName))
        {
            ShowError("Please enter a valid look name.");
            return;
        }

        _lookService.RenameUserLook(SelectedLook, NewLookName.Trim());
        LoadLooks();
        SelectedLook = Looks.FirstOrDefault(l => l.Name == NewLookName.Trim());
        LookStatusText = $"Renamed look to '{NewLookName.Trim()}'.";
    }

    public void DeleteLook()
    {
        if (SelectedLook == null || SelectedLook.IsBuiltIn) return;
        string deletedName = SelectedLook.Name;
        _lookService.DeleteUserLook(SelectedLook);
        LoadLooks();
        ResetToNeutralLook();
        LookStatusText = $"Deleted look: '{deletedName}'.";
    }

    public bool RecallLookByShortcut(string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return false;
        var match = Looks.FirstOrDefault(l => string.Equals(l.ShortcutKey, shortcut, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            SelectedLook = match;
            ApplyCinemaLook(match);
            return true;
        }
        return false;
    }

    private async Task ExportProcessedSnapshotAsync()
    {
        if (!IsCapturing) return;

        try
        {
            var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WebcamLab");
            if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);

            StatusMessage = "Exporting graded still snapshot (lossless PNG)...";
            var savedPath = await _cameraService.ExportProcessedSnapshotAsync(baseDir, _cameraService.LookSettings.Clone());

            _dispatcher.Invoke(() =>
            {
                CapturedMediaList.Insert(0, savedPath);
                StatusMessage = $"Graded snapshot saved: {Path.GetFileName(savedPath)} (+ companion JSON)";
            });
        }
        catch (Exception ex)
        {
            ShowError($"Graded snapshot export failed: {ex.Message}");
        }
    }

    // ==========================================
    // Phase 8 False-Color Helper Methods
    // ==========================================

    public void ApplyFalseColorSettings(FalseColorSettings fc)
    {
        _enableFalseColor = fc.Enabled;
        _selectedFalseColorProfile = fc.SelectedProfile;
        _analysisSource = fc.AnalysisSource;
        _arriIntermediateMode = fc.ArriIntermediateMode;
        _falseColorOpacity = fc.OverlayOpacity * 100.0;
        _skinToneTarget = fc.SkinToneTarget;
        _skinToneTolerance = fc.SkinToneTolerance;
        _middleGrayTarget = fc.MiddleGrayTarget;
        _middleGrayTolerance = fc.MiddleGrayTolerance;

        CustomZones.Clear();
        var zones = fc.CustomZones ?? FalseColorSettings.CreateDefault10Zones();
        foreach (var z in zones)
        {
            CustomZones.Add(new FalseColorZoneViewModel(z.Clone(), OnZoneEdited));
        }

        _cameraService.FalseColorSettings = fc.Clone();
        _cameraService.UpdateFalseColorSettings(_cameraService.FalseColorSettings);

        RefreshLegendZones();
        NotifyFalseColorProperties();
    }

    private void OnZoneEdited()
    {
        var zoneModels = CustomZones.Select(z => z.Model).ToList();
        if (FalseColorSettings.ValidateCustomZones(zoneModels, out string? error))
        {
            ZoneValidationError = null;
            _cameraService.FalseColorSettings.CustomZones = zoneModels;
            _cameraService.UpdateFalseColorSettings(_cameraService.FalseColorSettings);
            RefreshLegendZones();
        }
        else
        {
            ZoneValidationError = error;
        }
    }

    public void RestoreDefaultZones()
    {
        var defaultZones = FalseColorSettings.CreateDefault10Zones();
        CustomZones.Clear();
        foreach (var z in defaultZones)
        {
            CustomZones.Add(new FalseColorZoneViewModel(z, OnZoneEdited));
        }
        ZoneValidationError = null;
        _cameraService.FalseColorSettings.CustomZones = defaultZones;
        _cameraService.UpdateFalseColorSettings(_cameraService.FalseColorSettings);
        RefreshLegendZones();
    }

    public void ResetFalseColorDefaults()
    {
        var defaults = new FalseColorSettings();
        ApplyFalseColorSettings(defaults);
    }

    public void RefreshLegendZones()
    {
        ActiveLegendZones.Clear();
        if (_selectedFalseColorProfile == FalseColorProfile.ArriStyleReference)
        {
            var arriBands = FalseColorSettings.CreateArriReferenceZones();
            foreach (var b in arriBands)
            {
                ActiveLegendZones.Add(new FalseColorZoneViewModel(b));
            }
        }
        else
        {
            foreach (var z in CustomZones)
            {
                ActiveLegendZones.Add(new FalseColorZoneViewModel(z.Model));
            }
        }
    }

    private void NotifyFalseColorProperties()
    {
        OnPropertyChanged(nameof(EnableFalseColor));
        OnPropertyChanged(nameof(SelectedFalseColorProfile));
        OnPropertyChanged(nameof(IsCustomProfileSelected));
        OnPropertyChanged(nameof(IsArriProfileSelected));
        OnPropertyChanged(nameof(AnalysisSource));
        OnPropertyChanged(nameof(IsAnalysisSourceOriginal));
        OnPropertyChanged(nameof(IsAnalysisSourceGraded));
        OnPropertyChanged(nameof(ArriIntermediateMode));
        OnPropertyChanged(nameof(IsArriGrayscaleIntermediate));
        OnPropertyChanged(nameof(IsArriPassThroughIntermediate));
        OnPropertyChanged(nameof(FalseColorOpacity));
        OnPropertyChanged(nameof(FalseColorOpacityText));
        OnPropertyChanged(nameof(SkinToneTarget));
        OnPropertyChanged(nameof(SkinToneTolerance));
        OnPropertyChanged(nameof(SkinToneTargetRangeText));
        OnPropertyChanged(nameof(MiddleGrayTarget));
        OnPropertyChanged(nameof(MiddleGrayTolerance));
        OnPropertyChanged(nameof(MiddleGrayTargetRangeText));
        OnPropertyChanged(nameof(FalseColorBadgeVisibility));
        OnPropertyChanged(nameof(FalseColorBadgeText));
        OnPropertyChanged(nameof(FalseColorBadgeBrush));
        OnPropertyChanged(nameof(AnalysisSourceText));
        OnPropertyChanged(nameof(AnalysisSourceWarningVisibility));
    }

    private void OnHistogramReady(HistogramData data)
    {
        long now = Stopwatch.GetTimestamp();
        // Throttle UI update to ~20 Hz (every 50ms) to ensure 0 UI lag
        if ((now - _lastHistogramRenderTick) * 1000 / Stopwatch.Frequency < 50)
        {
            return;
        }
        _lastHistogramRenderTick = now;

        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            HighlightClipText = $"{data.HighlightClipPercent:F1}%";
            ShadowCrushText = $"{data.ShadowCrushPercent:F1}%";
            MeanLumaText = $"{data.MeanLuma:F1}";
            ObservedRangeText = $"{data.ObservedMinLuma} .. {data.ObservedMaxLuma}";
            BroadcastSuperWhiteText = $"{data.BroadcastSuperWhitePercent:F1}%";
            BroadcastSubBlackText = $"{data.BroadcastSubBlackPercent:F1}%";
            _histogramRenderer.Render(data, ShowRgbHistogram);
        }));
    }

    private void ApplyRealtekProcessing()
    {
        var mode = IsRealtekReducedDetailSelected
            ? RealtekProcessingMode.ReducedDetail
            : RealtekProcessingMode.Standard;

        var (success, msg) = _cameraService.RealtekIsp.ApplyProcessingMode(mode);
        RealtekFeedbackMessage = msg;
        HasRealtekFeedback = true;
        StatusMessage = msg;
    }

    private void RestoreRealtekBaseline()
    {
        var (success, msg) = _cameraService.RealtekIsp.RestoreInitialState();
        RealtekFeedbackMessage = msg;
        HasRealtekFeedback = true;
        StatusMessage = msg;
        IsRealtekStandardModeSelected = true;
    }

    private void OnRealtekIspStateChanged()
    {
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            OnPropertyChanged(nameof(IsRealtekProcessingSupported));
            OnPropertyChanged(nameof(IsRealtekFormatCompatible));
            OnPropertyChanged(nameof(RealtekStatusDescription));
            OnPropertyChanged(nameof(RealtekFormatNote));
            OnPropertyChanged(nameof(RealtekReadBackText));
            ApplyRealtekProcessingCommand.RaiseCanExecuteChanged();
            RestoreRealtekBaselineCommand.RaiseCanExecuteChanged();

            if (_cameraService.RealtekIsp.ActiveMode == RealtekProcessingMode.ReducedDetail)
            {
                _isRealtekReducedDetailSelected = true;
                _isRealtekStandardModeSelected = false;
            }
            else
            {
                _isRealtekStandardModeSelected = true;
                _isRealtekReducedDetailSelected = false;
            }
            OnPropertyChanged(nameof(IsRealtekStandardModeSelected));
            OnPropertyChanged(nameof(IsRealtekReducedDetailSelected));
        }));
    }

    public void Dispose()
    {
        _uiTelemetryTimer?.Stop();
        try
        {
            _cameraService.RealtekIsp.RestoreInitialState();
        }
        catch { }
        _cameraService.Dispose();
        GC.SuppressFinalize(this);
    }
}
