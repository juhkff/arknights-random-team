using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using arknights_random_team.Domain;
using arknights_random_team.Models;

namespace arknights_random_team.Views;

public sealed record StrategyComboItem(string DisplayName, RandomStrategyDefinition? Model);

public partial class GenerateView : UserControl
{
    public ObservableCollection<Staff> ResultList { get; } = [];

    public GenerateView()
    {
        DataContext = this;
        InitializeComponent();
        ApplyRosterView(AppState.UiPreferences.RosterViewMode, persist: false);
        ResultList.CollectionChanged += (_, _) => UpdateResultState();
        CountSlider.Maximum = AppOptions.MaxTeamSize;
        RefreshStrategyCombo();
        UpdateTeamSizeControls();
        UpdateResultState();
        // Height pressure depends on this view's arranged size, not only the global breakpoint.
        SizeChanged += (_, _) => ApplyGenerateLayout();
        AttachedToVisualTree += (_, _) =>
        {
            AppLayout.Changed -= ApplyGenerateLayout;
            AppLayout.Changed += ApplyGenerateLayout;
            AppState.Strategies.CollectionChanged -= OnStrategiesChanged;
            AppState.Strategies.CollectionChanged += OnStrategiesChanged;
            RefreshStrategyCombo();
            ApplyGenerateLayout();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            AppLayout.Changed -= ApplyGenerateLayout;
            AppState.Strategies.CollectionChanged -= OnStrategiesChanged;
        };
        Loaded += (_, _) => ApplyGenerateLayout();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void RosterView_Click(object? sender, RoutedEventArgs e)
    {
        var mode = ReferenceEquals(sender, RosterFormationButton)
            ? RosterViewMode.Formation
            : RosterViewMode.List;
        ApplyRosterView(mode);
    }

    private void ApplyRosterView(RosterViewMode mode, bool persist = true)
    {
        var formation = mode == RosterViewMode.Formation;
        if (persist)
            AppState.UiPreferences.RosterViewMode = formation ? RosterViewMode.Formation : RosterViewMode.List;

        if (RosterListButton is null || RosterFormationButton is null ||
            RosterItems is null || FormationItems is null)
            return;

        RosterListButton.IsChecked = !formation;
        RosterFormationButton.IsChecked = formation;
        RosterItems.IsVisible = !formation;
        FormationItems.IsVisible = formation;
    }

    private void OnStrategiesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshStrategyCombo();

    private void RefreshStrategyCombo()
    {
        var keepId = (StrategyCombo.SelectedItem as StrategyComboItem)?.Model?.Id;
        var list = new List<StrategyComboItem> { new("无策略", null) };
        list.AddRange(AppState.Strategies.Select(strategy => new StrategyComboItem(strategy.Name, strategy)));

        StrategyCombo.ItemsSource = list;
        StrategyCombo.SelectedItem = list.FirstOrDefault(x => x.Model?.Id == keepId) ?? list[0];
    }

    private void CountSlider_OnPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Slider.ValueProperty || e.Property == Slider.MaximumProperty)
            UpdateTeamSizeControls();
    }

    private void UpdateTeamSizeControls()
    {
        // XAML sets the slider value before the remaining controls are created.
        if (RandomNumText == null || CountSlider == null || FullPresetButton == null)
            return;

        var count = (int)CountSlider.Value;
        RandomNumText.Text = count.ToString();
        TeamSizeLimitText.Text = $"/ {(int)CountSlider.Maximum}";
        DecreaseCountButton.IsEnabled = count > CountSlider.Minimum;
        IncreaseCountButton.IsEnabled = count < CountSlider.Maximum;

        foreach (var preset in new[] { SoloPresetButton, FourPresetButton, SixPresetButton, FullPresetButton })
        {
            if (!int.TryParse(preset.Tag?.ToString(), out var size))
                continue;
            preset.IsEnabled = size >= CountSlider.Minimum && size <= CountSlider.Maximum;
            preset.Classes.Set("selected", count == size);
        }
    }

    private void SetTeamSize(int count) =>
        CountSlider.Value = Math.Clamp(count, (int)CountSlider.Minimum, (int)CountSlider.Maximum);

    private void DecreaseCount_Click(object? sender, RoutedEventArgs e) =>
        SetTeamSize((int)CountSlider.Value - 1);

    private void IncreaseCount_Click(object? sender, RoutedEventArgs e) =>
        SetTeamSize((int)CountSlider.Value + 1);

    private void CountPreset_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var count))
            SetTeamSize(count);
    }

    private void UpdateResultState()
    {
        if (ResultEmptyState == null || ResultCountText == null)
            return;

        ResultEmptyState.IsVisible = ResultList.Count == 0;
        ResultCountText.Text = $"{ResultList.Count} 名干员";
    }

    private void ApplyGenerateLayout()
    {
        if (ConfigGrid == null || StrategyConfig == null || CountConfig == null)
            return;

        // 短屏（横屏手机、软键盘弹出）把配置区限高并内部滚动：
        // 配置区在 Auto 行里，不限高就会把下方「出战名单」的 * 行压成 0。
        var available = Bounds.Height;
        if (ConfigScroll != null)
        {
            // 预留 260 = 生成按钮 44 + 内边距 32 + 行距 16 + 名单标题与间距 ~36 + 名单区最小 ~130。
            // 上限取可用高度的一半，短屏时把空间优先让给「出战名单」（方案 §4 U3：
            // 短屏优先让结果可读）；极短屏下限收到 0，配置区仍可滚动。
            // 下限 64：MaxHeight=0 会让 ScrollViewer 视口为 0、滚动条根本不出现，
            // 「极短屏仍可滚动」就落空了（本轮复核指出）。
            var cap = available > 0
                ? Math.Clamp(available - 260, 64, Math.Max(64, available * 0.5))
                : double.PositiveInfinity;
            ConfigScroll.MaxHeight = available > 0 && available < 560 ? cap : double.PositiveInfinity;
        }

        // A collapsed sidebar gives the 768px desktop enough room for both configuration columns.
        if (Bounds.Width is > 0 and < 600)
        {
            ConfigGrid.ColumnDefinitions = new ColumnDefinitions("*");
            ConfigGrid.RowDefinitions = new RowDefinitions("Auto,16,Auto");
            Grid.SetColumn(StrategyConfig, 0);
            Grid.SetRow(StrategyConfig, 0);
            Grid.SetColumn(CountConfig, 0);
            Grid.SetRow(CountConfig, 2);
        }
        else
        {
            ConfigGrid.ColumnDefinitions = new ColumnDefinitions("*,16,*");
            ConfigGrid.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(StrategyConfig, 0);
            Grid.SetRow(StrategyConfig, 0);
            Grid.SetColumn(CountConfig, 2);
            Grid.SetRow(CountConfig, 0);
        }
    }

    private async void Generate_Click(object? sender, RoutedEventArgs e)
    {
        var resultNum = (int)CountSlider.Value;
        var pool = AppState.StaffList
            .Where(x => x.IsSelected)
            .GroupBy(x => x.Name)
            .Select(g => g.First())
            .ToList();

        if (pool.Count <= 0)
        {
            await AppHost.AlertAsync("请先在干员列表中勾选参与随机的干员。");
            return;
        }

        if (resultNum > pool.Count)
        {
            await AppHost.AlertAsync("随机数量不能大于已选干员人数。");
            return;
        }

        var option = StrategyCombo.SelectedItem as StrategyComboItem;
        var strategy = option?.Model;

        ResultList.Clear();
        var random = new Random();

        if (strategy == null || strategy.Rules.Count == 0)
        {
            PickUniformNoReplace(pool, resultNum, random);
            return;
        }

        if (!StrategyRules.TryMerge(strategy.Rules, out var merged, out var mergeError))
        {
            await AppHost.AlertAsync(mergeError, "无法满足策略");
            return;
        }

        if (!merged.HasAny)
        {
            PickUniformNoReplace(pool, resultNum, random);
            return;
        }

        var rarityReq = merged.RarityExact;
        var careerExact = merged.CareerExact;
        var careerRange = merged.CareerRange;
        var staffSubsets = merged.StaffSubsets;

        if (rarityReq.Values.Sum(value => (long)value) > resultNum ||
            careerExact.Values.Sum(value => (long)value) > resultNum)
        {
            await AppHost.AlertAsync("策略中要求的稀有度人数或职业人数总和超过了当前「随机数量」，请调整策略或数量。", "无法满足策略");
            return;
        }

        if (StrategyRules.MinCareerSlots(careerExact, careerRange) > resultNum)
        {
            await AppHost.AlertAsync("策略中各职业数量（及范围下限）之和超过了当前「随机数量」，请调整策略或数量。", "无法满足策略");
            return;
        }

        foreach (var c in staffSubsets)
        {
            var inPool = pool.Count(s => c.Names.Contains(s.Name));
            var outPool = pool.Count - inPool;
            var maxTake = c.IsExact ? c.ExactOrLo : c.Hi;
            var nMin = Math.Max(c.ExactOrLo, resultNum - outPool);
            var nMax = Math.Min(Math.Min(maxTake, inPool), resultNum);
            if (nMin > nMax)
            {
                await AppHost.AlertAsync("「某些干员总数」与当前已选干员池或随机数量不兼容，请调整勾选或策略。", "无法满足策略");
                return;
            }
        }

        if (!ConstrainedTeamPicker.TryPick(pool, resultNum, rarityReq, careerExact, careerRange, staffSubsets, random, out var team))
        {
            await AppHost.AlertAsync("在当前已选干员池下无法凑出满足该策略的阵容，请增加/调整勾选干员或修改策略条目。", "无法满足策略");
            return;
        }

        foreach (var s in team)
            ResultList.Add(s);
    }

    private void PickUniformNoReplace(List<Staff> pool, int resultNum, Random random)
    {
        // 部分 Fisher–Yates：只做前 resultNum 次交换。
        // 原来的写法是「随机取下标 + HashSet 去重」，越接近池子大小重复命中越多，
        // 500 人池实测 P95 约 30ms（本轮基线里最慢的一条生成路径）。
        var indices = new int[pool.Count];
        for (var i = 0; i < indices.Length; i++)
            indices[i] = i;

        var take = Math.Min(resultNum, pool.Count);
        for (var i = 0; i < take; i++)
        {
            var j = random.Next(i, indices.Length);
            (indices[i], indices[j]) = (indices[j], indices[i]);
            ResultList.Add(pool[indices[i]]);
        }
    }
}
