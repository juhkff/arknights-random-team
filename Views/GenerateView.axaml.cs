using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
    public ObservableCollection<Staff> ResultList { get; } = [];

    public GenerateView()
    {
        DataContext = this;
        InitializeComponent();
        AppState.Strategies.CollectionChanged += OnStrategiesChanged;
        ResultList.CollectionChanged += (_, _) => UpdateResultState();
        CountSlider.Maximum = AppOptions.MaxTeamSize;
        RefreshStrategyCombo();
        RandomNumText.Text = ((int)CountSlider.Value).ToString();
        UpdateResultState();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

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
        if (e.Property != Slider.ValueProperty || RandomNumText == null)
            return;
        RandomNumText.Text = ((int)CountSlider.Value).ToString();
    }

    private void UpdateResultState()
    {
        if (ResultEmptyState == null || ResultCountText == null)
            return;

        ResultEmptyState.IsVisible = ResultList.Count == 0;
        ResultCountText.Text = $"{ResultList.Count} 名干员";
    }

    private async void Generate_Click(object? sender, RoutedEventArgs e)
    {
        var owner = this.FindWindow();
        var resultNum = (int)CountSlider.Value;
        var pool = AppState.StaffList
            .Where(x => x.IsSelected)
            .GroupBy(x => x.Name)
            .Select(g => g.First())
            .ToList();

        if (pool.Count <= 0)
        {
            await AppDialogs.Alert(owner, "请先在干员列表中勾选参与随机的干员。");
            return;
        }

        if (resultNum > pool.Count)
        {
            await AppDialogs.Alert(owner, "随机数量不能大于已选干员人数。");
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
            await AppDialogs.Alert(owner, mergeError, "无法满足策略");
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
            await AppDialogs.Alert(owner, "策略中要求的稀有度人数或职业人数总和超过了当前「随机数量」，请调整策略或数量。", "无法满足策略");
            return;
        }

        if (StrategyRules.MinCareerSlots(careerExact, careerRange) > resultNum)
        {
            await AppDialogs.Alert(owner, "策略中各职业数量（及范围下限）之和超过了当前「随机数量」，请调整策略或数量。", "无法满足策略");
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
                await AppDialogs.Alert(owner, "「某些干员总数」与当前已选干员池或随机数量不兼容，请调整勾选或策略。", "无法满足策略");
                return;
            }
        }

        if (!ConstrainedTeamPicker.TryPick(pool, resultNum, rarityReq, careerExact, careerRange, staffSubsets, random, out var team))
        {
            await AppDialogs.Alert(owner, "在当前已选干员池下无法凑出满足该策略的阵容，请增加/调整勾选干员或修改策略条目。", "无法满足策略");
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

    private void ResultGrid_Sorting(object? sender, DataGridColumnEventArgs e) =>
        DataGridMultiSort.Apply(ResultGrid, e);
}
