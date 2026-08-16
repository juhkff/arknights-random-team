using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Transformation;
using arknights_random_team.Views;

namespace arknights_random_team;

public partial class MainWindow : Window
{
    private readonly GenerateView _generate = new();
    private readonly InputView _input = new();
    private readonly StaffListView _list = new();
    private readonly RandomStrategyView _randomStrategy = new();
    private bool _drawerOpen;

    public MainWindow()
    {
        InitializeComponent();
        PageHost.Content = _generate;
        SetDrawerOpen(false);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void ChangeToGenerate(object? sender, RoutedEventArgs e) =>
        SwitchPage(_generate, "阵容生成");

    private void ChangeToInput(object? sender, RoutedEventArgs e) =>
        SwitchPage(_input, "干员录入");

    private void ChangeToList(object? sender, RoutedEventArgs e) =>
        SwitchPage(_list, "干员列表");

    private void ChangeToRandomStrategy(object? sender, RoutedEventArgs e) =>
        SwitchPage(_randomStrategy, "随机策略");

    private void SwitchPage(Control page, string title)
    {
        PageHost.Content = page;
        WindowTitle.Text = title;
        SetDrawerOpen(false);
    }

    private void ToggleDrawer(object? sender, RoutedEventArgs e) =>
        SetDrawerOpen(!_drawerOpen);

    private void CloseDrawer(object? sender, RoutedEventArgs e) =>
        SetDrawerOpen(false);

    private void DrawerScrim_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        SetDrawerOpen(false);

    private void SetDrawerOpen(bool open)
    {
        if (DrawerPanel is null || DrawerScrim is null)
            return;

        _drawerOpen = open;

        DrawerPanel.Classes.Set("open", open);
        DrawerScrim.Classes.Set("open", open);
        DrawerPanel.RenderTransform = TransformOperations.Parse(open ? "translateX(0px)" : "translateX(-120px)");
        DrawerScrim.Opacity = open ? 0.32 : 0;
        DrawerScrim.IsHitTestVisible = open;
    }
}
