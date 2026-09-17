using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using arknights_random_team.Domain;
using arknights_random_team.Models;

namespace arknights_random_team.Views;

public class StrategyComboItem
{
    public string DisplayName { get; set; } = "";
    public RandomStrategyDefinition? Model { get; set; }
}

public partial class GenerateView : UserControl
{
    private const double StackBreakpoint = 640;
    private bool _configStacked;
    private bool _watchingPool;

    public ObservableCollection<Staff> ResultList { get; } = [];

    public GenerateView()
    {
        DataContext = this;
        InitializeComponent();
        AppState.Strategies.CollectionChanged += OnStrategiesChanged;
        ResultList.CollectionChanged += (_, _) => UpdateResultState();
        CountSlider.Maximum = AppOptions.MaxTeamSize;
        ResultEmptyWebHint.IsVisible = SessionCopy.IsWeb;
        ResultEmptyDownloadButton.IsVisible = SessionCopy.IsWeb;
        AttachPoolWatch();
        RefreshStrategyCombo();
        UpdateTeamSizeControls();
        UpdateResultState();
        LayoutUpdated += (_, _) => ApplyCompactLayout(Bounds.Width);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void AttachPoolWatch()
    {
        if (_watchingPool)
            return;

        _watchingPool = true;
        AppState.StaffList.CollectionChanged += OnPoolCollectionChanged;
        foreach (var staff in AppState.StaffList)
            staff.PropertyChanged += OnPoolStaffChanged;
        UpdatePoolUi();
    }

    private void OnPoolCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (Staff staff in e.OldItems)
                staff.PropertyChanged -= OnPoolStaffChanged;
        }

        if (e.NewItems != null)
        {
            foreach (Staff staff in e.NewItems)
                staff.PropertyChanged += OnPoolStaffChanged;
        }

