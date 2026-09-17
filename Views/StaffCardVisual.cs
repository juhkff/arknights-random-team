using Avalonia.Data.Converters;
using Avalonia.Media;

namespace arknights_random_team.Views;

/// <summary>
/// 干员卡片上用到的一些取值转换。
/// 单独放在这里，免得为几个布尔判断各写一个转换器类。
/// </summary>
public static class StaffCardVisual
{
    /// <summary>
    /// 立绘透明度：未被选中参与随机的干员压暗一些，
    /// 让「已启用」的卡片在整片网格里自然凸显，不必依赖勾选框。
    /// </summary>
    public static readonly IValueConverter ArtOpacity =
        new FuncValueConverter<bool, double>(selected => selected ? 1.0 : 0.82);

    /// <summary>启用状态文字。</summary>
    public static readonly IValueConverter StatusText =
        new FuncValueConverter<bool, string>(selected => selected ? "编入" : "待命");

    /// <summary>
    /// 立绘是 180×360：半身像原尺寸铺满；头像放大 1.5 倍，只露出上半截
    /// （与 180×180 头像同一显示区域）。
    /// </summary>
    public static readonly IValueConverter ArtViewportTransform =
        new FuncValueConverter<bool, Transform>(portrait =>
            portrait
                ? new ScaleTransform(1, 1)
                : new ScaleTransform(1.5, 1.5));
}
