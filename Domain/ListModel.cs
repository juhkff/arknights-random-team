using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using arknights_random_team.Models;

namespace arknights_random_team.Domain;

public enum PoolFilterKind
{
    All,
    InPool,
    OutPool
}

public sealed class FilterOption<T> : AutomaticNotify
{
    private bool _isChecked;

    public FilterOption(T value, string label)
    {
        Value = value;
        Label = label;
    }

    public T Value { get; }

    public string Label { get; }

    public Action? Changed { get; set; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
                Changed?.Invoke();
        }
    }
}

public sealed class FilterChip
{
    public FilterChip(string label, Action remove)
    {
        Label = label;
        Remove = remove;
    }

    public string Label { get; }

    public Action Remove { get; }
}

public class ListModel : AutomaticNotify
{
    private readonly List<StaffSort> _sorts = [];
    private readonly HashSet<Staff> _subscribedStaff = [];
    private string _searchText = "";
    private PoolFilterKind _poolFilter = PoolFilterKind.All;
    private bool _isCardView;
    private bool _refreshing;
    private bool _editing;
    private bool _active;

    private bool IsPaused => AppState.IsBulkUpdating;

    public ObservableCollection<Staff> StaffList { get; }

    public ResettableCollection<Staff> VisibleStaff { get; } = [];

    public ResettableCollection<FilterChip> ActiveFilters { get; } = [];

    public IReadOnlyList<FilterOption<Career>> CareerFilters { get; }

    public IReadOnlyList<FilterOption<int>> RarityFilters { get; }

    public int StaffCount => StaffList.Count;

    public int SelectedStaffCount => StaffList.Count(staff => staff.IsSelected);

    public int VisibleCount => VisibleStaff.Count;

    public string SummaryText => $"共 {StaffCount} 名 · 已加入随机池 {SelectedStaffCount} 名";

    public string VisibleSummaryText =>
        HasActiveFilters ? $"显示 {VisibleCount} / {StaffCount} 名" : $"共 {StaffCount} 名";

    public string SelectAllScopeText =>
        HasActiveFilters
            ? $"批量入池作用于当前筛选的 {VisibleCount} 名干员"
            : "批量入池作用于全部干员";

    public string BatchMenuText => $"批量操作（{VisibleCount}）";

    public bool HasActiveFilters => ActiveFilters.Count > 0;

    public bool HasFlyoutFilters =>
        CareerFilters.Any(item => item.IsChecked) ||
        RarityFilters.Any(item => item.IsChecked) ||
        _poolFilter != PoolFilterKind.All;

    public bool ShowStaffEmpty => StaffCount == 0;

    public bool ShowFilterEmpty => StaffCount > 0 && VisibleCount == 0;

    public bool ShowGrid => IsGridView && VisibleCount > 0;

    public bool ShowCards => IsCardView && VisibleCount > 0;

    public bool ShowBatchBar => StaffCount > 0;

    public bool CanBatch => VisibleCount > 0;

    public ListModel()
    {
        StaffList = AppState.StaffList;

        // Restore the persisted list view preference.
        var saved = AppState.UiPreferences;
        // Avatar/Portrait are legacy persisted values; every non-table value now opens half-body cards.
        _isCardView = saved.ViewMode != StaffViewMode.Table;
        PersistViewMode();

        CareerFilters = Enum.GetValues<Career>()
            .Select(career => new FilterOption<Career>(career, career.ToString()) { Changed = RefreshVisible })
            .ToList();
        RarityFilters = AppOptions.Stars
            .Select(star => new FilterOption<int>(star, $"{star}★") { Changed = RefreshVisible })
            .ToList();

        RefreshVisible();
    }

    public void Activate()
    {
        if (_active)
            return;

        _active = true;
        StaffList.CollectionChanged += OnCollectionChanged;
        foreach (var staff in StaffList)
            Subscribe(staff);
        RefreshVisible();
    }

