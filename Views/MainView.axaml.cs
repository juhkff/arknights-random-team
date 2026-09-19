using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Styles.Controls;
using Material.Styles.Models;

namespace arknights_random_team.Views;

/// <summary>
/// 应用外壳：侧边导航 + 顶部标题 + 页面宿主。
/// 桌面端由 MainWindow 承载，浏览器端直接作为单视图生命周期的主视图。
/// </summary>
public partial class MainView : UserControl
{
    private readonly GenerateView _generate = new();
    private readonly InputView _input = new();
    private readonly StaffListView _list = new();
    private readonly RandomStrategyView _randomStrategy = new();
    private readonly ArtPrefetcher _artPrefetcher = ArtPrefetcher.Shared;
    private bool _drawerOpen;

    public MainView()
    {
        InitializeComponent();

        if (AppHost.Presenter?.Overlay is { } overlay)
            DialogHost.Content = overlay;

        if (AppState.UseFileStorage)
        {
            StorageTitle.Text = "本地数据";
            StorageDetail.Text = "退出时自动保存";
            ToolTip.SetTip(StoragePanel, "干员与策略会写入本地文件，退出时自动保存。");
        }
        else
        {
            StorageTitle.Text = "体验模式";
            StorageDetail.Text = "刷新后数据重置";
            ToolTip.SetTip(StoragePanel, "网页端仅保存在内存中，刷新或关闭页面后数据会重置。");
        }

        // Read-only mode and unstable files must remain visible after the startup snackbar expires.
        if (AppState.WriteBlockedFiles is { Count: > 0 } blockedFiles)
        {
            StorageTitle.Text = "未写回";
            StorageDetail.Text = $"{blockedFiles.Count} 个文件未保存";

            // These controls have local brush values, so set the warning colors directly.
            if (Application.Current?.TryGetResource("AppDangerBrush", ActualThemeVariant, out var danger) == true &&
                danger is IBrush dangerBrush)
            {
                StorageTitle.Foreground = dangerBrush;
                StorageDot.Background = dangerBrush;
                StoragePanel.BorderBrush = dangerBrush;

                if (Application.Current.TryGetResource("AppDangerSoftBrush", ActualThemeVariant, out var soft) == true &&
                    soft is IBrush softBrush)
                {
                    StoragePanel.Background = softBrush;
                }
            }

            ToolTip.SetTip(
                StoragePanel,
                $"当前会话无法安全写入：{string.Join("、", blockedFiles)}。磁盘内容不会被覆盖；请解除目录占用或写权限问题后重新启动。");
        }

        AttachedToVisualTree += (_, _) =>
        {
            AppNavigation.Requested -= OnNavigationRequested;
            AppNavigation.Requested += OnNavigationRequested;
            _artPrefetcher.Attach();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            AppNavigation.Requested -= OnNavigationRequested;
            _artPrefetcher.Detach();
        };
        PropertyChanged += OnViewPropertyChanged;
        KeyDown += OnShellKeyDown;
        AddHandler(KeyDownEvent, (_, _) => ArtImage.NotifyInteraction(),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, (_, _) => ArtImage.NotifyInteraction(),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => ArtImage.NotifyInteraction(),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, (_, e) =>
        {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed ||
                point.Properties.IsRightButtonPressed)
                ArtImage.NotifyInteraction();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Loaded += (_, _) =>
        {
            ApplyShellLayout();
            AppHost.NotifyShellReady();
            ReportLoadErrors();
        };

        SwitchPage(
            _generate,
            "阵容生成",
            "从已启用的干员中生成一支阵容",
            GenerateNavButton);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// 启动时如果有数据文件读不出来（损坏文件已改名留证），用 Snackbar 明确告知。
    /// 静默降级会让用户以为「干员自己没了」，比报错更糟。
    /// </summary>
    private static void ReportLoadErrors()
    {
        var messages = AppState.StartupNotices
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct()
            .ToArray();
        if (messages.Length == 0)
            return;

        // Keep multi-file diagnostics within the narrow-screen snackbar.
        var summary = messages.Length <= 2
            ? string.Join("\n", messages)
            : $"{messages[0]}\n（另有 {messages.Length - 1} 条提示，详见日志）";

        var text = new TextBlock
        {
            Text = summary,
            FontSize = 14,
            MaxWidth = 560,
            MaxHeight = 240,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        SnackbarHost.Post(new SnackbarModel(text, TimeSpan.FromSeconds(8)), "MainSnackbar", DispatcherPriority.Normal);
    }

    private void ChangeToGenerate(object? sender, RoutedEventArgs e) =>
        SwitchPage(_generate, "阵容生成", "从已启用的干员中生成一支阵容", GenerateNavButton);

    private void ChangeToInput(object? sender, RoutedEventArgs e) =>
        SwitchPage(_input, "干员录入", "手动添加干员或从数据库批量同步", InputNavButton);

    private void ChangeToList(object? sender, RoutedEventArgs e) =>
        SwitchPage(_list, "干员列表", "维护干员信息与随机池状态", ListNavButton);

    private void ChangeToRandomStrategy(object? sender, RoutedEventArgs e) =>
        SwitchPage(_randomStrategy, "随机策略", "组合稀有度、职业与指定干员规则", StrategyNavButton);

    private void OnNavigationRequested(AppPage page)
    {
        switch (page)
        {
            case AppPage.Generate:
                ChangeToGenerate(null, new RoutedEventArgs());
                break;
            case AppPage.Input:
                ChangeToInput(null, new RoutedEventArgs());
                break;
            case AppPage.List:
                ChangeToList(null, new RoutedEventArgs());
                break;
            case AppPage.Strategy:
                ChangeToRandomStrategy(null, new RoutedEventArgs());
                break;
        }
    }

    private void SwitchPage(Control page, string title, string subtitle, Button activeButton)
    {
        PageHost.Content = page;
        WindowTitle.Text = title;
        WindowSubtitle.Text = subtitle;

        foreach (var button in new[] { GenerateNavButton, InputNavButton, ListNavButton, StrategyNavButton })
            button.Classes.Set("active", button == activeButton);

        CloseDrawer();
    }

    private void MenuButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_drawerOpen)
            CloseDrawer();
        else
            OpenDrawer();
    }

    private void NavScrim_PointerPressed(object? sender, PointerPressedEventArgs e) => CloseDrawer();

    private void OnViewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty)
            ApplyShellLayout();
    }

    private void ApplyShellLayout()
    {
        AppLayout.Update(Bounds.Width);
        var drawer = AppLayout.UseDrawerNav;
        var compact = AppLayout.UseIconNav;
        var pad = AppLayout.PagePadding;

        WindowSubtitle.IsVisible = !AppLayout.IsPhone;
        WindowTitle.FontSize = AppLayout.IsPhone ? 20 : 24;

        BrandCopy.IsVisible = !compact;
        BrandMark.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        StorageCopy.IsVisible = !compact;
        StorageDot.IsVisible = compact;
        if (compact)
        {
            StoragePanel.Width = 40;
            StoragePanel.Height = 40;
            StoragePanel.Padding = new Thickness(0);
            StoragePanel.Margin = new Thickness(16, 12, 16, 16);
            StoragePanel.HorizontalAlignment = HorizontalAlignment.Center;
        }
        else
        {
            StoragePanel.ClearValue(WidthProperty);
            StoragePanel.ClearValue(HeightProperty);
            StoragePanel.Padding = new Thickness(12);
            StoragePanel.Margin = new Thickness(12, 12, 12, 16);
            StoragePanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        foreach (var button in new[] { GenerateNavButton, InputNavButton, ListNavButton, StrategyNavButton })
        {
            button.Classes.Set("compact", compact);
            if (button.Content is Grid grid)
            {
                grid.ColumnDefinitions = compact
                    ? new ColumnDefinitions("*")
                    : new ColumnDefinitions("4,40,*");
                foreach (var child in grid.Children)
                {
                    switch (child)
                    {
                        case TextBlock label:
                            label.IsVisible = !compact;
                            break;
                        case Avalonia.Controls.Shapes.Path icon:
                            Grid.SetColumn(icon, compact ? 0 : 1);
                            break;
                        case Border indicator:
                            indicator.IsVisible = !compact;
                            break;
                    }
                }
            }
        }

        PageHeader.Height = AppLayout.HeaderHeight;
        PageHeader.Padding = new Thickness(pad, 0);
        PageHost.Margin = new Thickness(pad);

        MenuButton.IsVisible = drawer;
        NavScrim.IsVisible = drawer && _drawerOpen;

        if (drawer)
        {
            Sidebar.Width = 208;
            Sidebar.IsVisible = _drawerOpen;
            MainContent.Margin = new Thickness(0);
        }
        else
        {
            _drawerOpen = false;
            Sidebar.IsVisible = true;
            Sidebar.Width = compact ? 72 : 208;
            MainContent.Margin = new Thickness(Sidebar.Width, 0, 0, 0);
            NavScrim.IsVisible = false;
        }
    }

    private void OpenDrawer()
    {
        if (!AppLayout.UseDrawerNav)
            return;

        _drawerOpen = true;
        Sidebar.IsVisible = true;
        Sidebar.Width = 208;
        NavScrim.IsVisible = true;

        // 打开即把焦点送进抽屉：否则键盘用户还要从头 Tab 一遍才轮到导航项。
        Dispatcher.UIThread.Post(() => GenerateNavButton.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>抽屉打开时用 Esc 关闭（与对话框的 Esc 行为一致）。</summary>
    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_drawerOpen)
            return;

        e.Handled = true;
        CloseDrawer();
    }

    private void CloseDrawer()
    {
        if (!_drawerOpen)
            return;

        _drawerOpen = false;
        NavScrim.IsVisible = false;
        if (AppLayout.UseDrawerNav)
            Sidebar.IsVisible = false;

        // 关闭后把焦点还给打开抽屉的按钮，避免掉回页面开头。
        Dispatcher.UIThread.Post(() => MenuButton.Focus(), DispatcherPriority.Loaded);
    }
}
