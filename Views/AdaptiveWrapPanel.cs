using Avalonia;
using Avalonia.Controls;

namespace arknights_random_team.Views;

/// <summary>
/// 按可用宽度计算列数的卡片网格，当前只服务于生成页的花名册
/// （干员列表已改用可虚拟化的 ItemsRepeater）。
/// 单元宽度落在 [MinItemWidth, MaxItemWidth]，单列时铺满内容区，避免固定宽度留下尾部空白。
/// </summary>
public sealed class AdaptiveWrapPanel : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<AdaptiveWrapPanel, double>(nameof(MinItemWidth), 156);

    public static readonly StyledProperty<double> MaxItemWidthProperty =
        AvaloniaProperty.Register<AdaptiveWrapPanel, double>(nameof(MaxItemWidth), 196);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<AdaptiveWrapPanel, double>(nameof(Spacing), 12);

    /// <summary>固定单元高度；不设置时按单元宽度（正方形）计算。</summary>
    public static readonly StyledProperty<double> FixedItemHeightProperty =
        AvaloniaProperty.Register<AdaptiveWrapPanel, double>(nameof(FixedItemHeight), double.NaN);

    static AdaptiveWrapPanel()
    {
        AffectsMeasure<AdaptiveWrapPanel>(
            MinItemWidthProperty,
            MaxItemWidthProperty,
            SpacingProperty,
            FixedItemHeightProperty);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double MaxItemWidth
    {
        get => GetValue(MaxItemWidthProperty);
        set => SetValue(MaxItemWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double FixedItemHeight
    {
        get => GetValue(FixedItemHeightProperty);
        set => SetValue(FixedItemHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Compute(availableSize.Width, out var columns, out var itemWidth, out var itemHeight);
        var constraint = new Size(itemWidth, itemHeight);
        foreach (var child in Children)
            child.Measure(constraint);

        return Extent(availableSize.Width, columns, itemWidth, itemHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Compute(finalSize.Width, out var columns, out var itemWidth, out var itemHeight);
        var spacing = Math.Max(0, Spacing);
        for (var i = 0; i < Children.Count; i++)
        {
            var col = i % columns;
            var row = i / columns;
            var x = col * (itemWidth + spacing);
            var y = row * (itemHeight + spacing);
            Children[i].Arrange(new Rect(x, y, itemWidth, itemHeight));
        }

        return Extent(finalSize.Width, columns, itemWidth, itemHeight);
    }

    private void Compute(double width, out int columns, out double itemWidth, out double itemHeight)
    {
        var minW = Math.Max(1, MinItemWidth);
        var maxW = Math.Max(minW, MaxItemWidth);
        var spacing = Math.Max(0, Spacing);
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
            width = minW;

        columns = Math.Max(1, (int)Math.Floor((width + spacing) / (minW + spacing)));
        if (columns == 1)
        {
            itemWidth = Math.Max(minW, width);
        }
        else
        {
            itemWidth = (width - spacing * (columns - 1)) / columns;
            while (itemWidth > maxW + 0.01)
            {
                var next = columns + 1;
                var nextWidth = (width - spacing * (next - 1)) / next;
                if (nextWidth < minW)
                    break;
                columns = next;
                itemWidth = nextWidth;
            }
        }

        itemWidth = Math.Max(1, itemWidth);
        var fixedHeight = FixedItemHeight;
        itemHeight = !double.IsNaN(fixedHeight) && fixedHeight > 0 ? fixedHeight : itemWidth;
    }

    private Size Extent(double width, int columns, double itemWidth, double itemHeight)
    {
        var spacing = Math.Max(0, Spacing);
        var count = Children.Count;
        var rows = count == 0 ? 0 : (count + columns - 1) / columns;
        var height = rows == 0 ? 0 : rows * itemHeight + (rows - 1) * spacing;
        var usedWidth = columns == 0 ? 0 : columns * itemWidth + (columns - 1) * spacing;
        if (double.IsNaN(width) || double.IsInfinity(width) || width < 0)
            width = usedWidth;
        return new Size(Math.Max(width, usedWidth), height);
    }
}
