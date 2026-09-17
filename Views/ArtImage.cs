using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace arknights_random_team.Views;

/// <summary>
/// 按需加载远程图片的辅助类，供干员卡片显示立绘使用。
///
/// 做法：在 <see cref="Image"/> 上挂一个 <see cref="SourceUriProperty"/>，
/// 控件进入可视树时才发起下载，离开时清空。滚出屏幕的卡片不会占用网络。
///
/// 缓存的是 <see cref="Task{TResult}"/> 而不是 <see cref="Bitmap"/>：
/// 同一个地址被多张卡片同时请求时只会下载一次，失败也会被记住，不会反复重试。
/// </summary>
public static class ArtImage
{
    private static readonly ConcurrentDictionary<Uri, Task<Bitmap?>> Cache = new();

    public static readonly AttachedProperty<IReadOnlyList<Uri>?> SourcesProperty =
        AvaloniaProperty.RegisterAttached<Image, IReadOnlyList<Uri>?>("Sources", typeof(ArtImage));

    /// <summary>记录哪些 Image 当前挂在可视树上（这个 Avalonia 版本没有现成的判断方法）。</summary>
    private static readonly ConditionalWeakTable<Image, object> Attached = new();

    public static readonly AttachedProperty<Uri?> SourceUriProperty =
        AvaloniaProperty.RegisterAttached<Image, Uri?>("SourceUri", typeof(ArtImage));

    static ArtImage()
    {
        SourceUriProperty.Changed.AddClassHandler<Image, Uri?>(OnSourceUriChanged);
        SourcesProperty.Changed.AddClassHandler<Image, IReadOnlyList<Uri>?>(OnSourcesChanged);
    }

    public static IReadOnlyList<Uri>? GetSources(Image image) => image.GetValue(SourcesProperty);

    public static void SetSources(Image image, IReadOnlyList<Uri>? value) => image.SetValue(SourcesProperty, value);

    private static void OnSourcesChanged(Image image, AvaloniaPropertyChangedEventArgs<IReadOnlyList<Uri>?> args)
    {
        image.AttachedToVisualTree -= OnAttached;
        image.DetachedFromVisualTree -= OnDetached;

        var list = args.NewValue.Value;
        if (list is null || list.Count == 0)
        {
            image.Source = null;
            return;
        }

        image.AttachedToVisualTree += OnAttached;
        image.DetachedFromVisualTree += OnDetached;

        if (Attached.TryGetValue(image, out _))
            _ = LoadAnyAsync(image, list);
    }

    public static Uri? GetSourceUri(Image image) => image.GetValue(SourceUriProperty);

    public static void SetSourceUri(Image image, Uri? value) => image.SetValue(SourceUriProperty, value);

    private static void OnSourceUriChanged(Image image, AvaloniaPropertyChangedEventArgs<Uri?> args)
    {
        image.AttachedToVisualTree -= OnAttached;
        image.DetachedFromVisualTree -= OnDetached;

        if (args.NewValue.Value is null)
        {
            image.Source = null;
            return;
        }

        image.AttachedToVisualTree += OnAttached;
        image.DetachedFromVisualTree += OnDetached;

        if (Attached.TryGetValue(image, out _))
            _ = LoadAsync(image, args.NewValue.Value);
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Image image)
            return;

        Attached.AddOrUpdate(image, None);

        if (GetSources(image) is { Count: > 0 } list)
            _ = LoadAnyAsync(image, list);
        else if (GetSourceUri(image) is { } uri)
            _ = LoadAsync(image, uri);
    }

    /// <summary>按顺序尝试各镜像，第一个成功的就用。</summary>
    private static async Task LoadAnyAsync(Image image, IReadOnlyList<Uri> candidates)
    {
        foreach (var uri in candidates)
        {
            var bitmap = await GetOrLoadAsync(uri).ConfigureAwait(true);
            if (bitmap is null)
                continue;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Attached.TryGetValue(image, out _) && ReferenceEquals(GetSources(image), candidates))
                    image.Source = bitmap;
            });
            return;
        }
    }

    private static readonly object None = new();

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // 离开可视树就断开引用，避免滚动时把整屏图片的位图一直攥在手里
        if (sender is not Image image)
            return;

        Attached.Remove(image);
        image.Source = null;
    }

    private static async Task LoadAsync(Image image, Uri uri)
    {
        var bitmap = await GetOrLoadAsync(uri).ConfigureAwait(true);
        if (bitmap is null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // 下载期间卡片可能已被回收或换了地址，这里确认一下再赋值
            if (Attached.TryGetValue(image, out _) && GetSourceUri(image) == uri)
                image.Source = bitmap;
        });
    }

    private static Task<Bitmap?> GetOrLoadAsync(Uri uri) =>
        Cache.GetOrAdd(uri, static u => LoadCoreAsync(u));

    private static async Task<Bitmap?> LoadCoreAsync(Uri uri)
    {
        try
        {
            await using var stream = await SharedHttp.GetStreamAsync(uri).ConfigureAwait(false);

            // 必须先读进 MemoryStream：Skia 解码需要可寻址的流，
            // 直接把响应的网络流交给 Bitmap 会抛「Unable to load bitmap from provided data」。
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);
            buffer.Position = 0;

            return new Bitmap(buffer);
        }
        catch
        {
            // 网络失败、图不存在、解码失败都视作「没有立绘」，卡片显示占位即可
            return null;
        }
    }
}

/// <summary>共享一个 <see cref="HttpClient"/>，避免每张图都新建连接池。</summary>
internal static class SharedHttp
{
    private static readonly HttpClient Client = Create();

    public static Task<Stream> GetStreamAsync(Uri uri) => Client.GetStreamAsync(uri);

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("arknights-random-team");
        return client;
    }
}
