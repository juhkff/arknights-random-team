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

        // 拖动判定必须监听「已被处理」的事件：
        // 卡片本身是 Button，它会把 PointerPressed/Moved 标记为已处理（ScrollViewer 也一样），
        // 于是挂在卡片 XAML 上的这三个处理器根本不会被调用——拖动判定形同虚设。
        // 实测复现：拖动 30px 后抬起，随机池被切换（方案 §3.6 明确禁止）。
        // 这里在视图层用 handledEventsToo 接管，按下时按来源限定到卡片。
        AddHandler(
            PointerPressedEvent,
            OnCardPointerPressedForDrag,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            PointerMovedEvent,
            (_, args) => TrackPressMove(args),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);

        // 表格同理：DataGrid 会把指针事件标记为已处理，挂在单元格 XAML 上的处理器收不到，
        // 结果是「点入池列没反应」（实测复现）。这里按 .cell-hit 容器在视图层接管。
        AddHandler(
            PointerPressedEvent,
            OnCellHitPointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            PointerReleasedEvent,
            OnCellHitPointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        Loaded += (_, _) =>
        {
            ClearGridSelection();
            UpdateCardPanel();
            ApplyListLayout();
            SyncViewButtons();
        };
        AppLayout.Changed += ApplyListLayout;
        // 宽高变化是全局静态事件，视图离开可视树时必须退订，否则被回收的实例仍会收到通知。
        DetachedFromVisualTree += (_, _) => AppLayout.Changed -= ApplyListLayout;
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
        if (Model is not { } model)
            return;

        // 卡片高度随视图切换：头像卡为「宽 + 名牌」，立绘卡按 5:3 再加名牌。
        // 虚拟化布局要求固定单元高度，所以这里必须显式给出，而不是按内容量算。
        if (StaffCards.Layout is UniformGridLayout layout)
            layout.MinItemHeight = CardHeight(model.UsePortrait);
    }

    private static double CardHeight(bool portrait) =>
        StaffCardVisual.MinCardWidth * (portrait ? 5d / 3d : 1d) + StaffCardVisual.NameplateHeight;

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

        // 刚发生过拖动（滚动列表）时，抬起不应被当成「切换随机池」。
        if (_pressSuppressed)
        {
            ResetPress();
            return;
        }

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

    // ---- 点击与拖动区分 ----
    //
    // 方案要求整卡快捷选池不能在指针刚按下时提交，并且要能区分点击与拖动：
    // 按下只记录起点，移动超过阈值就标记为拖动，抬起时不提交；键盘激活（空格/回车）
    // 不经过指针路径，永远按点击处理。

    private const double PressDragThreshold = 8;

    private Point _pressOrigin;
    private bool _pressTracking;
    private bool _pressSuppressed;

    private void BeginPress(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (e.Pointer.Type == PointerType.Mouse && !point.Properties.IsLeftButtonPressed)
            return;

        _pressOrigin = point.Position;
        _pressTracking = true;
        _pressSuppressed = false;
    }

    private void TrackPressMove(PointerEventArgs e)
    {
        if (!_pressTracking)
            return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _pressOrigin.X) > PressDragThreshold ||
            Math.Abs(current.Y - _pressOrigin.Y) > PressDragThreshold)
            _pressSuppressed = true;
    }

    /// <summary>抬起时判断这是一次「轻点」还是「拖动/移出目标」。</summary>
    private bool ReleaseIsTap(object? sender, PointerReleasedEventArgs e)
    {
        var suppressed = _pressSuppressed;
        ResetPress();
        if (suppressed)
            return false;

        return sender is not Visual visual || new Rect(visual.Bounds.Size).Contains(e.GetPosition(visual));
    }

    private void ResetPress()
    {
        _pressTracking = false;
        _pressSuppressed = false;
    }

    /// <summary>只对「卡片上的按下」开始跟踪拖动，其它控件的按下不参与。</summary>
    private void OnCardPointerPressedForDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source &&
            source.FindAncestorOfType<Button>(true) is { } button &&
            button.Classes.Contains("operator-card"))
        {
            BeginPress(e);
        }
    }

    private void OperatorCard_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginPress(e);

    private void OperatorCard_PointerMoved(object? sender, PointerEventArgs e) => TrackPressMove(e);

    /// <summary>
    /// 延后清理一次：Click 与本事件在同一轮输入处理里先后触发，
    /// 立即清理会让被拖动的那次抬起反而提交选池。
    /// </summary>
    private void OperatorCard_PointerReleased(object? sender, PointerReleasedEventArgs e) =>
        Dispatcher.UIThread.Post(ResetPress, DispatcherPriority.Input);

    /// <summary>键盘激活不参与拖动判定，先清掉可能残留的指针状态。</summary>
    private void OperatorCard_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter)
            ResetPress();
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
        Model?.ToggleSelectAll();
    }

    /// <summary>
    /// 事件来源是否落在复选框内部。
    /// 不能只判 <c>e.Source is CheckBox</c>：实际来源常常是复选框模板里的图形，
    /// 于是守卫失效——复选框自己切换一次、单元格处理器再切一次，净变化为零，
    /// 用户看到的就是「点入池列没反应」（实测复现）。
    /// </summary>
    private static bool IsFromCheckBox(object? source) =>
        source is Visual visual &&
        (visual is CheckBox || visual.FindAncestorOfType<CheckBox>(true) is not null);

    private static Border? CellHitTarget(object? source) =>
        source is Visual visual &&
        visual.FindAncestorOfType<Border>(true) is { } border &&
        border.Classes.Contains("cell-hit")
            ? border
            : null;

    private void OnCellHitPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (CellHitTarget(e.Source) is not null && !IsFromCheckBox(e.Source))
            BeginPress(e);
    }

    private void OnCellHitPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (IsFromCheckBox(e.Source) || CellHitTarget(e.Source) is not { } border || !ReleaseIsTap(border, e))
            return;

        if (border.DataContext is Staff staff)
            staff.IsSelected = !staff.IsSelected;
        else
            Model?.ToggleSelectAll();
    }

    private void SelectAllHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsFromCheckBox(e.Source))
            return;
        BeginPress(e);
    }

    private void SelectAllHeader_PointerMoved(object? sender, PointerEventArgs e) => TrackPressMove(e);

    private void SelectAllHeader_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (IsFromCheckBox(e.Source) || !ReleaseIsTap(sender, e))
            return;
        Model?.ToggleSelectAll();
    }

    private void SelectCell_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsFromCheckBox(e.Source))
            return;
        BeginPress(e);
    }

    private void SelectCell_PointerMoved(object? sender, PointerEventArgs e) => TrackPressMove(e);

    private void SelectCell_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (IsFromCheckBox(e.Source))
            return;
        if (sender is not Border { DataContext: Staff staff } || !ReleaseIsTap(sender, e))
            return;
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

        // 等级是唯一「自由文本 + 解析」的列：模型解析失败会静默丢掉这次输入，
        // 编辑期间就地标红并给出格式提示，不让用户以为已经改成功。
        if (e.Column.SortMemberPath == LevelSortPath && editor is TextBox levelBox)
        {
            levelBox.TextChanged -= LevelEditor_TextChanged;
            levelBox.TextChanged += LevelEditor_TextChanged;
            ValidateLevelEditor(levelBox);
        }
    }

    private const string LevelSortPath = "Level";

    private static void LevelEditor_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
            ValidateLevelEditor(box);
    }

    private static void ValidateLevelEditor(TextBox box)
    {
        var valid = Level.TryParse(box.Text, out _, out _);
        box.Classes.Set("input-error", !valid);
        ToolTip.SetTip(box, valid ? null : "等级格式应为「精二90级」，否则本次修改不会保存。");
    }

    private void StaffGrid_CellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        ClearRowHighlight(e.Row);
        Model?.EndEdit();
    }

    /// <summary>
    /// 键盘排序：列头可聚焦（见 App.axaml 的列头主题），空格或回车触发与鼠标点击一致的排序循环。
    /// 没有这条路径时，「键盘能完成主要操作」只覆盖选池与导航，排序仍只有鼠标可用。
    /// </summary>
    private void StaffGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter))
            return;

        // 这个 Avalonia 版本不公开 DataGridColumnHeader.Column，
        // 用「列头内容与列定义里的 Header 是同一个对象」反查，取不到时退回当前列。
        if (e.Source is not Visual source ||
            source.FindAncestorOfType<DataGridColumnHeader>(true) is not { } header)
            return;

        var column = StaffGrid.Columns.FirstOrDefault(c => ReferenceEquals(c.Header, header.Content))
                     ?? StaffGrid.CurrentColumn;
        if (column is null || string.IsNullOrWhiteSpace(column.SortMemberPath))
            return;

        _suppressRowSelection = true;
        Model?.CycleSort(column.SortMemberPath);
        e.Handled = true;

        // 排序会刷新可见投影（整表 Reset），DataGrid 会把焦点收回自己身上，
        // 于是「按一次空格排序、再按就落在网格上不再生效」。键盘用户期望停在刚操作的列头，
        // 这里在刷新之后把焦点还回去（实测：不还的话连按三次空格，排序状态卡在第一次的结果）。
        Dispatcher.UIThread.Post(
            () =>
            {
                if (TopLevel.GetTopLevel(header) is not null)
                    header.Focus();
            },
            DispatcherPriority.Loaded);
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

    private async void Detail_Click(object? sender, RoutedEventArgs e)
    {
        if (ResolveStaff(sender) is not { } staff)
            return;

        // 字段在面板里是草稿，保存才写回；模型已监听 Staff 的属性变化，
        // 名称/职业/稀有度/等级改动会自行重算当前筛选与排序，这里不需要额外刷新。
        await AppHost.ShowAsync<bool>(new StaffDetailDialog(staff));
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
