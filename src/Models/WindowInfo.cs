namespace ProjectDock.Models;

public sealed class WindowInfo
{
    public IntPtr Hwnd { get; init; }

    public string Title { get; init; } = string.Empty;

    public string ProcessName { get; init; } = string.Empty;

    public string ExePath { get; init; } = string.Empty;
}
