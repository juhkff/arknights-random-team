using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

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

    public MainView()
    {
        InitializeComponent();

        // 浏览器端把叠加层挂进「对话框宿主位」；桌面端 Presenter 是 WindowPresenter，
        // Overlay 为 null，这里什么也不加——桌面端的对话框是独立原生窗口。
        if (AppHost.Presenter?.Overlay is { } overlay)
            DialogHost.Content = overlay;

        SwitchPage(
            _generate,
            "阵容生成",
            "从已启用的干员中生成一支阵容",
            GenerateNavButton);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void ChangeToGenerate(object? sender, RoutedEventArgs e) =>
        SwitchPage(_generate, "阵容生成", "从已启用的干员中生成一支阵容", GenerateNavButton);

    private void ChangeToInput(object? sender, RoutedEventArgs e) =>
        SwitchPage(_input, "干员录入", "手动添加干员或从数据库批量同步", InputNavButton);

    private void ChangeToList(object? sender, RoutedEventArgs e) =>
        SwitchPage(_list, "干员列表", "维护干员信息与随机池状态", ListNavButton);

    private void ChangeToRandomStrategy(object? sender, RoutedEventArgs e) =>
        SwitchPage(_randomStrategy, "随机策略", "组合稀有度、职业与指定干员规则", StrategyNavButton);

    private void SwitchPage(Control page, string title, string subtitle, Button activeButton)
    {
        PageHost.Content = page;
        WindowTitle.Text = title;
        WindowSubtitle.Text = subtitle;

        foreach (var button in new[] { GenerateNavButton, InputNavButton, ListNavButton, StrategyNavButton })
            button.Classes.Set("active", button == activeButton);
    }
}
