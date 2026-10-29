using System;
using System.Windows.Media;
using WebcamLab.Core.Models;

namespace WebcamLab.ViewModels;

/// <summary>
/// Observable presentation wrapper for a single false-color exposure zone.
/// Supports two-way boundary and color editing with dynamic validation callback.
/// </summary>
public class FalseColorZoneViewModel : ViewModelBase
{
    private readonly FalseColorZone _model;
    private readonly Action? _onChanged;

    public int ZoneIndex => _model.ZoneIndex;

    public string Name
    {
        get => _model.Name;
        set
        {
            if (_model.Name != value)
            {
                _model.Name = value;
                OnPropertyChanged();
                _onChanged?.Invoke();
            }
        }
    }

    public string Description
    {
        get => _model.Description;
        set
        {
            if (_model.Description != value)
            {
                _model.Description = value;
                OnPropertyChanged();
                _onChanged?.Invoke();
            }
        }
    }

    public double MinIre
    {
        get => _model.MinIre;
        set
        {
            if (Math.Abs(_model.MinIre - value) > 0.001)
            {
                _model.MinIre = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RangeText));
                _onChanged?.Invoke();
            }
        }
    }

    public double MaxIre
    {
        get => _model.MaxIre;
        set
        {
            if (Math.Abs(_model.MaxIre - value) > 0.001)
            {
                _model.MaxIre = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RangeText));
                _onChanged?.Invoke();
            }
        }
    }

    public string HexColor
    {
        get => _model.HexColor;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value.StartsWith("#") && (value.Length == 7 || value.Length == 9))
            {
                try
                {
                    string hex = value.TrimStart('#');
                    if (hex.Length >= 6)
                    {
                        _model.R = Convert.ToByte(hex.Substring(0, 2), 16);
                        _model.G = Convert.ToByte(hex.Substring(2, 2), 16);
                        _model.B = Convert.ToByte(hex.Substring(4, 2), 16);
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(Brush));
                        _onChanged?.Invoke();
                    }
                }
                catch
                {
                    // Ignore parse errors while typing
                }
            }
        }
    }

    public SolidColorBrush Brush => new(Color.FromRgb(_model.R, _model.G, _model.B));

    public string RangeText => $"{MinIre:F1} – {MaxIre:F1} IRE";

    public FalseColorZone Model => _model;

    public FalseColorZoneViewModel(FalseColorZone model, Action? onChanged = null)
    {
        _model = model;
        _onChanged = onChanged;
    }
}
