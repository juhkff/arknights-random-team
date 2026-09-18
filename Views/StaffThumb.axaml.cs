using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace arknights_random_team.Views;

/// <summary>
/// 干员图块：一张图 + 「图还没到位时」的职业图标占位。
///
/// 这段逻辑原先在列表卡片、表格行、生成页花名册里各写了一遍，三处的图源、
/// 占位大小与裁剪方式稍有出入就会立刻表现为「两个页面长得不一样」。抽成一个组件后，
/// 调用方只声明用哪组图源和怎么裁，不再各自维护一份 <see cref="ArtImage"/> 挂载代码。
/// </summary>
public partial class StaffThumb : UserControl
{
    /// <summary>主图候选地址。</summary>
    public static readonly StyledProperty<IReadOnlyList<Uri>?> SourcesProperty =
        AvaloniaProperty.Register<StaffThumb, IReadOnlyList<Uri>?>(nameof(Sources));

    /// <summary>主图不可用时的占位图候选地址（通常是职业图标）。</summary>
    public static readonly StyledProperty<IReadOnlyList<Uri>?> PlaceholderSourcesProperty =
        AvaloniaProperty.Register<StaffThumb, IReadOnlyList<Uri>?>(nameof(PlaceholderSources));

    /// <summary>主图裁剪方式：头像用 UniformToFill，立绘用 Uniform。</summary>
    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<StaffThumb, Stretch>(nameof(Stretch), Stretch.UniformToFill);

    /// <summary>主图纵向对齐：头像看顶部，立绘居中。</summary>
    public static readonly StyledProperty<VerticalAlignment> VerticalArtAlignmentProperty =
        AvaloniaProperty.Register<StaffThumb, VerticalAlignment>(nameof(VerticalArtAlignment), VerticalAlignment.Top);

    /// <summary>占位职业图标的边长。</summary>
    public static readonly StyledProperty<double> PlaceholderSizeProperty =
        AvaloniaProperty.Register<StaffThumb, double>(nameof(PlaceholderSize), 48d);

    /// <summary>占位职业图标的不透明度；大卡片上更淡，小缩略图上更实。</summary>
    public static readonly StyledProperty<double> PlaceholderOpacityProperty =
        AvaloniaProperty.Register<StaffThumb, double>(nameof(PlaceholderOpacity), 0.7d);

    static StaffThumb()
    {
        SourcesProperty.Changed.AddClassHandler<StaffThumb>((thumb, _) => thumb.ApplyArt());
        PlaceholderSourcesProperty.Changed.AddClassHandler<StaffThumb>((thumb, _) => thumb.ApplyPlaceholder());
    }

    public StaffThumb() => InitializeComponent();

    public IReadOnlyList<Uri>? Sources
    {
        get => GetValue(SourcesProperty);
        set => SetValue(SourcesProperty, value);
    }

    public IReadOnlyList<Uri>? PlaceholderSources
    {
        get => GetValue(PlaceholderSourcesProperty);
        set => SetValue(PlaceholderSourcesProperty, value);
    }

    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public VerticalAlignment VerticalArtAlignment
    {
        get => GetValue(VerticalArtAlignmentProperty);
        set => SetValue(VerticalArtAlignmentProperty, value);
    }

    public double PlaceholderSize
    {
        get => GetValue(PlaceholderSizeProperty);
        set => SetValue(PlaceholderSizeProperty, value);
    }

    public double PlaceholderOpacity
    {
        get => GetValue(PlaceholderOpacityProperty);
        set => SetValue(PlaceholderOpacityProperty, value);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>重试当前图源；标记已处理后不会连带触发外层卡片的选池快捷操作。</summary>
    private void Retry_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ArtImage.Retry(ThumbArt);
    }

    private void ApplyArt()
    {
        if (ThumbArt is not null)
            ArtImage.SetSources(ThumbArt, Sources);
    }

    private void ApplyPlaceholder()
    {
        if (ThumbPlaceholder is not null)
            ArtImage.SetSources(ThumbPlaceholder, PlaceholderSources);
    }
}
