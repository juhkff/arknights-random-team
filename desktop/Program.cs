using System;
using Avalonia;
using arknights_random_team;

namespace arknights_random_team.Desktop;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var dir = AppState.DataDirectory;
        if (!string.IsNullOrEmpty(dir))
            Directory.SetCurrentDirectory(dir);

        FileStream? instanceLock = null;
        string? readOnlyReason = null;
        try
        {
            instanceLock = File.Open(
                Path.Combine(dir, ".arknights-random-team.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            readOnlyReason = $"无法独占数据目录（可能已有实例运行或目录不可写），当前以只读模式运行：{ex.Message}";
        }

        using (instanceLock)
        {
            if (readOnlyReason is not null)
                AppState.SetStorageReadOnly(readOnlyReason);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
