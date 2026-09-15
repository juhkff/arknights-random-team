using Avalonia.Media;

namespace arknights_random_team.Models;

/// <summary>
/// 干员的展示派生属性（职业徽记与配色、稀有度符号）。
/// 只做呈现层映射，不参与数据存储，因此与 <see cref="Staff"/> 的可序列化字段分开维护。
/// </summary>
public partial class Staff
{
    /// <summary>职业徽记：每个职业一个单字符图案，画在结果卡片左侧的徽章里。</summary>
    public string CareerGlyph => Career switch
    {
        Career.先锋 => "▲",
        Career.近卫 => "◆",
        Career.狙击 => "◎",
        Career.重装 => "■",
        Career.医疗 => "✚",
        Career.辅助 => "◇",
        Career.术师 => "✦",
        Career.特种 => "✕",
        _ => "·"
    };

    /// <summary>职业配色：用于结果卡片的左侧导轨与徽章。</summary>
    public IBrush CareerBrush => Career switch
    {
        Career.先锋 => new SolidColorBrush(Color.Parse("#5FD0E8")),
        Career.近卫 => new SolidColorBrush(Color.Parse("#E8734A")),
        Career.狙击 => new SolidColorBrush(Color.Parse("#E8C15F")),
        Career.重装 => new SolidColorBrush(Color.Parse("#C08CE0")),
        Career.医疗 => new SolidColorBrush(Color.Parse("#65E6A8")),
        Career.辅助 => new SolidColorBrush(Color.Parse("#7FA8F0")),
        Career.术师 => new SolidColorBrush(Color.Parse("#E06E9C")),
        Career.特种 => new SolidColorBrush(Color.Parse("#9BB0C4")),
        _ => new SolidColorBrush(Color.Parse("#7C93A8"))
    };

    /// <summary>稀有度符号，例如 6 星显示为 ★★★★★★。</summary>
    public string StarGlyphs => new string('★', Math.Clamp(Star, 0, 6));

    /// <summary>精英阶段与等级的数字读数，例如 2 / 80。</summary>
    public string LevelDigits => $"{Level.EliteLevel}-{Level.Rank:00}";
}
