using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ProjectDock.Models;
using ProjectDock.Services;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace ProjectDock;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\ProjectDock.SingleInstance";
    private const string ShowEventName = @"Local\ProjectDock.ShowPanel";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ProjectDock";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private WinForms.NotifyIcon? _tray;
    private Drawing.Icon? _trayImage;
    private MainWindow? _panel;
    private WinForms.ToolStripMenuItem? _startupItem;
    private AppSettings? _settings;
    private HotkeyService? _hotkeys;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogException(args.Exception);
            args.Handled = true;
        };

        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                EventWaitHandle.OpenExisting(ShowEventName).Set();
            }
            catch
            {
                // 已有实例未监听时忽略
            }

            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(() => _panel?.ShowPanel())),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        var store = new GroupStore();
        var windows = new WindowService();
        _panel = new MainWindow(store, windows);

        CreateTray();
        SetupHotkey();

        if (e.Args.Any(arg => string.Equals(arg, "--show", StringComparison.OrdinalIgnoreCase)))
        {
            Dispatcher.BeginInvoke(new Action(() => _panel.ShowPanel()), DispatcherPriority.ApplicationIdle);
        }

    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _hotkeys = null;

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        _trayImage?.Dispose();
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void CreateTray()
    {
        _trayImage = LoadTrayIcon();

        var menu = new WinForms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Drawing.Font("Microsoft YaHei UI", 9f),
        };

        var openItem = new WinForms.ToolStripMenuItem("打开面板");
        openItem.Click += (_, _) => _panel?.ShowPanel();
        menu.Items.Add(openItem);

        var refreshItem = new WinForms.ToolStripMenuItem("刷新窗口列表");
        refreshItem.Click += (_, _) => _panel?.Refresh();
        menu.Items.Add(refreshItem);

        var hotkeyItem = new WinForms.ToolStripMenuItem("快捷键…");
        hotkeyItem.Click += (_, _) => _panel?.OpenHotkeySettings();
        menu.Items.Add(hotkeyItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        _startupItem = new WinForms.ToolStripMenuItem("开机启动")
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled(),
        };
        _startupItem.CheckedChanged += OnStartupToggled;
        menu.Items.Add(_startupItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        menu.Items.Add(HelpMenu.Create());

        var exitItem = new WinForms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        _tray = new WinForms.NotifyIcon
        {
            Icon = _trayImage,
            Text = "ProjectDock 项目窗口分组",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left)
            {
                _panel?.TogglePanel();
            }
        };
    }

    private void OnStartupToggled(object? sender, EventArgs e)
    {
        var enabled = _startupItem?.Checked ?? false;
        SetStartup(enabled);
    }

    private void SetupHotkey()
    {
        _settings = AppSettings.Load();
        _hotkeys = new HotkeyService();
        _hotkeys.Pressed += () => _panel?.TogglePanel();

        if (_panel is not null)
        {
            _panel.HotkeyText = _settings.Hotkey;
            _panel.HotkeyApply = ApplyHotkey;
            _panel.HotkeyCapture = suspend =>
            {
                if (_hotkeys is not null)
                {
                    _hotkeys.Suspended = suspend;
                }
            };
        }

        if (!_hotkeys.TrySet(_settings.Hotkey, out var error))
        {
            _tray?.ShowBalloonTip(
                5000,
                "ProjectDock",
                $"快捷键 {_settings.Hotkey} 不可用：{error}。可在托盘菜单里改。",
                WinForms.ToolTipIcon.Warning);
        }
    }

    private string? ApplyHotkey(string text)
    {
        if (_hotkeys is null || _settings is null)
        {
            return "快捷键服务未初始化";
        }

        if (!_hotkeys.TrySet(text, out var error))
        {
            return error;
        }

        _settings.Hotkey = text;
        _settings.Save();
        return null;
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SetStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path))
                {
                    key.SetValue(RunValueName, $"\"{path}\" --silent");
                }
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // 注册表不可写时静默失败
        }
    }

    private void ExitApp()
    {
        _panel?.PrepareForExit();

        if (_tray is not null)
        {
            _tray.Visible = false;
        }

        Shutdown();
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // 回退到系统图标
        }

        return Drawing.SystemIcons.Application;
    }

    private static void LogException(Exception exception)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "ProjectDock-error.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {exception}\n\n");
        }
        catch
        {
            // 日志写失败时忽略
        }
    }
}