    public void Deactivate()
    {
        if (!_active)
            return;

        _active = false;
        StaffList.CollectionChanged -= OnCollectionChanged;
        foreach (var staff in _subscribedStaff)
            staff.PropertyChanged -= OnStaffPropertyChanged;
        _subscribedStaff.Clear();
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? ""))
                RefreshVisible();
        }
    }

    public PoolFilterKind PoolFilter
    {
        get => _poolFilter;
        set
        {
            if (SetProperty(ref _poolFilter, value))
            {
                OnPropertyChanged(nameof(IsPoolFilterAll));
                OnPropertyChanged(nameof(IsPoolFilterIn));
                OnPropertyChanged(nameof(IsPoolFilterOut));
                RefreshVisible();
            }
        }
    }

    public bool IsPoolFilterAll => _poolFilter == PoolFilterKind.All;

    public bool IsPoolFilterIn => _poolFilter == PoolFilterKind.InPool;

    public bool IsPoolFilterOut => _poolFilter == PoolFilterKind.OutPool;

    public IReadOnlyList<StaffSort> Sorts => _sorts;

    public bool HasSorts => _sorts.Count > 0;

    /// <summary>
    /// 排序状态摘要，按优先级列出「列 方向」。表头可追加多列排序；
    /// 第三次点击同一列会取消该列。无排序时显示默认顺序。
    /// </summary>
    public string SortSummary =>
        _sorts.Count == 0
            ? "默认顺序"
            : string.Join(" → ", _sorts.Select(Describe));

    private static string Describe(StaffSort sort) =>
        $"{PathLabel(sort.Path)} {(sort.Direction == ListSortDirection.Ascending ? "升序" : "降序")}";

    /// <summary>
    /// 四个可排序列当前的排序方向，供表头箭头直接绑定（渲染在列头模板里的 Path）。
    /// 排序由本模型驱动，DataGrid 自身的排序流程被视图层拦下（Sorting 事件里
    /// <c>e.Handled = true</c>），所以表头状态必须由模型提供，不能依赖 DataGrid 内部维护。
    /// </summary>
    public ListSortDirection? NameSort => DirectionFor("Name");

    public ListSortDirection? StarSort => DirectionFor("Star");

    public ListSortDirection? CareerSort => DirectionFor("Career");

    public ListSortDirection? LevelSort => DirectionFor("Level");

    private static string PathLabel(string path) => path switch
    {
        "Name" => "名称",
        "Star" => "稀有度",
        "Career" => "职业",
        "Level" => "精英 / 等级",
        _ => path
    };

    private void NotifySorts()
    {
        OnPropertyChanged(nameof(Sorts));
        OnPropertyChanged(nameof(HasSorts));
        OnPropertyChanged(nameof(SortSummary));
        OnPropertyChanged(nameof(NameSort));
        OnPropertyChanged(nameof(StarSort));
        OnPropertyChanged(nameof(CareerSort));
        OnPropertyChanged(nameof(LevelSort));
    }

    public bool? IsAllStaffSelected
    {
        get
        {
            if (VisibleStaff.Count == 0)
                return false;
            var selected = VisibleStaff.Select(item => item.IsSelected).Distinct().ToList();
            return selected.Count == 1 ? selected[0] : null;
        }
        set
        {
            if (!value.HasValue)
                return;
            ApplyBatchPool(value.Value);
        }
    }

    public void ToggleSelectAll() => IsAllStaffSelected = IsAllStaffSelected != true;

    public void SetPoolFilter(PoolFilterKind kind) => PoolFilter = kind;

    public void ClearFilters()
    {
        _refreshing = true;
        try
        {
            _searchText = "";
            _poolFilter = PoolFilterKind.All;
            foreach (var option in CareerFilters)
                option.IsChecked = false;
            foreach (var option in RarityFilters)
                option.IsChecked = false;
            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(PoolFilter));
            OnPropertyChanged(nameof(IsPoolFilterAll));
            OnPropertyChanged(nameof(IsPoolFilterIn));
            OnPropertyChanged(nameof(IsPoolFilterOut));
        }
        finally
        {
            _refreshing = false;
        }

        RefreshVisible();
    }

    public void ApplyBatchPool(bool selected)
    {
        var targets = VisibleStaff.ToList();

        // Publish projection and count changes once for the whole batch.
        using var scope = AppState.BeginBulkUpdate();
        foreach (var staff in targets)
            staff.IsSelected = selected;
    }

    public void BeginEdit() => _editing = true;

    public void EndEdit()
    {
        _editing = false;
        RefreshVisible();
    }

    public void CycleSort(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        path = NormalizeSortPath(path);
        var index = _sorts.FindIndex(sort => sort.Path == path);
        if (index < 0)
        {
            _sorts.Add(new StaffSort(path, ListSortDirection.Ascending));
        }
        else if (_sorts[index].Direction == ListSortDirection.Ascending)
        {
            _sorts[index] = new StaffSort(path, ListSortDirection.Descending);
        }
        else
        {
            _sorts.RemoveAt(index);
        }

        NotifySorts();
        RefreshVisible();
    }

    public ListSortDirection? DirectionFor(string path)
    {
        path = NormalizeSortPath(path);
        var match = _sorts.FirstOrDefault(sort => sort.Path == path);
        return match is null ? null : match.Direction;
    }

    /// <summary>Refreshes image diagnostics while the view is attached.</summary>
    public void OnArtStatsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ArtPreloadText));
        OnPropertyChanged(nameof(ArtCacheText));
        OnPropertyChanged(nameof(ArtSourceText));
        OnPropertyChanged(nameof(ArtFailureText));
        OnPropertyChanged(nameof(HasArtLoadFailures));
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var staff in _subscribedStaff)
                staff.PropertyChanged -= OnStaffPropertyChanged;
            _subscribedStaff.Clear();
            foreach (var staff in StaffList)
                Subscribe(staff);
        }
        else
        {
            if (e.OldItems is not null)
            {
                foreach (Staff staff in e.OldItems)
                    Unsubscribe(staff);
            }

            if (e.NewItems is not null)
            {
                foreach (Staff staff in e.NewItems)
                    Subscribe(staff);
            }
        }

        if (!IsPaused)
            RefreshVisible();
    }

    private void Subscribe(Staff staff)
    {
        if (!_subscribedStaff.Add(staff))
            return;

        staff.PropertyChanged += OnStaffPropertyChanged;
    }

    private void Unsubscribe(Staff staff)
    {
        if (_subscribedStaff.Remove(staff))
            staff.PropertyChanged -= OnStaffPropertyChanged;
    }

    private void OnStaffPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Staff.IsSelected))
        {
            if (IsPaused)
                return;

            if (_poolFilter == PoolFilterKind.All)
                NotifyCounts();
            else
                RefreshVisible();
            return;
        }

        if (_editing)
            return;

        if (!AffectsProjection(args.PropertyName))
            return;

        if (!IsPaused)
            RefreshVisible();
    }

    private bool AffectsProjection(string? property) => property switch
    {
        nameof(Staff.Name) => !string.IsNullOrWhiteSpace(_searchText) || HasSort("Name"),
        nameof(Staff.Career) => CareerFilters.Any(item => item.IsChecked) || HasSort("Career"),
        nameof(Staff.Star) => RarityFilters.Any(item => item.IsChecked) || HasSort("Star"),
        nameof(Staff.LevelLine) or nameof(Staff.LevelSortKey) => HasSort("Level"),
        _ => false
    };

    private bool HasSort(string path) => _sorts.Any(sort => sort.Path == path);

    public void RefreshVisible()
    {
        if (_editing || IsPaused || _refreshing)
            return;

        _refreshing = true;
        try
        {
            var filter = BuildFilter();
            IEnumerable<Staff> query = StaffList.Where(filter.Matches);
            if (_sorts.Count > 0)
            {
                IOrderedEnumerable<Staff>? ordered = null;
                foreach (var sort in _sorts)
                {
                    ordered = ordered is null
                        ? OrderFirst(query, sort)
                        : OrderThen(ordered, sort);
                }

                query = ordered ?? query;
            }

            var items = query.ToList();
            if (items.Count != VisibleStaff.Count || !items.SequenceEqual(VisibleStaff))
                VisibleStaff.ReplaceAll(items);

            RebuildChips();
            NotifyCounts();
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>批量更新结束后统一重建一次投影与汇总。</summary>
    public void OnBulkUpdateCompleted() => RefreshVisible();

    /// <summary>
    /// 把当前筛选条件固化成一份可复用的投影条件。
    ///
    /// 原实现对每名干员都重新构造职业/稀有度 HashSet，500 名干员时一次刷新要分配上千个集合。
    /// 现在每次刷新只构造一次，判定逐条走同一份数据。
    /// </summary>
    private ProjectionFilter BuildFilter()
    {
        var careers = CareerFilters.Where(item => item.IsChecked).Select(item => item.Value).ToHashSet();
        var stars = RarityFilters.Where(item => item.IsChecked).Select(item => item.Value).ToHashSet();
        return new ProjectionFilter(_searchText.Trim(), careers, stars, _poolFilter);
    }

    private readonly record struct ProjectionFilter(
        string Search,
        HashSet<Career> Careers,
        HashSet<int> Stars,
        PoolFilterKind Pool)
    {
        /// <summary>名称包含匹配；职业/稀有度组内为或，组间为且；随机池单独一组。</summary>
        public bool Matches(Staff staff)
        {
            if (Search.Length > 0 &&
                staff.Name.IndexOf(Search, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            if (Careers.Count > 0 && !Careers.Contains(staff.Career))
                return false;

            if (Stars.Count > 0 && !Stars.Contains(staff.Star))
                return false;

            return Pool switch
            {
                PoolFilterKind.InPool => staff.IsSelected,
                PoolFilterKind.OutPool => !staff.IsSelected,
                _ => true
            };
        }
    }

    private void RebuildChips()
    {
        var chips = new List<FilterChip>();
        var search = _searchText.Trim();
        if (search.Length > 0)
            chips.Add(new FilterChip($"搜索：{search}", () => SearchText = ""));

        foreach (var option in CareerFilters.Where(item => item.IsChecked))
        {
            var captured = option;
            chips.Add(new FilterChip(captured.Label, () => captured.IsChecked = false));
        }

        foreach (var option in RarityFilters.Where(item => item.IsChecked))
        {
            var captured = option;
            chips.Add(new FilterChip(captured.Label, () => captured.IsChecked = false));
        }

        if (_poolFilter == PoolFilterKind.InPool)
            chips.Add(new FilterChip("已入池", () => PoolFilter = PoolFilterKind.All));
        else if (_poolFilter == PoolFilterKind.OutPool)
            chips.Add(new FilterChip("未入池", () => PoolFilter = PoolFilterKind.All));

        if (chips.Count != ActiveFilters.Count ||
            !chips.Select(chip => chip.Label).SequenceEqual(ActiveFilters.Select(chip => chip.Label)))
            ActiveFilters.ReplaceAll(chips);
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(IsAllStaffSelected));
        OnPropertyChanged(nameof(StaffCount));
        OnPropertyChanged(nameof(SelectedStaffCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(VisibleSummaryText));
        OnPropertyChanged(nameof(SelectAllScopeText));
        OnPropertyChanged(nameof(BatchMenuText));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(HasFlyoutFilters));
        OnPropertyChanged(nameof(ShowStaffEmpty));
        OnPropertyChanged(nameof(ShowFilterEmpty));
        OnPropertyChanged(nameof(ShowGrid));
        OnPropertyChanged(nameof(ShowCards));
        OnPropertyChanged(nameof(ShowBatchBar));
        OnPropertyChanged(nameof(CanBatch));
    }

    public bool IsCardView
    {
        get => _isCardView;
        set
        {
            if (!SetProperty(ref _isCardView, value))
                return;

            PersistViewMode();
            NotifyViewMode();
        }
    }

    public bool IsGridView => !_isCardView;

    /// <summary>卡片模式现在固定使用半身像；旧头像/立绘偏好只负责迁移到这里。</summary>
    public bool IsHalfBodyView => _isCardView;

    /// <summary>全名单预热进度；浏览器不预热，显示按需加载说明。</summary>
    public string ArtPreloadText => Views.ArtImage.PreloadSummary;

    /// <summary>解码缓存占用，例如「内存图片 12 张 · 4.5 / 256 MiB」。</summary>
    public string ArtCacheText => Views.ArtImage.CacheSummary;

    /// <summary>本次运行从网络、内存和磁盘取图的次数。</summary>
    public string ArtSourceText =>
        $"网络 {Views.ArtImage.NetworkLoads} · 内存命中 {Views.ArtImage.CacheHits} · 本地缓存 {Views.ArtImage.DiskHits}";

    /// <summary>当前可视树中加载失败的图片数；没有失败时为空。</summary>
    public string ArtFailureText =>
        Views.ArtImage.CurrentFailedLoads > 0
            ? $"当前失败 {Views.ArtImage.CurrentFailedLoads} 张，名称和操作仍可使用"
            : "";

    /// <summary>Whether any currently attached image exhausted all candidate sources.</summary>
    public bool HasArtLoadFailures => Views.ArtImage.CurrentFailedLoads > 0;

    /// <summary>把当前视图选择写回偏好；旧头像/立绘值不会再继续写出。</summary>
    private void PersistViewMode() =>
        AppState.UiPreferences.ViewMode = IsCardView
            ? StaffViewMode.HalfBody
            : StaffViewMode.Table;

    private void NotifyViewMode()
    {
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsHalfBodyView));
        OnPropertyChanged(nameof(ShowGrid));
        OnPropertyChanged(nameof(ShowCards));
    }

    private static IOrderedEnumerable<Staff> OrderFirst(IEnumerable<Staff> source, StaffSort sort) =>
        sort.Direction == ListSortDirection.Ascending
            ? source.OrderBy(staff => SortKey(staff, sort.Path))
            : source.OrderByDescending(staff => SortKey(staff, sort.Path));

    private static IOrderedEnumerable<Staff> OrderThen(IOrderedEnumerable<Staff> source, StaffSort sort) =>
        sort.Direction == ListSortDirection.Ascending
            ? source.ThenBy(staff => SortKey(staff, sort.Path))
            : source.ThenByDescending(staff => SortKey(staff, sort.Path));

    private static IComparable SortKey(Staff staff, string path) => path switch
    {
        "Name" => staff.Name,
        "Star" => staff.Star,
        "Career" => (int)staff.Career,
        "Level" => staff.Level.EliteLevel * 1000 + staff.Level.Rank,
        _ => 0
    };

    private static string NormalizeSortPath(string path) => path switch
    {
        "Level.Description" or "Level.EliteLevel" or "LevelSortKey" => "Level",
        _ => path
    };
}

public sealed record StaffSort(string Path, ListSortDirection Direction);
