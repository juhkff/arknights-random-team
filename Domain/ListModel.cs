using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using arknights_random_team.Models;

namespace arknights_random_team.Domain;

public class ListModel : AutomaticNotify
{
    public ObservableCollection<Staff> StaffList { get; }

    public int StaffCount => StaffList.Count;

    public int SelectedStaffCount => StaffList.Count(staff => staff.IsSelected);

    public ListModel()
    {
        StaffList = AppState.StaffList;
        StaffList.CollectionChanged += OnCollectionChanged;
        foreach (var staff in StaffList)
            staff.PropertyChanged += OnStaffPropertyChanged;
    }

    public bool? IsAllStaffSelected
    {
        get
        {
            if (StaffList.Count == 0)
                return false;
            var selected = StaffList.Select(item => item.IsSelected).Distinct().ToList();
            return selected.Count == 1 ? selected[0] : null;
        }
        set
        {
            if (!value.HasValue)
                return;
            foreach (var staff in StaffList)
                staff.IsSelected = value.Value;
            OnPropertyChanged();
        }
    }

    public void ToggleSelectAll() => IsAllStaffSelected = IsAllStaffSelected != true;

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
                staff.PropertyChanged += OnStaffPropertyChanged;
        }

        OnPropertyChanged(nameof(IsAllStaffSelected));
        OnPropertyChanged(nameof(StaffCount));
        OnPropertyChanged(nameof(SelectedStaffCount));
    }

    private void OnStaffPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Staff.IsSelected))
        {
            OnPropertyChanged(nameof(IsAllStaffSelected));
            OnPropertyChanged(nameof(SelectedStaffCount));
        }
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

    /// <summary>卡片用半身立绘大图，而不是头像小图。</summary>
    public bool UsePortrait
    {
        get => _usePortrait;
        set
        {
            if (!SetProperty(ref _usePortrait, value))
                return;

            // 每张卡片的图源都要跟着换，逐张通知
            foreach (var staff in StaffList)
                staff.RaiseArtChanged();
        }
    }
}
