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

    /// <summary>
    /// 职业徽记的矢量路径，替代 <see cref="CareerGlyph"/> 用于界面绘制。
    ///
    /// 之所以不直接用字符：术师的 ✦（U+2726）与特种的 ✕（U+2715）连思源黑体都没有，
    /// 嵌入式字体里缺字形，WebAssembly 端（没有系统字体可回退）会画成空白，
    /// 结果就是卡片左侧徽章空着。矢量路径与字体无关，两端表现一致。
    /// 各形状与原字符语义一一对应。
    /// </summary>
    public string CareerIconPath => Career switch
    {
        Career.先锋 => "M 6,1 L 11,11 L 1,11 Z",                          // ▲ 实心三角
        Career.近卫 => "M 6,1 L 11,6 L 6,11 L 1,6 Z",                     // ◆ 实心菱形
        Career.狙击 => "M 6,1 A 5,5 0 1 0 6.01,1 Z M 6,4 A 2,2 0 1 1 5.99,4 Z", // ◎ 同心圆
        Career.重装 => "M 2,2 H 10 V 10 H 2 Z",                           // ■ 实心方块
        Career.医疗 => "M 4.5,1.5 H 7.5 V 4.5 H 10.5 V 7.5 H 7.5 V 10.5 H 4.5 V 7.5 H 1.5 V 4.5 H 4.5 Z", // ✚ 十字
        Career.辅助 => "M 6,1 L 11,6 L 6,11 L 1,6 Z M 6,4 L 8,6 L 6,8 L 4,6 Z", // ◇ 空心菱形
        Career.术师 => "M 6,0.5 L 7.6,4.4 L 11.5,6 L 7.6,7.6 L 6,11.5 L 4.4,7.6 L 0.5,6 L 4.4,4.4 Z", // ✦ 四角星
        Career.特种 => "M 1.6,1.6 L 10.4,10.4 M 10.4,1.6 L 1.6,10.4",     // ✕ 交叉
        _ => "M 2,6 H 10"                                                  // · 短横
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
