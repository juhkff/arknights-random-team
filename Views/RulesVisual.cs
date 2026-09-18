using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace arknights_random_team.Views;

/// <summary>
/// 随机策略卡片的规则展开规则。
///
/// 方案要求「名称＋少量规则摘要形成轻量卡片；较多规则按需展开」：规则少时直接摊开，
/// 规则较多时默认收起，只留一行条数摘要，点标题展开。这里只负责判断，
/// 展开/收起状态由 Expander 自己持有，编辑与删除入口不受影响。
/// </summary>
public static class RulesVisual
{
    /// <summary>规则条数超过这个值就默认收起。</summary>
    private const int CollapseThreshold = 3;

    /// <summary>规则较少时默认展开，较多时默认收起。</summary>
    public static readonly IValueConverter DefaultExpanded = new RuleCountConverter(
        count => count <= CollapseThreshold);

    /// <summary>规则标题：条数，规则较多时补一句可展开提示。</summary>
    public static readonly IValueConverter Header = new RuleCountConverter(
        count => count > CollapseThreshold ? $"{count} 条规则 · 点击展开或收起" : $"{count} 条规则");

    private static int CountOf(object? rules) => rules is ICollection collection ? collection.Count : 0;

    private sealed class RuleCountConverter : IValueConverter
    {
        private readonly Func<int, object> _project;

        public RuleCountConverter(Func<int, object> project) => _project = project;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            _project(CountOf(value));

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
