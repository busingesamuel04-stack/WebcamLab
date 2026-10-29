using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WebcamLab.Core.Models;
using WebcamLab.Core.ReverseEngineering;
using static WebcamLab.Core.ReverseEngineering.DirectShowTopologyService;

namespace WebcamLab.Capture;

public enum RealtekProcessingMode
{
    Standard = 0x01,        // Standard Realtek processing (baseline default, edge shaping / spatial sharpening)
    ReducedDetail = 0x00    // Reduced edge/detail processing [Experimental]
}

public class RealtekIspProcessingService : IDisposable
{
    private static readonly Guid Unit7Guid = new("0fb885c3-68c2-4547-90f7-8f47579d95fc");
    private const uint Control2Id = 2;
    private const uint Node4Id = 4; // Node 4 maps to Unit 7 in DirectShow KS topology

    private readonly object _lock = new();
    private IKsControl? _ksControl;
    private CameraDevice? _activeDevice;
    private CameraFormat? _activeFormat;

    private byte? _initialBaselineValue;
    private byte? _currentReadBackValue;
    private RealtekProcessingMode _activeMode = RealtekProcessingMode.Standard;
    private bool _isSupported;
    private bool _isFormatCompatible;
    private bool _isDegraded;
    private string? _lastError;

    public bool IsSupported
    {
        get { lock (_lock) return _isSupported; }
        private set { lock (_lock) _isSupported = value; OnStateChanged(); }
    }

    public bool IsFormatCompatible
    {
        get { lock (_lock) return _isFormatCompatible; }
        private set { lock (_lock) _isFormatCompatible = value; OnStateChanged(); }
    }

    public RealtekProcessingMode ActiveMode
    {
        get { lock (_lock) return _activeMode; }
        private set { lock (_lock) _activeMode = value; OnStateChanged(); }
    }

    public byte? InitialBaselineValue
    {
        get { lock (_lock) return _initialBaselineValue; }
    }

    public byte? CurrentReadBackValue
    {
        get { lock (_lock) return _currentReadBackValue; }
        private set { lock (_lock) _currentReadBackValue = value; OnStateChanged(); }
    }

    public bool IsDegraded
    {
        get { lock (_lock) return _isDegraded; }
        private set { lock (_lock) _isDegraded = value; OnStateChanged(); }
    }

    public string? LastError
    {
        get { lock (_lock) return _lastError; }
        private set { lock (_lock) _lastError = value; OnStateChanged(); }
    }

    public string StatusDescription
    {
        get
        {
            lock (_lock)
            {
                if (!_isSupported)
                {
                    return "Unavailable (Connected camera does not expose Realtek Unit 7 extension controls)";
                }

                if (!_isFormatCompatible)
                {
                    return $"Inactive for {_activeFormat?.FourCC ?? "current format"} (Experimental effect only observed in uncompressed YUY2)";
                }

                if (_isDegraded)
                {
                    return $"Warning: State mismatch or write error (Last Error: {_lastError})";
                }

                string modeStr = _activeMode == RealtekProcessingMode.Standard
                    ? "Standard Realtek processing (0x01)"
                    : "Reduced edge/detail processing (0x00) [Experimental]";

                return $"Active: {modeStr} [Read-back: 0x{(_currentReadBackValue ?? 0):X2}]";
            }
        }
    }

    public string FormatCompatibilityNote
    {
        get
        {
            lock (_lock)
            {
                if (_activeFormat == null) return "No format selected.";
                if (_activeFormat.FourCC == "YUY2")
                {
                    return "1080p YUY2 Uncompressed: Active. Mutating Control 2 modulates high-frequency edge energy and temporal noise.";
                }
                return $"{_activeFormat.FourCC} Compressed: Inactive. Hardware compression suppresses measurable differential effect.";
            }
        }
    }

    public event Action? StateChanged;

    private void OnStateChanged()
    {
        try { StateChanged?.Invoke(); } catch { }
    }

    /// <summary>
    /// Initializes hardware interface for the target camera device.
    /// Strictly non-destructive: reads and stores initial baseline value without mutation.
    /// </summary>
    public Task InitializeAsync(CameraDevice device)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                _activeDevice = device;
                _lastError = null;
                _isDegraded = false;

                // Safety Guard: Only bind to Dell UZ2315H (Realtek RTS5875)
                if (!device.IsDellUZ2315H)
                {
                    _isSupported = false;
                    _ksControl = null;
                    _initialBaselineValue = null;
                    _currentReadBackValue = null;
                    OnStateChanged();
                    return;
                }

                try
                {
                    var (dsReport, filterObj) = DirectShowTopologyService.InspectDellWebcamTopology();
                    _ksControl = (dsReport.KsControlObject ?? filterObj) as IKsControl;

                    if (_ksControl == null)
                    {
                        _isSupported = false;
                        _lastError = "Could not bind IKsControl on DirectShow filter.";
                        OnStateChanged();
                        return;
                    }

                    // Query Unit 7 Control 2 initial state
                    byte? val = ReadControl2Internal(_ksControl);
                    if (val.HasValue)
                    {
                        _initialBaselineValue = val.Value;
                        _currentReadBackValue = val.Value;
                        _activeMode = (val.Value == 0) ? RealtekProcessingMode.ReducedDetail : RealtekProcessingMode.Standard;
                        _isSupported = true;
                    }
                    else
                    {
                        _isSupported = false;
                        _lastError = "Querying Unit 7 Control 2 returned failure.";
                    }
                }
                catch (Exception ex)
                {
                    _isSupported = false;
                    _lastError = ex.Message;
                }

