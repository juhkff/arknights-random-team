using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using arknights_random_team.Models;

namespace arknights_random_team.Views;

public partial class OperatorSyncDialog : Window
{
    private readonly Dictionary<int, CheckBox> _starCheckBoxes = [];

    public OperatorSyncDialog()
    {
        InitializeComponent();
        BuildStarChoices(AppState.OperatorSyncSettings.SelectedStars);
    }

    public OperatorSyncSelection? Selection { get; private set; }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void BuildStarChoices(IReadOnlyCollection<int>? selectedStars)
    {
        var selected = selectedStars?.ToHashSet() ?? [];

        foreach (var star in AppOptions.Stars)
        {
            var checkBox = new CheckBox
            {
                Content = $"{star} 星",
                IsChecked = selected.Contains(star),
                Classes = { "sync-star" }
            };
            checkBox.Click += (_, _) => ValidationText.IsVisible = false;
            _starCheckBoxes[star] = checkBox;
            StarGrid.Children.Add(checkBox);
        }
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var checkBox in _starCheckBoxes.Values)
            checkBox.IsChecked = true;
        ValidationText.IsVisible = false;
    }

    private void ClearAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var checkBox in _starCheckBoxes.Values)
            checkBox.IsChecked = false;
    }

    private void Sync_Click(object? sender, RoutedEventArgs e)
    {
        var selectedStars = _starCheckBoxes
            .Where(item => item.Value.IsChecked == true)
            .Select(item => item.Key)
            .ToHashSet();
        if (selectedStars.Count == 0)
        {
            ValidationText.IsVisible = true;
            return;
        }

        Selection = new OperatorSyncSelection(selectedStars);
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close(false);
    }
}

public sealed record OperatorSyncSelection(IReadOnlySet<int> SelectedStars);
