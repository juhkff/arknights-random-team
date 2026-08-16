using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using arknights_random_team.Models;

namespace arknights_random_team.Domain;

public class ListModel : AutomaticNotify
{
    public ObservableCollection<Staff> StaffList { get; }

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
    }

    private void OnStaffPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Staff.IsSelected))
            OnPropertyChanged(nameof(IsAllStaffSelected));
    }
}
