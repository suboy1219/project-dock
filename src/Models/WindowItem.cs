using System.ComponentModel;
using System.Windows.Media;

namespace ProjectDock.Models;

public sealed class WindowItem : INotifyPropertyChanged
{
    private string _title;
    private bool _isForeground;

    public WindowItem(IntPtr hwnd, string title, string processName, string exePath, ImageSource? icon)
    {
        Hwnd = hwnd;
        _title = title;
        ProcessName = processName;
        ExePath = exePath;
        Icon = icon;
    }

    public IntPtr Hwnd { get; }

    public string ProcessName { get; }

    public string ExePath { get; }

    public ImageSource? Icon { get; }

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value)
            {
                return;
            }

            _title = value;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(ToolTipText));
        }
    }

    public bool IsForeground
    {
        get => _isForeground;
        set
        {
            if (_isForeground == value)
            {
                return;
            }

            _isForeground = value;
            OnPropertyChanged(nameof(IsForeground));
        }
    }

    public string ToolTipText => $"{_title}\n{ProcessName}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
