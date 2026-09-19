using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace arknights_random_team.Views;

/// <summary>
/// Loads operator art for attached images near the viewport. Work is shared per URI, network
/// concurrency is bounded, and successful bitmaps are retained within count and byte budgets.
/// </summary>
public static class ArtImage
{
    /// <summary>同时进行的下载数上限。浏览器并发更保守，避免 WASM 解码把内存顶满。</summary>
    private static readonly int MaxConcurrent = OperatingSystem.IsBrowser() ? 3 : 6;

    // A 180x360 portrait plus a 180x180 avatar costs about 380 KiB per operator.
    // Desktop can retain a full roster without creating offscreen card controls.
    // Browser only keeps what the viewport recently used; full-roster warmup is disabled.
    private static readonly int DefaultMaxCachedBitmaps = OperatingSystem.IsBrowser() ? 256 : 2048;
    private static readonly long DefaultMaxCachedBytes =
        (OperatingSystem.IsBrowser() ? 48L : 256L) * 1024 * 1024;

    /// <summary>
    /// 邻近预取范围（逻辑像素）。可见区外再往前一屏左右就开始下载，
    /// 滚动时不会看到成片空位；再远的不排队。
    /// </summary>
    private const double PrefetchMargin = 240;

    // Cache limits are process-wide and fixed at startup.
    private static readonly int MaxCachedBitmaps = ReadLimit("ARTIMAGE_CACHE_LIMIT", DefaultMaxCachedBitmaps);
    private static readonly long MaxCachedBytes = ReadByteLimit("ARTIMAGE_CACHE_BYTES", DefaultMaxCachedBytes);


