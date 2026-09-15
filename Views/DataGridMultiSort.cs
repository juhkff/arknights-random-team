using System.ComponentModel;
using Avalonia.Collections;
using Avalonia.Controls;

namespace arknights_random_team.Views;

internal static class DataGridMultiSort
{
    public static void Apply(DataGrid grid, DataGridColumnEventArgs e)
    {
        var sorts = grid.CollectionView?.SortDescriptions;
        var path = e.Column.SortMemberPath;
        if (sorts == null || string.IsNullOrWhiteSpace(path))
            return;

        e.Handled = true;
        var index = FindIndex(sorts, path);
        if (index < 0)
        {
            sorts.Add(DataGridSortDescription.FromPath(path, ListSortDirection.Ascending));
            return;
        }

        var current = sorts[index];
        sorts.RemoveAt(index);
        if (current.Direction == ListSortDirection.Ascending)
            sorts.Insert(index, DataGridSortDescription.FromPath(path, ListSortDirection.Descending));
    }

    private static int FindIndex(IList<DataGridSortDescription> sorts, string path)
    {
        for (var i = 0; i < sorts.Count; i++)
        {
            if (sorts[i].HasPropertyPath && sorts[i].PropertyPath == path)
                return i;
        }

        return -1;
    }
}
