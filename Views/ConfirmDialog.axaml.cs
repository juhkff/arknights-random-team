using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace arknights_random_team.Views;

/// <summary>确认对话框；确认返回 <c>true</c>，取消或按 Escape 返回 <c>false</c>。</summary>
public partial class ConfirmDialog : ModalContent
{
    public ConfirmDialog() : this("确认操作", "确定执行此操作？")
    {
    }

    public ConfirmDialog(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Ok_Click(object? sender, RoutedEventArgs e) => RequestClose(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => RequestClose(false);
}