                OnStateChanged();
            }
        });
    }

    /// <summary>
    /// Updates the active streaming format and recalculates format compatibility.
    /// </summary>
    public void UpdateActiveFormat(CameraFormat? format)
    {
        lock (_lock)
        {
            _activeFormat = format;
            _isFormatCompatible = format != null && format.FourCC == "YUY2";
            OnStateChanged();
        }
    }

    /// <summary>
    /// Explicitly applies the requested Realtek processing mode with mandatory read-back verification.
    /// </summary>
    public (bool Success, string Message) ApplyProcessingMode(RealtekProcessingMode requestedMode)
    {
        lock (_lock)
        {
            if (!_isSupported || _ksControl == null)
            {
                return (false, "Realtek ISP control is not supported on the active camera.");
            }

            byte targetByte = (byte)requestedMode;
            IntPtr pBuf = Marshal.AllocHGlobal(4);

            try
            {
                Marshal.WriteByte(pBuf, 0, targetByte);

                var propNode = new KSP_NODE
                {
                    Property = new KSIDENTIFIER
                    {
                        Set = Unit7Guid,
                        Id = Control2Id,
                        Flags = KSPROPERTY_TYPE_SET | KSPROPERTY_TYPE_TOPOLOGY
                    },
                    NodeId = Node4Id,
                    Reserved = 0
                };

                // 1. Perform write
                int hrSet = _ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pBuf, 1, out _);
                if (hrSet != 0)
                {
                    _isDegraded = true;
                    _lastError = $"KSPROPERTY_TYPE_SET returned 0x{hrSet:X8}";
                    OnStateChanged();
                    return (false, $"Write rejected by ISP firmware: 0x{hrSet:X8}");
                }

                // 2. Perform mandatory read-back
                byte? readBack = ReadControl2Internal(_ksControl);
                if (!readBack.HasValue)
                {
                    _isDegraded = true;
                    _lastError = "Read-back request failed after write.";
                    OnStateChanged();
                    return (false, "Write completed but read-back verification failed.");
                }

                CurrentReadBackValue = readBack.Value;

                // 3. Verify read-back matches target
                if (readBack.Value != targetByte)
                {
                    _isDegraded = true;
                    _lastError = $"Read-back value 0x{readBack.Value:X2} does not match requested 0x{targetByte:X2}";
                    OnStateChanged();
                    return (false, $"Verification failed: Read-back (0x{readBack.Value:X2}) != Target (0x{targetByte:X2})");
                }

                _activeMode = requestedMode;
                _isDegraded = false;
                _lastError = null;
                OnStateChanged();

                return (true, $"Successfully applied and verified 0x{targetByte:X2} ({requestedMode}).");
            }
            finally
            {
                Marshal.FreeHGlobal(pBuf);
            }
        }
    }

    /// <summary>
    /// Restores the hardware register to its initial baseline value recorded before any mutations.
    /// </summary>
    public (bool Success, string Message) RestoreInitialState()
    {
        lock (_lock)
        {
            if (!_isSupported || _ksControl == null || !_initialBaselineValue.HasValue)
            {
                return (true, "No baseline restoration required (control was not initialized or modified).");
            }

            byte baseline = _initialBaselineValue.Value;

            // If already at baseline, nothing to do
            if (_currentReadBackValue.HasValue && _currentReadBackValue.Value == baseline)
            {
                return (true, "Hardware register is already at initial baseline value.");
            }

            var result = ApplyProcessingMode((RealtekProcessingMode)baseline);
            if (result.Success)
            {
                _isDegraded = false;
                _lastError = null;
                return (true, $"Hardware state cleanly restored to initial baseline: 0x{baseline:X2}");
            }
            else
            {
                _isDegraded = true;
                _lastError = $"Restoration to baseline 0x{baseline:X2} failed: {result.Message}";
                return (false, $"CRITICAL: Failed to restore baseline value 0x{baseline:X2}: {result.Message}");
            }
        }
    }

    private static byte? ReadControl2Internal(IKsControl ksControl)
    {
        IntPtr pBuf = Marshal.AllocHGlobal(4);
        try
        {
            var propNode = new KSP_NODE
            {
                Property = new KSIDENTIFIER
                {
                    Set = Unit7Guid,
                    Id = Control2Id,
                    Flags = KSPROPERTY_TYPE_GET | KSPROPERTY_TYPE_TOPOLOGY
                },
                NodeId = Node4Id,
                Reserved = 0
            };

            int hr = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pBuf, 1, out uint bytesRet);
            if (hr == 0 && bytesRet >= 1)
            {
                return Marshal.ReadByte(pBuf, 0);
            }
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(pBuf);
        }
    }

    public void Dispose()
    {
        try
        {
            RestoreInitialState();
        }
        catch { }
        finally
        {
            lock (_lock)
            {
                _ksControl = null;
                _isSupported = false;
            }
        }
    }
}
