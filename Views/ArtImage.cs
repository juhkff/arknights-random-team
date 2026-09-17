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
/// 做法：在 <see cref="Image"/> 上挂 <see cref="SourcesProperty"/>（一组候选地址），
/// 控件进入可视树时才发起下载，离开可视树时清空。
///
/// 两处限流，干员一多时才不会卡：
/// 1. 下载并发上限 <see cref="MaxConcurrent"/>。卡片视图用的是 WrapPanel，没有虚拟化，
///    几百张卡会同时进入可视树 —— 不限并发就会瞬间打出几百个请求，
///    既拖慢界面又容易触发 CDN 限流。
/// 2. 位图缓存上限 <see cref="MaxCachedBitmaps"/>，超出后淘汰最久未用的，避免内存一直涨。
///
/// 缓存的是 <see cref="Task{TResult}"/> 而不是 <see cref="Bitmap"/>：
/// 同一个地址被多张卡片同时请求时只下载一次，失败也会被记住，不会反复重试。
/// </summary>
public static class ArtImage
{
    /// <summary>同时进行的下载数上限。</summary>
    private const int MaxConcurrent = 6;

    /// <summary>位图缓存上限（按张数），超出后淘汰最久未用的。</summary>
    private const int MaxCachedBitmaps = 200;

    private static readonly ConcurrentDictionary<Uri, Task<Bitmap?>> Cache = new();

    /// <summary>下载限流。</summary>
    private static readonly SemaphoreSlim Gate = new(MaxConcurrent, MaxConcurrent);

    /// <summary>已成功加载的地址，按最近使用排序，用于超限时淘汰。</summary>
    private static readonly LinkedList<Uri> UsageOrder = new();
    private static readonly HashSet<Uri> Tracked = [];
    private static readonly object UsageLock = new();

    /// <summary>每张图落地后触发一次，供界面刷新统计显示。</summary>
    public static event EventHandler? StatsChanged;

    private static int _cacheHits;
    private static int _networkLoads;

    /// <summary>本次运行中命中缓存的次数（不含重复请求）。</summary>
    public static int CacheHits => Volatile.Read(ref _cacheHits);

    /// <summary>本次运行中真正发起网络的次数。</summary>
    public static int NetworkLoads => Volatile.Read(ref _networkLoads);

    /// <summary>本次运行中从本地磁盘缓存读出的次数。</summary>
    public static int DiskHits => LocalImageStore.Hits;

    /// <summary>记录哪些 Image 当前挂在可视树上（这个 Avalonia 版本没有现成的判断方法）。</summary>
    private static readonly ConditionalWeakTable<Image, object> Attached = new();

    public static readonly AttachedProperty<IReadOnlyList<Uri>?> SourcesProperty =
        AvaloniaProperty.RegisterAttached<Image, IReadOnlyList<Uri>?>("Sources", typeof(ArtImage));

    /// <summary>
    /// 另一规格的候选地址（切换「头像 / 半身像」时显示的那一组）。
    /// 与 <see cref="SourcesProperty"/> 一起预载，切换只是换显示，不必重新下载 ——
    /// 否则切一次就要重载所有卡片，人一多界面会明显卡住。
    /// </summary>
    public static readonly AttachedProperty<IReadOnlyList<Uri>?> AlternateSourcesProperty =
        AvaloniaProperty.RegisterAttached<Image, IReadOnlyList<Uri>?>("AlternateSources", typeof(ArtImage));

    static ArtImage()
    {
        SourcesProperty.Changed.AddClassHandler<Image, IReadOnlyList<Uri>?>(OnSourcesChanged);
        AlternateSourcesProperty.Changed.AddClassHandler<Image, IReadOnlyList<Uri>?>(OnAlternateChanged);
    }

    public static IReadOnlyList<Uri>? GetSources(Image image) => image.GetValue(SourcesProperty);

    public static void SetSources(Image image, IReadOnlyList<Uri>? value) => image.SetValue(SourcesProperty, value);

    public static IReadOnlyList<Uri>? GetAlternateSources(Image image) => image.GetValue(AlternateSourcesProperty);

    public static void SetAlternateSources(Image image, IReadOnlyList<Uri>? value) =>
        image.SetValue(AlternateSourcesProperty, value);

