using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using arknights_random_team;

namespace arknights_random_team.Browser;

/// <summary>
/// WebAssembly 入口点。界面与逻辑完全复用共享库，仅运行环境不同：
/// 窗口的创建放在 <c>App.OnFrameworkInitializationCompleted</c>，桌面端与浏览器端各自走对应分支。
/// </summary>
internal static class Program
{
    public static Task Main(string[] args) => BuildAvaloniaApp().StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .With(new FontManagerOptions
            {
                // WebAssembly 下 Skia 枚举不到系统字体，必须用应用内嵌的字体，
                // 否则中文全部显示为方块（桌面端不受影响，仍用系统字体）。
                DefaultFamilyName = AppFonts.EmbeddedCjkFamily,
                FontFallbacks =
                [
                    new FontFallback { FontFamily = new FontFamily(AppFonts.EmbeddedCjkFamily) }
                ]
            });
}
