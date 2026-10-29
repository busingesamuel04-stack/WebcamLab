using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WebcamLab.Core.Native;

namespace WebcamLab.Core.Models;

public enum HardwarePropertyType
{
    VideoProcAmp,
    CameraControl
}

public class CameraHardwareProperty : INotifyPropertyChanged
{
    private int _currentValue;
    private bool _isAuto;
    private string _statusMessage = string.Empty;

    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public HardwarePropertyType PropertyType { get; init; }
    public int PropertyId { get; init; }
    public bool IsSupported { get; init; }
    public int Min { get; init; }
    public int Max { get; init; }
    public int Step { get; init; } = 1;
    public int DefaultValue { get; init; }
    public int InitialValue { get; set; }
    public bool InitialIsAuto { get; set; }
    public bool SupportsAuto { get; init; }
    public bool SupportsManual { get; init; }
    public string? DiagnosticDetails { get; init; }

    public int CurrentValue
    {
        get => _currentValue;
        set
        {
            if (_currentValue != value)
            {
                _currentValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayValue));
            }
        }
    }

    public bool IsAuto
    {
        get => _isAuto;
        set
        {
            if (_isAuto != value)
            {
                _isAuto = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsManual));
                OnPropertyChanged(nameof(DisplayValue));
            }
        }
    }

    public bool IsManual => !IsAuto;

    public string DisplayValue => IsAuto ? "Auto" : $"{CurrentValue}";

    public string RangeString => IsSupported ? $"[{Min} .. {Max}, Step: {Step}, Initial: {InitialValue}, Def: {DefaultValue}]" : "Not exposed by driver/hardware";

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
