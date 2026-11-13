using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ProjectDock.Models;
using ProjectDock.Services;

namespace ProjectDock;

public partial class MainWindow : Window
{
    private readonly GroupStore _store;
    private readonly WindowService _windows;
    private readonly DispatcherTimer _timer;
    private readonly MouseHookService _mouseHook;
    private DateTime _lastHiddenAt = DateTime.MinValue;
    private DateTime _graceUntil = DateTime.MinValue;
    private DispatcherTimer? _focusRetryTimer;
    private bool _suppressHide;
    private bool _dirty;
    private bool _allowClose;

    public string HotkeyText { get; set; } = AppSettings.DefaultHotkey;

    public Func<string, string?>? HotkeyApply { get; set; }

    public Action<bool>? HotkeyCapture { get; set; }

    private WindowItem? _pendingWindow;
    private FrameworkElement? _pendingWindowElement;
    private ProjectGroup? _pendingGroup;
    private FrameworkElement? _pendingGroupElement;
    private Point _pressPoint;
    private bool _dragging;
    private WindowItem? _dragWindow;
    private ProjectGroup? _dragGroup;
    private FrameworkElement? _dragSource;
    private ProjectGroup? _hoverGroup;
    private int _hoverIndex = -1;

    public MainWindow(GroupStore store, WindowService windows)
    {
        InitializeComponent();

        _store = store;
        _windows = windows;
        GroupList.ItemsSource = _store.Groups;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) =>
        {
            if (IsVisible)
            {
                Refresh();
            }
        };
        _timer.Start();

        _mouseHook = new MouseHookService();
        _mouseHook.ButtonDown += OnGlobalButtonDown;

