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
    private string _searchText = "";
    private PoolFilterKind _poolFilter = PoolFilterKind.All;
    private int _sortIndex;
    private bool _isCardView;
    private bool _usePortrait;
    private bool _isCompactTable;
    private bool _refreshing;
    private bool _editing;

    public ObservableCollection<Staff> StaffList { get; }

    public ResettableCollection<Staff> VisibleStaff { get; } = [];

    public ResettableCollection<FilterChip> ActiveFilters { get; } = [];

    public IReadOnlyList<FilterOption<Career>> CareerFilters { get; }

    public IReadOnlyList<FilterOption<int>> RarityFilters { get; }

    public int StaffCount => StaffList.Count;

    public int SelectedStaffCount => StaffList.Count(staff => staff.IsSelected);

    public int UnselectedStaffCount => StaffCount - SelectedStaffCount;

    public int VisibleCount => VisibleStaff.Count;

    public string SummaryText => $"共 {StaffCount} 名 · 已加入随机池 {SelectedStaffCount} 名";

    public string VisibleSummaryText =>
        HasActiveFilters ? $"显示 {VisibleCount} / {StaffCount} 名" : $"共 {StaffCount} 名";

    public string SelectAllScopeText =>
        HasActiveFilters
            ? $"全选与批量入池作用于当前筛选的 {VisibleCount} 名干员"
            : "全选作用于全部干员";

    public string BatchMenuText => $"批量操作（{VisibleCount}）";

    public bool HasActiveFilters => ActiveFilters.Count > 0;

    public bool ShowStaffEmpty => StaffCount == 0;

    public bool ShowFilterEmpty => StaffCount > 0 && VisibleCount == 0;

    public bool ShowGrid => IsGridView && VisibleCount > 0;

    public bool ShowCards => IsCardView && VisibleCount > 0;

    public bool ShowBatchBar => StaffCount > 0;

    public bool CanBatch => VisibleCount > 0;

    public ListModel()
    {
        StaffList = AppState.StaffList;

        // 恢复上次的视图选择：方案要求「已有用户的选择应优先」，新用户仍是默认表格。
        var saved = AppState.UiPreferences;
        _isCardView = saved.ViewMode != StaffViewMode.Table;
        _usePortrait = saved.ViewMode == StaffViewMode.Portrait;
        _isCompactTable = saved.IsCompactTable;

        CareerFilters = Enum.GetValues<Career>()
            .Select(career => new FilterOption<Career>(career, career.ToString()) { Changed = RefreshVisible })
            .ToList();
        RarityFilters = AppOptions.Stars
            .Select(star => new FilterOption<int>(star, $"{star}★") { Changed = RefreshVisible })
            .ToList();

        StaffList.CollectionChanged += OnCollectionChanged;
        Views.ArtImage.StatsChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ArtLoadInfo));
            OnPropertyChanged(nameof(HasArtLoadFailures));
        };

        foreach (var staff in StaffList)
        {
            staff.PropertyChanged += OnStaffPropertyChanged;
            staff.UsePortrait = _usePortrait;
        }

        RefreshVisible();
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

    public int SortIndex
    {
        get => _sortIndex;
        set
        {
            if (!SetProperty(ref _sortIndex, value))
                return;

            _sorts.Clear();
            if (value is > 0 and <= 4)
            {
                var path = value switch
                {
                    1 => "Name",
                    2 => "Star",
                    3 => "Career",
                    _ => "Level"
                };
                _sorts.Add(new StaffSort(path, ListSortDirection.Ascending));
            }

            NotifySorts();
            RefreshVisible();
        }
    }

    public IReadOnlyList<StaffSort> Sorts => _sorts;

    /// <summary>是否存在生效中的排序，用于工具栏摘要的显隐。</summary>
    public bool HasSorts => _sorts.Count > 0;

    /// <summary>
    /// 排序状态摘要，按优先级列出「列 方向」。表头可追加多列排序，
    /// 工具栏据此说明当前排序顺序，避免多列排序时下拉框显示「默认排序」造成误解。
    /// </summary>
    public string SortSummary =>
        _sorts.Count == 0
            ? "默认排序"
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
        "Level" => "等级",
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
        _refreshing = true;
        try
        {
            foreach (var staff in targets)
                staff.IsSelected = selected;
        }
        finally
        {
            _refreshing = false;
        }

        RefreshVisible();
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

        SetProperty(ref _sortIndex, _sorts.Count == 1 ? IndexOfPath(_sorts[0].Path) : 0, nameof(SortIndex));
        NotifySorts();
        RefreshVisible();
    }

    public ListSortDirection? DirectionFor(string path)
    {
        path = NormalizeSortPath(path);
        var match = _sorts.FirstOrDefault(sort => sort.Path == path);
        return match is null ? null : match.Direction;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (Staff staff in e.OldItems)
                staff.PropertyChanged -= OnStaffPropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (Staff staff in e.NewItems)
            {
                staff.PropertyChanged += OnStaffPropertyChanged;
                staff.UsePortrait = _usePortrait;
            }
        }

        RefreshVisible();
    }

    private void OnStaffPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Staff.IsSelected))
        {
            NotifyCounts();
            if (_poolFilter != PoolFilterKind.All)
                RefreshVisible();
            return;
        }

        if (_editing)
            return;

        if (AffectsProjection(args.PropertyName))
            RefreshVisible();
    }

    private bool AffectsProjection(string? property) => property switch
    {
        nameof(Staff.Name) => !string.IsNullOrWhiteSpace(_searchText) || HasSort("Name"),
        nameof(Staff.Career) => CareerFilters.Any(item => item.IsChecked) || HasSort("Career"),
        nameof(Staff.Star) => RarityFilters.Any(item => item.IsChecked) || HasSort("Star"),
        nameof(Staff.Level) or nameof(Staff.LevelDigits) or nameof(Staff.EliteLabel) or nameof(Staff.LevelLine)
            => HasSort("Level"),
        _ => false
    };

    private bool HasSort(string path) => _sorts.Any(sort => sort.Path == path);

    public void RefreshVisible()
    {
        if (_refreshing || _editing)
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
        OnPropertyChanged(nameof(UnselectedStaffCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(VisibleSummaryText));
        OnPropertyChanged(nameof(SelectAllScopeText));
        OnPropertyChanged(nameof(BatchMenuText));
        OnPropertyChanged(nameof(HasActiveFilters));
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

    public bool IsAvatarView => _isCardView && !_usePortrait;

    public bool IsPortraitView => _isCardView && _usePortrait;

    public string ArtLoadInfo =>
        Views.ArtImage.NetworkLoads == 0 && Views.ArtImage.DiskHits == 0
            ? "立绘：尚未加载"
            : $"立绘：网络 {Views.ArtImage.NetworkLoads} 张 · 内存命中 {Views.ArtImage.CacheHits} · 本地缓存 {Views.ArtImage.DiskHits}";

    public bool HasArtLoadFailures => Views.ArtImage.FailedLoads > 0;

    public bool UsePortrait
    {
        get => _usePortrait;
        set
        {
            if (!SetProperty(ref _usePortrait, value))
                return;

            foreach (var staff in StaffList)
                staff.UsePortrait = value;

            PersistViewMode();
            NotifyViewMode();
        }
    }

    /// <summary>把当前视图选择写回偏好；桌面端退出时随其它数据一起落盘。</summary>
    private void PersistViewMode() =>
        AppState.UiPreferences.ViewMode = IsCardView
            ? (UsePortrait ? StaffViewMode.Portrait : StaffViewMode.Avatar)
            : StaffViewMode.Table;

    private void NotifyViewMode()
    {
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsAvatarView));
        OnPropertyChanged(nameof(IsPortraitView));
        OnPropertyChanged(nameof(ShowGrid));
        OnPropertyChanged(nameof(ShowCards));
    }

    /// <summary>
    /// 表格紧凑模式：行高 44、行内头像 32；关闭时按方案的普通档 56 / 40。
    /// 行高由 StaffListView 的样式按 <c>DataGrid#StaffGrid.compact</c> 类切换，
    /// 行内头像与职业图标尺寸则由下面两个属性提供给绑定，避免尺寸散落在两处。
    /// </summary>
    public bool IsCompactTable
    {
        get => _isCompactTable;
        set
        {
            if (!SetProperty(ref _isCompactTable, value))
                return;

            AppState.UiPreferences.IsCompactTable = value;
            OnPropertyChanged(nameof(RowAvatarSize));
            OnPropertyChanged(nameof(RowCareerIconSize));
        }
    }

    /// <summary>表格行内头像边长：普通 40，紧凑 32。</summary>
    public double RowAvatarSize => IsCompactTable ? 32 : 40;

    /// <summary>表格行内职业图标边长：普通 18，紧凑 16。</summary>
    public double RowCareerIconSize => IsCompactTable ? 16 : 18;

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
        "Level" => staff.Level.EliteLevel * 100 + staff.Level.Rank,
        _ => 0
    };

    private static string NormalizeSortPath(string path) =>
        path is "Level.Description" or "Level.EliteLevel" ? "Level" : path;

    private static int IndexOfPath(string path) => path switch
    {
        "Name" => 1,
        "Star" => 2,
        "Career" => 3,
        "Level" => 4,
        _ => 0
    };
}

public sealed record StaffSort(string Path, ListSortDirection Direction);
