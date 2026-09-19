using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace arknights_random_team.Views;

/// <summary>
/// 无交互的编队半身像卡片。干员由父级 ItemsControl 提供，组件只读取其 DataContext。
/// </summary>
public partial class SquadPortrait : UserControl
{
    public SquadPortrait() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
