using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using arknights_random_team.Views;

namespace arknights_random_team;

public partial class App : Application
{
    private bool _allowClose;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        AppState.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Decode roster art while the window and first page are still being constructed.
        // 浏览器端不预热整份名单：WASM 内存有限，只按可见区域加载。
        if (!OperatingSystem.IsBrowser())
            ArtPrefetcher.Shared.Attach();

        switch (ApplicationLifetime)
        {
            // 桌面端：常规窗口 + 退出时保存。
            // 对话框走原生窗口（与引入 Web 版之前一致），因此注册窗口展示方式。
            case IClassicDesktopStyleApplicationLifetime desktop:
                AppHost.Presenter = new WindowPresenter();
                var mainWindow = new MainWindow { Icon = LoadAppIcon() };
                desktop.MainWindow = mainWindow;
                mainWindow.Closing += async (_, e) =>
                {
                    if (_allowClose || AppState.Save())
                        return;

                    e.Cancel = true;
                    var leave = await AppHost.ConfirmAsync(
                        $"{AppState.LastSaveError ?? "本地数据保存失败。"}\n仍要退出吗？本次未保存的修改将丢失。",
                        "保存失败");
                    if (!leave)
                        return;

                    _allowClose = true;
                    mainWindow.Close();
                };
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
