using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using arknights_random_team.Views;

namespace arknights_random_team;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        AppState.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        switch (ApplicationLifetime)
        {
            // 桌面端：常规窗口 + 退出时保存
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow { Icon = LoadAppIcon() };
                desktop.Exit += (_, _) => AppState.Save();
                break;

            // 浏览器（WebAssembly）：单视图生命周期，没有窗口系统，必须显式挂载主视图
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new MainView();
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 窗口 / 任务栏图标。只给桌面端用：浏览器端没有窗口系统，设置 WindowIcon 会抛 PlatformNotSupported。
    /// </summary>
    private static WindowIcon? LoadAppIcon()
    {
        try
        {
            return new WindowIcon(Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://arknights-random-team.Core/Assets/icon-256.png")));
        }
        catch (Exception ex)
        {
            AppState.LogTrace($"窗口图标加载失败（不影响使用）：{ex.Message}");
            return null;
        }
    }
}
