using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using arknights_random_team.Domain;
using arknights_random_team.Models;

namespace arknights_random_team.Views;

public partial class StrategyEditorDialog : ModalContent
{
    /// <inheritdoc />
    public override bool PreferFullScreenOnPhone => true;

    private readonly RandomStrategyDefinition _target;
    private readonly string _backupName;
    private readonly List<StrategyRule> _backupRules;
    private readonly List<string> _staffSubsetDraft = [];
    private StrategyRule? _editingRule;
    private bool _rulesListSelectionSuppress;
    private bool _committed;

    public StrategyEditorDialog() : this(new RandomStrategyDefinition())
    {
    }

    public StrategyEditorDialog(RandomStrategyDefinition target)
    {
        _target = target;
        _backupName = _target.Name;
        _backupRules = _target.Rules.Select(CloneRule).ToList();

        InitializeComponent();
        NameBox.Text = _target.Name;
        RulesList.ItemsSource = _target.Rules;
        _target.Rules.CollectionChanged += Rules_CollectionChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            _target.Rules.CollectionChanged -= Rules_CollectionChanged;

            // 关闭时仍未提交，说明是被 Escape 之类的方式取消的，需要回滚草稿改动。
            if (!_committed)
                RestoreBackup();
        };
        UpdateRulesState();
        StarCombo.SelectedItem = FieldLimits.MaxStar;
        CareerCombo.SelectedItem = Career.先锋;
        CareerConstraintModeCombo.SelectionChanged += CareerConstraintModeCombo_SelectionChanged;
        StaffSubsetModeCombo.SelectionChanged += StaffSubsetModeCombo_SelectionChanged;
        WireDigitOnlyTextBoxes();
        UpdateStaffSubsetInputPanels();
        RefreshStaffSubsetTagPanel();
        UpdateSubmitButtonLabels();

        ApplyEditorLayout();
        SizeChanged += (_, _) => ApplyEditorLayout();
        Loaded += (_, _) => ApplyEditorLayout();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// 宽屏保持「左侧名称与规则列表 + 右侧构建器」两栏；窄屏改成上下单栏，
    /// 规则列表限高并自行滚动，构建器占满剩余高度。
    /// </summary>
    private void ApplyEditorLayout()
    {
        if (EditorBody is null || EditorSidebar is null || EditorForm is null)
            return;

        var width = Bounds.Width > 0
            ? Bounds.Width
            : !double.IsNaN(Width)
                ? Width
                : 960;
        var narrow = width < 900;
        if (narrow)
        {
            EditorBody.ColumnDefinitions = new ColumnDefinitions("*");
            EditorBody.RowDefinitions = new RowDefinitions("Auto,16,*");
            EditorBody.Margin = new Thickness(width < 600 ? 16 : 20);

            Grid.SetColumn(EditorSidebar, 0);
            Grid.SetRow(EditorSidebar, 0);
            EditorSidebar.MaxHeight = 280;
            EditorSidebar.ClipToBounds = true;

            Grid.SetColumn(EditorForm, 0);
            Grid.SetRow(EditorForm, 2);
            return;
        }

        EditorBody.ColumnDefinitions = new ColumnDefinitions("280,20,*");
        EditorBody.RowDefinitions = new RowDefinitions("*");
        EditorBody.Margin = new Thickness(24, 20);

        Grid.SetColumn(EditorSidebar, 0);
        Grid.SetRow(EditorSidebar, 0);
        EditorSidebar.MaxHeight = double.PositiveInfinity;
        EditorSidebar.ClipToBounds = false;

        Grid.SetColumn(EditorForm, 2);
        Grid.SetRow(EditorForm, 0);
    }

    private void Rules_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateRulesState();

    private void UpdateRulesState() => RulesEmptyState.IsVisible = _target.Rules.Count == 0;

