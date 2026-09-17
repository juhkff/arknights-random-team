using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace arknights_random_team;

/// <summary>
/// 桌面端落盘、网页端仅会话内存：界面文案与桌面版下载入口集中在这里，
/// 避免各页各写一套、把「退出时自动保存」错用到浏览器上。
/// </summary>
public static class SessionCopy
{
    public const string DesktopReleasesUrl = "https://github.com/juhkff/arknights-random-team/releases/latest";

    public static bool IsWeb => !AppState.UseFileStorage;

    public static string StorageTitle => IsWeb ? "会话内存" : "本地数据";

    public static string StorageKicker => IsWeb ? "SESSION MEMORY" : "LOCAL STORAGE";

    public static string StorageHint => IsWeb ? "刷新清空" : "退出时自动保存";

    public static string WebEmptyHint =>
        "网页版数据只在本次会话有效，刷新即清空。长期使用请下载桌面客户端。";

    public static async Task OpenDesktopDownloadAsync(Visual? from)
    {
        var uri = new Uri(DesktopReleasesUrl);
        var topLevel = from is null ? null : TopLevel.GetTopLevel(from);
        if (topLevel is null)
            topLevel = ResolveTopLevel();

        if (topLevel is not null)
            await topLevel.Launcher.LaunchUriAsync(uri);
    }

    private static TopLevel? ResolveTopLevel()
    {
        return Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            ISingleViewApplicationLifetime single => TopLevel.GetTopLevel(single.MainView),
            _ => null
        };
    }
}
