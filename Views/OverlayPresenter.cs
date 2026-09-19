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

    /// <summary>Scrim placed between nested dialog layers.</summary>
    private static readonly IBrush LayerScrimBrush = new SolidColorBrush(Color.FromArgb(0x8C, 0x05, 0x0A, 0x0F));

    /// <summary>Uses the global dialog radius with a matching fallback.</summary>
    private static CornerRadius DialogRadius =>
        Application.Current?.TryGetResource("AppRadiusDialog", Application.Current.ActualThemeVariant, out var value) == true
            && value is CornerRadius radius
                ? radius
                : new CornerRadius(12);

    private static IBrush CardBackground(IBrush? requested)
    {
        if (requested is not null && requested is not ISolidColorBrush { Color.A: 0 })
            return requested;

        return Application.Current?.TryGetResource(
                   "AppBackgroundBrush",
                   Application.Current.ActualThemeVariant,
                   out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(Color.Parse("#0F1419"));
    }

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
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult<T?>(default);

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

        // 小屏全屏：复杂编辑面板在 <600 档铺满可用空间，不再留一圈居中卡片。
        // 另外「高度不够」时也要全屏：844×390 这类横屏短屏宽度上不属于手机档，
        // 但把 960×440 的编辑器塞进 358 高的居中卡片只会把底部按钮挤出可视区。
        // 这里只做首次判定；宿主尺寸变化后会由 UpdateSizing 重新判定。
        var hostHeight = Overlay.Bounds.Height;
        var requestedHeight = !double.IsNaN(contentHeight) ? contentHeight : dialog.MinHeight;
        var fullScreenOnPhone = ShouldFillHost(dialog, requestedHeight, hostHeight);

        var card = new Border
        {
            Child = dialog,
            Background = CardBackground(dialog.Background),
            BorderBrush = CardBorderBrush,
            BorderThickness = fullScreenOnPhone ? new Thickness(0) : new Thickness(1),
            CornerRadius = fullScreenOnPhone ? new CornerRadius(0) : DialogRadius,
            ClipToBounds = true,
            Width = fullScreenOnPhone ? double.NaN : contentWidth,
            Height = fullScreenOnPhone ? double.NaN : contentHeight
        };

        // Capture alignment before full-screen mode replaces it with Stretch.
        var baseHorizontalContentAlignment = dialog.HorizontalContentAlignment;
        var baseVerticalContentAlignment = dialog.VerticalContentAlignment;

        if (fullScreenOnPhone)
            ApplyFullScreen(dialog);

        return ShowCoreAsync<T>(
            dialog,
            card,
            new EntryLayout(
                contentWidth,
                contentHeight,
                requestedHeight,
                baseHorizontalContentAlignment,
                baseVerticalContentAlignment),
            fullScreenOnPhone,
            cancellationToken);
    }

    /// <summary>是否应该铺满宿主：手机档的复杂面板，或宿主高度装不下它声明的高度。</summary>
    private static bool ShouldFillHost(ModalContent dialog, double requestedHeight, double hostHeight) =>
        (dialog.PreferFullScreenOnPhone && AppLayout.IsPhone) ||
        (hostHeight > 0 && requestedHeight > 0 && requestedHeight > hostHeight - 32);

    /// <summary>全屏模式：内容与外层都要拉伸，去掉描边与圆角。</summary>
    private static void ApplyFullScreen(ModalContent dialog)
    {
        // 对话框自身与内容都要拉伸：没有固定高度的对话框走的是「居中」分支，
        // 不在这里覆盖就会在整屏里居中、上下各留一大片空白。
        dialog.HorizontalAlignment = HorizontalAlignment.Stretch;
        dialog.VerticalAlignment = VerticalAlignment.Stretch;
        dialog.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        dialog.VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    private Task<T?> ShowCoreAsync<T>(
        ModalContent dialog,
        Border card,
        EntryLayout layout,
        bool fullScreen,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var entry = new Entry(
            dialog,
            card,
            layout,
            fullScreen,
            result => completion.TrySetResult(result is T typed ? typed : default))
        {
            PreviousFocus = TopLevel.GetTopLevel(Overlay)?.FocusManager?.GetFocusedElement()
        };

        entry.CloseRequestedHandler = (_, e) =>
        {
            entry.Result = e.Result;
            Dismiss(entry);
        };
        dialog.CloseRequested += entry.CloseRequestedHandler;

        _stack.Add(entry);
        Overlay.DialogLayer.Children.Add(entry.LayerScrim);
        Overlay.DialogLayer.Children.Add(card);
        Overlay.IsActive = true;

        if (cancellationToken.CanBeCanceled)
        {
            entry.CancellationRegistration = cancellationToken.Register(
                () => Dispatcher.UIThread.Post(() => Dismiss(entry)));
        }

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

        // Check focus before detaching the dialog from the visual tree.
        var focused = TopLevel.GetTopLevel(Overlay)?.FocusManager?.GetFocusedElement();
        var focusWasInside = focused is null || IsWithin(entry.Visual, focused);

        entry.Dialog.CloseRequested -= entry.CloseRequestedHandler;
        Overlay.DialogLayer.Children.Remove(entry.Visual);
        Overlay.DialogLayer.Children.Remove(entry.LayerScrim);
        Overlay.IsActive = _stack.Count > 0;

        entry.PublishResult();
        entry.CancellationRegistration.Dispose();

        if (_stack.Count > 0)
            UpdateSizing();

        if (focusWasInside)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (entry.PreviousFocus?.Focus() == true)
                        return;
                    if (_stack.Count > 0)
                        FocusDialog(_stack[^1]);
                },
                DispatcherPriority.Loaded);
        }
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

        for (var index = 0; index < _stack.Count; index++)
        {
            var entry = _stack[index];

            // 遮罩铺满宿主：只有下面还压着别的弹层时才需要显示。
            entry.LayerScrim.Width = hostWidth;
            entry.LayerScrim.Height = hostHeight;
            Canvas.SetLeft(entry.LayerScrim, 0);
            Canvas.SetTop(entry.LayerScrim, 0);
            entry.LayerScrim.IsVisible = index > 0;

            // 非顶层不参与命中：遮罩已经挡住指针，这里再显式关掉，
            // 避免将来换掉遮罩实现后又退回「父层能被点到」。
            entry.Visual.IsHitTestVisible = index == _stack.Count - 1;

            // Re-evaluate full-screen mode whenever the host size changes.
            var fullScreen = ShouldFillHost(entry.Dialog, entry.Layout.RequestedHeight, hostHeight);
            ApplyEntryMode(entry, fullScreen, hostWidth, hostHeight);
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

    /// <summary>
    /// 按当前模式约束一层弹层：全屏时铺满宿主，否则回到卡片尺寸并夹在宿主内。
    /// 两个方向都要支持——宿主变矮要进全屏，变高要能退回卡片。
    /// </summary>
    private static void ApplyEntryMode(Entry entry, bool fullScreen, double hostWidth, double hostHeight)
    {
        var card = entry.Visual;
        entry.FullScreen = fullScreen;

        if (fullScreen)
        {
            card.Width = hostWidth;
            card.Height = hostHeight;
            card.MaxWidth = hostWidth;
            card.MaxHeight = hostHeight;
            card.BorderThickness = new Thickness(0);
            card.CornerRadius = new CornerRadius(0);
            entry.Dialog.MaxWidth = hostWidth;
            entry.Dialog.MaxHeight = hostHeight;
            ApplyFullScreen(entry.Dialog);
            return;
        }

        var maxW = Math.Max(160, hostWidth - 32);
        var maxH = Math.Max(160, hostHeight - 32);
        card.Width = entry.Layout.ContentWidth;
        card.Height = entry.Layout.ContentHeight;
        card.MaxWidth = maxW;
        card.MaxHeight = maxH;
        card.BorderThickness = new Thickness(1);
        card.CornerRadius = DialogRadius;
        entry.Dialog.MaxWidth = maxW;
        entry.Dialog.MaxHeight = maxH;

        // 回到打开时的对齐方式（固定高度的对话框是拉伸，其余居中）。
        var stretch = !double.IsNaN(entry.Layout.ContentHeight);
        entry.Dialog.HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        entry.Dialog.VerticalAlignment = stretch ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        entry.Dialog.HorizontalContentAlignment = entry.Layout.HorizontalContentAlignment;
        entry.Dialog.VerticalContentAlignment = entry.Layout.VerticalContentAlignment;
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

    private readonly record struct EntryLayout(
        double ContentWidth,
        double ContentHeight,
        double RequestedHeight,
        HorizontalAlignment HorizontalContentAlignment,
        VerticalAlignment VerticalContentAlignment);

    private sealed class Entry(
        ModalContent dialog,
        Border visual,
        EntryLayout layout,
        bool fullScreen,
        Action<object?> complete)
    {
        public ModalContent Dialog { get; } = dialog;

        public Border Visual { get; } = visual;

        public EntryLayout Layout { get; } = layout;

        public bool FullScreen { get; set; } = fullScreen;

        public Border LayerScrim { get; } = new() { Background = LayerScrimBrush, IsVisible = false };

        public object? Result { get; set; }

        public CancellationTokenRegistration CancellationRegistration { get; set; }

        public IInputElement? PreviousFocus { get; set; }

        public EventHandler<ModalCloseRequestedEventArgs>? CloseRequestedHandler { get; set; }

        public void PublishResult() => complete(Result);
    }
}
