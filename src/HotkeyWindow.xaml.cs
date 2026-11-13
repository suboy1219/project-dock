using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ProjectDock.Models;
using ProjectDock.Services;

namespace ProjectDock;

public partial class HotkeyWindow : Window
{
    private readonly Func<string, string?> _apply;
    private string _value;

    public HotkeyWindow(string current, Func<string, string?> apply)
    {
        InitializeComponent();

        _apply = apply;
        _value = current;
        ComboText.Text = current;
        UpdateWarning();

        Loaded += (_, _) => Focus();
    }

    public string Value => _value;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            ComboText.Text = ModifierPreview();
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            return;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0)
        {
            return;
        }

        _value = HotkeyText.Format(ToNative(modifiers), (uint)virtualKey);
        ComboText.Text = _value;
        ErrorText.Visibility = Visibility.Collapsed;
        UpdateWarning();
        e.Handled = true;
    }

    private void OnCaptureClick(object sender, MouseButtonEventArgs e)
    {
        Focus();
        e.Handled = true;
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _value = AppSettings.DefaultHotkey;
        ComboText.Text = _value;
        ErrorText.Visibility = Visibility.Collapsed;
        UpdateWarning();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var error = _apply(_value);
        if (!string.IsNullOrEmpty(error))
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void UpdateWarning()
    {
        WarnText.Visibility = string.Equals(_value, "Alt+F4", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private string ModifierPreview()
    {
        var modifiers = Keyboard.Modifiers;
        var parts = new List<string>(4);

        if ((modifiers & ModifierKeys.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            parts.Add("Win");
        }

        parts.Add("…");
        return string.Join("+", parts);
    }

    private static uint ToNative(ModifierKeys modifiers)
    {
        var value = 0u;

        if ((modifiers & ModifierKeys.Control) != 0)
        {
            value |= NativeMethods.MOD_CONTROL;
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            value |= NativeMethods.MOD_ALT;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            value |= NativeMethods.MOD_SHIFT;
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            value |= NativeMethods.MOD_WIN;
        }

        return value;
    }
}
