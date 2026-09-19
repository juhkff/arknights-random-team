using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace arknights_random_team;

/// <summary>桌面端窗口：只负责窗口属性，界面内容全部在 <see cref="Views.MainView"/> 里。</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Views.ThemedWindowChrome.Apply(this);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
