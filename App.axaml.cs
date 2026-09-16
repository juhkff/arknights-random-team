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
            // 桌面端：常规窗口 + 退出时保存。
            // 对话框走原生窗口（与引入 Web 版之前一致），因此注册窗口展示方式。
            case IClassicDesktopStyleApplicationLifetime desktop:
                AppHost.Presenter = new WindowPresenter();
                desktop.MainWindow = new MainWindow { Icon = LoadAppIcon() };
                desktop.Exit += (_, _) => AppState.Save();
                break;

            // 浏览器（WebAssembly）：单视图生命周期，没有窗口系统，
            // 对话框只能用应用内叠加层，因此注册叠加层展示方式。
            case ISingleViewApplicationLifetime singleView:
                AppHost.Presenter = new OverlayPresenter(new ModalLayer());
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
