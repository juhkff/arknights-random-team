using System.Collections.Specialized;
using Avalonia;
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
        UpdateClearButton();
        AppState.StaffList.CollectionChanged += StaffList_CollectionChanged;
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        Loaded += (_, _) => ClearGridSelection();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void GridView_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ListModel model)
            model.IsCardView = false;
    }

    private void CardView_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ListModel model)
            return;

        model.IsCardView = true;

        // 卡片第一次显示时才需要立绘，切过去顺手把每张卡的图源刷一遍，
        // 让滚动到可视区的那些卡片按需发起下载。
        foreach (var staff in model.StaffList)
            staff.RaiseArtChanged();
    }

    private void AvatarMode_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ListModel model)
            model.UsePortrait = false;
    }

    private void PortraitMode_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ListModel model)
            model.UsePortrait = true;
    }

    private void StaffList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateClearButton();

    private void UpdateClearButton()
    {
        var hasStaff = AppState.StaffList.Count > 0;
        ClearAllButton.IsEnabled = hasStaff;
        StaffEmptyState.IsVisible = !hasStaff;
    }

    private async void ClearAll_Click(object? sender, RoutedEventArgs e)
    {
        var count = AppState.StaffList.Count;
        if (count == 0)
            return;

        if (!await AppHost.ConfirmAsync($"确定清空全部 {count} 名干员？此操作无法撤销。"))
            return;

        AppState.StaffList.Clear();
        AppState.SaveOperatorData();
        ClearGridSelection();
    }

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
        DataGridMultiSort.Apply(StaffGrid, e);

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

        if (!await AppHost.ConfirmAsync($"确定删除「{staff.Name}」？"))
            return;

        AppState.StaffList.Remove(staff);
    }
}
