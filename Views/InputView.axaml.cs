using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Styles.Controls;
using Material.Styles.Models;
using arknights_random_team.Models;

namespace arknights_random_team.Views;

public partial class InputView : UserControl
{
    private int _star = 1;
    private readonly List<TextBlock> _starGlyphs = [];
    private bool _updatingLevelText;

    public InputView()
    {
        InitializeComponent();
        BuildStarBar();
        CareerCombo.SelectedIndex = -1;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void BuildStarBar()
    {
        for (var i = 1; i <= 6; i++)
        {
            var star = i;
            var glyph = new TextBlock
            {
                Text = "★",
                Classes = { "star-glyph" }
            };
            glyph.PointerPressed += (_, e) =>
            {
                SetStar(star);
                e.Handled = true;
            };
            _starGlyphs.Add(glyph);
            StarPanel.Children.Add(glyph);
        }

        SetStar(1);
    }

    private void SetStar(int star)
    {
        _star = star;
        for (var i = 0; i < _starGlyphs.Count; i++)
        {
            _starGlyphs[i].Foreground = i < star
                ? new SolidColorBrush(Color.Parse("#673AB7"))
                : new SolidColorBrush(Color.Parse("#D1C4E9"));
        }
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
