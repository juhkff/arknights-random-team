using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using arknights_random_team.Domain;

namespace arknights_random_team.Views;

public partial class RandomStrategyView : UserControl
{
    public RandomStrategyView()
    {
        InitializeComponent();
        StrategyItems.ItemsSource = AppState.Strategies;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void AddStrategy_Click(object? sender, RoutedEventArgs e)
    {
        var draft = new RandomStrategyDefinition();
        var win = new StrategyEditorWindow(draft);
        var ok = await win.ShowDialog<bool>(this.FindWindow()!);
        if (ok)
            AppState.Strategies.Add(draft);
    }

    private async void EditStrategy_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not RandomStrategyDefinition def)
            return;

        var win = new StrategyEditorWindow(def);
        await win.ShowDialog<bool>(this.FindWindow()!);
        RefreshList();
    }

    private async void DeleteStrategy_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not RandomStrategyDefinition def)
            return;

        if (!await AppDialogs.Confirm(this.FindWindow(), $"确定删除策略「{def.Name}」？"))
            return;

        AppState.Strategies.Remove(def);
    }

    private void RefreshList()
    {
        StrategyItems.ItemsSource = null;
        StrategyItems.ItemsSource = AppState.Strategies;
    }
}
