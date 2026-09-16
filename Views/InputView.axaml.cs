using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Styles.Controls;
using Material.Styles.Models;
using arknights_random_team.Domain;
using arknights_random_team.Models;

namespace arknights_random_team.Views;

public partial class InputView : UserControl
{
    private readonly OperatorSyncService _operatorSyncService = new();
    private readonly List<ToggleButton> _starChoices = [];
    private int _star = 1;
    private bool _updatingLevelText;
    private bool _syncInProgress;

    public InputView()
    {
        InitializeComponent();
        _starChoices.AddRange([Star1Button, Star2Button, Star3Button, Star4Button, Star5Button, Star6Button]);
        SetStar(1);
        CareerCombo.SelectedIndex = -1;
        UpdateSyncStatus();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OpenSyncButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OperatorSyncDialog();
        var accepted = await AppHost.ShowAsync<bool>(dialog);
        if (!accepted || dialog.Selection is not { } selection)
            return;

        AppState.OperatorSyncSettings.SelectedStars = selection.SelectedStars.ToHashSet();
        AppState.SaveOperatorSyncSettings();
        await SyncOperatorsAsync(selection.SelectedStars);
    }

    private async Task SyncOperatorsAsync(IReadOnlySet<int> selectedStars)
    {
        if (_syncInProgress)
            return;

        _syncInProgress = true;
        OpenSyncButton.IsEnabled = false;
        SetSyncStatus("正在同步所选稀有度...");

        try
        {
            var result = await _operatorSyncService.SyncAsync(
                AppState.StaffList,
                AppState.OperatorSyncSettings,
                selectedStars);
            AppState.SaveOperatorData();

            SetSyncStatus(
                $"新增 {result.Added} 名，校正 {result.Updated} 名，跳过 {result.Unchanged} 名。",
                isSuccess: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            SetSyncStatus($"同步失败：{ex.Message}", isError: true);
        }
        finally
        {
            _syncInProgress = false;
            OpenSyncButton.IsEnabled = true;
        }
    }

    private void UpdateSyncStatus()
    {
        if (AppState.OperatorSyncSettingsError is { } settingsError)
        {
            SetSyncStatus(settingsError, isError: true);
            return;
        }

        var settings = AppState.OperatorSyncSettings;
        if (settings.LastSuccessfulSync is not { } lastSync)
        {
            SetSyncStatus(settings.SelectedStars is { Count: > 0 }
                ? "尚未同步"
                : "尚未设置同步稀有度");
            return;
        }

        var stars = string.Join("、", settings.SelectedStars!.Order());
        SetSyncStatus($"上次同步 {lastSync.ToLocalTime():yyyy-MM-dd HH:mm} · {stars} 星", isSuccess: true);
    }

    private void SetSyncStatus(string message, bool isSuccess = false, bool isError = false)
    {
        SyncStatusText.Text = message;
        SyncStatusPanel.Classes.Set("success", isSuccess);
        SyncStatusPanel.Classes.Set("error", isError);
    }

    private void StarChoice_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: var value } && int.TryParse(value?.ToString(), out var star))
            SetStar(star);
    }

    private void SetStar(int star)
    {
        _star = star;
        foreach (var choice in _starChoices)
            choice.IsChecked = choice.Tag?.ToString() == star.ToString();
    }

    private void Input_Click(object? sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text?.Trim() ?? "";
        if (name.Length <= 0)
            return;

        if (CareerCombo.SelectedItem is not Career career)
        {
            PostSnack("请选择职阶");
            return;
        }

        if (AppState.GetNameSet().Contains(name))
        {
            PostSnack("列表中已有该干员");
            return;
        }

        AppState.StaffList.Add(new Staff
        {
            Name = name,
            Star = _star,
            Career = career,
            IsSelected = true,
            Level = new Level(ParseElite(EliteTextBox.Text), ParseRank(RankTextBox.Text))
        });
        PostSnack("添加成功");
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

    private void EliteTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingLevelText)
            return;

        var filtered = new string((EliteTextBox.Text ?? "").Where(ch => ch is >= '0' and <= '2').Take(1).ToArray());
        SetLevelText(EliteTextBox, filtered);
    }

    private void RankTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingLevelText)
            return;

        var digits = new string((RankTextBox.Text ?? "").Where(char.IsDigit).Take(2).ToArray());
        if (digits.Length > 0 && int.TryParse(digits, out var value))
        {
            if (value == 0)
                digits = "";
            else if (value > 90)
                digits = "90";
        }

        SetLevelText(RankTextBox, digits);
    }

    private void SetLevelText(TextBox box, string text)
    {
        if (box.Text == text)
            return;

        _updatingLevelText = true;
        box.Text = text;
        box.CaretIndex = text.Length;
        _updatingLevelText = false;
    }

    private void EliteTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        EliteTextBox.Text = ParseElite(EliteTextBox.Text).ToString();
    }

    private void RankTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        RankTextBox.Text = ParseRank(RankTextBox.Text).ToString();
    }

    private static int ParseElite(string? text) =>
        int.TryParse(text, out var elite) && elite is >= 0 and <= 2 ? elite : 2;

    private static int ParseRank(string? text) =>
        int.TryParse(text, out var rank) && rank is >= 1 and <= 90 ? rank : 1;
}
