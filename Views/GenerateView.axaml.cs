using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Collections;
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
        RefreshStrategyCombo();
        RandomNumText.Text = ((int)CountSlider.Value).ToString();
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

        ConstrainedTeamPicker.MergeRules(strategy, out var rarityReq, out var careerExact, out var careerRange, out var staffSubsets);
        if (rarityReq.Count == 0 && careerExact.Count == 0 && careerRange.Count == 0 && staffSubsets.Count == 0)
        {
            PickUniformNoReplace(pool, resultNum, random);
            return;
        }

        if (careerRange.Values.Any(x => x.lo > x.hi))
        {
            await AppDialogs.Alert(owner, "策略中存在互相冲突的职业数量范围条目（交集为空），请修改后重试。", "无法满足策略");
            return;
        }

        if (rarityReq.Values.Sum() > resultNum || careerExact.Values.Sum() > resultNum)
        {
            await AppDialogs.Alert(owner, "策略中要求的稀有度人数或职业人数总和超过了当前「随机数量」，请调整策略或数量。", "无法满足策略");
            return;
        }

        var minCareer = 0;
        foreach (Career career in Enum.GetValues<Career>())
        {
            if (careerExact.TryGetValue(career, out var ex))
                minCareer += ex;
            else if (careerRange.TryGetValue(career, out var rg))
                minCareer += rg.lo;
        }

        if (minCareer > resultNum)
        {
            await AppDialogs.Alert(owner, "策略中各职业数量（及范围下限）之和超过了当前「随机数量」，请调整策略或数量。", "无法满足策略");
            return;
        }

        foreach (var c in staffSubsets)
        {
            var inPool = pool.Count(s => c.Names.Contains(s.Name));
            var outPool = pool.Count(s => !c.Names.Contains(s.Name));
            if (c.IsExact)
            {
                var n = c.ExactOrLo;
                if (n > resultNum)
                {
                    await AppDialogs.Alert(owner, "「限制特定干员人数」的固定值超过了当前「随机数量」。", "无法满足策略");
                    return;
                }

                if (n > inPool)
                {
                    await AppDialogs.Alert(owner, "「限制特定干员人数」：在已选干员池中，指定干员不足以满足固定人数，请调整勾选或策略。", "无法满足策略");
                    return;
                }

                if (n == 0 && outPool < resultNum)
                {
                    await AppDialogs.Alert(owner, "「限制特定干员人数」为 0 时，需要足够多的「非指定」已选干员填满阵容，请调整勾选或策略。", "无法满足策略");
                    return;
                }
            }
            else if (c.ExactOrLo > inPool || c.Hi > resultNum || c.ExactOrLo > resultNum)
            {
                await AppDialogs.Alert(owner, "「限制特定干员人数」的范围与当前已选干员池或随机数量不兼容，请调整。", "无法满足策略");
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

    private void ResultGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        var sorts = ResultGrid.CollectionView?.SortDescriptions;
        if (sorts is null)
            return;

        var path = e.Column.SortMemberPath;
        if (string.IsNullOrEmpty(path))
            return;

        var current = sorts.FirstOrDefault(item => item.HasPropertyPath && item.PropertyPath == path);
        if (current?.Direction != ListSortDirection.Descending)
            return;

        e.Handled = true;
        sorts.Clear();
    }
}
