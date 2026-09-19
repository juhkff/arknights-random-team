using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace arknights_random_team.Views;

/// <summary>
/// 浏览器端（单视图生命周期）的叠加层宿主。
///
/// 这里只提供根遮罩和对话框画布；嵌套栈、逐层遮罩、焦点与 Escape 都由
/// <see cref="OverlayPresenter"/> 管理。
/// 之所以不让对话框自己用 <c>Window</c>：单视图下 <c>TopLevel</c> 不是 <c>Window</c>，
/// 没有窗口系统，<c>ShowDialog</c> 永远显示不出来。
/// </summary>
public sealed class ModalLayer : ContentControl
{
    private readonly Panel _root;
    private readonly Border _scrim;
    private readonly Canvas _dialogLayer;

    public ModalLayer()
    {
        // 对话框内没有可聚焦控件时焦点需要有个落点，Escape 才能通过隧道事件到达这里。
        Focusable = true;

        _scrim = new Border
        {
            // 遮罩同时承担两件事：压暗背后的界面，以及吃掉所有指针事件避免误触底层页面。
            Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x05, 0x0A, 0x0F)),
            IsVisible = false
        };

        // 对话框层：每个对话框自己决定尺寸，这里只负责叠加与居中。
        _dialogLayer = new Canvas { IsVisible = false };

        _root = new Panel();
        _root.Children.Add(_scrim);
        _root.Children.Add(_dialogLayer);
        Content = _root;
    }

    /// <summary>对话框容器；由 <see cref="OverlayPresenter"/> 增删子元素。</summary>
    public Canvas DialogLayer => _dialogLayer;

    /// <summary>是否处于「有对话框打开」的状态。</summary>
    public bool IsActive
    {
        get => _dialogLayer.IsVisible;
        set
        {
            _scrim.IsVisible = value;
            _dialogLayer.IsVisible = value;
        }
    }
}
