using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private Staff? _nameEditStaff;
    private string? _nameEditOriginal;
    private bool _cardLayoutUpdateQueued;

    public StaffListView()
    {
        DataContext = new ListModel();
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);

        // DataGrid、Button 与 ScrollViewer 会处理指针事件，因此在根控件统一监听，
        // 避免 XAML 元素各挂一套重复且经常收不到事件的手势处理器。
        AddHandler(PointerPressedEvent, OnInteractivePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnInteractivePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnInteractivePointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged, RoutingStrategies.Bubble, handledEventsToo: true);
        // DataGrid owns ScrollBars directly instead of using a ScrollViewer.
        StaffGrid.AddHandler(RangeBase.ValueChangedEvent, (_, e) =>
        {
            if (e.Source is ScrollBar)
                ArtImage.NotifyInteraction();
        }, RoutingStrategies.Bubble, handledEventsToo: true);

        Loaded += (_, _) =>
        {
            ClearGridSelection();
            UpdateCardPanel();
            ApplyListLayout();
            SyncViewButtons();
        };

        // Recompute after the repeater has a real arranged width. Updating the layout from
        // ScrollViewer.Viewport while it is arranging creates a measure/arrange feedback loop,
        // so resize/viewport notifications are coalesced onto the next UI pass.
        StaffCards.SizeChanged += (_, _) => QueueCardLayoutUpdate();
        StaffCardsScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ViewportProperty)
                QueueCardLayoutUpdate();
        };

        // 视图自身尺寸变化后重算：ApplyListLayout 里有依赖 Bounds 的窄屏宽度计算，
        // 只在断点变化时触发会漏掉「同一断点内的尺寸变化」。
        SizeChanged += (_, _) => ApplyListLayout();

        // Global subscriptions follow the visual-tree lifetime because the shell reuses this view.
        AttachedToVisualTree += (_, _) =>
        {
            AppLayout.Changed -= ApplyListLayout;
            AppLayout.Changed += ApplyListLayout;
            if (Model is { } model)
            {
                model.Activate();
                AppState.BulkUpdateCompleted -= model.OnBulkUpdateCompleted;
                AppState.BulkUpdateCompleted += model.OnBulkUpdateCompleted;
                ArtImage.StatsChanged -= model.OnArtStatsChanged;
                ArtImage.StatsChanged += model.OnArtStatsChanged;
            }

            ApplyListLayout();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            ResetPress();
            AppLayout.Changed -= ApplyListLayout;
            if (Model is { } model)
            {
                AppState.BulkUpdateCompleted -= model.OnBulkUpdateCompleted;
                ArtImage.StatsChanged -= model.OnArtStatsChanged;
                model.Deactivate();
            }

            FlushPendingSearch();
        };

        if (DataContext is ListModel model)
            model.PropertyChanged += OnModelPropertyChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private ListModel? Model => DataContext as ListModel;

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ListModel.IsCardView))
        {
            QueueCardLayoutUpdate();
            ApplyListLayout();
        }
        if (e.PropertyName is nameof(ListModel.ShowBatchBar) or nameof(ListModel.HasActiveFilters))
            ApplyListLayout();
    }

    private void GridView_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            model.IsCardView = false;
        Dispatcher.UIThread.Post(SyncViewButtons);
    }

    private void HalfBodyMode_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model)
            return;

        model.IsCardView = true;
        UpdateCardPanel();
        Dispatcher.UIThread.Post(SyncViewButtons);
    }

    private void SyncViewButtons()
    {
        if (Model is not { } model)
            return;

        GridViewButton.IsChecked = model.IsGridView;
        HalfBodyModeButton.IsChecked = model.IsHalfBodyView;
    }

    private void QueueCardLayoutUpdate()
    {
        if (_cardLayoutUpdateQueued)
            return;

        _cardLayoutUpdateQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _cardLayoutUpdateQueued = false;
                UpdateCardPanel();
            },
            DispatcherPriority.Loaded);
    }

    private void UpdateCardPanel()
    {
        if (StaffCards.Layout is not UniformGridLayout layout)
            return;

        var available = StaffCardsScroll.Viewport.Width - StaffCards.Margin.Left - StaffCards.Margin.Right;
        if (available <= 0)
            return;

        var (_, itemWidth) = StaffCardVisual.FitColumns(available);
        var itemHeight = StaffCardVisual.CardHeight(itemWidth);
        if (Math.Abs(layout.MinItemWidth - itemWidth) < 0.5 &&
            Math.Abs(layout.MinItemHeight - itemHeight) < 0.5)
            return;

        layout.MinItemWidth = itemWidth;
        layout.MinItemHeight = itemHeight;
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

        // Keep both view segments visible on narrow layouts; only sorting and row density move to a menu.
        var phone = AppLayout.IsPhone;
        WideToolsPanel.IsVisible = !phone;
        PhoneToolsButton.IsVisible = phone;
        FilterChipsList.IsVisible = !phone;
        SelectAllScopeLabel.IsVisible = !phone && model.HasActiveFilters;

        Grid.SetColumnSpan(SummaryPanel, phone ? 3 : 1);
        Grid.SetRow(ViewModeSwitch, phone ? 1 : 0);
        Grid.SetColumn(ViewModeSwitch, phone ? 0 : 1);
        Grid.SetRow(TopMoreButton, phone ? 1 : 0);
        ViewModeSwitch.HorizontalAlignment = phone ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        ViewModeSwitch.Margin = phone ? new Thickness(0, 8, 8, 0) : new Thickness(8, 0);
        TopMoreButton.Margin = phone ? new Thickness(0, 8, 0, 0) : default;

        if (phone && Bounds.Width > 0)
        {
            // Bounds already excludes page margins; leave room for the WrapPanel margin.
            SearchBox.Width = Math.Max(120, Bounds.Width - 8);
        }
        else
        {
            // Keep search/filter/sort on one row without forcing the flexible search field too narrow.
            var reserved = model.IsGridView ? 332 : 268;
            SearchBox.Width = Bounds.Width > 0
                ? Math.Clamp(Bounds.Width - reserved, 200, 360)
                : 240;
        }

        SyncViewButtons();
    }

    /// <summary>窄屏菜单里的排序：与宽屏下拉走同一份 SortIndex。</summary>
    private void PhoneSort_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } &&
            int.TryParse(tag, out var index) &&
            Model is { } model)
        {
            model.SortIndex = index;
        }
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

    /// <summary>
    /// 离开页面时把还没到点的搜索立即结算，然后停表。
    /// 只停表会把「刚打完字就切页」的输入丢掉；只结算不停止则让离屏页继续跑计时器。
    /// </summary>
    private void FlushPendingSearch()
    {
        if (_searchDebounce is { IsEnabled: true } timer)
        {
            timer.Stop();
            if (Model is { } model)
                model.SearchText = SearchBox.Text ?? "";
        }
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
        PostSnack(result.Message);
    }

    private void BatchAdd_Click(object? sender, RoutedEventArgs e) => Model?.ApplyBatchPool(true);

    private void BatchRemove_Click(object? sender, RoutedEventArgs e) => Model?.ApplyBatchPool(false);

    private async void ClearAll_Click(object? sender, RoutedEventArgs e)
    {
        var snapshot = AppState.StaffList.ToList();
        if (snapshot.Count == 0)
            return;

        if (!await AppHost.ConfirmAsync($"确定清空全部 {snapshot.Count} 名干员？此操作无法撤销，且不受当前筛选限制。"))
            return;

        AppState.StaffList.Clear();
        if (!AppState.SaveOperatorData())
        {
            // 磁盘仍保留旧列表，因此内存也恢复到清空前的状态。
            using (AppState.BeginBulkUpdate())
            {
                foreach (var staff in snapshot)
                    AppState.StaffList.Add(staff);
            }

            await AppHost.AlertAsync(
                $"{AppState.LastSaveError ?? "保存失败。"}已撤销本次清空。",
                "清空失败");
            return;
        }

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

        if (_pressTracking && !ReferenceEquals(_pressTarget, sender))
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
            if (visual.FindAncestorOfType<MenuItem>(true) is not null)
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
    private IPointer? _pressPointer;
    private Visual? _pressTarget;
    private InputElement? _pressCapture;

    /// <summary>
    /// 记录一次「有效按下」：鼠标只认左键，触摸与笔都算。
    /// 同时记下指针与目标控件——抬起时要用它们判断这次抬起是否属于同一次按下。
    /// </summary>
    private void BeginPress(PointerPressedEventArgs e, Visual? target = null)
    {
        var point = e.GetCurrentPoint(this);
        if (e.Pointer.Type == PointerType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            // 右键/中键按下不是有效手势：连跟踪都不开始，抬起时自然不会提交。
            ResetPress();
            return;
        }

        ResetPress();
        _pressOrigin = point.Position;
        _pressTracking = true;
        _pressSuppressed = false;
        _pressPointer = e.Pointer;
        _pressTarget = target;

        var pointer = e.Pointer;
        Dispatcher.UIThread.Post(() => AttachPressCapture(pointer), DispatcherPriority.Input);
    }

    private void AttachPressCapture(IPointer pointer)
    {
        if (!_pressTracking || !ReferenceEquals(_pressPointer, pointer))
            return;

        if (pointer.Captured is null && _pressTarget is InputElement target)
            pointer.Capture(target);

        if (pointer.Captured is not InputElement captured || ReferenceEquals(_pressCapture, captured))
            return;

        _pressCapture?.RemoveHandler(PointerCaptureLostEvent, OnPointerCaptureLost);
        _pressCapture = captured;
        captured.AddHandler(
            PointerCaptureLostEvent,
            OnPointerCaptureLost,
            RoutingStrategies.Direct,
            handledEventsToo: true);
    }

    private void TrackPressMove(PointerEventArgs e)
    {
        if (!_pressTracking || !ReferenceEquals(e.Pointer, _pressPointer))
            return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _pressOrigin.X) > PressDragThreshold ||
            Math.Abs(current.Y - _pressOrigin.Y) > PressDragThreshold)
            _pressSuppressed = true;
    }

    private bool ReleaseIsTap(Visual target, PointerReleasedEventArgs e)
    {
        var isTap = _pressTracking &&
                    !_pressSuppressed &&
                    ReferenceEquals(_pressPointer, e.Pointer) &&
                    ReferenceEquals(_pressTarget, target) &&
                    (e.Pointer.Type != PointerType.Mouse || e.InitialPressMouseButton == MouseButton.Left) &&
                    new Rect(target.Bounds.Size).Contains(e.GetPosition(target));
        ResetPress();
        return isTap;
    }

    private void ResetPress()
    {
        var pointer = _pressPointer;
        var capture = _pressCapture;
        capture?.RemoveHandler(PointerCaptureLostEvent, OnPointerCaptureLost);
        _pressCapture = null;
        _pressTracking = false;
        _pressSuppressed = false;
        _pressPointer = null;
        _pressTarget = null;

        if (capture is not null && ReferenceEquals(pointer?.Captured, capture))
            pointer.Capture(null);
    }

    private void OnInteractivePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_pressTracking && !ReferenceEquals(_pressPointer, e.Pointer))
        {
            e.Handled = true;
            return;
        }

        if (e.Source is not Visual source)
        {
            ResetPress();
            return;
        }

        var button = source.FindAncestorOfType<Button>(true);
        if (button?.Classes.Contains("operator-card") == true)
        {
            BeginPress(e, button);
            return;
        }

        if (!IsFromCheckBox(source) && CellHitTarget(source) is { } cell)
        {
            BeginPress(e, cell);
            return;
        }

        ResetPress();
    }

    private void OnInteractivePointerMoved(object? sender, PointerEventArgs e)
    {
        TrackPressMove(e);
        AttachPressCapture(e.Pointer);
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // A viewport/extent relayout is not user activity. Only real offset movement should
        // keep image prefetch responsive while scrolling by wheel, touch inertia, keyboard,
        // scrollbar thumb, or programmatic navigation.
        if (!e.OffsetDelta.NearlyEquals(default))
            ArtImage.NotifyInteraction();
    }

    private void OnInteractivePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressTracking && !ReferenceEquals(_pressPointer, e.Pointer))
        {
            e.Handled = true;
            return;
        }

        if (_pressTarget is Border { } cell && cell.Classes.Contains("cell-hit"))
        {
            if (ReferenceEquals(CellHitTarget(e.Source), cell) &&
                !IsFromCheckBox(e.Source) &&
                ReleaseIsTap(cell, e))
            {
                if (cell.DataContext is Staff staff)
                    staff.IsSelected = !staff.IsSelected;
                else
                    Model?.ToggleSelectAll();
            }
            else
            {
                ResetPress();
            }

            return;
        }

        // Button raises Click while handling PointerReleased; clear after that handler has read drag state.
        Dispatcher.UIThread.Post(ResetPress, DispatcherPriority.Input);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_pressTracking || !ReferenceEquals(_pressPointer, e.Pointer))
            return;

        // Button may release capture immediately before raising Click. Defer cleanup so the Click handler
        // can still see whether this gesture crossed the drag threshold.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_pressTracking && ReferenceEquals(_pressPointer, e.Pointer))
                    ResetPress();
            },
            DispatcherPriority.Input);
    }

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
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (_suppressRowSelection)
                        ClearGridSelection();
                },
                DispatcherPriority.Background);
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

        // 名称是生成阵容时的分组键：留一份原值，编辑结束若校验不通过就还原，
        // 否则表格可以直接把名称写成空或与别人重名（方案 §4 U5）。
        if (e.Column.SortMemberPath == NameSortPath && e.Row.DataContext is Staff nameStaff)
        {
            _nameEditStaff = nameStaff;
            _nameEditOriginal = nameStaff.Name;
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

    private const string NameSortPath = "Name";

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

        if (e.Column.SortMemberPath == NameSortPath && _nameEditStaff is { } staff)
        {
            var original = _nameEditOriginal ?? "";
            _nameEditStaff = null;
            _nameEditOriginal = null;

            var normalized = staff.Name.Trim();
            if (StaffValidator.ValidateName(normalized, AppState.StaffList, staff) is { } error)
            {
                staff.Name = original;
                PostSnack($"{error}已还原为「{original}」。");
            }
            else
            {
                AppState.RenameStaff(staff, original, normalized);
            }
        }

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
                ClearGridSelection();
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
