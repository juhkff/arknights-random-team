using System.ComponentModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using arknights_random_team.Domain;
using arknights_random_team.Models;

namespace arknights_random_team.Views;

public partial class StaffListView : UserControl
{
    private bool _suppressRowSelection;

    public StaffListView()
    {
        DataContext = new ListModel();
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        Loaded += (_, _) => ClearGridSelection();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual visual)
            return;

        if (visual.FindAncestorOfType<DataGridColumnHeader>(true) is not null)
        {
            _suppressRowSelection = true;
            return;
        }

        if (visual.FindAncestorOfType<DataGridRow>(true) is not null)
            _suppressRowSelection = false;
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ListModel model)
            model.ToggleSelectAll();
    }

    private void SelectAllHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is CheckBox)
            return;
        if (DataContext is ListModel model)
            model.ToggleSelectAll();
    }

    private void SelectCell_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is CheckBox)
            return;
        if (sender is Border { DataContext: Staff staff })
            staff.IsSelected = !staff.IsSelected;
    }

    private void StaffGrid_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is not Control editor)
            return;

        editor.MinWidth = 0;
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (editor is Decorator { Child: Control inner })
        {
            inner.MinWidth = 0;
            inner.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    private void StaffGrid_CellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        ClearRowHighlight(e.Row);
    }

    private void StaffGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        _suppressRowSelection = true;

        var sorts = StaffGrid.CollectionView?.SortDescriptions;
        if (sorts is not null)
        {
            var path = e.Column.SortMemberPath;
            if (!string.IsNullOrEmpty(path))
            {
                var current = sorts.FirstOrDefault(item => item.HasPropertyPath && item.PropertyPath == path);
                if (current?.Direction == ListSortDirection.Descending)
                {
                    e.Handled = true;
                    sorts.Clear();
                }
            }
        }

        void ClearIfSuppressed()
        {
            if (_suppressRowSelection)
                ClearGridSelection();
        }

        Dispatcher.UIThread.Post(ClearIfSuppressed, DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(ClearIfSuppressed, DispatcherPriority.Background);
    }

    private void StaffGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressRowSelection && StaffGrid.SelectedItem is not null)
            StaffGrid.SelectedItem = null;
    }

    private void ClearGridSelection()
    {
        if (StaffGrid.SelectedItem is not null)
            StaffGrid.SelectedItem = null;
        if (StaffGrid.SelectedIndex >= 0)
            StaffGrid.SelectedIndex = -1;
        _suppressRowSelection = false;
    }

    private void ClearRowHighlight(DataGridRow? row = null)
    {
        StaffGrid.SelectedItem = null;
        if (row is null)
            return;

        row.IsHitTestVisible = false;
        Dispatcher.UIThread.Post(() => row.IsHitTestVisible = true, DispatcherPriority.Input);
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Staff staff })
            return;

        var owner = this.FindWindow();
        if (!await AppDialogs.Confirm(owner, $"确定删除「{staff.Name}」？"))
            return;

        AppState.StaffList.Remove(staff);
    }
}
