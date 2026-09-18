using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Data.Converters;

namespace arknights_random_team.Views;

/// <summary>
/// 表头排序箭头的显隐转换器。
/// ListModel 把每列的排序方向暴露成 <see cref="ListSortDirection"/>? 属性，
/// 表头据此显示向上或向下的箭头；箭头画在列头模板里，不依赖 DataGrid 内部的排序状态。
/// </summary>
public static class SortVisual
{
    /// <summary>方向为升序时返回 true，用于升序箭头。</summary>
    public static readonly IValueConverter IsAscending = new DirectionConverter(ListSortDirection.Ascending);

    /// <summary>方向为降序时返回 true，用于降序箭头。</summary>
    public static readonly IValueConverter IsDescending = new DirectionConverter(ListSortDirection.Descending);

    private sealed class DirectionConverter : IValueConverter
    {
        private readonly ListSortDirection _direction;

        public DirectionConverter(ListSortDirection direction) => _direction = direction;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is ListSortDirection current && current == _direction;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
