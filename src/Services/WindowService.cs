using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ProjectDock.Models;

namespace ProjectDock.Services;

public sealed class WindowService
{
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW",
        "Windows.UI.Core.CoreWindow",
        "MultitaskingViewFrame",
        "ForegroundStaging",
        "XamlExplorerHostIslandWindow",
        "TopLevelWindowForOverflowXamlIsland",
    };

    private static readonly HashSet<string> ShellFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "此电脑", "回收站", "网络", "下载", "文档", "图片", "音乐", "视频", "桌面", "主文件夹",
        "This PC", "Recycle Bin", "Network", "Downloads", "Documents", "Pictures", "Music",
        "Videos", "Desktop", "Home",
    };

    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Untitled", "Settings", "Extensions", "Welcome", "Get Started",
        "Visual Studio Code", "Visual Studio Code - Insiders", "Cursor", "Windsurf",
    };

    private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    public List<WindowInfo> Enumerate()
    {
        var result = new List<WindowInfo>();
        var selfPid = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                return true;
            }

            var length = NativeMethods.GetWindowTextLength(hWnd);
            if (length == 0)
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == selfPid || pid == 0)
            {
                return true;
            }

            var exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            {
                return true;
            }

            if (NativeMethods.GetAncestor(hWnd, NativeMethods.GA_ROOT) != hWnd)
            {
                return true;
            }

            if (IsCloaked(hWnd))
            {
                return true;
            }

            var className = GetClassName(hWnd);
            if (ExcludedClasses.Contains(className))
            {
                return true;
            }

            var title = GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            var process = GetProcessInfo(pid);
            if (process is null)
            {
                return true;
            }

            result.Add(new WindowInfo
            {
                Hwnd = hWnd,
                Title = title,
                ProcessName = process.Value.Name,
                ExePath = process.Value.Path,
            });

            return true;
        }, IntPtr.Zero);

        return result;
    }

    public ImageSource? GetIcon(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return null;
        }

        if (_iconCache.TryGetValue(exePath, out var cached))
        {
            return cached;
        }

        ImageSource? image = null;
        try
        {
            var info = new NativeMethods.SHFILEINFO();
            var handle = NativeMethods.SHGetFileInfo(
                exePath,
                0,
                ref info,
                (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
                NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

            if (handle != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                image = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                NativeMethods.DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            image = null;
        }

        _iconCache[exePath] = image;
        return image;
    }

    public static string? DetectProjectName(WindowInfo info)
    {
        var title = info.Title.Trim();
        if (title.Length == 0)
        {
            return null;
        }

        if (title.StartsWith("管理员: ", StringComparison.Ordinal))
        {
            title = title["管理员: ".Length..].Trim();
        }

        var process = info.ProcessName.ToLowerInvariant();

        if (process is "code" or "code-insiders" or "cursor" or "windsurf")
        {
            var parts = title.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3 ? Sanitize(parts[^2]) : null;
        }

        if (process is "unity" or "unityhub")
        {
            var parts = title.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[^1].StartsWith("Unity", StringComparison.OrdinalIgnoreCase))
            {
                return Sanitize(parts[1]);
            }

            return parts.Length >= 2 ? Sanitize(parts[^1]) : null;
        }

        if (process is "explorer")
        {
            if (title.Contains(" - ", StringComparison.Ordinal) || ShellFolders.Contains(title))
            {
                return null;
            }

            return Sanitize(title);
        }

        if (process is "devenv")
        {
            var parts = title.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? Sanitize(parts[0]) : null;
        }

        return null;
    }

    public static bool Activate(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
        {
            return false;
        }

        if (NativeMethods.IsIconic(hWnd))
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
        }

        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = false;

        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        }

        var ok = NativeMethods.SetForegroundWindow(hWnd);
        NativeMethods.BringWindowToTop(hWnd);

        if (attached)
        {
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }

        return ok;
    }

    public static void Close(IntPtr hWnd)
    {
        if (hWnd != IntPtr.Zero && NativeMethods.IsWindow(hWnd))
        {
            NativeMethods.PostMessage(hWnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static bool IsCloaked(IntPtr hWnd)
    {
        try
        {
            var result = NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out var cloaked, sizeof(int));
            return result == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        var length = NativeMethods.GetWindowTextLength(hWnd);
        var buffer = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hWnd, buffer, buffer.Capacity);
        return buffer.ToString().Trim();
    }

    private static string GetClassName(IntPtr hWnd)
    {
        var buffer = new StringBuilder(256);
        NativeMethods.GetClassName(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static (string Name, string Path)? GetProcessInfo(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            var path = string.Empty;

            try
            {
                path = process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                // 权限不足时只保留进程名
            }

            return (name, path);
        }
        catch
        {
            return null;
        }
    }

    private static string? Sanitize(string value)
    {
        var text = value.Trim().Trim('.', ' ', '-');
        if (text.Length == 0)
        {
            return null;
        }

        if (GenericNames.Contains(text) || text.StartsWith("Untitled-", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (text.Length > 40)
        {
            text = text[..40].Trim();
        }

        return text;
    }
}
