using Avalonia;
using Avalonia.Controls;

namespace arknights_random_team.Views;

/// <summary>
/// 子控件仍按单元格实际宽度排布，但测量时不向 DataGrid 报告宽度，避免星号列被内容撑开。
/// </summary>
public class CellFill : Decorator
{
    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(availableSize);
        return new Size(0, Child?.DesiredSize.Height ?? 0);
    }
}
