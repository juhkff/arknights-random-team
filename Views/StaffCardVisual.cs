using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace arknights_random_team.Views;

/// <summary>
/// 干员卡片上用到的一些取值转换。
/// 单独放在这里，免得为几个布尔判断各写一个转换器类。
/// </summary>
public static class StaffCardVisual
{
    public const double MinCardWidth = 156;
    public const double MaxCardWidth = 196;
    public const double CardSpacing = 12;
    public const double NameplateHeight = 68;

    public static readonly IValueConverter ArtStretch =
        new FuncValueConverter<bool, Stretch>(portrait =>
            portrait ? Stretch.Uniform : Stretch.UniformToFill);

    public static readonly IValueConverter ArtAlignment =
        new FuncValueConverter<bool, VerticalAlignment>(portrait =>
            portrait ? VerticalAlignment.Center : VerticalAlignment.Top);

    public static readonly IValueConverter PoolBorder =
        new FuncValueConverter<bool, IBrush>(selected =>
            Brush(selected ? "AppPrimaryBrush" : "AppBorderBrush"));

    public static readonly IValueConverter IsArtPlaceholder =
        new FuncValueConverter<ArtLoadState, bool>(state => state != ArtLoadState.Loaded);

    /// <summary>正在加载：占位图标压暗一点，和「没有图源」「加载失败」区分开。</summary>
    public static readonly IValueConverter IsArtLoading =
        new FuncValueConverter<ArtLoadState, bool>(state => state == ArtLoadState.Loading);

    /// <summary>候选地址全部失败：只有这一态给出重试入口。</summary>
    public static readonly IValueConverter IsArtFailed =
        new FuncValueConverter<ArtLoadState, bool>(state => state == ArtLoadState.Failed);

    private static IBrush Brush(string key)
    {
        if (Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) == true &&
            value is IBrush brush)
            return brush;

        return Brushes.Gray;
    }
}
