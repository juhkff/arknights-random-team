using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace arknights_random_team.Views;

/// <summary>纯提示对话框；只有一个「知道了」按钮，关闭时不返回结果。</summary>
public partial class AlertDialog : ModalContent
{
    public AlertDialog() : this("提示", "")
    {
    }

    public AlertDialog(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        Loaded += (_, _) => OkButton.Focus();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Ok_Click(object? sender, RoutedEventArgs e) => RequestClose(null);
}
