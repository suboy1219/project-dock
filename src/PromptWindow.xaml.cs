using System.Windows;
using System.Windows.Input;

namespace ProjectDock;

public partial class PromptWindow : Window
{
    public PromptWindow(string title, string value)
    {
        InitializeComponent();

        TitleText.Text = title;
        ValueBox.Text = value;

        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };

        PreviewKeyDown += OnPreviewKeyDown;
    }

    public string Value => ValueBox.Text.Trim();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DialogResult = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
