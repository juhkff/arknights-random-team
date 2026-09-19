using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace arknights_random_team.Views;

/// <summary>
/// 桌面端的对话框展示方式：包装成真正的系统窗口并模态显示。
///
/// 等价于改造前的写法（<c>Window</c> + <c>ShowDialog(owner)</c>）：
/// 拥有与主窗口同一套自绘标题栏、可拖动到屏幕任意位置、不受主窗口尺寸限制，
/// 策略编辑器这类大窗口也能保持它自己的客户区尺寸。
/// </summary>
public sealed class WindowPresenter : IModalPresenter
{
    /// <inheritdoc />
    public ModalLayer? Overlay => null;

    /// <inheritdoc />
    public Task<T?> ShowAsync<T>(ModalContent dialog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult<T?>(default);

        var owner = Owner();
        var window = new ModalWindow(dialog)
        {
            // 按 Escape 的语义与原实现一致：bool 对话框返回 false（取消），其余返回 null。
            EscapeResult = typeof(T) == typeof(bool) ? false : null
        };

        CancellationTokenRegistration registration = default;
        if (cancellationToken.CanBeCanceled)
        {
            EventHandler? onClosed = null;
            onClosed = (_, _) =>
            {
                window.Closed -= onClosed;
                registration.Dispose();
            };
            window.Closed += onClosed;
            registration = cancellationToken.Register(() =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (window.IsVisible)
                        window.Close();
                }));
        }

        // Use an owned modal window when possible, otherwise fall back to a regular window.
        if (owner is not null)
            window.ShowDialog(owner);
        else
            window.Show();

        return ReadResult<T>(window);
    }

    /// <inheritdoc />
    public Task<bool> ShowAsync(ModalContent dialog, CancellationToken cancellationToken = default) =>
        ShowAsync<bool>(dialog, cancellationToken);

    private static async Task<T?> ReadResult<T>(ModalWindow window)
    {
        await window.WaitForClosed();
        return window.HasDialogResult && window.DialogResult is T typed ? typed : default;
    }

    private static Window? Owner()
    {
        if (Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime { Windows.Count: > 0 } desktop)
        {
            return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
        }

        return null;
    }
}

/// <summary>
/// 承载 <see cref="ModalContent"/> 的窗口外壳。
/// 之所以需要它：<see cref="ModalContent"/> 是 <c>UserControl</c>（浏览器端不能是 <c>Window</c>），
/// 桌面端要还原原生窗口，就得有人把它包进窗口并负责窗体行为。
/// </summary>
internal sealed class ModalWindow : Window
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ModalWindow(ModalContent content)
    {
        var contentWidth = content.Width;
        var contentHeight = content.Height;
        var fixedHeight = !double.IsNaN(contentHeight);

        // 固定高度的对话框铺满窗口客户区，让内部 * 行和滚动区拿到剩余高度。
        // 确认框/提示框只有宽度、高度按内容自适应，必须居中而不是拉伸。
        if (fixedHeight)
        {
            content.Width = double.NaN;
            content.Height = double.NaN;
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            content.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
        }

        var shell = DialogShell.For(content.GetType());
        Title = shell.Title;
        CanResize = shell.Resizable;
        CanMinimize = false;
        if (shell.MinWidth > 0)
            MinWidth = shell.MinWidth;
        if (shell.MinHeight > 0)
            MinHeight = shell.MinHeight + ThemedTitleBar.BarHeight;

        var titleBar = new ThemedTitleBar
        {
            ChromeTitle = shell.Title,
            ShowMinimize = false,
            ShowMaximize = shell.Resizable
        };
        var host = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);
        host.Children.Add(titleBar);
        host.Children.Add(content);
        Content = host;
        ThemedWindowChrome.Apply(this);

        // 与改造前一致：居中于宿主、不占任务栏。
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // 尺寸就是内容在 XAML 里声明的那套，与改造前逐个对话框的设置一一对应：
        // 策略编辑器 960×740 可缩放；确认框/提示框只有宽度、高度按内容自适应；
        // 选择干员与同步干员为固定尺寸。
        Width = !double.IsNaN(contentWidth)
            ? contentWidth
            : content.MaxWidth is > 0 and < double.PositiveInfinity
                ? content.MaxWidth
                : double.NaN;
        Height = !double.IsNaN(contentHeight)
            ? contentHeight + ThemedTitleBar.BarHeight
            : contentHeight;
        if (double.IsNaN(contentWidth) && double.IsNaN(contentHeight))
            SizeToContent = SizeToContent.WidthAndHeight;
        else if (double.IsNaN(contentHeight))
            SizeToContent = SizeToContent.Height;

        content.CloseRequested += OnContentCloseRequested;

        // Escape 原本由各对话框自己的 Window.KeyDown 处理，现在统一收在窗口这一层。
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        Closed += (_, _) =>
        {
            content.CloseRequested -= OnContentCloseRequested;
            _closed.TrySetResult();
        };
    }

    /// <summary>
    /// 对话框关闭时的返回值。
    /// Avalonia 12 的 <c>Window</c> 不再暴露 <c>Result</c> 属性（结果直接由
    /// <c>ShowDialog&lt;T&gt;</c> 的 Task 给出），而这里必须按运行时类型读取结果，
    /// 所以自己记录一次。
    /// </summary>
    public object? DialogResult { get; private set; }

    /// <summary>是否显式给出过结果；没给过时应当返回 <c>default</c> 而不是猜测。</summary>
    public bool HasDialogResult { get; private set; }

    /// <summary>
    /// 按 Escape 关闭时的返回值。由展示层按结果类型给出（<c>bool</c> 对话框给 <c>false</c>，其余给 <c>null</c>）；
    /// 未指定时退回 <c>null</c>。
    /// </summary>
    public object? EscapeResult { get; set; }

    public Task WaitForClosed() => _closed.Task;

    /// <summary>
    /// 带上结果关闭窗口。
    /// 之所以需要自己记录：Avalonia 12 的 <c>Window</c> 既没有可读的 <c>Result</c> 属性，
    /// <c>Close</c> 也不是 virtual（结果只由 <c>ShowDialog&lt;T&gt;</c> 的 Task 给出），
    /// 而这里必须按运行时类型读取结果，无法在编译期选定泛型。
    /// </summary>
    public void CloseWith(object? dialogResult)
    {
        DialogResult = dialogResult;
        HasDialogResult = true;
        Close(dialogResult);
    }

    private void OnContentCloseRequested(object? sender, ModalCloseRequestedEventArgs e) => CloseWith(e.Result);

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        CloseWith(EscapeResult);
    }
}
