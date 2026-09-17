using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using arknights_random_team.Models;

namespace arknights_random_team.Domain;

public class ListModel : AutomaticNotify
{
    public ObservableCollection<Staff> StaffList { get; }

    /// <summary>表格 / 卡片绑定筛选后的名单，源名单仍是 <see cref="StaffList"/>。</summary>
    public ObservableCollection<Staff> FilteredStaffList { get; } = [];

    public int StaffCount => StaffList.Count;

    public int SelectedStaffCount => StaffList.Count(staff => staff.IsSelected);

    public int UnselectedStaffCount => StaffCount - SelectedStaffCount;

    public int FilteredCount => FilteredStaffList.Count;

    public bool HasActiveFilter =>
        !string.IsNullOrWhiteSpace(_searchText) ||
        _starFilters.Count > 0 ||
        _careerFilters.Count > 0 ||
        _filterSelected ||
        _filterStandby;

    /// <summary>名单里有人，但当前搜索 / 筛选一个都对不上。</summary>
    public bool IsFilterEmpty => StaffCount > 0 && FilteredCount == 0;

    public string FilterSummary => HasActiveFilter ? $"筛选 {FilteredCount} 名" : "";

    public ListModel()
    {
        StaffList = AppState.StaffList;
        StaffList.CollectionChanged += OnCollectionChanged;
        Views.ArtImage.StatsChanged += (_, _) => OnPropertyChanged(nameof(ArtLoadInfo));
        foreach (var staff in StaffList)
        {
            staff.PropertyChanged += OnStaffPropertyChanged;
            staff.UsePortrait = _usePortrait;
        }

        RefreshFilter();
    }

    public bool? IsAllStaffSelected
    {
        get
        {
            if (FilteredStaffList.Count == 0)
                return false;
            var selected = FilteredStaffList.Select(item => item.IsSelected).Distinct().ToList();
            return selected.Count == 1 ? selected[0] : null;
        }
        set
        {
            if (!value.HasValue)
                return;
            foreach (var staff in FilteredStaffList)
                staff.IsSelected = value.Value;
            OnPropertyChanged();
        }
    }

    public void ToggleSelectAll() => IsAllStaffSelected = IsAllStaffSelected != true;

    private string _searchText = "";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? ""))
                RefreshFilter();
        }
    }

    private readonly HashSet<int> _starFilters = [];
    private readonly HashSet<Career> _careerFilters = [];
    private bool _filterSelected;
    private bool _filterStandby;

    public bool IsStarFilterOn(int star) => _starFilters.Contains(star);

    public bool IsCareerFilterOn(Career career) => _careerFilters.Contains(career);

    public bool FilterSelectedOnly => _filterSelected;

    public bool FilterStandbyOnly => _filterStandby;

    public void ToggleStarFilter(int star)
    {
        if (!_starFilters.Add(star))
            _starFilters.Remove(star);
        RefreshFilter();
    }

    public void ToggleCareerFilter(Career career)
    {
        if (!_careerFilters.Add(career))
            _careerFilters.Remove(career);
        RefreshFilter();
    }

    public void ToggleSelectedFilter()
    {
        _filterSelected = !_filterSelected;
        RefreshFilter();
    }

    public void ToggleStandbyFilter()
    {
        _filterStandby = !_filterStandby;
        RefreshFilter();
    }

    public void ClearFilters()
    {
        _searchText = "";
        _starFilters.Clear();
        _careerFilters.Clear();
        _filterSelected = false;
        _filterStandby = false;
        OnPropertyChanged(nameof(SearchText));
        RefreshFilter();
    }

    public void RefreshFilter()
    {
        var q = _searchText.Trim();
        IEnumerable<Staff> src = StaffList;
        if (q.Length > 0)
            src = src.Where(s => (s.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
        if (_starFilters.Count > 0)
            src = src.Where(s => _starFilters.Contains(s.Star));
        if (_careerFilters.Count > 0)
            src = src.Where(s => _careerFilters.Contains(s.Career));
        if (_filterSelected ^ _filterStandby)
            src = src.Where(s => s.IsSelected == _filterSelected);

        var list = src.ToList();
        FilteredStaffList.Clear();
        foreach (var staff in list)
            FilteredStaffList.Add(staff);

        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(HasActiveFilter));
        OnPropertyChanged(nameof(IsFilterEmpty));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(IsAllStaffSelected));
        OnPropertyChanged(nameof(FilterSelectedOnly));
        OnPropertyChanged(nameof(FilterStandbyOnly));
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

        OnPropertyChanged(nameof(StaffCount));
        OnPropertyChanged(nameof(SelectedStaffCount));
        OnPropertyChanged(nameof(UnselectedStaffCount));
        RefreshFilter();
    }

    private void OnStaffPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Staff.IsSelected))
        {
            OnPropertyChanged(nameof(IsAllStaffSelected));
            OnPropertyChanged(nameof(SelectedStaffCount));
            OnPropertyChanged(nameof(UnselectedStaffCount));
            if (_filterSelected || _filterStandby)
                RefreshFilter();
            return;
        }

        // 没开对应筛选时不必重建列表，免得表格排序和滚动位置被清掉
        if (args.PropertyName == nameof(Staff.Name) && !string.IsNullOrWhiteSpace(_searchText))
            RefreshFilter();
        else if (args.PropertyName == nameof(Staff.Star) && _starFilters.Count > 0)
            RefreshFilter();
        else if (args.PropertyName == nameof(Staff.Career) && _careerFilters.Count > 0)
            RefreshFilter();
    }

    // ---- 展示方式：表格 / 卡片，以及卡片立绘规格 ----

    private bool _isCardView;
    private bool _usePortrait;

    /// <summary>是否用卡片视图（默认表格；表格才支持行内编辑与多列排序）。</summary>
    public bool IsCardView
    {
        get => _isCardView;
        set
        {
            if (!SetProperty(ref _isCardView, value))
                return;

            OnPropertyChanged(nameof(IsGridView));
        }
    }

    /// <summary>
    /// 表格视图是否可见。用独立布尔量而不是枚举加转换器，
    /// 是为了在 XAML 里直接绑 IsVisible。
    /// </summary>
    public bool IsGridView => !_isCardView;

    /// <summary>
    /// 立绘加载情况，显示在卡片视图头部，用来直观看出缓存的命中程度。
    /// 这是一次性统计（本次运行内累计），不参与任何逻辑判断。
    /// </summary>
    public string ArtLoadInfo =>
        Views.ArtImage.NetworkLoads == 0 && Views.ArtImage.DiskHits == 0
            ? "立绘：尚未加载"
            : $"立绘：网络 {Views.ArtImage.NetworkLoads} 张 · 内存命中 {Views.ArtImage.CacheHits} · 本地缓存 {Views.ArtImage.DiskHits}";

    /// <summary>卡片按半身像区域显示（否则裁切为头像区域）。不换立绘文件。</summary>
    public bool UsePortrait
    {
        get => _usePortrait;
        set
        {
            if (!SetProperty(ref _usePortrait, value))
                return;

            foreach (var staff in StaffList)
                staff.UsePortrait = value;
        }
    }
}
