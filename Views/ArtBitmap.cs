using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace arknights_random_team.Views;

/// <summary>
/// Art fills an already-sized thumbnail. A new bitmap only invalidates rendering, never the
/// card's measurement or the scroll extent; intrinsic image size is deliberately ignored.
/// </summary>
public sealed class ArtBitmap : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<ArtBitmap, IImage?>(nameof(Source));
    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<ArtBitmap, Stretch>(nameof(Stretch), Stretch.UniformToFill);
    public static readonly StyledProperty<VerticalAlignment> ArtAlignmentProperty =
        AvaloniaProperty.Register<ArtBitmap, VerticalAlignment>(nameof(ArtAlignment), VerticalAlignment.Center);

    static ArtBitmap() => AffectsRender<ArtBitmap>(SourceProperty, StretchProperty, ArtAlignmentProperty);

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public VerticalAlignment ArtAlignment
    {
        get => GetValue(ArtAlignmentProperty);
        set => SetValue(ArtAlignmentProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Source is not { } source || source.Size.Width <= 0 || source.Size.Height <= 0 ||
            Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var size = Stretch.CalculateSize(Bounds.Size, source.Size);
        var y = ArtAlignment switch
        {
            VerticalAlignment.Top => 0,
            VerticalAlignment.Bottom => Bounds.Height - size.Height,
            _ => (Bounds.Height - size.Height) / 2
        };
        var destination = new Rect(new Point((Bounds.Width - size.Width) / 2, y), size);
        using (context.PushClip(new Rect(Bounds.Size)))
            context.DrawImage(source, new Rect(source.Size), destination);
    }
}