    private void RulesList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_rulesListSelectionSuppress)
            return;
        if (RulesList.SelectedItem is StrategyRule r)
        {
            _editingRule = r;
            LoadRuleIntoForms(r);
            UpdateSubmitButtonLabels();
        }
        else
        {
            _editingRule = null;
            ClearStrategyEntryForms();
            UpdateSubmitButtonLabels();
        }
    }

    private void CancelRuleEdit_Click(object? sender, RoutedEventArgs e) => ClearSelectionAndEdit();

    private void ClearSelectionAndEdit()
    {
        _editingRule = null;
        _rulesListSelectionSuppress = true;
        RulesList.SelectedItem = null;
        _rulesListSelectionSuppress = false;
        ClearStrategyEntryForms();
        UpdateSubmitButtonLabels();
    }

    private void ClearStrategyEntryFieldsInner()
    {
        StarCombo.SelectedItem = FieldLimits.MaxStar;
        RarityCountBox.Text = "";
        CareerCombo.SelectedItem = Career.先锋;
        CareerConstraintModeCombo.SelectedIndex = 0;
        CareerCountBox.Text = "";
        CareerRangeMinBox.Text = "";
        CareerRangeMaxBox.Text = "";
        _staffSubsetDraft.Clear();
        StaffSubsetModeCombo.SelectedIndex = 0;
        StaffSubsetExactCountBox.Text = "";
        StaffSubsetRangeMinBox.Text = "";
        StaffSubsetRangeMaxBox.Text = "";
    }

    private void ClearStrategyEntryForms()
    {
        ClearStrategyEntryFieldsInner();
        UpdateCareerConstraintPanels();
        UpdateStaffSubsetInputPanels();
        RefreshStaffSubsetTagPanel();
    }

    private void LoadRuleIntoForms(StrategyRule r)
    {
        ClearStrategyEntryFieldsInner();
        switch (r.Kind)
        {
            case StrategyRuleKind.Rarity:
                StarCombo.SelectedItem = r.Star;
                RarityCountBox.Text = r.Count.ToString();
                break;
            case StrategyRuleKind.Career:
                CareerCombo.SelectedItem = r.Career;
                CareerConstraintModeCombo.SelectedIndex = 0;
                CareerCountBox.Text = r.Count.ToString();
                break;
            case StrategyRuleKind.CareerRange:
                CareerCombo.SelectedItem = r.Career;
                CareerConstraintModeCombo.SelectedIndex = 1;
                CareerRangeMinBox.Text = r.Count.ToString();
                CareerRangeMaxBox.Text = r.CountMax.ToString();
                break;
            case StrategyRuleKind.StaffSubsetExact:
                foreach (var n in r.StaffNames)
                {
                    if (!string.IsNullOrWhiteSpace(n) && !_staffSubsetDraft.Contains(n.Trim()))
                        _staffSubsetDraft.Add(n.Trim());
                }
                StaffSubsetModeCombo.SelectedIndex = 0;
                StaffSubsetExactCountBox.Text = r.Count.ToString();
                break;
            case StrategyRuleKind.StaffSubsetRange:
                foreach (var n in r.StaffNames)
                {
                    if (!string.IsNullOrWhiteSpace(n) && !_staffSubsetDraft.Contains(n.Trim()))
                        _staffSubsetDraft.Add(n.Trim());
                }
                StaffSubsetModeCombo.SelectedIndex = 1;
                StaffSubsetRangeMinBox.Text = r.Count.ToString();
                StaffSubsetRangeMaxBox.Text = r.CountMax.ToString();
                break;
        }

        UpdateCareerConstraintPanels();
        UpdateStaffSubsetInputPanels();
        RefreshStaffSubsetTagPanel();
    }

    private void UpdateSubmitButtonLabels()
    {
        RarityRuleSubmitButton.Content = _editingRule?.Kind == StrategyRuleKind.Rarity ? "保存" : "添加";
        var careerEdit = _editingRule?.Kind is StrategyRuleKind.Career or StrategyRuleKind.CareerRange;
        CareerRuleSubmitButton.Content = careerEdit ? "保存" : "添加";
        var staffEdit = _editingRule?.Kind is StrategyRuleKind.StaffSubsetExact or StrategyRuleKind.StaffSubsetRange;
        StaffSubsetRuleSubmitButton.Content = staffEdit ? "保存" : "添加";
        UpdateEditingChrome();
    }

    private void UpdateEditingChrome()
    {
        var editing = _editingRule != null;
        RulesHintText.Text = editing ? "正在编辑选中规则" : "未选择规则";
        RulesHintText.Classes.Set("editing", editing);
        SetSectionEditing(RaritySection, _editingRule?.Kind == StrategyRuleKind.Rarity);
        SetSectionEditing(CareerSection, _editingRule?.Kind is StrategyRuleKind.Career or StrategyRuleKind.CareerRange);
        SetSectionEditing(StaffSection,
            _editingRule?.Kind is StrategyRuleKind.StaffSubsetExact or StrategyRuleKind.StaffSubsetRange);
    }

    private static void SetSectionEditing(Border section, bool on) => section.Classes.Set("editing", on);

    private void ReplaceRuleAt(StrategyRule oldRule, StrategyRule newRule)
    {
        var idx = _target.Rules.IndexOf(oldRule);
        if (idx < 0)
            return;
        _target.Rules[idx] = newRule;
        ClearSelectionAndEdit();
    }

    private void CareerConstraintModeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        UpdateCareerConstraintPanels();

    private void UpdateCareerConstraintPanels()
    {
        var exact = CareerConstraintModeCombo.SelectedIndex == 0;
        CareerCountBox.IsVisible = exact;
        CareerRangeMinBox.IsVisible = !exact;
        CareerRangeMaxBox.IsVisible = !exact;
    }

    private void WireDigitOnlyTextBoxes()
    {
        foreach (var tb in new[]
                 {
                     RarityCountBox, CareerCountBox, CareerRangeMinBox, CareerRangeMaxBox,
                     StaffSubsetExactCountBox, StaffSubsetRangeMinBox, StaffSubsetRangeMaxBox
                 })
        {
            tb.TextInput += DigitOnlyTextBox_TextInput;
            tb.KeyDown += DigitOnlyTextBox_KeyDown;
            tb.TextChanged += DigitOnlyTextBox_TextChanged;
        }
    }

    private void StaffSubsetModeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        UpdateStaffSubsetInputPanels();

    private void UpdateStaffSubsetInputPanels()
    {
        var exact = StaffSubsetModeCombo.SelectedIndex == 0;
        StaffSubsetExactCountBox.IsVisible = exact;
        StaffSubsetRangeInputPanel.IsVisible = !exact;
    }

    private void RefreshStaffSubsetTagPanel()
    {
        var existing = StaffSubsetFlowPanel.Children
            .OfType<Control>()
            .Where(c => c.Classes.Contains("staff-chip"))
            .ToList();
        foreach (var chip in existing)
            StaffSubsetFlowPanel.Children.Remove(chip);

        var insertAt = StaffSubsetFlowPanel.Children.IndexOf(StaffSubsetAddButton);
        if (insertAt < 0)
            insertAt = 1;

        var tagBg = TryGetResource("AppPrimarySoftBrush", ActualThemeVariant, out var brush)
                    && brush is IBrush b
            ? b
            : new SolidColorBrush(Color.FromRgb(24, 58, 56));

        foreach (var name in _staffSubsetDraft)
        {
            var border = new Border
            {
                Background = tagBg,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 4, 3),
                Margin = new Thickness(0, 0, 6, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            border.Classes.Add("staff-chip");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = name,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });
            var remove = new Button
            {
                Content = "×",
                MinWidth = 28,
                Width = 28,
                MinHeight = 28,
                Height = 28,
                Padding = new Thickness(0),
                Tag = name,
                VerticalAlignment = VerticalAlignment.Center
            };
            remove.Classes.Add("danger");
            remove.Classes.Add("compact");
            ToolTip.SetTip(remove, "移除干员");
            remove.Click += RemoveStaffSubsetTag_Click;
            row.Children.Add(remove);
            border.Child = row;
            StaffSubsetFlowPanel.Children.Insert(insertAt++, border);
        }
    }

    private void RemoveStaffSubsetTag_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string name)
            return;
        _staffSubsetDraft.Remove(name);
        RefreshStaffSubsetTagPanel();
    }

    private async void StaffSubsetAddButton_Click(object? sender, RoutedEventArgs e)
    {
        var dlg = new StaffPickDialog(_staffSubsetDraft);
        var ok = await AppHost.ShowAsync<bool>(dlg);
        if (!ok || dlg.SelectedStaffNames.Count == 0)
            return;

        var nameSet = AppState.GetNameSet();
        foreach (var n in dlg.SelectedStaffNames)
        {
            var trimmed = n.Trim();
            if (!nameSet.Contains(trimmed))
            {
                await AppHost.AlertAsync($"干员「{trimmed}」未在干员列表中录入，已跳过。");
                continue;
            }

            if (_staffSubsetDraft.Contains(trimmed))
                continue;
            _staffSubsetDraft.Add(trimmed);
        }

        RefreshStaffSubsetTagPanel();
    }

    private async void AddStaffSubsetRule_Click(object? sender, RoutedEventArgs e)
    {
        if (_staffSubsetDraft.Count == 0)
        {
            await AppHost.AlertAsync("请至少选择一名干员。");
            return;
        }

        if (_editingRule != null &&
            _editingRule.Kind is not StrategyRuleKind.StaffSubsetExact and not StrategyRuleKind.StaffSubsetRange)
        {
            await AppHost.AlertAsync("当前选中条目不是「某些干员总数」类型。请先点「取消编辑」或选择对应条目后再操作。");
            return;
        }

        var nameSet = AppState.GetNameSet();
        foreach (var n in _staffSubsetDraft)
        {
            if (!nameSet.Contains(n))
            {
                await AppHost.AlertAsync($"干员「{n}」未在干员列表中录入。");
                return;
            }
        }

        var names = new List<string>(_staffSubsetDraft);
        StrategyRule newRule;
        if (StaffSubsetModeCombo.SelectedIndex == 0)
        {
            if (!int.TryParse(StaffSubsetExactCountBox.Text?.Trim(), out var cn) || cn < 0)
            {
                await AppHost.AlertAsync("请输入正确的固定人数（非负整数）。");
                return;
            }

            if (cn > names.Count)
            {
                await AppHost.AlertAsync("固定人数不能大于已选干员种类数。");
                return;
            }

            newRule = new StrategyRule
            {
                Kind = StrategyRuleKind.StaffSubsetExact,
                StaffNames = names,
                Count = cn
            };
        }
        else
        {
            if (!int.TryParse(StaffSubsetRangeMinBox.Text?.Trim(), out var lo) || lo < 0)
            {
                await AppHost.AlertAsync("请输入正确的范围下限（非负整数）。");
                return;
            }

            if (!int.TryParse(StaffSubsetRangeMaxBox.Text?.Trim(), out var hi))
            {
                await AppHost.AlertAsync("请输入正确的范围上限（整数）。");
                return;
            }

            if (lo > hi)
            {
                await AppHost.AlertAsync("范围下限不能大于上限。");
                return;
            }

            if (hi > names.Count)
            {
                await AppHost.AlertAsync("范围上限不能大于已选干员种类数。");
                return;
            }

            newRule = new StrategyRule
            {
                Kind = StrategyRuleKind.StaffSubsetRange,
                StaffNames = names,
                Count = lo,
                CountMax = hi
            };
        }

        if (!await CommitRuleAsync(newRule))
            return;

        _staffSubsetDraft.Clear();
        RefreshStaffSubsetTagPanel();
    }

    private static void DigitOnlyTextBox_TextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Text != null && e.Text.Any(ch => !char.IsAsciiDigit(ch)))
            e.Handled = true;
    }

    private static void DigitOnlyTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
            e.Handled = true;
    }

    private static void DigitOnlyTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb)
            return;
        var raw = tb.Text ?? "";
        if (raw.Length == 0 || raw.All(char.IsAsciiDigit))
            return;
        var caret = tb.CaretIndex;
        var keptBeforeCaret = raw.Take(caret).Count(char.IsAsciiDigit);
        var filtered = new string(raw.Where(char.IsAsciiDigit).ToArray());
        tb.Text = filtered;
        tb.CaretIndex = Math.Min(Math.Max(0, keptBeforeCaret), filtered.Length);
    }

    private static StrategyRule CloneRule(StrategyRule r) =>
        new()
        {
            Kind = r.Kind,
            Star = r.Star,
            Career = r.Career,
            Count = r.Count,
            CountMax = r.CountMax,
            StaffNames = [.. r.StaffNames]
        };

    private void RestoreBackup()
    {
        _target.Name = _backupName;
        _target.Rules.Clear();
        foreach (var r in _backupRules)
            _target.Rules.Add(CloneRule(r));
    }

    private void RemoveRule_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not StrategyRule rule)
            return;
        if (rule == _editingRule)
        {
            _rulesListSelectionSuppress = true;
            _target.Rules.Remove(rule);
            _editingRule = null;
            RulesList.SelectedItem = null;
            _rulesListSelectionSuppress = false;
            ClearStrategyEntryForms();
            UpdateSubmitButtonLabels();
            return;
        }

        _target.Rules.Remove(rule);
    }

    private async Task<bool> CommitRuleAsync(StrategyRule newRule)
    {
        var prospective = _target.Rules.ToList();
        if (_editingRule != null)
        {
            var idx = prospective.IndexOf(_editingRule);
            if (idx < 0)
                return false;
            prospective[idx] = newRule;
        }
        else
        {
            prospective.Add(newRule);
        }

        if (!StrategyRules.TryValidate(prospective, out var error))
        {
            await AppHost.AlertAsync(error, "条目冲突");
            return false;
        }

        if (_editingRule != null)
            ReplaceRuleAt(_editingRule, newRule);
        else
            _target.Rules.Add(newRule);

        return true;
    }

    private async void AddRarityRule_Click(object? sender, RoutedEventArgs e)
    {
        if (_editingRule != null && _editingRule.Kind != StrategyRuleKind.Rarity)
        {
            await AppHost.AlertAsync("当前选中条目不是「某星干员总数」类型。请先点「取消编辑」或选择对应条目后再操作。");
            return;
        }

        if (StarCombo.SelectedItem is not int star)
            return;
        if (!int.TryParse(RarityCountBox.Text?.Trim(), out var n) || n <= 0)
        {
            await AppHost.AlertAsync("请输入正确的稀有度数量（正整数）。");
            return;
        }

        var newRule = new StrategyRule { Kind = StrategyRuleKind.Rarity, Star = star, Count = n };
        await CommitRuleAsync(newRule);
    }

    private async void AddCareerConstraint_Click(object? sender, RoutedEventArgs e)
    {
        if (CareerCombo.SelectedItem is not Career career)
            return;

        if (_editingRule != null &&
            _editingRule.Kind is not StrategyRuleKind.Career and not StrategyRuleKind.CareerRange)
        {
            await AppHost.AlertAsync("当前选中条目不是「某职业总数」类型。请先点「取消编辑」或选择对应条目后再操作。");
            return;
        }

        if (CareerConstraintModeCombo.SelectedIndex == 0)
        {
            if (!int.TryParse(CareerCountBox.Text?.Trim(), out var n) || n <= 0)
            {
                await AppHost.AlertAsync("请输入正确的职业数量（正整数）。");
                return;
            }

            var newRule = new StrategyRule { Kind = StrategyRuleKind.Career, Career = career, Count = n };
            await CommitRuleAsync(newRule);
            return;
        }

        if (!int.TryParse(CareerRangeMinBox.Text?.Trim(), out var lo) || lo < 0)
        {
            await AppHost.AlertAsync("请输入正确的范围下限（非负整数）。");
            return;
        }

        if (!int.TryParse(CareerRangeMaxBox.Text?.Trim(), out var hi))
        {
            await AppHost.AlertAsync("请输入正确的范围上限（整数）。");
            return;
        }

        if (lo > hi)
        {
            await AppHost.AlertAsync("范围下限不能大于上限。");
            return;
        }

        var newRangeRule = new StrategyRule
        {
            Kind = StrategyRuleKind.CareerRange,
            Career = career,
            Count = lo,
            CountMax = hi
        };
        await CommitRuleAsync(newRangeRule);
    }

    private async void Ok_Click(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            await AppHost.AlertAsync("请填写策略名称。");
            return;
        }

        _target.Name = name;

        if (!StrategyRules.TryValidate(_target.Rules, out var error))
        {
            await AppHost.AlertAsync(error, "策略存在冲突");
            return;
        }

        _committed = true;
        RequestClose(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        RestoreBackup();
        _committed = true;
        RequestClose(false);
    }
}
