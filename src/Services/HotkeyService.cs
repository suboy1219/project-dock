using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ProjectDock.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private readonly Dispatcher _dispatcher;
    private IntPtr _hook;
    private DateTime _lastFired = DateTime.MinValue;

    public HotkeyService()
    {
        _callback = OnKey;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public uint Modifiers { get; private set; }

    public uint Key { get; private set; }

    public bool IsActive => _hook != IntPtr.Zero;

    public bool Suspended { get; set; }

    public event Action? Pressed;

    public bool TrySet(string text, out string? error)
    {
        if (!HotkeyText.TryParse(text, out var modifiers, out var key, out error))
        {
            return false;
        }

        Modifiers = modifiers;
        Key = key;

        if (_hook == IntPtr.Zero)
        {
            _hook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _callback,
                NativeMethods.GetModuleHandle(null),
                0);
        }

        if (_hook == IntPtr.Zero)
        {
            error = "无法安装键盘钩子，快捷键暂时不可用";
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || Key == 0)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (Suspended)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (wParam != (IntPtr)NativeMethods.WM_KEYDOWN && wParam != (IntPtr)NativeMethods.WM_SYSKEYDOWN)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        if (info.vkCode != Key || !MatchesModifiers())
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if ((DateTime.UtcNow - _lastFired).TotalMilliseconds < 250)
        {
            return (IntPtr)1;
        }

        _lastFired = DateTime.UtcNow;
        _dispatcher.BeginInvoke(new Action(() => Pressed?.Invoke()), DispatcherPriority.Input);
        return (IntPtr)1;
    }

    private bool MatchesModifiers()
    {
        var ctrl = IsDown(NativeMethods.VK_CONTROL);
        var alt = IsDown(NativeMethods.VK_MENU);
        var shift = IsDown(NativeMethods.VK_SHIFT);
        var win = IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN);

        return ctrl == ((Modifiers & NativeMethods.MOD_CONTROL) != 0)
            && alt == ((Modifiers & NativeMethods.MOD_ALT) != 0)
            && shift == ((Modifiers & NativeMethods.MOD_SHIFT) != 0)
            && win == ((Modifiers & NativeMethods.MOD_WIN) != 0);
    }

    private static bool IsDown(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
}
