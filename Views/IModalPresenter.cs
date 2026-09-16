using Avalonia.Controls;

namespace arknights_random_team.Views;

/// <summary>
/// 对话框的展示方式。两端的内容完全相同（<see cref="ModalContent"/>），
/// 差别只在「画在哪里」：
/// <list type="bullet">
///   <item>桌面端用 <see cref="WindowPresenter"/>：真正的系统窗口 + 模态，可拖动、不受主窗口大小限制。</item>
///   <item>浏览器端用 <see cref="OverlayPresenter"/>：单视图生命周期没有窗口系统，只能用遮罩叠加层。</item>
/// </list>
/// </summary>
public interface IModalPresenter
{
    /// <summary>显示对话框，返回它关闭时给出的结果。</summary>
    Task<T?> ShowAsync<T>(ModalContent dialog, CancellationToken cancellationToken = default);

    /// <summary>未指定结果类型的对话框，按返回 <c>bool</c> 处理。</summary>
    Task<bool> ShowAsync(ModalContent dialog, CancellationToken cancellationToken = default);

    /// <summary>叠加层宿主；桌面端为 <c>null</c>（不需要往主视图里塞任何东西）。</summary>
    ModalLayer? Overlay { get; }
}
