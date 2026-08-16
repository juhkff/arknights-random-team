using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using arknights_random_team.Models;

namespace arknights_random_team.Views;

public partial class StaffPickDialog : Window
{
    private readonly List<Staff> _all;
    private readonly HashSet<string> _excludeNames;

    public IReadOnlyList<string> SelectedStaffNames { get; private set; } = [];

    public StaffPickDialog() : this(Array.Empty<string>())
    {
    }

    public StaffPickDialog(IEnumerable<string>? excludeNames)
    {
        _excludeNames = excludeNames?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        _all = AppState.StaffList
            .GroupBy(s => s.Name)
            .Select(g => g.First())
            .OrderBy(s => s.Name)
            .ToList();

        InitializeComponent();
        ApplyFilter();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void ApplyFilter()
    {
        var q = SearchBox.Text?.Trim() ?? "";
        IEnumerable<Staff> src = _all.Where(s => !_excludeNames.Contains((s.Name ?? "").Trim()));
        if (q.Length > 0)
            src = src.Where(s => s.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

        StaffList.ItemsSource = src.ToList();
        StaffList.SelectedItems?.Clear();
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void StaffList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        _ = TryConfirm();
    }

    private void StaffList_DoubleTapped(object? sender, TappedEventArgs e) => _ = TryConfirm();

    private void Ok_Click(object? sender, RoutedEventArgs e) => _ = TryConfirm();

    private async Task TryConfirm()
    {
        var names = (StaffList.SelectedItems?.OfType<Staff>() ?? [])
            .Select(s => s.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct()
            .ToList();

        if (names.Count == 0)
        {
            await AppDialogs.Alert(this, "请先在列表中选择至少一名干员（可点击多选）。");
            return;
        }

        SelectedStaffNames = names;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
