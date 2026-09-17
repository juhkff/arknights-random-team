using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace arknights_random_team.Views;

/// <summary>
/// 应用外壳：侧边导航 + 顶部标题 + 页面宿主。
/// 桌面端由 MainWindow 承载，浏览器端直接作为单视图生命周期的主视图。
/// 宽度低于 900 时侧栏收成顶栏标签，给网页窄屏和触控留出内容区。
/// </summary>
public partial class MainView : UserControl
{
    private const double CompactBreakpoint = 900;

    private readonly GenerateView _generate = new();
    private readonly InputView _input = new();
    private readonly StaffListView _list = new();
    private readonly RandomStrategyView _randomStrategy = new();
    private bool _compactApplied;
    private bool _compact;

    public MainView()
    {
        InitializeComponent();

        // 浏览器端把叠加层挂进「对话框宿主位」；桌面端 Presenter 是 WindowPresenter，
        // Overlay 为 null，这里什么也不加——桌面端的对话框是独立原生窗口。
        if (AppHost.Presenter?.Overlay is { } overlay)
            DialogHost.Content = overlay;

        StorageTitleText.Text = SessionCopy.StorageTitle;
        StorageKickerText.Text = SessionCopy.StorageKicker;
        StorageHintText.Text = SessionCopy.StorageHint;
        DesktopDownloadButton.IsVisible = SessionCopy.IsWeb;

        AppNavigation.Requested += OnNavigateRequested;
        SizeChanged += (_, e) => ApplyChrome(e.NewSize.Width);
        AttachedToVisualTree += (_, _) => ApplyChrome(Bounds.Width);

        SwitchPage(
            _generate,
            "阵容生成",
            "从已启用的干员中生成一支阵容",
            GenerateNavButton,
            CompactGenerateButton);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnNavigateRequested(AppPage page)
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

    private void ChangeToGenerate(object? sender, RoutedEventArgs e) =>
        SwitchPage(_generate, "阵容生成", "从已启用的干员中生成一支阵容", GenerateNavButton, CompactGenerateButton);

    private void ChangeToInput(object? sender, RoutedEventArgs e) =>
        SwitchPage(_input, "干员录入", "手动添加干员或从数据库批量同步", InputNavButton, CompactInputButton);

    private void ChangeToList(object? sender, RoutedEventArgs e) =>
        SwitchPage(_list, "干员列表", "维护干员信息与随机池状态", ListNavButton, CompactListButton);

    private void ChangeToRandomStrategy(object? sender, RoutedEventArgs e) =>
        SwitchPage(_randomStrategy, "随机策略", "组合稀有度、职业与指定干员规则", StrategyNavButton, CompactStrategyButton);

    private void SwitchPage(Control page, string title, string subtitle, Button sidebarButton, Button compactButton)
    {
        PageHost.Content = page;
        WindowTitle.Text = title;
        WindowSubtitle.Text = subtitle;

        foreach (var button in new[] { GenerateNavButton, InputNavButton, ListNavButton, StrategyNavButton })
            button.Classes.Set("active", button == sidebarButton);
        foreach (var button in new[] { CompactGenerateButton, CompactInputButton, CompactListButton, CompactStrategyButton })
            button.Classes.Set("active", button == compactButton);
    }

    private async void OpenDesktop_Click(object? sender, RoutedEventArgs e) =>
        await SessionCopy.OpenDesktopDownloadAsync(this);

    private void ApplyChrome(double width)
    {
        if (width <= 0 || CompactBar is null)
            return;

        var compact = width < CompactBreakpoint;
        if (_compactApplied && compact == _compact)
            return;

        _compactApplied = true;
        _compact = compact;
        CompactBar.IsVisible = compact;
        CompactSessionStrip.IsVisible = compact && SessionCopy.IsWeb;
        Sidebar.IsVisible = !compact;
        ContentPane.Margin = compact ? new Thickness(0) : new Thickness(212, 0, 0, 0);
        ContentPane.RowDefinitions = compact
            ? new RowDefinitions("64,*")
            : new RowDefinitions("96,*");
        PageHeader.Padding = compact ? new Thickness(16, 0) : new Thickness(24, 0);
        WindowTitle.FontSize = compact ? 18 : 24;
        WindowSubtitle.IsVisible = !compact;
        HeaderMark.IsVisible = !compact;
        PageHost.Margin = compact ? new Thickness(12) : new Thickness(24);
    }
}
