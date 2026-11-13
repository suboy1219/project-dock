using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace ProjectDock.Services;

public sealed class MouseHookService : IDisposable
{
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private readonly IntPtr _callbackPointer;
    private readonly Dispatcher _dispatcher;
    private IntPtr _hook;

    public MouseHookService()
    {
        _callback = OnMouse;
        _callbackPointer = Marshal.GetFunctionPointerForDelegate(_callback);
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public event Action<Point>? ButtonDown;

    public bool IsRunning => _hook != IntPtr.Zero;

    public void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hook = NativeMethods.SetWindowsHookExPtr(
            NativeMethods.WH_MOUSE_LL,
            _callbackPointer,
            NativeMethods.GetModuleHandle(null),
            0);

        Diagnostics.Log($"hook start ok={_hook != IntPtr.Zero} err={Marshal.GetLastWin32Error()}");
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        Diagnostics.Log("hook stop");
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose() => Stop();

    private IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = (uint)wParam.ToInt64();
            if (message is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN)
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var point = new Point(info.pt.X, info.pt.Y);
                Diagnostics.Log($"hook down {point.X},{point.Y}");
                _dispatcher.BeginInvoke(new Action(() => ButtonDown?.Invoke(point)), DispatcherPriority.Input);
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
