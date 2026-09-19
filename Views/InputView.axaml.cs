using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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
    private readonly List<ToggleButton> _starChoices = [];
    private int _star = 1;
    private bool _updatingLevelText;
    private bool _updatingElite;
    private bool _syncInProgress;

    public InputView()
    {
        InitializeComponent();
        _starChoices.AddRange([Star1Button, Star2Button, Star3Button, Star4Button, Star5Button, Star6Button]);
        SetStar(1);
        CareerCombo.SelectedIndex = -1;
        UpdateSyncStatus();
        // 订阅与退订成对：外壳复用同一实例，只在构造里订阅会在切走时留下回调。
        AttachedToVisualTree += (_, _) =>
        {
            AppLayout.Changed -= ApplyInputLayout;
            AppLayout.Changed += ApplyInputLayout;
            UpdateSyncStatus();
            ApplyInputLayout();
        };
        DetachedFromVisualTree += (_, _) => AppLayout.Changed -= ApplyInputLayout;
        Loaded += (_, _) => ApplyInputLayout();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OpenSyncButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_syncInProgress)
            return;

        _syncInProgress = true;
        OpenSyncButton.IsEnabled = false;

        try
        {
            var result = await OperatorSyncFlow.RunAsync(() => SetSyncStatus("正在同步所选稀有度..."));
            if (!result.Ran)
            {
                UpdateSyncStatus();
                return;
            }

            SetSyncStatus(result.Message, isSuccess: !result.IsError, isError: result.IsError);
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
        _star = FieldLimits.ClampStar(star);
        foreach (var choice in _starChoices)
            choice.IsChecked = choice.Tag?.ToString() == _star.ToString();
        RefreshEliteChoices();
        ClampRankField();
    }

    private void Input_Click(object? sender, RoutedEventArgs e)
    {
        NameError.IsVisible = false;
        CareerError.IsVisible = false;
        LevelError.IsVisible = false;

        var name = NameTextBox.Text?.Trim() ?? "";
        // 名称规则（空名 / 重名）与详情面板共用一份 StaffValidator。
        if (StaffValidator.ValidateName(name, AppState.StaffList) is { } nameError)
        {
            NameError.Text = nameError;
            NameError.IsVisible = true;
            NameTextBox.Focus();
            return;
        }

        if (CareerCombo.SelectedItem is not Career career)
        {
            CareerError.Text = "请选择职阶";
            CareerError.IsVisible = true;
            CareerCombo.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(RankTextBox.Text))
        {
            LevelError.Text = "请填写当前等级";
            LevelError.IsVisible = true;
            RankTextBox.Focus();
            return;
        }

        var elite = CurrentElite();
        var rank = FieldLimits.ClampRankFor(ParseRank(RankTextBox.Text), _star, elite);
        AppState.StaffList.Add(new Staff
        {
            Name = name,
            Star = _star,
            Career = career,
            IsSelected = true,
            Level = new Level(elite, rank)
        });
        PostSnack("添加成功");

        // 成功后清空表单：否则用户不改内容直接再点一次，必然撞「列表中已有该干员」——
        // 实测复现（连点两次：52 → 53 之后 53 → 53），这是新手最容易踩的一步。
        ResetManualForm();
    }

    /// <summary>把手动录入表单恢复到刚打开时的状态，并把焦点放回名称框。</summary>
    private void ResetManualForm()
    {
        NameTextBox.Text = "";
        RankTextBox.Text = "1";
        CareerCombo.SelectedIndex = -1;
        RefreshEliteChoices();
        ClampRankField();
        NameError.IsVisible = false;
        CareerError.IsVisible = false;
        LevelError.IsVisible = false;
        NameTextBox.Focus();
    }

    private void ApplyInputLayout()
    {
        if (InputColumns == null || ManualPanel == null || SyncPanel == null)
            return;

        if (AppLayout.IsNarrow)
        {
            InputColumns.ColumnDefinitions = new ColumnDefinitions("*");
            InputColumns.RowDefinitions = new RowDefinitions("Auto,16,Auto");
            Grid.SetColumn(ManualPanel, 0);
            Grid.SetRow(ManualPanel, 0);
            Grid.SetColumn(SyncPanel, 0);
            Grid.SetRow(SyncPanel, 2);
        }
        else
        {
            InputColumns.ColumnDefinitions = new ColumnDefinitions("3*,20,2*");
            InputColumns.RowDefinitions = new RowDefinitions("*");
            Grid.SetColumn(ManualPanel, 0);
            Grid.SetRow(ManualPanel, 0);
            Grid.SetColumn(SyncPanel, 2);
            Grid.SetRow(SyncPanel, 0);
        }
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

    private void EliteCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingElite)
            return;

        ClampRankField();
    }

    private void RankTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingLevelText)
            return;

        ClampRankField(keepEmpty: true);
    }

    private void RankTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            return;

        if (e.Key is Key.Back or Key.Delete or Key.Tab or Key.Enter or Key.Escape
            or Key.Left or Key.Right or Key.Home or Key.End)
            return;

        if (e.Key is >= Key.D0 and <= Key.D9 || e.Key is >= Key.NumPad0 and <= Key.NumPad9)
            return;

        e.Handled = true;
    }

    private void RankTextBox_TextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || e.Text.Any(ch => !char.IsDigit(ch)))
            e.Handled = true;
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

    private void RankTextBox_LostFocus(object? sender, RoutedEventArgs e) =>
        ClampRankField(keepEmpty: false);

    private void RefreshEliteChoices()
    {
        var maxElite = FieldLimits.MaxEliteForStar(_star);
        var elite = CurrentElite();
        _updatingElite = true;
        try
        {
            EliteCombo.ItemsSource = Enumerable.Range(0, maxElite + 1)
                .Select(FieldLimits.FormatElite)
                .ToList();
            EliteCombo.SelectedIndex = Math.Clamp(elite < 0 ? maxElite : elite, 0, maxElite);
        }
        finally
        {
            _updatingElite = false;
        }
    }

    private void ClampRankField(bool keepEmpty = false)
    {
        var max = FieldLimits.MaxRankFor(_star, CurrentElite());
        RankTextBox.PlaceholderText = $"1–{max}";
        RankTextBox.MaxLength = max.ToString().Length;

        var raw = RankTextBox.Text ?? "";
        if (keepEmpty && string.IsNullOrEmpty(raw))
            return;

        var digits = new string(raw.Where(char.IsDigit).Take(RankTextBox.MaxLength).ToArray());
        if (digits.Length == 0)
        {
            SetLevelText(RankTextBox, keepEmpty ? "" : FieldLimits.MinRank.ToString());
            return;
        }

        if (!int.TryParse(digits, out var value))
        {
            SetLevelText(RankTextBox, FieldLimits.MinRank.ToString());
            return;
        }

        if (value > max)
            digits = max.ToString();
        else if (!keepEmpty && value < FieldLimits.MinRank)
            digits = FieldLimits.MinRank.ToString();
        else if (keepEmpty && value == 0)
            digits = "";

        SetLevelText(RankTextBox, digits);
    }

    private int CurrentElite()
    {
        var max = FieldLimits.MaxEliteForStar(_star);
        return EliteCombo.SelectedIndex < 0 ? max : Math.Clamp(EliteCombo.SelectedIndex, 0, max);
    }

    private static int ParseRank(string? text) =>
        int.TryParse(text, out var rank) ? rank : FieldLimits.MinRank;
}
