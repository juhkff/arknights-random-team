using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace arknights_random_team.Views;

/// <summary>
/// 干员卡片上用到的一些取值转换。
/// 单独放在这里，免得为几个布尔判断各写一个转换器类。
/// </summary>
public static class StaffCardVisual
{
    public const double MinCardWidth = 128;
    public const double MaxCardWidth = 168;
    public const double CardSpacing = 10;
    public const double CardToolbarHeight = 36;

    /// <summary>半身像主体的高宽比；SquadPortrait 自身按同一比例排布。</summary>
    public const double HalfBodyRatio = 1.85d;

    /// <summary>
    /// 按可用宽度算列数与实际单元宽度。
    ///
    /// 先定列数，再把单元宽度夹在 [<see cref="MinCardWidth"/>, <see cref="MaxCardWidth"/>] 之间，
    /// 配合 <c>ItemsStretch="None"</c> 让半身像卡片保持稳定比例。
    /// </summary>
    public static (int Columns, double ItemWidth) FitColumns(double available, double spacing = CardSpacing)
    {
        if (available <= 0)
            return (1, MinCardWidth);

        // ItemsRepeater 12.0 UniformGridLayout estimates its extent/anchors using
        // floor(available / (itemWidth + spacing)), including trailing spacing.
        // Actual wrapping omits that trailing gap. Reserve it here too so both paths
        // agree on the column count; whole DIPs also avoid floating-point boundaries.
        var columns = Math.Max(1, (int)Math.Floor(available / (MinCardWidth + spacing)));
        var width = Math.Floor(available / columns - spacing);
        return (columns, Math.Clamp(width, 1, MaxCardWidth));
    }

    /// <summary>卡片高度由半身像主体与下方 36px 工具条推出。</summary>
    public static double CardHeight(double itemWidth) =>
        itemWidth * HalfBodyRatio + CardToolbarHeight;

    /// <summary>表格入池行的左侧色条；未入池为透明。</summary>
    public static readonly IValueConverter PoolAccent =
        new FuncValueConverter<bool, IBrush>(selected =>
            selected ? Brush("AppPrimaryBrush") : Brushes.Transparent);

    public static readonly IValueConverter PoolOpacity =
        new FuncValueConverter<bool, double>(selected => selected ? 1 : 0);

    public static readonly IValueConverter PoolRing =
        new FuncValueConverter<bool, IBrush>(selected =>
            selected ? Brush("AppPrimaryBrush") : Brush("AppBorderBrush"));

    public static readonly IValueConverter IsArtLoaded =
        new FuncValueConverter<ArtLoadState, bool>(state => state == ArtLoadState.Loaded);

    /// <summary>星级标签：全站统一显示成「6★」，避免下拉框里只显示裸数字。</summary>
    public static readonly IValueConverter StarLabel =
        new FuncValueConverter<int, string>(star => $"{star}★");

    /// <summary>正在加载：占位图标压暗一点，和「没有图源」「加载失败」区分开。</summary>
    public static readonly IValueConverter IsArtLoading =
        new FuncValueConverter<ArtLoadState, bool>(state => state == ArtLoadState.Loading);

    /// <summary>有图源但还没轮到下载：占位比「没有图源」更淡一点，但不像加载中那么暗。</summary>
    public static readonly IValueConverter IsArtQueued =
        new FuncValueConverter<ArtLoadState, bool>(state => state == ArtLoadState.Queued);

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
