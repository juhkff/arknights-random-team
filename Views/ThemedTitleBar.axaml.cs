using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace arknights_random_team.Views;

/// <summary>
/// 画在窗口客户区里的标题栏。必须放在 <see cref="Window"/> 的内容树中，
/// Win32 才能把 <c>ElementRole=TitleBar</c> 命中成拖动区域。
/// 三个窗控按钮用 <c>ElementRole=User</c>，避免被当成系统最大化热区弹出贴靠布局。
/// </summary>
public partial class ThemedTitleBar : UserControl
{
    public const double BarHeight = 40;

    private Window? _window;

    public ThemedTitleBar() => InitializeComponent();

    public bool ShowMinimize { get; set; } = true;

    public bool ShowMaximize { get; set; } = true;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DetachWindow();
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is null)
            return;

        MinButton.IsVisible = ShowMinimize && _window.CanMinimize;
        MaxButton.IsVisible = ShowMaximize && _window.CanResize;
        TitleText.Text = _window.Title;
        _window.PropertyChanged += OnWindowPropertyChanged;
        UpdateMaximizeGlyph();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        DetachWindow();
        base.OnDetachedFromVisualTree(e);
    }

    private void DetachWindow()
    {
        if (_window is null)
            return;
        _window.PropertyChanged -= OnWindowPropertyChanged;
        _window = null;
    }

    private void OnWindowPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (_window is null)
            return;
        if (e.Property == Window.TitleProperty)
            TitleText.Text = _window.Title;
        else if (e.Property == Window.WindowStateProperty)
            UpdateMaximizeGlyph();
        else if (e.Property == Window.CanMinimizeProperty)
            MinButton.IsVisible = ShowMinimize && _window.CanMinimize;
        else if (e.Property == Window.CanResizeProperty || e.Property == Window.CanMaximizeProperty)
            MaxButton.IsVisible = ShowMaximize && _window.CanResize;
    }

    private void UpdateMaximizeGlyph()
    {
        var restored = _window?.WindowState != WindowState.Maximized;
        MaximizeGlyph.IsVisible = restored;
        RestoreGlyph.IsVisible = !restored;
        ToolTip.SetTip(MaxButton, restored ? "最大化" : "还原");
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is Avalonia.Visual visual && visual.FindAncestorOfType<Button>(true) is not null)
            return;

        if (e.ClickCount >= 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        _window.BeginMoveDrag(e);
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        if (_window is not null)
            _window.WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object? sender, RoutedEventArgs e) => _window?.Close();

    private void ToggleMaximize()
    {
        if (_window is null || !ShowMaximize || !_window.CanResize)
            return;
        _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
