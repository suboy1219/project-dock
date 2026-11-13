using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace ProjectDock.Models;

public sealed class ProjectGroup : INotifyPropertyChanged
{
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x5B, 0x8C, 0xFF),
        Color.FromRgb(0xA8, 0x78, 0xE6),
        Color.FromRgb(0x6B, 0xD0, 0x8C),
        Color.FromRgb(0xE8, 0xB3, 0x4B),
        Color.FromRgb(0x58, 0xC7, 0xD6),
        Color.FromRgb(0xE0, 0x7A, 0x9A),
    };

    private string _name;
    private string _colorHex;
    private bool _isExpanded;
    private bool _isActive;
    private bool _isDropTarget;
    private SolidColorBrush _accentBrush = Frozen(0x5B, 0x8C, 0xFF);
    private SolidColorBrush _countBrush = Frozen(0x2A, 0x35, 0x4C);

    public ProjectGroup(string id, string name, string colorHex, bool isUngrouped = false)
    {
        Id = id;
        _name = name;
        _colorHex = colorHex;
        IsUngrouped = isUngrouped;
        Windows.CollectionChanged += OnWindowsChanged;
        RebuildBrushes();
    }

    public string Id { get; }

    public bool IsUngrouped { get; }

    public bool IsAuto { get; set; }

    public ObservableCollection<WindowItem> Windows { get; } = new();

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            _name = value.Trim();
            OnPropertyChanged(nameof(Name));
        }
    }

    public string ColorHex
    {
        get => _colorHex;
        set
        {
            if (_colorHex == value)
            {
                return;
            }

            _colorHex = value;
            RebuildBrushes();
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(ContentVisibility));
            OnPropertyChanged(nameof(Chevron));
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(BorderBrush));
        }
    }

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (_isDropTarget == value)
            {
                return;
            }

            _isDropTarget = value;
            OnPropertyChanged(nameof(IsDropTarget));
            OnPropertyChanged(nameof(CardBrush));
            OnPropertyChanged(nameof(BorderBrush));
        }
    }

    public int Count => Windows.Count;

    public string CountText => Count.ToString();

    public string Chevron => IsExpanded ? "\uE70E" : "\uE70D";

    public Visibility ContentVisibility => IsExpanded && Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BadgeVisibility => Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Brush CardBrush => IsDropTarget
        ? Frozen(0x21, 0x27, 0x33)
        : IsUngrouped
            ? Frozen(0x13, 0x16, 0x1C)
            : Frozen(0x17, 0x1B, 0x23);

    public Brush BorderBrush => IsDropTarget || IsActive ? _accentBrush : Frozen(0x26, 0x2B, 0x36);

    public Brush AccentBrush => _accentBrush;

    public Brush CountBackground => _countBrush;

    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();

    public event PropertyChangedEventHandler? PropertyChanged;

    public static string NextColor(int index) => Palette[index % Palette.Length].ToString();

    private void OnWindowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ContentVisibility));
        OnPropertyChanged(nameof(BadgeVisibility));
    }

    private void RebuildBrushes()
    {
        var color = ParseColor(_colorHex);
        _accentBrush = new SolidColorBrush(color);
        _accentBrush.Freeze();
        _countBrush = new SolidColorBrush(Color.FromArgb(0x38, color.R, color.G, color.B));
        _countBrush.Freeze();
        OnPropertyChanged(nameof(AccentBrush));
        OnPropertyChanged(nameof(CountBackground));
        OnPropertyChanged(nameof(BorderBrush));
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return Palette[0];
        }
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
