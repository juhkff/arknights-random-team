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

public partial class StrategyEditorWindow : Window
{
    private readonly RandomStrategyDefinition _target;
    private readonly string _backupName;
    private readonly List<StrategyRule> _backupRules;
    private readonly List<string> _staffSubsetDraft = [];
    private StrategyRule? _editingRule;
    private bool _rulesListSelectionSuppress;
    private bool _committed;

    public StrategyEditorWindow() : this(new RandomStrategyDefinition())
    {
    }

    public StrategyEditorWindow(RandomStrategyDefinition target)
    {
        _target = target;
        _backupName = _target.Name;
        _backupRules = _target.Rules.Select(CloneRule).ToList();

        InitializeComponent();
        NameBox.Text = _target.Name;
        RulesList.ItemsSource = _target.Rules;
        StarCombo.SelectedItem = 6;
        CareerCombo.SelectedItem = Career.先锋;
        CareerConstraintModeCombo.SelectionChanged += CareerConstraintModeCombo_SelectionChanged;
        StaffSubsetModeCombo.SelectionChanged += StaffSubsetModeCombo_SelectionChanged;
        WireDigitOnlyTextBoxes();
        UpdateStaffSubsetInputPanels();
        RefreshStaffSubsetTagPanel();
        UpdateSubmitButtonLabels();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void RulesList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_rulesListSelectionSuppress)
            return;
        if (RulesList.SelectedItem is StrategyRule r)
        {
            _editingRule = r;
            LoadRuleIntoForms(r);
            UpdateSubmitButtonLabels();
            CancelRuleEditButton.IsVisible = true;
        }
        else
        {
            _editingRule = null;
            ClearStrategyEntryForms();
            UpdateSubmitButtonLabels();
            CancelRuleEditButton.IsVisible = false;
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
        CancelRuleEditButton.IsVisible = false;
    }

    private void ClearStrategyEntryFieldsInner()
    {
        StarCombo.SelectedItem = 6;
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
    }

