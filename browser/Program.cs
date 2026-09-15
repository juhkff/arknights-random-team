using Avalonia;
using Avalonia.Browser;
using arknights_random_team;

namespace arknights_random_team.Browser;

/// <summary>
/// WebAssembly 入口点。界面与逻辑完全复用桌面端，仅入口与运行环境不同。
/// </summary>
internal static class Program
{
    public static Task Main(string[] args) => BuildAvaloniaApp().StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
