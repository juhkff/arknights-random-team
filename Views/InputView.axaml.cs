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
        _star = star;
        foreach (var choice in _starChoices)
            choice.IsChecked = choice.Tag?.ToString() == star.ToString();
    }

    private void Input_Click(object? sender, RoutedEventArgs e)
    {
        NameError.IsVisible = false;
        CareerError.IsVisible = false;
        LevelError.IsVisible = false;

        var name = NameTextBox.Text?.Trim() ?? "";
        // 名称规则（空名 / 重名）与详情面板、表格内联编辑共用一份 StaffValidator。
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

        if (string.IsNullOrWhiteSpace(EliteTextBox.Text) || string.IsNullOrWhiteSpace(RankTextBox.Text))
        {
            LevelError.Text = "请填写精英阶段与当前等级";
            LevelError.IsVisible = true;
            EliteTextBox.Focus();
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

        // 成功后清空表单：否则用户不改内容直接再点一次，必然撞「列表中已有该干员」——
        // 实测复现（连点两次：52 → 53 之后 53 → 53），这是新手最容易踩的一步。
        ResetManualForm();
    }

    /// <summary>把手动录入表单恢复到刚打开时的状态，并把焦点放回名称框。</summary>
    private void ResetManualForm()
    {
        NameTextBox.Text = "";
        EliteTextBox.Text = "2";
        RankTextBox.Text = "1";
        CareerCombo.SelectedIndex = -1;
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

    private void EliteTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingLevelText)
            return;

        var filtered = new string((EliteTextBox.Text ?? "")
            .Where(ch => ch >= '0' + FieldLimits.MinElite && ch <= '0' + FieldLimits.MaxElite)
            .Take(1)
            .ToArray());
        SetLevelText(EliteTextBox, filtered);
    }

    private void RankTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingLevelText)
            return;

        var maxDigits = FieldLimits.MaxRank.ToString().Length;
        var digits = new string((RankTextBox.Text ?? "").Where(char.IsDigit).Take(maxDigits).ToArray());
        if (digits.Length > 0 && int.TryParse(digits, out var value))
        {
            if (value < FieldLimits.MinRank)
                digits = "";
            else if (value > FieldLimits.MaxRank)
                digits = FieldLimits.MaxRank.ToString();
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
        int.TryParse(text, out var elite) &&
        elite is >= FieldLimits.MinElite and <= FieldLimits.MaxElite
            ? elite
            : FieldLimits.MaxElite;

    private static int ParseRank(string? text) =>
        int.TryParse(text, out var rank) &&
        rank is >= FieldLimits.MinRank and <= FieldLimits.MaxRank
            ? rank
            : FieldLimits.MinRank;
}
