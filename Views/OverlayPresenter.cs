using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace arknights_random_team.Views;

/// <summary>
/// 浏览器端的对话框展示方式：在 <see cref="ModalLayer"/> 叠加层里显示。
///
/// 之所以不用 <c>Window.ShowDialog</c>：浏览器端是单视图生命周期，
/// <c>TopLevel.GetTopLevel(visual)</c> 返回的是 <c>SingleViewTopLevel</c> 而不是 <c>Window</c>，
/// 既没有窗口系统，<c>ShowDialog</c> 也无从挂载，所以对话框在网页上永远不会显示。
/// 桌面端请用 <see cref="WindowPresenter"/>。
/// </summary>
public sealed class OverlayPresenter : IModalPresenter
{
    /// <summary>卡片外壳的描边色。对话框内容本身不含描边——桌面端那条描边由窗口提供。</summary>
    private static readonly IBrush CardBorderBrush = new SolidColorBrush(Color.Parse("#303A45"));

    /// <summary>
    /// 弹层圆角：优先取 <c>App.axaml</c> 里的 <c>AppRadiusDialog</c> 令牌（方案 §2.2：弹层 12），
    /// 取不到时退回同值常量。放在这里是让「弹层圆角」只有一个来源，而不是 XAML 一份、C# 一份。
    /// </summary>
    private static CornerRadius DialogRadius =>
        Application.Current?.TryGetResource("AppRadiusDialog", Application.Current.ActualThemeVariant, out var value) == true
            && value is CornerRadius radius
                ? radius
                : new CornerRadius(12);

    private readonly List<Entry> _stack = [];
    private bool _repositionPending;

