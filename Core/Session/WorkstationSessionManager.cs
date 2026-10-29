using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WebcamLab.Core.Native;

namespace WebcamLab.Core.Session;

public class SessionInterruptionEvent
{
    public DateTime TimestampUtc { get; set; }
    public double ElapsedSeconds { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Monitors Windows workstation session state (lock/unlock) and manages thread execution state.
/// Explicitly handles Windows Camera Privacy suspension during session lock events without
/// attempting to circumvent OS security policies or fabricating artificial frames.
/// </summary>
public sealed class WorkstationSessionManager : IDisposable
{
    private static readonly Lazy<WorkstationSessionManager> _instance = new(() => new WorkstationSessionManager());
    public static WorkstationSessionManager Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly List<SessionInterruptionEvent> _recordingEvents = [];
    private bool _isSessionLocked;
    private DateTime? _lockStartTimeUtc;
    private double _cumulativeLockDurationSec;
    private long _recordingStartQpc;
    private bool _isRecordingActive;
    private bool _disposed;

    public bool IsSessionLocked
    {
        get { lock (_lock) return _isSessionLocked; }
    }

    public double CumulativeLockDurationSec
    {
        get { lock (_lock) return _cumulativeLockDurationSec; }
    }

    public event EventHandler<SessionInterruptionEvent>? SessionStateChanged;

    private WorkstationSessionManager()
    {
        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WorkstationSessionManager] Failed to subscribe to SystemEvents: {ex.Message}");
        }
    }

    public void StartRecordingSession()
    {
        lock (_lock)
        {
            _recordingEvents.Clear();
            _isRecordingActive = true;
            _recordingStartQpc = Stopwatch.GetTimestamp();
            _cumulativeLockDurationSec = 0.0;
            _lockStartTimeUtc = null;
        }
    }

    public void StopRecordingSession()
    {
        lock (_lock)
        {
            if (_isSessionLocked && _lockStartTimeUtc.HasValue)
            {
                double duration = (DateTime.UtcNow - _lockStartTimeUtc.Value).TotalSeconds;
                _cumulativeLockDurationSec += duration;
            }
            _isRecordingActive = false;
        }
    }

    public IReadOnlyList<SessionInterruptionEvent> GetRecordingEvents()
    {
        lock (_lock)
        {
            return new List<SessionInterruptionEvent>(_recordingEvents);
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        double elapsedSec = 0.0;
        lock (_lock)
        {
            if (_isRecordingActive && _recordingStartQpc > 0)
            {
                elapsedSec = (double)(Stopwatch.GetTimestamp() - _recordingStartQpc) / Stopwatch.Frequency;
            }

            SessionInterruptionEvent evt;
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    _isSessionLocked = true;
                    _lockStartTimeUtc = DateTime.UtcNow;
                    evt = new SessionInterruptionEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        ElapsedSeconds = elapsedSec,
                        EventType = "SessionLock",
                        Description = "Workstation session locked. Windows Camera Privacy architecture suspends IMFSourceReader frame delivery."
                    };
                    break;

                case SessionSwitchReason.SessionUnlock:
                    _isSessionLocked = false;
                    if (_lockStartTimeUtc.HasValue)
                    {
                        double lockSpan = (DateTime.UtcNow - _lockStartTimeUtc.Value).TotalSeconds;
                        _cumulativeLockDurationSec += lockSpan;
                        _lockStartTimeUtc = null;
                    }
                    evt = new SessionInterruptionEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        ElapsedSeconds = elapsedSec,
                        EventType = "SessionUnlock",
                        Description = "Workstation session unlocked. Windows Camera Privacy permits resumption of IMFSourceReader frame delivery."
                    };
                    break;

                default:
                    evt = new SessionInterruptionEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        ElapsedSeconds = elapsedSec,
                        EventType = e.Reason.ToString(),
                        Description = $"Session state transition: {e.Reason}"
                    };
                    break;
            }

            if (_isRecordingActive)
            {
                _recordingEvents.Add(evt);
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { SessionStateChanged?.Invoke(this, evt); } catch { }
            });
        }
    }

    /// <summary>
    /// Informs Windows that the display and system should remain active to prevent idle sleep.
    /// Note: Per Microsoft documentation, this API does NOT prevent explicit user workstation lock (Win+L)
    /// or bypass Windows Camera Privacy boundaries.
    /// </summary>
    public static bool RequestActiveExecutionState(out string diagnosticMessage)
    {
        try
        {
            var flags = MediaFoundationNative.EXECUTION_STATE.ES_CONTINUOUS |
                        MediaFoundationNative.EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                        MediaFoundationNative.EXECUTION_STATE.ES_DISPLAY_REQUIRED;

            var previous = MediaFoundationNative.SetThreadExecutionState(flags);
            if (previous == 0)
            {
                int error = Marshal.GetLastWin32Error();
                diagnosticMessage = $"SetThreadExecutionState failed with Win32 error 0x{error:X8}";
                return false;
            }

            diagnosticMessage = $"SetThreadExecutionState succeeded (Previous: 0x{(uint)previous:X8}, Requested: 0x{(uint)flags:X8})";
            return true;
        }
        catch (Exception ex)
        {
            diagnosticMessage = $"SetThreadExecutionState exception: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Restores default Windows idle and power behavior.
    /// </summary>
    public static bool ClearActiveExecutionState(out string diagnosticMessage)
    {
        try
        {
            var flags = MediaFoundationNative.EXECUTION_STATE.ES_CONTINUOUS;
            var previous = MediaFoundationNative.SetThreadExecutionState(flags);
            diagnosticMessage = $"SetThreadExecutionState cleared (Previous: 0x{(uint)previous:X8})";
            return true;
        }
        catch (Exception ex)
        {
            diagnosticMessage = $"ClearActiveExecutionState exception: {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
        catch { }
    }
}
