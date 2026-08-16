using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace arknights_random_team.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() : this("确定执行此操作？")
    {
    }

    public ConfirmDialog(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close(false);
    }
}
