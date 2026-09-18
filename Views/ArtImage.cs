using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
    private const int DefaultMaxCachedBitmaps = 200;

    /// <summary>
    /// 邻近预取范围（逻辑像素）。可见区外再往前一屏左右就开始下载，
    /// 滚动时不会看到成片空位；再远的不排队。
    /// </summary>
    private const double PrefetchMargin = 240;

    /// <summary>
    /// 位图缓存上限。允许用环境变量覆盖，方便把上限调到很小以压测淘汰路径
    /// （淘汰曾经因为误释放位图导致 ObjectDisposedException）。
    /// </summary>
    private static int MaxCachedBitmaps
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("ARTIMAGE_CACHE_LIMIT");
            return int.TryParse(raw, out var v) && v > 0 ? v : DefaultMaxCachedBitmaps;
        }
    }

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
    private static int _failedLoads;

    /// <summary>本次运行中命中缓存的次数（不含重复请求）。</summary>
    public static int CacheHits => Volatile.Read(ref _cacheHits);

    /// <summary>本次运行中真正发起网络的次数。</summary>
    public static int NetworkLoads => Volatile.Read(ref _networkLoads);

    /// <summary>本次运行中从本地磁盘缓存读出的次数。</summary>
    public static int DiskHits => LocalImageStore.Hits;

    /// <summary>候选地址均失败的次数。有候选 URL 不等于加载成功。</summary>
    public static int FailedLoads => Volatile.Read(ref _failedLoads);

    public static readonly AttachedProperty<ArtLoadState> LoadStateProperty =
        AvaloniaProperty.RegisterAttached<Image, ArtLoadState>("LoadState", typeof(ArtImage));

    /// <summary>记录哪些 Image 当前挂在可视树上（这个 Avalonia 版本没有现成的判断方法）。</summary>
    private static readonly ConditionalWeakTable<Image, object> Attached = new();

    /// <summary>每张图最近一次算出的可见区，用来判断是否在「可见 + 邻近」范围内。</summary>
    private static readonly ConditionalWeakTable<Image, ViewportState> Viewports = new();

    private sealed class ViewportState
    {
        public Rect Viewport { get; set; }

        /// <summary>是否收到过可见区上报。没收到时不做限制，避免平台不支持就永远不加载。</summary>
        public bool Received { get; set; }
    }

    public static readonly AttachedProperty<CancellationTokenSource?> LoadCtsProperty =
        AvaloniaProperty.RegisterAttached<Image, CancellationTokenSource?>("LoadCts", typeof(ArtImage));

    public static readonly AttachedProperty<IReadOnlyList<Uri>?> SourcesProperty =
        AvaloniaProperty.RegisterAttached<Image, IReadOnlyList<Uri>?>("Sources", typeof(ArtImage));

    /// <summary>
    /// 另一规格的候选地址。切换视图时由绑定换到 <see cref="SourcesProperty"/> 再加载，
    /// 不再在每张卡上同时预载另一套图，避免切入头像模式就拉全部立绘。
    /// </summary>
    public static readonly AttachedProperty<IReadOnlyList<Uri>?> AlternateSourcesProperty =
        AvaloniaProperty.RegisterAttached<Image, IReadOnlyList<Uri>?>("AlternateSources", typeof(ArtImage));

    static ArtImage()
    {
        SourcesProperty.Changed.AddClassHandler<Image, IReadOnlyList<Uri>?>(OnSourcesChanged);
    }

    public static IReadOnlyList<Uri>? GetSources(Image image) => image.GetValue(SourcesProperty);

    public static void SetSources(Image image, IReadOnlyList<Uri>? value) => image.SetValue(SourcesProperty, value);

    public static IReadOnlyList<Uri>? GetAlternateSources(Image image) => image.GetValue(AlternateSourcesProperty);

    public static void SetAlternateSources(Image image, IReadOnlyList<Uri>? value) =>
        image.SetValue(AlternateSourcesProperty, value);

    public static ArtLoadState GetLoadState(Image image) => image.GetValue(LoadStateProperty);

    public static void SetLoadState(Image image, ArtLoadState value) => image.SetValue(LoadStateProperty, value);

    /// <summary>
    /// 手动重试当前候选地址。
    ///
    /// 失败结果会被缓存下来（避免对同一批 404 反复发请求），所以重试必须先丢掉这些
    /// 「已完成但拿不到图」的缓存项，再重新按顺序加载；否则重试只是白跑一趟。
    /// </summary>
    public static void Retry(Image image)
    {
        if (GetSources(image) is not { Count: > 0 } list)
            return;

        foreach (var uri in list)
        {
            if (!Cache.TryGetValue(uri, out var cached) || !cached.IsCompleted)
                continue;

            if (!cached.IsCompletedSuccessfully || cached.Result is null)
                Cache.TryRemove(uri, out _);
        }

        if (Attached.TryGetValue(image, out _))
        {
            StartLoad(image, list);
            return;
        }

        SetLoadState(image, ArtLoadState.Loading);
    }

    private static void OnSourcesChanged(Image image, AvaloniaPropertyChangedEventArgs<IReadOnlyList<Uri>?> args)
    {
        image.AttachedToVisualTree -= OnAttached;
        image.DetachedFromVisualTree -= OnDetached;
        CancelLoad(image);
        image.Source = null;

        var list = args.NewValue.Value;
        if (list is null || list.Count == 0)
        {
            SetLoadState(image, ArtLoadState.Empty);
            return;
        }

        image.AttachedToVisualTree += OnAttached;
        image.DetachedFromVisualTree += OnDetached;
        // 有图源但还没轮到下载时是「排队中」，与「加载中」必须能区分（方案 §3.3）。
        SetLoadState(image, ArtLoadState.Queued);

        if (Attached.TryGetValue(image, out _))
        {
            ScheduleVisibleLoad(image, list);
            return;
        }

        // 图已经挂在可视树上、却是第一次拿到图源（绑定晚于挂载）：补登记再排队。
        // 不做这一步的话它不会收到挂载事件，状态会永远停在「排队中」，图永远不出现。
        if (TopLevel.GetTopLevel(image) is not null)
        {
            Track(image);
            Dispatcher.UIThread.Post(() => LoadWhenViewportUnknown(image, list), DispatcherPriority.Background);
            ScheduleVisibleLoad(image, list);
        }
    }

    /// <summary>
    /// 把一张图纳入跟踪：登记「已挂载」并订阅可见区事件。
    /// 先订阅再登记没有任何顺序要求，但必须幂等——挂载回调与图源回调都可能调它。
    /// </summary>
    private static void Track(Image image)
    {
        Attached.AddOrUpdate(image, new object());
        image.EffectiveViewportChanged -= OnEffectiveViewportChanged;
        image.EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Image image)
            return;

        Track(image);

        if (GetSources(image) is { Count: > 0 } list)
        {
            SetLoadState(image, ArtLoadState.Queued);

            // 兜底：如果这个平台/版本压根不上报可见区，就不能让图永远停在占位。
            // 等到布局之后仍没有收到任何可见区信息，就退回「挂上即加载」的老行为。
            Dispatcher.UIThread.Post(() => LoadWhenViewportUnknown(image, list), DispatcherPriority.Background);
        }
        else
        {
            SetLoadState(image, ArtLoadState.Empty);
        }
    }

    private static void LoadWhenViewportUnknown(Image image, IReadOnlyList<Uri> list)
    {
        if (!Attached.TryGetValue(image, out _) ||
            !ReferenceEquals(GetSources(image), list) ||
            HasViewportInfo(image))
            return;

        if (image.GetValue(LoadCtsProperty) is null)
            StartLoad(image, list);
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // 离开可视树就断开引用。位图仍留在缓存里（所以不会重新走网络），
        // 但这里不再持有它 —— 否则 200 张半身像会长期占约 49MB 内存。
        if (sender is not Image image)
            return;

        image.EffectiveViewportChanged -= OnEffectiveViewportChanged;
        Viewports.Remove(image);
        CancelLoad(image);
        Attached.Remove(image);
        image.Source = null;
    }

    /// <summary>
    /// 可见区变化：只给「当前可见 + 邻近一屏」的图排队下载。
    ///
    /// 原来的做法是图片一挂上可视树就全部排队，一百多张卡片同时抢 6 个并发名额，
    /// 后排的图要等很久才出现。现在按 <see cref="Layoutable.EffectiveViewportChanged"/>
    /// 给出的实际可见区决定谁先下，滚出范围的主动让出名额（位图仍在缓存里）。
    /// </summary>
    private static void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (sender is not Image image)
            return;

        Viewports.AddOrUpdate(image, new ViewportState { Viewport = e.EffectiveViewport, Received = true });
        if (GetSources(image) is { Count: > 0 } list)
            UpdateVisibleLoad(image, list);
    }

    /// <summary>先把可见区判定排到布局之后，避免刚换图源时还没量出尺寸就下结论。</summary>
    private static void ScheduleVisibleLoad(Image image, IReadOnlyList<Uri> list)
    {
        if (HasViewportInfo(image))
        {
            UpdateVisibleLoad(image, list);
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (Attached.TryGetValue(image, out _) && ReferenceEquals(GetSources(image), list))
                    UpdateVisibleLoad(image, list);
            },
            DispatcherPriority.Loaded);
    }

    private static void UpdateVisibleLoad(Image image, IReadOnlyList<Uri> list)
    {
        if (IsWithinPrefetchRange(image))
        {
            if (image.GetValue(LoadCtsProperty) is null)
                StartLoad(image, list);
            return;
        }

        // 滚出预取范围：让出并发名额，回到「排队中」，滚回来时命中缓存立即显示。
        CancelLoad(image);
        if (GetLoadState(image) == ArtLoadState.Loading)
            SetLoadState(image, ArtLoadState.Queued);
    }

    private static bool IsWithinPrefetchRange(Image image)
    {
        if (!Viewports.TryGetValue(image, out var state))
            return false;

        // 没收到过可见区上报时不做限制（配合 OnAttached 的兜底），
        // 收到过就按实际可见区判断：空可见区说明完全在屏幕外。
        if (!state.Received)
            return true;

        var bounds = new Rect(image.Bounds.Size);
        bounds.Inflate(PrefetchMargin);
        return bounds.Intersects(state.Viewport);
    }

    /// <summary>是否收到过该图的可见区上报。没收到过就不能拿「可见区为空」当结论。</summary>
    private static bool HasViewportInfo(Image image) =>
        Viewports.TryGetValue(image, out var state) && state.Received;

    private static void CancelLoad(Image image)
    {
        if (image.GetValue(LoadCtsProperty) is not { } cts)
            return;

        image.SetValue(LoadCtsProperty, null);
        cts.Cancel();
        cts.Dispose();
    }

    private static void StartLoad(Image image, IReadOnlyList<Uri> list)
    {
        CancelLoad(image);
        var cts = new CancellationTokenSource();
        image.SetValue(LoadCtsProperty, cts);
        _ = LoadAnyAsync(image, list, cts.Token);
    }

    /// <summary>按顺序尝试各候选地址，第一个成功的就用。</summary>
    private static async Task LoadAnyAsync(Image image, IReadOnlyList<Uri> candidates, CancellationToken ct)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => SetLoadState(image, ArtLoadState.Loading));

            foreach (var uri in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var bitmap = await GetOrLoadAsync(uri, ct).ConfigureAwait(true);
                if (bitmap is null)
                    continue;

                StatsChanged?.Invoke(null, EventArgs.Empty);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (Attached.TryGetValue(image, out _) && ReferenceEquals(GetSources(image), candidates))
                    {
                        image.Source = bitmap;
                        SetLoadState(image, ArtLoadState.Loaded);
                    }
                });
                return;
            }

            Interlocked.Increment(ref _failedLoads);
            StatsChanged?.Invoke(null, EventArgs.Empty);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Attached.TryGetValue(image, out _) && ReferenceEquals(GetSources(image), candidates))
                {
                    image.Source = null;
                    SetLoadState(image, ArtLoadState.Failed);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // 切视图或离开页面时主动取消，避免占着下载名额。
        }
    }

    private static async Task<Bitmap?> GetOrLoadAsync(Uri uri, CancellationToken ct)
    {
        if (Cache.TryGetValue(uri, out var existing))
        {
            if (existing.IsCompletedSuccessfully && existing.Result is not null)
                Interlocked.Increment(ref _cacheHits);
            Touch(uri);
            return await existing.WaitAsync(ct).ConfigureAwait(false);
        }

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Cache.TryGetValue(uri, out existing))
            {
                if (existing.IsCompletedSuccessfully && existing.Result is not null)
                    Interlocked.Increment(ref _cacheHits);
                Touch(uri);
                return await existing.ConfigureAwait(false);
            }

            Interlocked.Increment(ref _networkLoads);
            var load = LoadCoreAsync(uri);
            Cache[uri] = load;
            var bitmap = await load.ConfigureAwait(false);
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
            // 全身立绘约 1024 宽，列表里同时解码上百张会占几百 MB。
            // 卡片显示宽度只有 180，解码到 512 足够清晰。
            if (PngWidth(bytes) is > 512)
                return Bitmap.DecodeToWidth(buffer, 512);

            return new Bitmap(buffer);
        }
        catch
        {
            // 网络失败、图不存在、解码失败都视作「没有立绘」，卡片显示占位即可
            return null;
        }
    }

    /// <summary>PNG IHDR 里的宽度；不是 PNG 或太短则返回 null。</summary>
    private static int? PngWidth(byte[] bytes)
    {
        if (bytes.Length < 24 || bytes[0] != 0x89 || bytes[1] != (byte)'P')
            return null;

        return (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
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

                // 只把它移出缓存，绝不 Dispose 位图：
                // 缓存与 Image.Source 是两个独立引用，被淘汰的位图可能还挂在
                // 某个可见的 Image 上，一旦释放，下次渲染就会抛 ObjectDisposedException。
                // 内存上界由缓存条目数保证，移出后由 GC 回收。
                Cache.TryRemove(oldest, out _);
            }
        }
    }
}

public enum ArtLoadState
{
    /// <summary>没有可用图源（例如手动录入、拼不出地址的干员）。</summary>
    Empty,

    /// <summary>有图源但还没轮到下载：在预取范围外，或并发名额已满。</summary>
    Queued,

    /// <summary>正在下载或解码。</summary>
    Loading,

    /// <summary>已成功显示。</summary>
    Loaded,

    /// <summary>候选地址全部失败。</summary>
    Failed
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
