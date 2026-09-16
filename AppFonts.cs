namespace arknights_random_team;

/// <summary>应用内嵌字体。仅 WebAssembly 端需要——那里的 Skia 无法枚举系统字体。</summary>
public static class AppFonts
{
    /// <summary>
    /// 思源黑体简体子集，以 AvaloniaResource 形式打包。
    ///
    /// 子集范围 = 原 GB2312 字集 + 干员名里超出 GB2312 的 6 个字（² ™ 吽 祐 菈 鸮）。
    /// 这 6 个字原先缺字形，网页上会显示成空白，例如「白面鸮」。
    ///
    /// 注意：必须是 OTF/TTF。实测 Avalonia 的内嵌字体加载器不支持 WOFF2
    /// （会抛 Could not create glyphTypeface），所以别为了省体积换成 woff2。
    /// </summary>
    public const string EmbeddedCjkFamily =
        "avares://arknights-random-team.Core/Assets/NotoSansSC-App.otf#Noto Sans CJK SC";
}
