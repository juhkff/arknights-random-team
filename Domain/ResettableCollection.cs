using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace arknights_random_team.Domain;

/// <summary>一次 Reset 替换全部项，避免筛选刷新时对 DataGrid 连发几百次 Add。</summary>
public sealed class ResettableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
