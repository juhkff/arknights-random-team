using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace arknights_random_team.Views;

/// <summary>
/// 作战终端顶栏。桌面窗口里负责拖动和窗控；浏览器与叠加层只显示同一套视觉，不创建系统窗口。
/// 窗控按钮用 <c>ElementRole=User</c>，避免被当成系统最大化热区弹出贴靠布局。
/// </summary>
public partial class ThemedTitleBar : UserControl
{
    public const double BarHeight = 40;

    private Window? _window;

    public ThemedTitleBar() => InitializeComponent();

    /// <summary>覆盖窗口标题。浏览器和叠加层没有 <see cref="Window.Title"/> 时用这个。</summary>
    public string? ChromeTitle { get; set; }

    public bool ShowCaptionButtons { get; set; } = true;

    public bool ShowMinimize { get; set; } = true;

    public bool ShowMaximize { get; set; } = true;

    public bool ShowClose { get; set; } = true;

    /// <summary>没有宿主窗口时，关闭按钮走这条事件（浏览器叠加层）。</summary>
    public event EventHandler? CloseClicked;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DetachWindow();
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
            _window.PropertyChanged += OnWindowPropertyChanged;
        ApplyChrome();
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

    private void ApplyChrome()
    {
        TitleText.Text = !string.IsNullOrWhiteSpace(ChromeTitle)
            ? ChromeTitle
            : _window?.Title ?? "明日方舟随机阵容";

        var captions = ShowCaptionButtons;
        CaptionButtons.IsVisible = captions;
        MinButton.IsVisible = captions && ShowMinimize && (_window?.CanMinimize ?? false);
        MaxButton.IsVisible = captions && ShowMaximize && (_window?.CanResize ?? ShowMaximize);
        CloseButton.IsVisible = captions && ShowClose;
        UpdateMaximizeGlyph();
    }

    private void OnWindowPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (_window is null)
            return;
        if (e.Property == Window.TitleProperty && string.IsNullOrWhiteSpace(ChromeTitle))
            TitleText.Text = _window.Title;
        else if (e.Property == Window.WindowStateProperty)
            UpdateMaximizeGlyph();
        else if (e.Property is not null &&
                 (e.Property == Window.CanMinimizeProperty ||
                  e.Property == Window.CanResizeProperty ||
                  e.Property == Window.CanMaximizeProperty))
            ApplyChrome();
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

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.Close();
            return;
        }

        CloseClicked?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleMaximize()
    {
        if (_window is null || !ShowMaximize || !_window.CanResize)
            return;
        _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
