namespace arknights_random_team;

/// <summary>应用内嵌字体。仅 WebAssembly 端需要——那里的 Skia 无法枚举系统字体。</summary>
public static class AppFonts
{
    /// <summary>思源黑体简体子集（国标 GB2312 全字集），以 AvaloniaResource 形式打包。</summary>
    public const string EmbeddedCjkFamily =
        "avares://arknights-random-team.Core/Assets/NotoSansSC-App.otf#Noto Sans SC";
}
