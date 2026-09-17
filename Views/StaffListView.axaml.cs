using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Styles.Controls;
using Material.Styles.Models;
using arknights_random_team.Domain;
using arknights_random_team.Models;

namespace arknights_random_team.Views;

public partial class StaffListView : UserControl
{
    private bool _suppressRowSelection;
    private DispatcherTimer? _searchDebounce;

    public StaffListView()
    {
        DataContext = new ListModel();
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        Loaded += (_, _) =>
        {
            ClearGridSelection();
            UpdateCardPanel();
            ApplyListLayout();
            SyncViewButtons();
        };
        AppLayout.Changed += ApplyListLayout;
        if (DataContext is ListModel model)
            model.PropertyChanged += OnModelPropertyChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private ListModel? Model => DataContext as ListModel;

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ListModel.UsePortrait) or nameof(ListModel.IsCardView))
            UpdateCardPanel();
        if (e.PropertyName is nameof(ListModel.ShowBatchBar) or nameof(ListModel.HasActiveFilters))
            ApplyListLayout();
    }

    private void GridView_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            model.IsCardView = false;
        Dispatcher.UIThread.Post(SyncViewButtons);
    }

    private void AvatarMode_Click(object? sender, RoutedEventArgs e) => ShowCardView(portrait: false);

    private void PortraitMode_Click(object? sender, RoutedEventArgs e) => ShowCardView(portrait: true);

    private void ShowCardView(bool portrait)
    {
        if (Model is not { } model)
            return;

        var enteringCards = !model.IsCardView;
        model.UsePortrait = portrait;
        model.IsCardView = true;
        UpdateCardPanel();
        Dispatcher.UIThread.Post(SyncViewButtons);

        if (!enteringCards)
            return;

        foreach (var staff in model.StaffList)
            staff.RaiseArtChanged();
    }

    private void SyncViewButtons()
    {
        if (Model is not { } model)
            return;

        GridViewButton.IsChecked = model.IsGridView;
        AvatarModeButton.IsChecked = model.IsAvatarView;
        PortraitModeButton.IsChecked = model.IsPortraitView;
    }

    private void UpdateCardPanel()
    {
        if (StaffCards.ItemsPanelRoot is AdaptiveWrapPanel panel && Model is { } model)
            panel.PortraitMode = model.UsePortrait;
    }

    private void ApplyListLayout()
    {
        if (BatchFooter == null || BatchRow == null || BatchBar == null || BatchMenuButton == null || Model is not { } model)
            return;

        var narrow = AppLayout.IsNarrow;
        BatchFooter.IsVisible = model.ShowBatchBar;
        BatchRow.IsVisible = model.ShowBatchBar;
        BatchBar.IsVisible = model.ShowBatchBar && !narrow;
        BatchMenuButton.IsVisible = model.ShowBatchBar && narrow;
        SyncViewButtons();
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _searchDebounce.Tick -= SearchDebounce_Tick;
        _searchDebounce.Tick += SearchDebounce_Tick;
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void SearchDebounce_Tick(object? sender, EventArgs e)
    {
        _searchDebounce?.Stop();
        if (Model is { } model)
            model.SearchText = SearchBox.Text ?? "";
    }

    private void PoolAll_Click(object? sender, RoutedEventArgs e) => Model?.SetPoolFilter(PoolFilterKind.All);

    private void PoolIn_Click(object? sender, RoutedEventArgs e) => Model?.SetPoolFilter(PoolFilterKind.InPool);

    private void PoolOut_Click(object? sender, RoutedEventArgs e) => Model?.SetPoolFilter(PoolFilterKind.OutPool);

    private void ClearFilters_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model)
            return;
        model.ClearFilters();
        SearchBox.Text = "";
    }

    private void RemoveChip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: FilterChip chip })
            chip.Remove();
        if (Model is { } model)
            SearchBox.Text = model.SearchText;
    }

    private void AddManual_Click(object? sender, RoutedEventArgs e) => AppNavigation.Go(AppPage.Input);

    private async void AddSync_Click(object? sender, RoutedEventArgs e)
    {
        var result = await OperatorSyncFlow.RunAsync();
        if (!result.Ran)
            return;
        Model?.RefreshVisible();
        PostSnack(result.Message);
    }

    private void BatchAdd_Click(object? sender, RoutedEventArgs e) => Model?.ApplyBatchPool(true);

    private void BatchRemove_Click(object? sender, RoutedEventArgs e) => Model?.ApplyBatchPool(false);

    private async void ClearAll_Click(object? sender, RoutedEventArgs e)
    {
        var count = AppState.StaffList.Count;
        if (count == 0)
            return;

        if (!await AppHost.ConfirmAsync($"确定清空全部 {count} 名干员？此操作无法撤销，且不受当前筛选限制。"))
            return;

        AppState.StaffList.Clear();
        AppState.SaveOperatorData();
        ClearGridSelection();
    }

    private async void ArtStats_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            await AppHost.AlertAsync(model.ArtLoadInfo, "图片加载统计");
    }

    private void OperatorCard_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Staff staff })
            return;
        if (e.Source is Visual visual)
        {
            if (visual is CheckBox || visual.FindAncestorOfType<CheckBox>() is not null)
                return;
            if (visual.FindAncestorOfType<Button>(true) is { } inner && inner != sender)
                return;
            if (visual.FindAncestorOfType<MenuItem>() is not null)
                return;
        }

        staff.IsSelected = !staff.IsSelected;
    }

    private void CardPoolCheck_Click(object? sender, RoutedEventArgs e) => e.Handled = true;

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
        Model?.ToggleSelectAll();
    }

    private void SelectAllHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is CheckBox)
            return;
        Model?.ToggleSelectAll();
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
        Model?.BeginEdit();
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
        Model?.EndEdit();
    }

    private void StaffGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        _suppressRowSelection = true;
        if (Model is { } model)
            model.CycleSort(e.Column.SortMemberPath);
        e.Handled = true;

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
        if (ResolveStaff(sender) is not { } staff)
            return;

        if (!await AppHost.ConfirmAsync($"确定删除「{staff.Name}」？"))
            return;

        AppState.StaffList.Remove(staff);
    }

    private static Staff? ResolveStaff(object? sender)
    {
        if (sender is not Control control)
            return null;
        if (control.Tag is Staff tagged)
            return tagged;
        return control.DataContext as Staff;
    }

    private static void PostSnack(string message)
    {
        var text = new TextBlock
        {
            Text = message,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        SnackbarHost.Post(new SnackbarModel(text, TimeSpan.FromSeconds(2.5)), "MainSnackbar", DispatcherPriority.Normal);
    }
}
