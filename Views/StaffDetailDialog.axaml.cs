using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using arknights_random_team.Domain;
using arknights_random_team.Models;

namespace arknights_random_team.Views;

/// <summary>
/// 干员详情 / 编辑面板。
///
/// 卡片视图原本只有「点击整卡切换入池」和「⋯ → 删除干员」两个入口：触摸设备既看不到被
/// 省略的名称，也没有编辑入口，方案要求的「触摸端可通过详情查看，不依赖 tooltip」因此
/// 落不了地。这个面板就是那个明确入口——标题下方给出完整名称，字段可改，保存前统一校验，
/// 取消不改动任何数据（字段是草稿，保存时才写回）。
/// </summary>
public partial class StaffDetailDialog : ModalContent
{
    /// <inheritdoc />
    public override bool PreferFullScreenOnPhone => true;

    private readonly Staff _staff;

    /// <summary>XAML 加载器使用的无参构造；实际调用请用 <see cref="StaffDetailDialog(Staff)"/>。</summary>
    public StaffDetailDialog() : this(new Staff())
    {
    }

    public StaffDetailDialog(Staff staff)
    {
        _staff = staff;
        InitializeComponent();

        CareerBox.ItemsSource = AppOptions.Careers;
        StarBox.ItemsSource = AppOptions.Stars;
        LoadFromStaff();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void LoadFromStaff()
    {
        HeaderNameText.Text = string.IsNullOrWhiteSpace(_staff.Name) ? "（未命名）" : _staff.Name;
        NameBox.Text = _staff.Name;
        CareerBox.SelectedItem = _staff.Career;
        StarBox.SelectedItem = _staff.Star;
        LevelBox.Text = _staff.Level.Description;
        PoolBox.IsChecked = _staff.IsSelected;

        DetailThumb.Sources = _staff.AvatarUris;
        DetailThumb.PlaceholderSources = _staff.CareerIconUris;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        // 每次提交先清掉上一次的错误，并把错误文案统一走 StaffValidator，
        // 与录入页、表格内联编辑保持同一套规则与措辞。
        ClearError(NameError);
        ClearError(LevelError);

        var name = NameBox.Text?.Trim() ?? "";
        if (StaffValidator.ValidateName(name, AppState.StaffList, _staff) is { } nameError)
        {
            ShowError(NameError, nameError, NameBox);
            return;
        }

        var levelText = LevelBox.Text?.Trim() ?? "";
        if (StaffValidator.ValidateLevel(levelText) is { } levelError)
        {
            ShowError(LevelError, levelError, LevelBox);
            return;
        }

        AppState.RenameStaff(_staff, _staff.Name, name);
        if (CareerBox.SelectedItem is Career career)
            _staff.Career = career;
        if (StarBox.SelectedItem is int star)
            _staff.Star = star;
        _staff.Level.Description = levelText;
        _staff.IsSelected = PoolBox.IsChecked == true;

        RequestClose(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => RequestClose(false);

    private static void ClearError(TextBlock target)
    {
        target.Text = "";
        target.IsVisible = false;
    }

    /// <summary>显示错误并聚焦到出错的字段：触摸与键盘用户都不该靠「找红字」发现问题。</summary>
    private static void ShowError(TextBlock target, string message, Control? focusTarget = null)
    {
        target.Text = message;
        target.IsVisible = true;
        focusTarget?.Focus();
    }
}