        UpdatePoolUi();
    }

    private void OnPoolStaffChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Staff.IsSelected))
            UpdatePoolUi();
    }

    private static int PoolCount =>
        AppState.StaffList.Where(x => x.IsSelected).GroupBy(x => x.Name).Count();

    private void UpdatePoolUi()
    {
        if (PoolCountText is null)
            return;

        var pool = PoolCount;
        PoolCountText.Text = $"随机池 {pool} 名";
        UpdateEmptyCopy();
    }

    private void ApplyCompactLayout(double width)
    {
        if (ConfigGrid is null || StrategyPane is null || CountPane is null || width <= 0)
            return;

        var stack = width < StackBreakpoint;
        if (stack == _configStacked)
            return;

        _configStacked = stack;
        if (stack)
        {
            ConfigGrid.ColumnDefinitions = new ColumnDefinitions("*");
            ConfigGrid.RowDefinitions = new RowDefinitions("Auto,16,Auto");
            Grid.SetColumn(StrategyPane, 0);
            Grid.SetRow(StrategyPane, 0);
            Grid.SetColumn(CountPane, 0);
            Grid.SetRow(CountPane, 2);
        }
        else
        {
            ConfigGrid.ColumnDefinitions = new ColumnDefinitions("*,24,*");
            ConfigGrid.RowDefinitions = new RowDefinitions("*");
            Grid.SetColumn(StrategyPane, 0);
            Grid.SetRow(StrategyPane, 0);
            Grid.SetColumn(CountPane, 2);
            Grid.SetRow(CountPane, 0);
        }
    }

    private void OnStrategiesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshStrategyCombo();

    private void RefreshStrategyCombo()
    {
        var keepId = (StrategyCombo.SelectedItem as StrategyComboItem)?.Model?.Id;
        var list = new List<StrategyComboItem>
        {
            new() { DisplayName = "无策略", Model = null }
        };
        list.AddRange(AppState.Strategies.Select(s => new StrategyComboItem { DisplayName = s.Name, Model = s }));

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
        UpdateEmptyCopy();

        if (ResultList.Count > 0)
            QueueRosterReveal();
    }

    private void UpdateEmptyCopy()
    {
        if (ResultEmptyCaption is null || ResultEmptyAction is null || ResultList.Count > 0)
            return;

        var roster = AppState.StaffList.Count;
        var pool = PoolCount;
        if (roster == 0)
        {
            ResultEmptyCaption.Text = "还没有干员。先去录入，再回来生成阵容。";
            ResultEmptyAction.Content = "去干员录入";
            ResultEmptyAction.Tag = AppPage.Input;
            ResultEmptyAction.IsVisible = true;
        }
        else if (pool == 0)
        {
            ResultEmptyCaption.Text = "随机池是空的。到干员列表把干员编入后再生成。";
            ResultEmptyAction.Content = "去干员列表";
            ResultEmptyAction.Tag = AppPage.List;
            ResultEmptyAction.IsVisible = true;
        }
        else
        {
            ResultEmptyCaption.Text = "选好策略与人数，点击「生成阵容」开始。";
            ResultEmptyAction.IsVisible = false;
        }
    }

    private void ResultEmptyAction_Click(object? sender, RoutedEventArgs e)
    {
        if (ResultEmptyAction.Tag is AppPage page)
            AppNavigation.GoTo(page);
    }

    private async void OpenDesktop_Click(object? sender, RoutedEventArgs e) =>
        await SessionCopy.OpenDesktopDownloadAsync(this);

    /// <summary>
    /// 编队卡片逐张浮现：容器生成后再统一播放，避免和 ItemsControl 的布局打架。
    /// </summary>
    private void QueueRosterReveal()
    {
        Dispatcher.UIThread.Post(PlayRosterReveal, DispatcherPriority.Background);
    }

    private void PlayRosterReveal()
    {
        if (RosterItems == null)
            return;

        // 用定时器逐张揭示：比 Animation.RunAsync 更好预测，动画结束后不会残留中间值。
        var cards = RosterItems.GetRealizedContainers().OfType<Control>().ToList();
        foreach (var card in cards)
        {
            card.Opacity = 0;
            card.RenderTransform = new TranslateTransform(0, 14);
        }

        var step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        timer.Tick += (_, _) =>
        {
            if (step >= cards.Count)
            {
                timer.Stop();
                return;
            }

            Reveal(cards[step]);
            step++;

            if (step >= cards.Count)
                timer.Stop();
        };
        timer.Start();
    }

    /// <summary>把一张卡片从「隐藏上浮」推到最终位置，随后清掉临时属性。</summary>
    private static void Reveal(Control card)
    {
        card.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(280), Easing = new CubicEaseOut() }
        };

        card.Opacity = 1;
        card.RenderTransform = new TranslateTransform(0, 0);
    }

    private void Generate_Click(object? sender, RoutedEventArgs e)
    {
        var resultNum = (int)CountSlider.Value;
        var pool = AppState.StaffList
            .Where(x => x.IsSelected)
            .GroupBy(x => x.Name)
            .Select(g => g.First())
            .ToList();

        if (pool.Count <= 0)
        {
            AppNotice.Post("请先在干员列表中把干员编入随机池。");
            return;
        }

        if (resultNum > pool.Count)
        {
            AppNotice.Post($"随机数量不能大于随机池人数（当前 {pool.Count} 名）。");
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
            AppNotice.Post(mergeError);
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

        if (rarityReq.Values.Sum() > resultNum || careerExact.Values.Sum() > resultNum)
        {
            AppNotice.Post("策略要求的稀有度或职业人数超过了当前阵容人数，请调整策略或数量。");
            return;
        }

        if (StrategyRules.MinCareerSlots(careerExact, careerRange) > resultNum)
        {
            AppNotice.Post("策略中各职业数量下限之和超过了当前阵容人数，请调整策略或数量。");
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
                AppNotice.Post("「某些干员总数」与当前随机池或人数不兼容，请调整编入或策略。");
                return;
            }
        }

        if (!ConstrainedTeamPicker.TryPick(pool, resultNum, rarityReq, careerExact, careerRange, staffSubsets, random, out var team))
        {
            AppNotice.Post("当前随机池凑不出该策略，请增加编入干员或修改策略。");
            return;
        }

        foreach (var s in team)
            ResultList.Add(s);
    }

    private void PickUniformNoReplace(List<Staff> pool, int resultNum, Random random)
    {
        var indexSet = new HashSet<int>();
        var length = pool.Count;
        for (var i = 0; i < resultNum; i++)
        {
            int curIndex;
            while (indexSet.Contains(curIndex = random.Next(length))) { }
            indexSet.Add(curIndex);
        }

        foreach (var index in indexSet)
            ResultList.Add(pool[index]);
    }
}
