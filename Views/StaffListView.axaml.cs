using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        if (DataContext is ListModel model)
            model.PropertyChanged += ListModel_PropertyChanged;

        StaffEmptyWebHint.IsVisible = SessionCopy.IsWeb;
        StaffEmptyDownloadButton.IsVisible = SessionCopy.IsWeb;
        UpdateEmptyState();
        AppState.StaffList.CollectionChanged += StaffList_CollectionChanged;
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        Loaded += (_, _) => ClearGridSelection();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void ListModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ListModel.IsFilterEmpty)
            or nameof(ListModel.FilteredCount)
            or nameof(ListModel.StaffCount))
            UpdateEmptyState();
    }

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
        foreach (var staff in model.FilteredStaffList)
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
        UpdateEmptyState();

    private void UpdateEmptyState()
    {
        var model = DataContext as ListModel;
        var hasStaff = AppState.StaffList.Count > 0;
        ClearAllButton.IsEnabled = hasStaff;
        StaffEmptyState.IsVisible = !hasStaff;
        FilterEmptyState.IsVisible = model?.IsFilterEmpty == true;
    }

    private void StarFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: var tag } button || DataContext is not ListModel model)
            return;
        if (!int.TryParse(tag?.ToString(), out var star))
            return;

        var want = button.IsChecked == true;
        if (model.IsStarFilterOn(star) != want)
            model.ToggleStarFilter(star);
    }

    private void CareerFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: var tag } button || DataContext is not ListModel model)
            return;
        if (!Enum.TryParse<Career>(tag?.ToString(), out var career))
            return;

        var want = button.IsChecked == true;
        if (model.IsCareerFilterOn(career) != want)
            model.ToggleCareerFilter(career);
    }

    private void SelectedFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ListModel model)
            return;
        var want = SelectedFilterButton.IsChecked == true;
        if (model.FilterSelectedOnly != want)
            model.ToggleSelectedFilter();
    }

    private void StandbyFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ListModel model)
            return;
        var want = StandbyFilterButton.IsChecked == true;
        if (model.FilterStandbyOnly != want)
            model.ToggleStandbyFilter();
    }

    private void ClearFilters_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ListModel model)
            return;

        model.ClearFilters();
        foreach (var button in FilterChipHost.Children.OfType<ToggleButton>())
            button.IsChecked = false;
    }

    private void GoToInput_Click(object? sender, RoutedEventArgs e) =>
        AppNavigation.GoTo(AppPage.Input);

    private async void OpenDesktop_Click(object? sender, RoutedEventArgs e) =>
        await SessionCopy.OpenDesktopDownloadAsync(this);

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

        // 整张干员卡可点击切换「是否参与随机」。
        // 卡片里套了职业徽记、名牌等 Border，不能只取最近的祖先。
        // 删除按钮要单独处理，点到按钮上时直接放行。
        if (visual.FindAncestorOfType<Button>(true) is null &&
            TryFindOperatorCard(visual) is { } cardStaff)
        {
            cardStaff.IsSelected = !cardStaff.IsSelected;
            return;
        }

        if (visual.FindAncestorOfType<DataGridColumnHeader>(true) is not null)
        {
            _suppressRowSelection = true;
            return;
        }

        if (visual.FindAncestorOfType<DataGridRow>(true) is not null)
            _suppressRowSelection = false;
    }

    private static Staff? TryFindOperatorCard(Visual visual)
    {
        for (Visual? current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Border border &&
                (border.Classes.Contains("operator-card-host") || border.Classes.Contains("operator-card")) &&
                border.DataContext is Staff staff)
                return staff;
        }

        return null;
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