    /// <summary>把另一规格也预载进来（后台排队，不阻塞当前显示）。</summary>
    private static void OnAlternateChanged(Image image, AvaloniaPropertyChangedEventArgs<IReadOnlyList<Uri>?> args)
    {
        if (args.NewValue.Value is not { Count: > 0 } list)
            return;

        foreach (var uri in list)
            _ = GetOrLoadAsync(uri);
    }

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

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Image image)
            return;

        Attached.AddOrUpdate(image, new object());

        if (GetAlternateSources(image) is { Count: > 0 } alt)
        {
            foreach (var uri in alt)
                _ = GetOrLoadAsync(uri);
        }

        if (GetSources(image) is { Count: > 0 } list)
            _ = LoadAnyAsync(image, list);
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // 离开可视树就断开引用。位图仍留在缓存里（所以不会重新走网络），
        // 但这里不再持有它 —— 否则 200 张半身像会长期占约 49MB 内存。
        if (sender is not Image image)
            return;

        Attached.Remove(image);
        image.Source = null;
    }

    /// <summary>按顺序尝试各候选地址，第一个成功的就用。</summary>
    private static async Task LoadAnyAsync(Image image, IReadOnlyList<Uri> candidates)
    {
        foreach (var uri in candidates)
        {
            var bitmap = await GetOrLoadAsync(uri).ConfigureAwait(true);
            if (bitmap is null)
                continue;

            // 让头部的加载统计跟着刷新
            StatsChanged?.Invoke(null, EventArgs.Empty);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 下载期间卡片可能已被回收或换了地址，这里确认一下再赋值
                if (Attached.TryGetValue(image, out _) && ReferenceEquals(GetSources(image), candidates))
                    image.Source = bitmap;
            });
            return;
        }
    }

    private static Task<Bitmap?> GetOrLoadAsync(Uri uri)
    {
        if (Cache.TryGetValue(uri, out var existing))
        {
            if (existing.IsCompletedSuccessfully && existing.Result is not null)
                Interlocked.Increment(ref _cacheHits);
            Touch(uri);
            return existing;
        }

        var task = LoadThrottledAsync(uri);
        // 并发请求同一地址时，先到的那个任务胜出，后来的复用同一个任务
        task = Cache.GetOrAdd(uri, task);
        return task;
    }

    private static async Task<Bitmap?> LoadThrottledAsync(Uri uri)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Interlocked.Increment(ref _networkLoads);
            var bitmap = await LoadCoreAsync(uri).ConfigureAwait(false);
            if (bitmap is not null)
                Touch(uri);
            return bitmap;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<Bitmap?> LoadCoreAsync(Uri uri)
    {
        try
        {
            var bytes = await ReadBytesAsync(uri).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
                return null;

            // 解码需要可寻址的流：Skia 对普通网络流会抛
            // 「Unable to load bitmap from provided data」，所以统一先拿到字节。
            using var buffer = new MemoryStream(bytes);
            return new Bitmap(buffer);
        }
        catch
        {
            // 网络失败、图不存在、解码失败都视作「没有立绘」，卡片显示占位即可
            return null;
        }
    }

    /// <summary>取图片字节：嵌入资源直接读，其余先查本地缓存，再走网络并回写缓存。</summary>
    private static async Task<byte[]?> ReadBytesAsync(Uri uri)
    {
        // 嵌入资源（avares://）不走网络：HttpClient 不认这个协议，
        // 必须先分流，否则会先失败一次再回退，白白等一个超时。
        if (uri.Scheme == "avares")
        {
            await using var asset = Avalonia.Platform.AssetLoader.Open(uri);
            using var assetBuffer = new MemoryStream();
            await asset.CopyToAsync(assetBuffer).ConfigureAwait(false);
            return assetBuffer.ToArray();
        }

        // 本地磁盘缓存：重启程序后也还在
        if (await LocalImageStore.TryReadAsync(uri).ConfigureAwait(false) is { } cached)
            return cached;

        await using var stream = await SharedHttp.GetStreamAsync(uri).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);

        var bytes = buffer.ToArray();
        // 回写不阻塞显示
        _ = LocalImageStore.WriteAsync(uri, bytes);
        return bytes;
    }

    /// <summary>登记一次使用，并在超出上限时淘汰最久未用的位图。</summary>
    private static void Touch(Uri uri)
    {
        lock (UsageLock)
        {
            if (Tracked.Contains(uri))
                UsageOrder.Remove(uri);

            UsageOrder.AddLast(uri);
            Tracked.Add(uri);

            while (UsageOrder.Count > MaxCachedBitmaps)
            {
                var oldest = UsageOrder.First!.Value;
                UsageOrder.RemoveFirst();
                Tracked.Remove(oldest);

                if (Cache.TryRemove(oldest, out var task) &&
                    task.IsCompletedSuccessfully &&
                    task.Result is { } bitmap)
                {
                    bitmap.Dispose();
                }
            }
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