    private static int ReadLimit(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;

    private static long ReadByteLimit(string name, long fallback) =>
        long.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;

    /// <summary>Shared in-flight and successful loads, one entry per URI.</summary>
    private static readonly ConcurrentDictionary<Uri, LoadEntry<Bitmap>> Cache = new();

    /// <summary>下载限流。</summary>
    private static readonly SemaphoreSlim Gate = new(MaxConcurrent, MaxConcurrent);
    // Visible controls only. Background warming uses PrefetchGate so it cannot exhaust
    // the slots that the operator list needs when it first appears.
    private static readonly int ForegroundSlots = OperatingSystem.IsBrowser() ? 2 : 4;
    private static readonly SemaphoreSlim ForegroundGate = new(ForegroundSlots, ForegroundSlots);
    private static readonly SemaphoreSlim PrefetchGate = new(1, 1);
    private static readonly ConcurrentDictionary<Uri, LoadEntry<byte[]>> Downloads = new();
    private static int _foregroundLoads;
    private static long _lastActivity;
    private static readonly SemaphoreSlim DecodeGate = new(1, 1);
    private const int InteractionQuietMs = 120;
    private static bool IsInteracting => Environment.TickCount64 - Volatile.Read(ref _lastActivity) < InteractionQuietMs;

    internal static void NotifyInteraction() => Volatile.Write(ref _lastActivity, Environment.TickCount64);

    private static async Task WaitForIdleAsync(CancellationToken ct)
    {
        // Yield to visible loads and in-progress input. Do not keep waiting after the last
        // click: that delayed warming until the operator list was opened and then sat idle.
        while (Volatile.Read(ref _foregroundLoads) != 0 || IsInteracting)
            await Task.Delay(50, ct).ConfigureAwait(false);
    }

    private sealed record ResolvedSource(IReadOnlyList<Uri> Candidates, Uri Uri);
    private static readonly ConcurrentDictionary<Uri, ResolvedSource> ResolvedSources = new();
    private static IReadOnlyList<Uri>[] _preloadGroups = [];
    private static int _preloadFinished;
    private static int _preloadLimited;

    internal static bool PreloadCapacityReached => Volatile.Read(ref _preloadLimited) != 0;
    public static string PreloadSummary
    {
        get
        {
            var groups = Volatile.Read(ref _preloadGroups);
            if (groups.Length == 0)
                return OperatingSystem.IsBrowser() ? "可见区域按需加载" : "图片就绪 0/0";
            var ready = groups.Count(IsPrepared);
            var suffix = ready == groups.Length ? "" : PreloadCapacityReached ? " · 已达缓存预算" :
                Volatile.Read(ref _preloadFinished) == 0 ? " · 准备中" : $" · {groups.Length - ready} 张待加载或重试";
            return $"图片就绪 {ready}/{groups.Length}{suffix}";
        }
    }

    public static string CacheSummary
    {
        get
        {
            lock (UsageLock)
                return $"内存图片 {Tracked.Count} 张 · {_trackedBytes / 1048576d:F1} / {MaxCachedBytes / 1048576d:F0} MiB";
        }
    }

    internal static void ReportPreloadProgress(bool finished)
    {
        Dispatcher.UIThread.VerifyAccess();
        Volatile.Write(ref _preloadFinished, finished ? 1 : 0);
        NotifyStatsChanged();
    }

    internal static void SetRosterSources(IEnumerable<IReadOnlyList<Uri>> sources)
    {
        var groups = sources.Where(group => group.Count > 0).ToArray();
        var current = groups.SelectMany(group => group).ToHashSet();
        lock (UsageLock)
        {
            Volatile.Write(ref _preloadGroups, groups);
            Volatile.Write(ref _preloadFinished, 0);
            Volatile.Write(ref _preloadLimited, 0);
            var currentGroups = groups.ToLookup(group => group[0]);
            foreach (var pair in ResolvedSources.ToArray())
                if (!currentGroups[pair.Key].Any(group => group.SequenceEqual(pair.Value.Candidates)))
                    ResolvedSources.TryRemove(pair);

            // Old stage fallbacks may remain cached, but only sources selected by a current
            // group are protected when admission needs space. A new elite image can replace
            // its old base image without evicting another operator from the warmed roster.
            foreach (var uri in Tracked.Keys.Where(uri => uri.Scheme != "avares" && !current.Contains(uri)).ToArray())
                RemoveCached(uri);
        }
    }

    internal static bool IsPrepared(IReadOnlyList<Uri> candidates) =>
        TryGetPrepared(candidates, out _, out _, touch: false);

    internal static bool RememberPreparedSource(IReadOnlyList<Uri> candidates)
    {
        if (!TryGetPrepared(candidates, out _, out var uri, touch: false))
            return false;
        RememberSource(candidates, uri!);
        return true;
    }

    /// <summary>Use the same decoded bitmap as foreground controls, with one idle background load.</summary>
    internal static async Task<bool> PrefetchAsync(IReadOnlyList<Uri> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0)
            return true;
        await PrefetchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (RememberPreparedSource(candidates))
                return true;
            await Task.Delay(25, ct).ConfigureAwait(false);
            foreach (var uri in candidates)
            {
                ct.ThrowIfCancellationRequested();
                await WaitForIdleAsync(ct).ConfigureAwait(false);
                var bitmap = await GetOrLoadCoreAsync(uri, ct, background: true).ConfigureAwait(false);
                if (bitmap is null)
                    continue;
                // A successful decode can still be rejected by the cache budget. Stop here;
                // mirrors cannot improve capacity and must not evict the already-warmed roster.
                if (!TryGetBitmap(uri, out _, touch: false))
                    return false;
                RememberSource(candidates, uri);
                return true;
            }
            return false;
        }
        finally
        {
            PrefetchGate.Release();
        }
    }

    private static void RememberSource(IReadOnlyList<Uri> candidates, Uri uri)
    {
        if (candidates.Count == 0)
            return;
        var key = candidates[0];
        if (ResolvedSources.TryGetValue(key, out var current) &&
            current.Uri == uri && current.Candidates.SequenceEqual(candidates))
            return;
        lock (UsageLock)
        {
            if (ResolvedSources.TryGetValue(key, out current) &&
                current.Uri == uri && current.Candidates.SequenceEqual(candidates))
                return;
            ResolvedSources[key] = new ResolvedSource(candidates, uri);
            if (ResolvedSources.Count > MaxCachedBitmaps * 2)
                foreach (var oldKey in ResolvedSources.Keys.Take(ResolvedSources.Count - MaxCachedBitmaps * 2))
                    ResolvedSources.TryRemove(oldKey, out _);
        }
    }

    private static bool TryGetPrepared(
        IReadOnlyList<Uri> candidates, out Bitmap? bitmap, out Uri? selected, bool touch = true)
    {
        bitmap = null;
        selected = null;
        if (candidates.Count == 0)
            return false;
        if (ResolvedSources.TryGetValue(candidates[0], out var resolved) &&
            resolved.Candidates.SequenceEqual(candidates) && TryGetBitmap(resolved.Uri, out bitmap, touch))
        {
            selected = resolved.Uri;
            return true;
        }
        foreach (var uri in candidates)
        {
            if (TryGetBitmap(uri, out bitmap, touch))
            {
                selected = uri;
                return true;
            }
            // Do not substitute an old default-stage image for a not-yet-loaded elite image.
            if (!FailedCache.ContainsKey(uri))
                break;
        }
        return false;
    }

    private static HashSet<Uri> GetProtectedRosterSources()
    {
        var sources = new HashSet<Uri>();
        foreach (var group in Volatile.Read(ref _preloadGroups))
            if (TryGetPrepared(group, out _, out var selected, touch: false))
                sources.Add(selected!);
        return sources;
    }

    private static bool TryGetBitmap(Uri uri, out Bitmap? bitmap, bool touch)
    {
        bitmap = null;
        if (!Cache.TryGetValue(uri, out var entry) || !entry.Work.IsValueCreated ||
            !entry.Work.Value.IsCompletedSuccessfully || entry.Work.Value.Result is not { } loaded)
            return false;
        bitmap = loaded;
        if (touch)
            TouchIfCached(uri, entry, loaded);
        return true;
    }

    /// <summary>已成功加载的地址，按最近使用排序，用于超限时淘汰；值是该位图占用的近似字节数。</summary>
    private static readonly LinkedList<Uri> UsageOrder = new();
    private static readonly Dictionary<Uri, long> Tracked = [];
    private static readonly object UsageLock = new();
    private static long _trackedBytes;

    /// <summary>每张图落地后触发一次，供界面刷新统计显示。</summary>
    public static event EventHandler? StatsChanged;

    private static int _cacheHits;
    private static int _networkLoads;
    private static int _failedLoads;

    private static readonly ConcurrentDictionary<Uri, byte> FailedCache = new();
    private static readonly HashSet<Control> FailedImages = [];
    private static readonly object FailedImagesLock = new();
    private const int MaxFailedCacheEntries = 512;

    /// <summary>本次运行中命中缓存的次数（不含重复请求）。</summary>
    public static int CacheHits => Volatile.Read(ref _cacheHits);

    /// <summary>本次运行中真正发起 HTTP 的次数（内嵌资源与磁盘命中的快速路径不计入）。</summary>
    public static int NetworkLoads => Volatile.Read(ref _networkLoads);

    /// <summary>本次运行中从本地磁盘缓存读出的次数。</summary>
    public static int DiskHits => LocalImageStore.Hits;

    /// <summary>累计的加载失败次数，仅供诊断：重试成功也不会减少。</summary>
    public static int FailedLoads => Volatile.Read(ref _failedLoads);

    /// <summary>当前可视树中最终加载失败的图片数。</summary>
    public static int CurrentFailedLoads
    {
        get
        {
            lock (FailedImagesLock)
                return FailedImages.Count;
        }
    }

    public static readonly AttachedProperty<ArtLoadState> LoadStateProperty =
        AvaloniaProperty.RegisterAttached<Control, ArtLoadState>("LoadState", typeof(ArtImage));

    /// <summary>记录哪些 Image 当前挂在可视树上（这个 Avalonia 版本没有现成的判断方法）。</summary>
    private static readonly ConditionalWeakTable<Control, object> Attached = new();

    /// <summary>每张图最近一次算出的可见区，用来判断是否在「可见 + 邻近」范围内。</summary>
    private static readonly ConditionalWeakTable<Control, ViewportState> Viewports = new();

    private sealed record ViewportState(Rect Viewport);

    private sealed class LoadEntry<T> where T : class
    {
        private readonly CancellationTokenSource _cts = new();
        private int _waiters;
        private bool _accepting = true;
        public volatile bool ForegroundRequested;

        public LoadEntry(Func<CancellationToken, Task<T?>> load)
        {
            var token = _cts.Token;
            Work = new Lazy<Task<T?>>(
                () => Task.Run(() => load(token)),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public Lazy<Task<T?>> Work { get; }

        public bool TryAcquire(bool foreground = false)
        {
            lock (this)
            {
                if (!_accepting)
                    return false;
                if (foreground)
                    ForegroundRequested = true;
                _waiters++;
                return true;
            }
        }

        public bool Release(bool retainCompleted, out bool lastWaiter)
        {
            lock (this)
            {
                lastWaiter = --_waiters == 0;
                if (!lastWaiter)
                    return false;
                var completed = Work.IsValueCreated && Work.Value.IsCompleted;
                if (!retainCompleted || !completed)
                    _accepting = false;
                return !completed;
            }
        }

        public void Cancel()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }

    private static readonly AttachedProperty<CancellationTokenSource?> LoadCtsProperty =
        AvaloniaProperty.RegisterAttached<Control, CancellationTokenSource?>("LoadCts", typeof(ArtImage));

    public static readonly AttachedProperty<IReadOnlyList<Uri>?> SourcesProperty =
        AvaloniaProperty.RegisterAttached<Control, IReadOnlyList<Uri>?>("Sources", typeof(ArtImage));

    static ArtImage()
    {
        SourcesProperty.Changed.AddClassHandler<Control, IReadOnlyList<Uri>?>(OnSourcesChanged);
    }

    public static IReadOnlyList<Uri>? GetSources(Control image) => image.GetValue(SourcesProperty);

    public static void SetSources(Control image, IReadOnlyList<Uri>? value) => image.SetValue(SourcesProperty, value);

    public static ArtLoadState GetLoadState(Control image) => image.GetValue(LoadStateProperty);

    public static void SetLoadState(Control image, ArtLoadState value)
    {
        image.SetValue(LoadStateProperty, value);

        bool changed;
        lock (FailedImagesLock)
            changed = value == ArtLoadState.Failed ? FailedImages.Add(image) : FailedImages.Remove(image);
        if (changed)
            NotifyStatsChanged();
    }

    /// <summary>
    /// 手动重试当前候选地址。
    ///
    /// 失败结果会被缓存下来（避免对同一批 404 反复发请求），所以重试必须先丢掉这些
    /// 「已完成但拿不到图」的缓存项，再重新按顺序加载；否则重试只是白跑一趟。
    /// </summary>
    public static void Retry(Control image)
    {
        if (GetSources(image) is not { Count: > 0 } list)
            return;

        lock (UsageLock)
            ResolvedSources.TryRemove(list[0], out _);
        foreach (var uri in list)
        {
            var cacheFailed = Cache.TryGetValue(uri, out var entry) &&
                              entry.Work.IsValueCreated &&
                              entry.Work.Value.IsCompleted &&
                              (!entry.Work.Value.IsCompletedSuccessfully || entry.Work.Value.Result is null);
            if (cacheFailed)
                Cache.TryRemove(new KeyValuePair<Uri, LoadEntry<Bitmap>>(uri, entry!));

            if (FailedCache.TryRemove(uri, out _) || cacheFailed)
                LocalImageStore.Invalidate(uri);
        }

        if (Attached.TryGetValue(image, out _))
        {
            StartLoad(image, list);
            return;
        }

        SetLoadState(image, ArtLoadState.Loading);
    }

    // UI work is coalesced per recycled control and bounded per tick. These collections and
    // the timer belong to the UI thread; workers only post completed results at Background priority.
    private static readonly Dictionary<Control, IReadOnlyList<Uri>> PendingStarts = new();
    private static readonly Dictionary<Control, PendingResult> PendingResults = new();
    private static DispatcherTimer? _uiTimer;
    private static bool _statsPending;
    private static long _lastStats;
    private sealed record PendingResult(IReadOnlyList<Uri> Sources, CancellationTokenSource Owner, Bitmap? Bitmap);

    private static void SetSource(Control control, Bitmap? bitmap)
    {
        switch (control)
        {
            case ArtBitmap art:
                art.Source = bitmap;
                break;
            case Image image:
                RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
                image.Source = bitmap;
                break;
        }
    }

    private static void EnsureUiTimer()
    {
        _uiTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background,
            (_, _) => ProcessPendingImages());
        if (!_uiTimer.IsEnabled)
            _uiTimer.Start();
    }

    private static void ProcessPendingImages()
    {
        var watch = Stopwatch.StartNew();
        var remaining = 2;
        // These bitmaps are already decoded. ArtBitmap changes only painting, not layout.
        foreach (var (image, result) in PendingResults.ToArray())
        {
            PendingResults.Remove(image);
            if (!IsCurrentLoad(image, result.Sources, result.Owner))
                continue;
            SetSource(image, result.Bitmap);
            if (result.Bitmap is null)
                Interlocked.Increment(ref _failedLoads);
            SetLoadState(image, result.Bitmap is null ? ArtLoadState.Failed : ArtLoadState.Loaded);
            NotifyStatsChanged();
            if (--remaining == 0 || watch.Elapsed.TotalMilliseconds >= 2)
                break;
        }

        if (remaining > 0 && watch.Elapsed.TotalMilliseconds < 2)
        {
            foreach (var (image, sources) in PendingStarts.ToArray())
            {
                if (!Attached.TryGetValue(image, out _) || !ReferenceEquals(GetSources(image), sources) ||
                    HasViewportInfo(image) && !IsWithinPrefetchRange(image))
                {
                    PendingStarts.Remove(image);
                    continue;
                }
                if (!TryApplyPrepared(image, sources))
                {
                    if (IsInteracting)
                        continue;
                    PendingStarts.Remove(image);
                    StartLoadNow(image, sources);
                }
                if (--remaining == 0 || watch.Elapsed.TotalMilliseconds >= 2)
                    break;
            }
        }

        if (_statsPending && !IsInteracting && Environment.TickCount64 - _lastStats >= 250)
        {
            _statsPending = false;
            _lastStats = Environment.TickCount64;
            StatsChanged?.Invoke(null, EventArgs.Empty);
        }
        if (PendingStarts.Count == 0 && PendingResults.Count == 0 && !_statsPending)
            _uiTimer!.Stop();
    }

    private static void NotifyStatsChanged()
    {
        _statsPending = true;
        EnsureUiTimer();
    }

    private static void OnSourcesChanged(Control image, AvaloniaPropertyChangedEventArgs<IReadOnlyList<Uri>?> args)
    {
        image.AttachedToVisualTree -= OnAttached;
        image.DetachedFromVisualTree -= OnDetached;
        image.AttachedToVisualTree += OnAttached;
        image.DetachedFromVisualTree += OnDetached;
        CancelLoad(image);
        SetSource(image, null);

        var list = args.NewValue.Value;
        if (list is null || list.Count == 0)
        {
            if (TopLevel.GetTopLevel(image) is not null)
            {
                Track(image);
            }
            else
            {
                image.EffectiveViewportChanged -= OnEffectiveViewportChanged;
                Viewports.Remove(image);
                Attached.Remove(image);
            }

            SetLoadState(image, ArtLoadState.Empty);
            return;
        }
        // Sources are queued until the image reaches the prefetch range.
        SetLoadState(image, ArtLoadState.Queued);

        // 图源可能在挂载之后才到，也可能在离树期间被复用。以真实可视树状态为准，
        // 不使用弱表中的旧登记决定是否启动下载。
        if (TopLevel.GetTopLevel(image) is not null)
        {
            Track(image);
            if (TryApplyPrepared(image, list))
                return;
            Dispatcher.UIThread.Post(() => LoadWhenViewportUnknown(image, list), DispatcherPriority.Background);
            ScheduleVisibleLoad(image, list);
        }
        else
        {
            image.EffectiveViewportChanged -= OnEffectiveViewportChanged;
            Viewports.Remove(image);
            Attached.Remove(image);
        }
    }

    /// <summary>
    /// 把一张图纳入跟踪：登记「已挂载」并订阅可见区事件。
    /// 先订阅再登记没有任何顺序要求，但必须幂等——挂载回调与图源回调都可能调它。
    /// </summary>
    private static void Track(Control image)
    {
        Attached.AddOrUpdate(image, new object());
        image.EffectiveViewportChanged -= OnEffectiveViewportChanged;
        image.EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control image)
            return;

        Track(image);

        if (GetSources(image) is { Count: > 0 } list)
        {
            SetLoadState(image, ArtLoadState.Queued);
            if (TryApplyPrepared(image, list))
                return;

            // 兜底：如果这个平台/版本压根不上报可见区，就不能让图永远停在占位。
            // 等到布局之后仍没有收到任何可见区信息，就退回「挂上即加载」的老行为。
            Dispatcher.UIThread.Post(() => LoadWhenViewportUnknown(image, list), DispatcherPriority.Background);
        }
        else
        {
            SetLoadState(image, ArtLoadState.Empty);
        }
    }

    private static void LoadWhenViewportUnknown(Control image, IReadOnlyList<Uri> list)
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
        if (sender is not Control image)
            return;

        image.EffectiveViewportChanged -= OnEffectiveViewportChanged;
        Viewports.Remove(image);
        CancelLoad(image);
        Attached.Remove(image);
        SetSource(image, null);
        SetLoadState(
            image,
            GetSources(image) is { Count: > 0 } ? ArtLoadState.Queued : ArtLoadState.Empty);
    }

    /// <summary>Loads images in or near the effective viewport and releases work outside it.</summary>
    private static void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (sender is not Control image)
            return;

        Viewports.AddOrUpdate(image, new ViewportState(e.EffectiveViewport));
        if (GetSources(image) is { Count: > 0 } list)
            UpdateVisibleLoad(image, list);
    }

    /// <summary>先把可见区判定排到布局之后，避免刚换图源时还没量出尺寸就下结论。</summary>
    private static void ScheduleVisibleLoad(Control image, IReadOnlyList<Uri> list)
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

    private static void UpdateVisibleLoad(Control image, IReadOnlyList<Uri> list)
    {
        if (GetLoadState(image) == ArtLoadState.Loaded)
            return;
        if (IsWithinPrefetchRange(image))
        {
            if (TryApplyPrepared(image, list))
                return;
            if (image.GetValue(LoadCtsProperty) is null)
                StartLoad(image, list);
            return;
        }

        // 滚出预取范围：让出并发名额，回到「排队中」，滚回来时命中缓存立即显示。
        CancelLoad(image);
        if (GetLoadState(image) == ArtLoadState.Loading)
            SetLoadState(image, ArtLoadState.Queued);
    }

    private static bool IsWithinPrefetchRange(Control image)
    {
        if (!Viewports.TryGetValue(image, out var state))
            return false;

        // 预取范围取「固定下限」与「半屏」的较大者：立绘卡一行就有 338 高，
        // 固定 240 连一行都不到，等于只有已经进入视口的图才会开始下载。
        var margin = Math.Max(PrefetchMargin, state.Viewport.Height * 0.5);

        var bounds = new Rect(image.Bounds.Size).Inflate(margin);
        return bounds.Intersects(state.Viewport);
    }

    private static bool HasViewportInfo(Control image) => Viewports.TryGetValue(image, out _);

    private static void CancelLoad(Control image)
    {
        PendingStarts.Remove(image);
        PendingResults.Remove(image);
        if (image.GetValue(LoadCtsProperty) is not { } cts)
            return;

        image.SetValue(LoadCtsProperty, null);
        cts.Cancel();
        cts.Dispose();
    }

    private static bool TryApplyPrepared(Control image, IReadOnlyList<Uri> list)
    {
        if (!Attached.TryGetValue(image, out _) || TopLevel.GetTopLevel(image) is null ||
            !ReferenceEquals(GetSources(image), list) || !TryGetPrepared(list, out var bitmap, out var selected))
            return false;

        RememberSource(list, selected!);
        CancelLoad(image);
        SetSource(image, bitmap);
        SetLoadState(image, ArtLoadState.Loaded);
        Interlocked.Increment(ref _cacheHits);
        return true;
    }

    private static void StartLoad(Control image, IReadOnlyList<Uri> list)
    {
        if (TryApplyPrepared(image, list))
            return;
        CancelLoad(image);
        PendingStarts[image] = list;
        EnsureUiTimer();
    }

    private static void StartLoadNow(Control image, IReadOnlyList<Uri> list)
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        image.SetValue(LoadCtsProperty, cts);
        SetLoadState(image, ArtLoadState.Loading);
        _ = LoadAnyAsync(image, list, cts, token);
    }

    private static bool IsCurrentLoad(
        Control image,
        IReadOnlyList<Uri> candidates,
        CancellationTokenSource owner) =>
        Attached.TryGetValue(image, out _) &&
        TopLevel.GetTopLevel(image) is not null &&
        ReferenceEquals(GetSources(image), candidates) &&
        ReferenceEquals(image.GetValue(LoadCtsProperty), owner);

    /// <summary>按顺序尝试各候选地址，第一个成功的就用。</summary>
    private static async Task LoadAnyAsync(
        Control image,
        IReadOnlyList<Uri> candidates,
        CancellationTokenSource owner,
        CancellationToken ct)
    {
        try
        {
            // Order carries semantic priorities (elite stage / full illustration / fallback),
            // so a cached lower-priority variant must never bypass the preferred art.
            Bitmap? loaded = null;
            foreach (var uri in candidates)
            {
                ct.ThrowIfCancellationRequested();
                loaded = await GetOrLoadAsync(uri, ct).ConfigureAwait(false);
                if (loaded is not null)
                {
                    RememberSource(candidates, uri);
                    break;
                }
            }

            ct.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentLoad(image, candidates, owner))
                    return;
                PendingResults[image] = new PendingResult(candidates, owner, loaded);
                EnsureUiTimer();
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            // 切视图或离开页面时主动取消，避免占着下载名额。
        }
    }

    private static async Task<Bitmap?> GetOrLoadAsync(Uri uri, CancellationToken ct)
    {
        Interlocked.Increment(ref _foregroundLoads);
        try
        {
            await ForegroundGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await GetOrLoadCoreAsync(uri, ct).ConfigureAwait(false);
            }
            finally
            {
                ForegroundGate.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _foregroundLoads);
        }
    }

    private static async Task<Bitmap?> GetOrLoadCoreAsync(Uri uri, CancellationToken ct, bool background = false)
    {
        if (FailedCache.ContainsKey(uri))
            return null;

        LoadEntry<Bitmap> entry;
        while (true)
        {
            entry = Cache.GetOrAdd(uri, static key => new LoadEntry<Bitmap>(ct => LoadSharedAsync(key, ct)));
            if (entry.TryAcquire(foreground: !background))
                break;
            Cache.TryRemove(new KeyValuePair<Uri, LoadEntry<Bitmap>>(uri, entry));
        }

        try
        {
            var wasCompleted = entry.Work.IsValueCreated && entry.Work.Value.IsCompleted;
            var bitmap = await entry.Work.Value.WaitAsync(ct).ConfigureAwait(false);
            if (bitmap is null)
            {
                Cache.TryRemove(new KeyValuePair<Uri, LoadEntry<Bitmap>>(uri, entry));
                return null;
            }

            if (wasCompleted)
                Interlocked.Increment(ref _cacheHits);

            return bitmap;
        }
        finally
        {
            var cancel = entry.Release(retainCompleted: true, out _);
            if (cancel)
            {
                Cache.TryRemove(new KeyValuePair<Uri, LoadEntry<Bitmap>>(uri, entry));
                entry.Cancel();
            }
            else if (entry.Work.IsValueCreated && entry.Work.Value.IsCompleted)
            {
                var completed = entry.Work.Value;
                if (completed.IsCompletedSuccessfully && completed.Result is { } bitmap)
                    TouchIfCached(uri, entry, bitmap, background);
                else
                    Cache.TryRemove(new KeyValuePair<Uri, LoadEntry<Bitmap>>(uri, entry));
            }
        }
    }

    // Share encoded downloads between foreground decoding and background disk warming.
    private static async Task<Bitmap?> DownloadAndUseAsync(
        Uri uri, CancellationToken ct, Func<byte[], Task<Bitmap?>> consume)
    {
        LoadEntry<byte[]> entry;
        while (true)
        {
            entry = Downloads.GetOrAdd(uri,
                static key => new LoadEntry<byte[]>(token => DownloadCoreAsync(key, token)));
            if (entry.TryAcquire())
                break;
            Downloads.TryRemove(new KeyValuePair<Uri, LoadEntry<byte[]>>(uri, entry));
        }
        try
        {
            var bytes = await entry.Work.Value.WaitAsync(ct).ConfigureAwait(false);
            return bytes is null ? null : await consume(bytes).ConfigureAwait(false);
        }
        finally
        {
            var cancel = entry.Release(retainCompleted: false, out var lastWaiter);
            if (lastWaiter)
                Downloads.TryRemove(new KeyValuePair<Uri, LoadEntry<byte[]>>(uri, entry));
            if (cancel)
            {
                entry.Cancel();
                // Don't start the next prefetch until the abandoned HTTP request really exits.
                try { await entry.Work.Value.ConfigureAwait(false); }
                catch { /* The abandoning waiter already has its own cancellation/failure. */ }
            }
        }
    }

    private static async Task<byte[]?> DownloadCoreAsync(Uri uri, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Interlocked.Increment(ref _networkLoads);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var token = timeout.Token;
            await using var stream = await SharedHttp.GetStreamAsync(uri, token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int count;
            while ((count = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + count > 32L * 1024 * 1024)
                    throw new InvalidDataException("Image exceeds the 32 MB download limit.");
                buffer.Write(chunk, 0, count);
            }
            return buffer.ToArray();
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<Bitmap?> DecodeAndStoreAsync(Uri uri, byte[] bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var bitmap = await DecodeAsync(bytes, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested)
        {
            bitmap?.Dispose();
            ct.ThrowIfCancellationRequested();
        }
        if (bitmap is not null)
            await LocalImageStore.WriteAsync(uri, bytes).ConfigureAwait(false);
        if (ct.IsCancellationRequested)
        {
            bitmap?.Dispose();
            ct.ThrowIfCancellationRequested();
        }
        return bitmap;
    }

    /// <summary>Loads one URI. Local paths bypass the network concurrency gate.</summary>
    private static async Task<Bitmap?> LoadSharedAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            if (await ReadLocalAsync(uri, cancellationToken).ConfigureAwait(false) is { Length: > 0 } local)
            {
                try
                {
                    if (await DecodeAsync(local, cancellationToken).ConfigureAwait(false) is { } cached)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            cached.Dispose();
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        return TrackResult(uri, cached);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Remove corrupt local bytes and fall through to the network once.
                }

                LocalImageStore.Invalidate(uri);
            }

            var bitmap = await DownloadAndUseAsync(uri, cancellationToken,
                bytes => DecodeAndStoreAsync(uri, bytes, cancellationToken)).ConfigureAwait(false);
            return TrackResult(uri, bitmap);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 网络失败、图不存在、解码失败都视作「没有立绘」，卡片显示占位即可
            return TrackResult(uri, null);
        }
    }

    /// <summary>内嵌资源与本地磁盘缓存的快速路径；都没有就返回 null，交给网络。</summary>
    private static async Task<byte[]?> ReadLocalAsync(Uri uri, CancellationToken cancellationToken)
    {
        // 嵌入资源（avares://）不走网络：HttpClient 不认这个协议，
        // 必须先分流，否则会先失败一次再回退，白白等一个超时。
        if (uri.Scheme == "avares")
        {
            await using var asset = Avalonia.Platform.AssetLoader.Open(uri);
            using var assetBuffer = new MemoryStream();
            await asset.CopyToAsync(assetBuffer, cancellationToken).ConfigureAwait(false);
            return assetBuffer.ToArray();
        }

        // 本地磁盘缓存：重启程序后也还在
        return await LocalImageStore.TryReadAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Bitmap?> DecodeAsync(byte[] bytes, CancellationToken ct)
    {
        await DecodeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Disk hits and completed HTTP responses must also yield to active scrolling.
            while (IsInteracting)
                await Task.Delay(40, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return await Task.Run(() => Decode(bytes), ct).ConfigureAwait(false);
        }
        finally
        {
            DecodeGate.Release();
        }
    }

    /// <summary>Bound list art to about two physical pixels per logical pixel, including tall PNGs.</summary>
    private static Bitmap? Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
            return null;

        // 解码需要可寻址的流：Skia 对普通网络流会抛
        // 「Unable to load bitmap from provided data」，所以统一先拿到字节。
        using var buffer = new MemoryStream(bytes);
        // The largest list image is about 196 x 327 logical pixels. Keep 2x detail without
        // uploading oversized portraits to the renderer; the compressed original stays on disk.
        if (PngSize(bytes) is { } size)
        {
            var scale = Math.Min(1, Math.Min(384d / size.Width, 640d / size.Height));
            if (scale < 1)
                return Bitmap.DecodeToWidth(buffer, Math.Max(1, (int)(size.Width * scale)));
        }

        return new Bitmap(buffer);
    }

    private static Bitmap? TrackResult(Uri uri, Bitmap? bitmap)
    {
        if (bitmap is null)
        {
            FailedCache[uri] = 0;
            TrimFailedCache();
            return null;
        }

        FailedCache.TryRemove(uri, out _);
        return bitmap;
    }

    private static void TrimFailedCache()
    {
        var excess = FailedCache.Count - MaxFailedCacheEntries;
        if (excess <= 0)
            return;

        foreach (var key in FailedCache.Keys.Take(excess))
            FailedCache.TryRemove(key, out _);
    }

    private static PixelSize? PngSize(byte[] bytes)
    {
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return null;
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        return width > 0 && height > 0 ? new PixelSize(width, height) : null;
    }

    /// <summary>
    /// 登记一次使用，并按「字节预算 + 条数上限」淘汰最久未用的位图。
    ///
    /// 只按张数限额不等于内存上界：512 宽的立绘一张就可能超过 1MB，
    /// 而且被淘汰的位图若仍挂在可见控件上，并不会立刻归还内存。
    /// </summary>
    private static void TouchIfCached(Uri uri, LoadEntry<Bitmap> expected, Bitmap bitmap, bool background = false)
    {
        lock (UsageLock)
        {
            if (!Cache.TryGetValue(uri, out var current) || !ReferenceEquals(current, expected))
                return;

            // Admission follows the shared request, not whichever waiter happens to finish first.
            background &= !expected.ForegroundRequested;
            var bytes = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
            if (Tracked.Remove(uri, out var previous))
            {
                UsageOrder.Remove(uri);
                _trackedBytes -= previous;
            }

            HashSet<Uri>? protectedSources = null;
            while (UsageOrder.Count >= MaxCachedBitmaps || _trackedBytes + bytes > MaxCachedBytes)
            {
                protectedSources ??= GetProtectedRosterSources();
                var expendable = UsageOrder.FirstOrDefault(key => !protectedSources.Contains(key) && key.Scheme != "avares");
                if (expendable is not null)
                {
                    RemoveCached(expendable);
                    continue;
                }
                if (background || UsageOrder.First is null)
                {
                    // Keep the prepared head of the roster instead of churning the entire cache.
                    Cache.TryRemove(new KeyValuePair<Uri, LoadEntry<Bitmap>>(uri, expected));
                    if (background)
                        Volatile.Write(ref _preloadLimited, 1);
                    return;
                }
                RemoveCached(UsageOrder.First.Value);
            }

            UsageOrder.AddLast(uri);
            Tracked[uri] = bytes;
            _trackedBytes += bytes;
        }
    }

    // Caller holds UsageLock. A control can still own the bitmap after cache eviction.
    private static void RemoveCached(Uri uri)
    {
        UsageOrder.Remove(uri);
        if (Tracked.Remove(uri, out var bytes))
            _trackedBytes -= bytes;
        Cache.TryRemove(uri, out _);
    }
}

public enum ArtLoadState
{
    /// <summary>没有可用图源（例如手动录入、拼不出地址的干员）。</summary>
    Empty,

    /// <summary>有图源但仍在等待控件进入可加载范围。</summary>
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

    public static Task<Stream> GetStreamAsync(Uri uri, CancellationToken cancellationToken) =>
        Client.GetStreamAsync(uri, cancellationToken);

    private static HttpClient Create()
    {
        // Candidate mirrors are tried sequentially; keep each failed attempt short.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("arknights-random-team");
        return client;
    }
}
