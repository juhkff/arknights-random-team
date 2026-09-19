using System.Collections.Specialized;
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
        UpdateStrategyState();
        AttachedToVisualTree += (_, _) =>
        {
            AppState.Strategies.CollectionChanged -= Strategies_CollectionChanged;
            AppState.Strategies.CollectionChanged += Strategies_CollectionChanged;
            UpdateStrategyState();
        };
        DetachedFromVisualTree += (_, _) =>
            AppState.Strategies.CollectionChanged -= Strategies_CollectionChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Strategies_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateStrategyState();

    private void UpdateStrategyState()
    {
        StrategyCountText.Text = $"{AppState.Strategies.Count} 个方案";
        StrategyEmptyState.IsVisible = AppState.Strategies.Count == 0;
    }

    private async void AddStrategy_Click(object? sender, RoutedEventArgs e)
    {
        var draft = new RandomStrategyDefinition();
        var ok = await AppHost.ShowAsync<bool>(new StrategyEditorDialog(draft));
        if (ok)
            AppState.Strategies.Add(draft);
    }

    private async void EditStrategy_Click(object? sender, RoutedEventArgs e)
    {
        if (ResolveStrategy(sender) is not { } def)
            return;

        await AppHost.ShowAsync<bool>(new StrategyEditorDialog(def));
    }

    private async void DeleteStrategy_Click(object? sender, RoutedEventArgs e)
    {
        if (ResolveStrategy(sender) is not { } def)
            return;

        if (!await AppHost.ConfirmAsync($"确定删除策略「{def.Name}」？"))
            return;

        AppState.Strategies.Remove(def);
    }

    private static RandomStrategyDefinition? ResolveStrategy(object? sender)
    {
        if (sender is not Control control)
            return null;
        if (control.Tag is RandomStrategyDefinition tagged)
            return tagged;
        return control.DataContext as RandomStrategyDefinition;
    }

}