    private void ReplaceRuleAt(StrategyRule oldRule, StrategyRule newRule)
    {
        var idx = _target.Rules.IndexOf(oldRule);
        if (idx < 0)
            return;
        _target.Rules.RemoveAt(idx);
        _target.Rules.Insert(idx, newRule);
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

        var tagBg = TryGetResource("MaterialPrimaryLightBrush", ActualThemeVariant, out var brush)
                    && brush is IBrush b
            ? b
            : new SolidColorBrush(Color.FromRgb(237, 231, 246));

        foreach (var name in _staffSubsetDraft)
        {
            var border = new Border
            {
                Background = tagBg,
                CornerRadius = new CornerRadius(12),
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
                Padding = new Thickness(4, 0),
                Tag = name,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (TryGetResource("FlatButton", ActualThemeVariant, out var st) && st is ControlTheme flat)
                remove.Theme = flat;
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
        var ok = await dlg.ShowDialog<bool>(this);
        if (!ok || dlg.SelectedStaffNames.Count == 0)
            return;

        var nameSet = AppState.GetNameSet();
        foreach (var n in dlg.SelectedStaffNames)
        {
            var trimmed = n.Trim();
            if (!nameSet.Contains(trimmed))
            {
                await AppDialogs.Alert(this, $"干员「{trimmed}」未在干员列表中录入，已跳过。");
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
            await AppDialogs.Alert(this, "请至少选择一名干员。");
            return;
        }

        if (_editingRule != null &&
            _editingRule.Kind is not StrategyRuleKind.StaffSubsetExact and not StrategyRuleKind.StaffSubsetRange)
        {
            await AppDialogs.Alert(this, "当前选中条目不是干员池限制类型。请先点「取消编辑」或选择对应条目后再操作。");
            return;
        }

        var nameSet = AppState.GetNameSet();
        foreach (var n in _staffSubsetDraft)
        {
            if (!nameSet.Contains(n))
            {
                await AppDialogs.Alert(this, $"干员「{n}」未在干员列表中录入。");
                return;
            }
        }

        var names = new List<string>(_staffSubsetDraft);
        if (StaffSubsetModeCombo.SelectedIndex == 0)
        {
            if (!int.TryParse(StaffSubsetExactCountBox.Text?.Trim(), out var cn) || cn < 0)
            {
                await AppDialogs.Alert(this, "请输入正确的固定人数（非负整数）。");
                return;
            }

            if (cn > names.Count)
            {
                await AppDialogs.Alert(this, "固定人数不能大于已选干员种类数。");
                return;
            }

            var newRule = new StrategyRule
            {
                Kind = StrategyRuleKind.StaffSubsetExact,
                StaffNames = names,
                Count = cn
            };
            if (_editingRule != null)
            {
                ReplaceRuleAt(_editingRule, newRule);
                return;
            }

            _target.Rules.Add(newRule);
        }
        else
        {
            if (!int.TryParse(StaffSubsetRangeMinBox.Text?.Trim(), out var lo) || lo < 0)
            {
                await AppDialogs.Alert(this, "请输入正确的范围下限（非负整数）。");
                return;
            }

            if (!int.TryParse(StaffSubsetRangeMaxBox.Text?.Trim(), out var hi))
            {
                await AppDialogs.Alert(this, "请输入正确的范围上限（整数）。");
                return;
            }

            if (lo > hi)
            {
                await AppDialogs.Alert(this, "范围下限不能大于上限。");
                return;
            }

            if (hi > names.Count)
            {
                await AppDialogs.Alert(this, "范围上限不能大于已选干员种类数。");
                return;
            }

            var newRuleR = new StrategyRule
            {
                Kind = StrategyRuleKind.StaffSubsetRange,
                StaffNames = names,
                Count = lo,
                CountMax = hi
            };
            if (_editingRule != null)
            {
                ReplaceRuleAt(_editingRule, newRuleR);
                return;
            }

            _target.Rules.Add(newRuleR);
        }

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
            StaffNames = [..r.StaffNames]
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
            CancelRuleEditButton.IsVisible = false;
            return;
        }

        _target.Rules.Remove(rule);
    }

    private async void AddRarityRule_Click(object? sender, RoutedEventArgs e)
    {
        if (_editingRule != null && _editingRule.Kind != StrategyRuleKind.Rarity)
        {
            await AppDialogs.Alert(this, "当前选中条目不是「固定特定稀有度总量」类型。请先点「取消编辑」或选择对应条目后再操作。");
            return;
        }

        if (StarCombo.SelectedItem is not int star)
            return;
        if (!int.TryParse(RarityCountBox.Text?.Trim(), out var n) || n <= 0)
        {
            await AppDialogs.Alert(this, "请输入正确的稀有度数量（正整数）。");
            return;
        }

        var newRule = new StrategyRule { Kind = StrategyRuleKind.Rarity, Star = star, Count = n };
        if (_editingRule != null)
            ReplaceRuleAt(_editingRule, newRule);
        else
            _target.Rules.Add(newRule);
    }

    private async void AddCareerConstraint_Click(object? sender, RoutedEventArgs e)
    {
        if (CareerCombo.SelectedItem is not Career career)
            return;

        if (_editingRule != null &&
            _editingRule.Kind is not StrategyRuleKind.Career and not StrategyRuleKind.CareerRange)
        {
            await AppDialogs.Alert(this, "当前选中条目不是职业限制类型。请先点「取消编辑」或选择对应条目后再操作。");
            return;
        }

        if (CareerConstraintModeCombo.SelectedIndex == 0)
        {
            if (!int.TryParse(CareerCountBox.Text?.Trim(), out var n) || n <= 0)
            {
                await AppDialogs.Alert(this, "请输入正确的职业数量（正整数）。");
                return;
            }

            var newRule = new StrategyRule { Kind = StrategyRuleKind.Career, Career = career, Count = n };
            if (_editingRule != null)
                ReplaceRuleAt(_editingRule, newRule);
            else
                _target.Rules.Add(newRule);
            return;
        }

        if (!int.TryParse(CareerRangeMinBox.Text?.Trim(), out var lo) || lo < 0)
        {
            await AppDialogs.Alert(this, "请输入正确的范围下限（非负整数）。");
            return;
        }

        if (!int.TryParse(CareerRangeMaxBox.Text?.Trim(), out var hi))
        {
            await AppDialogs.Alert(this, "请输入正确的范围上限（整数）。");
            return;
        }

        if (lo > hi)
        {
            await AppDialogs.Alert(this, "范围下限不能大于上限。");
            return;
        }

        var newRangeRule = new StrategyRule
        {
            Kind = StrategyRuleKind.CareerRange,
            Career = career,
            Count = lo,
            CountMax = hi
        };
        if (_editingRule != null)
            ReplaceRuleAt(_editingRule, newRangeRule);
        else
            _target.Rules.Add(newRangeRule);
    }

    private async void Ok_Click(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            await AppDialogs.Alert(this, "请填写策略名称。");
            return;
        }

        _target.Name = name;
        _committed = true;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        RestoreBackup();
        _committed = true;
        Close(false);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_committed)
            RestoreBackup();
        base.OnClosing(e);
    }
}