        Deactivated += OnDeactivated;
        SizeChanged += (_, _) =>
        {
            if (IsVisible)
            {
                PositionBottomRight();
            }
        };
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += (_, _) => ApplyToolWindowStyle();
        Closing += (_, e) =>
        {
            if (_allowClose)
            {
                return;
            }

            e.Cancel = true;
            HidePanel();
        };
        MenuPopup.Closed += (_, _) =>
        {
            _suppressHide = false;
            _lastHiddenAt = DateTime.MinValue;
        };
    }

    public void ShowPanel()
    {
        Diagnostics.Log("ShowPanel");
        Refresh();
        Show();
        HintText.Text = $"按项目整理正在运行的窗口 · {HotkeyText} 呼出";
        UpdateLayout();
        PositionBottomRight();
        ForceForeground();
        _mouseHook.Start();

        _graceUntil = DateTime.UtcNow.AddMilliseconds(600);
    }

    public void HidePanel()
    {
        Diagnostics.Log("HidePanel");
        _lastHiddenAt = DateTime.UtcNow;
        CancelFocusRetry();
        _mouseHook.Stop();
        _graceUntil = DateTime.MinValue;
        Hide();
        FlushState();
    }

    public void PrepareForExit()
    {
        _allowClose = true;
        _mouseHook.Dispose();
    }

    public void TogglePanel()
    {
        if (IsVisible)
        {
            HidePanel();
            return;
        }

        if ((DateTime.UtcNow - _lastHiddenAt).TotalMilliseconds < 220)
        {
            return;
        }

        ShowPanel();
    }

    public void Refresh()
    {
        var windows = _windows.Enumerate();
        var alive = windows.Select(w => w.Hwnd).ToHashSet();
        var foreground = NativeMethods.GetForegroundWindow();
        var changed = false;

        foreach (var group in _store.Groups)
        {
            for (var i = group.Windows.Count - 1; i >= 0; i--)
            {
                if (!alive.Contains(group.Windows[i].Hwnd))
                {
                    group.Windows.RemoveAt(i);
                    changed = true;
                }
            }
        }

        var known = _store.Groups
            .SelectMany(g => g.Windows)
            .GroupBy(i => i.Hwnd)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var info in windows)
        {
            if (known.TryGetValue(info.Hwnd, out var existing))
            {
                existing.Title = info.Title;
                existing.IsForeground = info.Hwnd == foreground;
                continue;
            }

            var target = _store.Assign(info);
            target.Windows.Add(new WindowItem(
                info.Hwnd,
                info.Title,
                info.ProcessName,
                info.ExePath,
                _windows.GetIcon(info.ExePath))
            {
                IsForeground = info.Hwnd == foreground,
            });
            changed = true;
        }

        foreach (var group in _store.Groups)
        {
            foreach (var item in group.Windows)
            {
                item.IsForeground = item.Hwnd == foreground;
            }

            group.IsActive = group.Windows.Any(i => i.IsForeground);
        }

        if (changed)
        {
            _store.PruneAutoGroups();
            _dirty = true;
        }

        EmptyHint.Visibility = _store.Groups.Sum(g => g.Windows.Count) == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateHeight();
    }

    private void UpdateHeight()
    {
        var total = 100.0;

        foreach (var group in _store.Groups)
        {
            total += 42;
            if (group.IsExpanded)
            {
                total += group.Windows.Count * 33;
            }
        }

        if (NewGroupBar.Visibility == Visibility.Visible)
        {
            total += 46;
        }

        Height = Math.Clamp(total, 280, 720);

        if (IsVisible)
        {
            PositionBottomRight();
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        Diagnostics.Log($"Deactivated grace={DateTime.UtcNow < _graceUntil} suppress={_suppressHide} menu={MenuPopup.IsOpen}");

        if (_suppressHide || MenuPopup.IsOpen)
        {
            return;
        }

        if (DateTime.UtcNow < _graceUntil)
        {
            QueueFocusRetry();
            return;
        }

        HidePanel();
    }

    private void OnGlobalButtonDown(Point point)
    {
        Diagnostics.Log($"button down visible={IsVisible} suppress={_suppressHide} menu={MenuPopup.IsOpen}");
        if (!IsVisible || _suppressHide || MenuPopup.IsOpen)
        {
            return;
        }

        var target = NativeMethods.WindowFromPoint(new NativeMethods.POINT
        {
            X = (int)point.X,
            Y = (int)point.Y,
        });

        if (target == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetWindowThreadProcessId(target, out var pid);
        if (pid == (uint)Environment.ProcessId)
        {
            Diagnostics.Log("own window, keep");
            return;
        }

        Diagnostics.Log($"target pid={pid} class={GetClassName(target)}");

        HidePanel();
    }

    private static string GetClassName(IntPtr handle)
    {
        var buffer = new System.Text.StringBuilder(256);
        NativeMethods.GetClassName(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private void QueueFocusRetry()
    {
        if (_focusRetryTimer is not null)
        {
            return;
        }

        var remaining = _graceUntil - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        _focusRetryTimer = new DispatcherTimer { Interval = remaining + TimeSpan.FromMilliseconds(60) };
        _focusRetryTimer.Tick += (_, _) =>
        {
            CancelFocusRetry();
            RecoverFocus();
        };
        _focusRetryTimer.Start();
    }

    private void CancelFocusRetry()
    {
        if (_focusRetryTimer is null)
        {
            return;
        }

        _focusRetryTimer.Stop();
        _focusRetryTimer = null;
    }

    private void RecoverFocus()
    {
        if (!IsVisible || _suppressHide || MenuPopup.IsOpen || IsOwnWindowForeground())
        {
            return;
        }

        ForceForeground();

        _focusRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _focusRetryTimer.Tick += (_, _) =>
        {
            CancelFocusRetry();

            if (!IsVisible || _suppressHide || MenuPopup.IsOpen || IsOwnWindowForeground())
            {
                return;
            }

            HidePanel();
        };
        _focusRetryTimer.Start();
    }

    private static bool IsOwnWindowForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    private void ForceForeground()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = false;

        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        }

        NativeMethods.SetForegroundWindow(handle);
        NativeMethods.BringWindowToTop(handle);

        if (attached)
        {
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }

        Activate();
        Focus();

    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (NewGroupBar.Visibility == Visibility.Visible)
        {
            CloseNewGroupBar();
            e.Handled = true;
            return;
        }

        HidePanel();
        e.Handled = true;
    }

    private void OnAddGroupClick(object sender, RoutedEventArgs e)
    {
        NewGroupBar.Visibility = Visibility.Visible;
        NewGroupText.Text = string.Empty;
        NewGroupText.Focus();
        Keyboard.Focus(NewGroupText);
        UpdateHeight();
    }

    private void OnHotkeyClick(object sender, RoutedEventArgs e) => OpenHotkeySettings();

    public void OpenHotkeySettings()
    {
        if (HotkeyApply is null)
        {
            return;
        }

        _suppressHide = true;
        HotkeyCapture?.Invoke(true);

        try
        {
            var dialog = new HotkeyWindow(HotkeyText, HotkeyApply);

            if (IsVisible)
            {
                dialog.Owner = this;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (dialog.ShowDialog() == true)
            {
                HotkeyText = dialog.Value;
                HintText.Text = $"按项目整理正在运行的窗口 · {HotkeyText} 呼出";
            }
        }
        finally
        {
            HotkeyCapture?.Invoke(false);
            _suppressHide = false;
        }
    }

    private void OnNewGroupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var name = NewGroupText.Text.Trim();
            if (name.Length > 0)
            {
                _store.CreateGroup(name);
                _dirty = false;
                Refresh();
            }

            CloseNewGroupBar();
            e.Handled = true;
        }
    }

    private void OnNewGroupLostFocus(object sender, RoutedEventArgs e)
    {
        if (NewGroupBar.Visibility == Visibility.Visible)
        {
            CloseNewGroupBar();
        }
    }

    private void CloseNewGroupBar()
    {
        NewGroupBar.Visibility = Visibility.Collapsed;
        NewGroupText.Text = string.Empty;
        UpdateHeight();
    }

    private void OnGroupHeaderClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ProjectGroup group)
        {
            return;
        }

        if (_dragging)
        {
            CompleteDrop();
            e.Handled = true;
            return;
        }

        ClearPending();
        group.IsExpanded = !group.IsExpanded;
        _dirty = true;
        UpdateHeight();
        e.Handled = true;
    }

    private void OnGroupHeaderPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ProjectGroup group)
        {
            return;
        }

        if (group.IsUngrouped)
        {
            return;
        }

        _pendingGroup = group;
        _pendingGroupElement = element;
        _pressPoint = e.GetPosition(Shell);
        element.CaptureMouse();
    }

    private void OnGroupHeaderMouseMove(object sender, MouseEventArgs e)
    {
        if (_pendingGroup is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ClearPending();
            return;
        }

        var point = e.GetPosition(Shell);
        if (!_dragging && (point - _pressPoint).Length > 5)
        {
            BeginDragGroup(_pendingGroup, point);
        }

        if (_dragging)
        {
            UpdateGhost(point);
            UpdateHover(point);
        }
    }

    private void OnGroupRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ProjectGroup group)
        {
            return;
        }

        e.Handled = true;
        var items = new List<(string Header, Action Action)>();

        if (!group.IsUngrouped)
        {
            items.Add(("重命名", () => RenameGroup(group)));
            items.Add(("删除分组", () =>
            {
                _store.RemoveGroup(group);
                Refresh();
            }));
            items.Add(("-", () => { }));
        }

        items.Add((group.IsExpanded ? "折叠" : "展开", () =>
        {
            group.IsExpanded = !group.IsExpanded;
            _dirty = true;
            UpdateHeight();
        }));

        items.Add(("-", () => { }));
        items.Add(("新建项目组…", CreateGroupWithPrompt));

        ShowMenu(items);
    }

    private void OnWindowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not WindowItem item)
        {
            return;
        }

        e.Handled = true;

        if (_dragging)
        {
            CompleteDrop();
            return;
        }

        ClearPending();
        HidePanel();
        WindowService.Activate(item.Hwnd);
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsCloseGlyph(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is not FrameworkElement element || element.DataContext is not WindowItem)
        {
            return;
        }

        _pendingWindow = (WindowItem)element.DataContext;
        _pendingWindowElement = element;
        _pressPoint = e.GetPosition(Shell);
        element.CaptureMouse();
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (_pendingWindow is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ClearPending();
            return;
        }

        var point = e.GetPosition(Shell);
        if (!_dragging && (point - _pressPoint).Length > 5)
        {
            BeginDragWindow(_pendingWindow, point);
        }

        if (_dragging)
        {
            UpdateGhost(point);
            UpdateHover(point);
        }
    }

    private void BeginDragWindow(WindowItem item, Point point)
    {
        _dragging = true;
        _dragWindow = item;
        _dragSource = _pendingWindowElement;
        _suppressHide = true;

        if (_dragSource is not null)
        {
            _dragSource.Opacity = 0.35;
        }

        GhostIcon.Source = item.Icon;
        GhostIcon.Visibility = item.Icon is null ? Visibility.Collapsed : Visibility.Visible;
        GhostText.Text = item.Title;
        DragGhost.Visibility = Visibility.Visible;
        UpdateGhost(point);
        UpdateHover(point);
    }

    private void BeginDragGroup(ProjectGroup group, Point point)
    {
        _dragging = true;
        _dragGroup = group;
        _dragSource = _pendingGroupElement;
        _suppressHide = true;

        if (_dragSource is not null)
        {
            _dragSource.Opacity = 0.35;
        }

        GhostIcon.Source = null;
        GhostIcon.Visibility = Visibility.Collapsed;
        GhostText.Text = "移动分组 · " + group.Name;
        DragGhost.Visibility = Visibility.Visible;
        UpdateGhost(point);
        UpdateHover(point);
    }

    private void UpdateGhost(Point point)
    {
        DragGhost.Margin = new Thickness(point.X + 14, point.Y + 10, 0, 0);
    }

    private void UpdateHover(Point point)
    {
        var (group, card) = ResolveTarget(point);

        if (!ReferenceEquals(group, _hoverGroup))
        {
            if (_hoverGroup is not null)
            {
                _hoverGroup.IsDropTarget = false;
            }

            _hoverGroup = group;

            if (_hoverGroup is not null)
            {
                _hoverGroup.IsDropTarget = true;
            }
        }

        if (group is null || card is null)
        {
            _hoverIndex = -1;
            return;
        }

        _hoverIndex = _dragGroup is not null
            ? GroupIndexAt(group, card, point)
            : RowIndexAt(group, card, point);
    }

    private (ProjectGroup? Group, FrameworkElement? Card) ResolveTarget(Point point)
    {
        DependencyObject? node = Shell.InputHitTest(point) as DependencyObject;

        while (node is not null)
        {
            if (node is FrameworkElement element && element.DataContext is ProjectGroup group)
            {
                return (group, element);
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return (null, null);
    }

    private int RowIndexAt(ProjectGroup group, DependencyObject scope, Point point)
    {
        var host = FindRowsHost(scope, group);
        if (host is null)
        {
            return group.Windows.Count;
        }

        var origin = host.TransformToAncestor(Shell).Transform(new Point(0, 0));
        if (point.Y < origin.Y - 2)
        {
            return group.Windows.Count;
        }

        var index = 0;
        for (var i = 0; i < group.Windows.Count; i++)
        {
            if (host.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row)
            {
                continue;
            }

            var center = row.TransformToAncestor(Shell)
                .Transform(new Point(0, row.ActualHeight / 2)).Y;
            if (point.Y > center)
            {
                index = i + 1;
            }
        }

        return index;
    }

    private int GroupIndexAt(ProjectGroup target, FrameworkElement card, Point point)
    {
        var bounds = card.TransformToAncestor(Shell).TransformBounds(new Rect(card.RenderSize));
        var after = point.Y > bounds.Top + bounds.Height / 2;
        var ordered = _store.Groups.Where(g => !g.IsUngrouped).ToList();
        var index = ordered.IndexOf(target);

        if (index < 0)
        {
            return -1;
        }

        return after ? index + 1 : index;
    }

    private static ItemsControl? FindRowsHost(DependencyObject root, ProjectGroup group)
    {
        if (root is ItemsControl control && ReferenceEquals(control.ItemsSource, group.Windows))
        {
            return control;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindRowsHost(VisualTreeHelper.GetChild(root, i), group);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void CompleteDrop()
    {
        var window = _dragWindow;
        var group = _dragGroup;
        var target = _hoverGroup;
        var index = _hoverIndex;

        EndDrag();

        if (window is not null && target is not null)
        {
            _store.MoveTo(window, target, index >= 0 ? index : null);
            Refresh();
            return;
        }

        if (group is not null && target is not null && !ReferenceEquals(group, target) && index >= 0)
        {
            _store.MoveGroup(group, index);
            Refresh();
        }
    }

    private void EndDrag()
    {
        if (_hoverGroup is not null)
        {
            _hoverGroup.IsDropTarget = false;
            _hoverGroup = null;
        }

        if (_dragSource is not null)
        {
            _dragSource.Opacity = 1;
            _dragSource = null;
        }

        DragGhost.Visibility = Visibility.Collapsed;
        GhostIcon.Source = null;
        DragGhost.Margin = new Thickness(0);

        ClearPending();

        _dragging = false;
        _dragWindow = null;
        _dragGroup = null;
        _hoverIndex = -1;
        _suppressHide = false;
    }

    private void ClearPending()
    {
        if (_pendingWindowElement?.IsMouseCaptured == true)
        {
            _pendingWindowElement.ReleaseMouseCapture();
        }

        if (_pendingGroupElement?.IsMouseCaptured == true)
        {
            _pendingGroupElement.ReleaseMouseCapture();
        }

        _pendingWindow = null;
        _pendingWindowElement = null;
        _pendingGroup = null;
        _pendingGroupElement = null;
    }

    private static bool IsCloseGlyph(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is FrameworkElement element && element.Tag as string == "close")
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private void OnCloseWindowClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (sender is not FrameworkElement element || element.DataContext is not WindowItem item)
        {
            return;
        }

        WindowService.Close(item.Hwnd);
        Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Background);
    }

    private void OnWindowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not WindowItem item)
        {
            return;
        }

        e.Handled = true;
        var items = new List<(string Header, Action Action)>();

        foreach (var group in _store.Groups)
        {
            if (group.Windows.Contains(item))
            {
                continue;
            }

            var target = group;
            items.Add(($"移到「{target.Name}」", () =>
            {
                _store.MoveTo(item, target);
                _dirty = false;
                Refresh();
            }));
        }

        items.Add(("新建项目组并移入…", () =>
        {
            var name = Prompt("新建项目组", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var group = _store.CreateGroup(name);
            _store.MoveTo(item, group);
            _dirty = false;
            Refresh();
        }));

        items.Add(("-", () => { }));

        items.Add(("关闭窗口", () =>
        {
            WindowService.Close(item.Hwnd);
            Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Background);
        }));

        ShowMenu(items);
    }

    private void RenameGroup(ProjectGroup group)
    {
        var name = Prompt("重命名项目组", group.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        group.Name = name;
        _store.Save();
        Refresh();
    }

    private void CreateGroupWithPrompt()
    {
        var name = Prompt("新建项目组", string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _store.CreateGroup(name);
        Refresh();
    }

    private string? Prompt(string title, string value)
    {
        _suppressHide = true;
        try
        {
            var dialog = new PromptWindow(title, value)
            {
                Owner = this,
            };

            return dialog.ShowDialog() == true ? dialog.Value : null;
        }
        finally
        {
            _suppressHide = false;
        }
    }

    private void ShowMenu(List<(string Header, Action Action)> items)
    {
        MenuItems.Children.Clear();

        foreach (var (header, action) in items)
        {
            if (header == "-")
            {
                MenuItems.Children.Add(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x35, 0x42)),
                    Margin = new Thickness(8, 4, 8, 4),
                });
                continue;
            }

            var button = new Button
            {
                Content = header,
                Style = (Style)FindResource("MenuButton"),
            };
            button.Click += (_, _) =>
            {
                MenuPopup.IsOpen = false;
                action();
            };
            MenuItems.Children.Add(button);
        }

        _suppressHide = true;
        MenuPopup.PlacementTarget = this;
        MenuPopup.IsOpen = true;
    }

    private void PositionBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - ActualWidth - 4);
        Top = Math.Max(area.Top, area.Bottom - ActualHeight + 2);
    }

    private void ApplyToolWindowStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_TOOLWINDOW);
    }

    private void FlushState()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        _store.Save();
    }
}
