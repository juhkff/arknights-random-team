using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace arknights_random_team.Views;

/// <summary>
/// 无交互的编队半身像卡片。干员由父级 ItemsControl 提供，组件只读取其 DataContext。
/// </summary>
public partial class SquadPortrait : UserControl
{
    public static readonly StyledProperty<bool> ShowChromeProperty =
        AvaloniaProperty.Register<SquadPortrait, bool>(nameof(ShowChrome), true);

    public bool ShowChrome
    {
        get => GetValue(ShowChromeProperty);
        set => SetValue(ShowChromeProperty, value);
    }

    public SquadPortrait()
    {
        InitializeComponent();
        UpdateChrome();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ShowChromeProperty)
                UpdateChrome();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void UpdateChrome()
    {
        if (PortraitFrame is null)
            return;

        if (ShowChrome)
        {
            PortraitFrame.BorderThickness = new Thickness(1);
            PortraitFrame.CornerRadius =
                TryGetResource("AppRadiusSurface", ActualThemeVariant, out var value) && value is CornerRadius radius
                    ? radius
                    : new CornerRadius(8);
            return;
        }

        PortraitFrame.BorderThickness = default;
        PortraitFrame.CornerRadius = default;
    }
}