    /// <param name="layer">主视图里的叠加层，由 <c>App</c> 创建、<c>MainView</c> 挂进可视树。</param>
    public OverlayPresenter(ModalLayer layer)
    {
        Overlay = layer ?? throw new ArgumentNullException(nameof(layer));

        // Escape 关闭最上层对话框。用隧道阶段处理，保证焦点在对话框内任意控件上时都能生效。
        Overlay.AddHandler(InputElement.KeyDownEvent, OnLayerKeyDown, RoutingStrategies.Tunnel);

        // 叠加层尺寸变化（如浏览器窗口缩放）时重新约束并居中。
        Overlay.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty)
                UpdateSizing();
        };
    }

    /// <inheritdoc />
    public ModalLayer Overlay { get; }

    /// <inheritdoc />
    public Task<T?> ShowAsync<T>(ModalContent dialog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        // 对话框内容只描述自己（根是 Grid，背景与描边由窗口提供，桌面端即如此）。
        // 叠加层没有窗口，所以这里补一层卡片外壳：圆角、描边，并接住本该由窗口提供的背景。
        var contentWidth = dialog.Width;
        var contentHeight = dialog.Height;
        var fixedHeight = !double.IsNaN(contentHeight);

        if (fixedHeight)
        {
            dialog.Width = double.NaN;
            dialog.Height = double.NaN;
            dialog.HorizontalAlignment = HorizontalAlignment.Stretch;
            dialog.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            dialog.HorizontalAlignment = HorizontalAlignment.Center;
            dialog.VerticalAlignment = VerticalAlignment.Center;
        }

        // 小屏全屏：复杂编辑面板在 &lt;600 档铺满可用空间，不再留一圈居中卡片。
        var fullScreenOnPhone = dialog.PreferFullScreenOnPhone && AppLayout.IsPhone;

        var card = new Border
        {
            Child = dialog,
            Background = dialog.Background ?? Brushes.Transparent,
            BorderBrush = CardBorderBrush,
            BorderThickness = fullScreenOnPhone ? new Thickness(0) : new Thickness(1),
            // 弹层圆角取全局令牌（方案 §2.2：弹层 12），全屏模式下去掉圆角。
            CornerRadius = fullScreenOnPhone ? new CornerRadius(0) : DialogRadius,
            ClipToBounds = true,
            Width = fullScreenOnPhone ? double.NaN : contentWidth,
            Height = fullScreenOnPhone ? double.NaN : contentHeight
        };

        if (fullScreenOnPhone)
        {
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.VerticalAlignment = VerticalAlignment.Stretch;

            // 对话框自身与内容都要拉伸：没有固定高度的对话框走的是「居中」分支，
            // 不在这里覆盖就会在整屏里居中、上下各留一大片空白。
            dialog.HorizontalAlignment = HorizontalAlignment.Stretch;
            dialog.VerticalAlignment = VerticalAlignment.Stretch;
            dialog.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            dialog.VerticalContentAlignment = VerticalAlignment.Stretch;
        }

        return ShowCoreAsync<T>(dialog, card, fullScreenOnPhone, cancellationToken);
    }

    private Task<T?> ShowCoreAsync<T>(ModalContent dialog, Control visual, bool fullScreen, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (cancellationToken.IsCancellationRequested)
        {
            completion.SetResult(default);
            return completion.Task;
        }

        var entry = new Entry(dialog, visual, Dismiss, result => completion.TrySetResult(result is T typed ? typed : default))
        {
            // 记下打开前的焦点，关闭时归还，键盘用户不会掉回页面开头。
            PreviousFocus = TopLevel.GetTopLevel(Overlay)?.FocusManager?.GetFocusedElement(),
            FullScreen = fullScreen
        };

        CancellationTokenRegistration registration = default;
        if (cancellationToken.CanBeCanceled)
            registration = cancellationToken.Register(() => Dismiss(entry));

        entry.Cleanup = () => registration.Dispose();

        entry.CloseRequestedHandler = (_, e) =>
        {
            entry.Result = e.Result;
            Dismiss(entry);
        };
        dialog.CloseRequested += entry.CloseRequestedHandler;

        _stack.Add(entry);
        Overlay.DialogLayer.Children.Add(visual);
        Overlay.IsActive = true;

        // Tab 焦点圈禁：只在对话框内部循环，背景页面不再能被 Tab 走到
        // （遮罩只挡指针，挡不住键盘）。对话框移除时这条设置随之失效。
        KeyboardNavigation.SetTabNavigation(entry.Dialog, KeyboardNavigationMode.Cycle);

        UpdateSizing();

        // 尺寸要等首次布局完成后才有效，这里补一次定位并交给它焦点。
        Dispatcher.UIThread.Post(() => FocusDialog(entry), DispatcherPriority.Loaded);

        return completion.Task;
    }

    /// <inheritdoc />
    public Task<bool> ShowAsync(ModalContent dialog, CancellationToken cancellationToken = default) =>
        ShowAsync<bool>(dialog, cancellationToken);

    private void OnLayerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _stack.Count == 0)
            return;

        e.Handled = true;
        Dismiss(_stack[^1]);
    }

    /// <summary>关闭对话框：从可视树与栈中移除，并把结果交回等待方。</summary>
    private void Dismiss(Entry entry)
    {
        if (!_stack.Remove(entry))
            return;

        // 焦点是否还停在这个正在关闭的对话框里，必须在移除之前判断：
        // 移除之后它的控件已经脱离可视树，任何「还在栈里吗」的检查都必然为假（原实现的漏洞）。
        var focused = TopLevel.GetTopLevel(Overlay)?.FocusManager?.GetFocusedElement();
        var focusWasInside = focused is null || IsWithin(entry.Visual, focused);

        entry.Dialog.CloseRequested -= entry.CloseRequestedHandler;
        Overlay.DialogLayer.Children.Remove(entry.Visual);
        Overlay.IsActive = _stack.Count > 0;

        entry.PublishResult();
        entry.Cleanup?.Invoke();

        if (focusWasInside)
            RestoreFocus(entry);

        if (_stack.Count > 0)
            Dispatcher.UIThread.Post(() => FocusDialog(_stack[^1]), DispatcherPriority.Loaded);
    }

    private void FocusDialog(Entry entry)
    {
        // 排队的回调可能在对话框已关闭后才执行，这里再确认一次它仍在栈上。
        if (!_stack.Contains(entry))
            return;

        // 首次布局后尺寸才有效，这里再量一次，避免第一帧停在左上角。
        UpdateSizing();

        // 浏览器端没有窗口系统，不会自动把焦点交给弹出的内容；
        // 必须显式把焦点移进对话框，否则键盘操作与 Escape 都不会生效。
        if (FocusFirstFocusable(entry.Dialog) || entry.Dialog.Focus())
            return;

        // 对话框内没有任何可聚焦控件时，退而求其次：把焦点收在宿主上，
        // 这样 Escape 的隧道事件依然能到达这里。
        Overlay.Focus();
    }

    /// <summary>按可视树顺序找到第一个可聚焦的控件并交给它焦点。</summary>
    private static bool FocusFirstFocusable(Visual root)
    {
        foreach (var descendant in root.GetVisualDescendants())
        {
            if (descendant is not Control { Focusable: true, IsEffectivelyVisible: true } control)
                continue;

            if (control.Focus())
                return true;
        }

        return false;
    }

    /// <summary>
    /// 把焦点交还给对话框打开前持有焦点的控件。
    /// 是否需要归还由调用方（<see cref="Dismiss"/>）在移除对话框之前判断，这里只负责归还。
    /// </summary>
    private void RestoreFocus(Entry entry)
    {
        if (entry.PreviousFocus is not { } previous)
            return;

        Dispatcher.UIThread.Post(() => previous.Focus(), DispatcherPriority.Loaded);
    }

    private static bool IsWithin(Visual ancestor, IInputElement? candidate)
    {
        for (var node = candidate as Visual; node is not null; node = node.GetVisualParent())
        {
            if (ReferenceEquals(node, ancestor))
                return true;
        }

        return false;
    }

    /// <summary>把每个对话框约束在叠加层范围内，并安排一次重新居中。</summary>
    private void UpdateSizing()
    {
        if (_stack.Count == 0)
            return;

        var hostWidth = Overlay.Bounds.Width;
        var hostHeight = Overlay.Bounds.Height;
        if (hostWidth <= 0 || hostHeight <= 0)
            return;

        foreach (var entry in _stack)
        {
            if (entry.FullScreen)
            {
                // 小屏全屏：直接按宿主尺寸给宽高，由下面的定位把它摆到 (0,0)。
                entry.Visual.Width = hostWidth;
                entry.Visual.Height = hostHeight;
                entry.Visual.MaxWidth = hostWidth;
                entry.Visual.MaxHeight = hostHeight;
                entry.Dialog.MaxWidth = hostWidth;
                entry.Dialog.MaxHeight = hostHeight;
                entry.Visual.InvalidateMeasure();
                continue;
            }

            var maxW = Math.Max(160, hostWidth - 32);
            var maxH = Math.Max(160, hostHeight - 32);
            entry.Visual.MaxWidth = maxW;
            entry.Visual.MaxHeight = maxH;
            entry.Dialog.MaxWidth = maxW;
            entry.Dialog.MaxHeight = maxH;
            entry.Visual.InvalidateMeasure();
        }

        // 拖动窗口大小时会连续触发，合并成一次定位，避免回调堆积。
        if (_repositionPending)
            return;

        _repositionPending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _repositionPending = false;
                foreach (var entry in _stack)
                {
                    if (entry.FullScreen)
                    {
                        Canvas.SetLeft(entry.Visual, 0);
                        Canvas.SetTop(entry.Visual, 0);
                        continue;
                    }

                    CenterInLayer(entry.Visual);
                }
            },
            DispatcherPriority.Loaded);
    }

    private void CenterInLayer(Control card)
    {
        var hostWidth = Overlay.Bounds.Width;
        var hostHeight = Overlay.Bounds.Height;
        if (hostWidth <= 0 || hostHeight <= 0)
            return;

        var size = card.DesiredSize;
        var width = Math.Min(size.Width, Math.Max(0, hostWidth - 32));
        var height = Math.Min(size.Height, Math.Max(0, hostHeight - 32));

        Canvas.SetLeft(card, Math.Max(0, (hostWidth - width) / 2));
        Canvas.SetTop(card, Math.Max(0, (hostHeight - height) / 2));
    }

    /// <summary>一次对话框会话：连接内容控件、栈与等待中的调用方。</summary>
    private sealed class Entry(
        ModalContent dialog,
        Control visual,
        Action<Entry> dismiss,
        Action<object?> complete)
    {
        private readonly Action<object?> _complete = complete;

        public ModalContent Dialog { get; } = dialog;

        /// <summary>实际放进叠加层的控件：卡片外壳，其子元素才是对话框内容。</summary>
        public Control Visual { get; } = visual;

        /// <summary>小屏全屏：铺满叠加层，而不是居中留边（叠加层用 Canvas 定位，Stretch 不起作用）。</summary>
        public bool FullScreen { get; init; }

        /// <summary>关闭请求携带的返回值。</summary>
        public object? Result { get; set; }

        /// <summary>移除后释放取消令牌订阅。</summary>
        public Action? Cleanup { get; set; }

        /// <summary>对话框打开前持有焦点的控件，关闭时归还。</summary>
        public IInputElement? PreviousFocus { get; set; }

        /// <summary>内容的关闭请求处理器，移除时需要退订。</summary>
        public EventHandler<ModalCloseRequestedEventArgs>? CloseRequestedHandler { get; set; }

        public void PublishResult() => _complete(Result);

        /// <summary>交给宿主统一移除，保证栈与可视树一致。</summary>
        public void Dismiss() => dismiss(this);
    }
}
