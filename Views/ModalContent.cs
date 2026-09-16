using Avalonia.Controls;

namespace arknights_random_team.Views;

/// <summary>
/// 对话框内容的基类。
///
/// 浏览器端（WebAssembly）使用单视图生命周期，整个应用只有一个可视根，没有窗口系统，
/// 因此对话框不能用 <c>Window</c> 承载——必须作为叠加层内容挂到主视图里。
/// 这些对话框同时供桌面端使用，两端因此共用同一套实现与同一份布局。
/// </summary>
public abstract class ModalContent : UserControl
{
    /// <summary>请求关闭当前对话框；<paramref name="result"/> 会作为宿主 <c>ShowModalAsync</c> 的返回值。</summary>
    public event EventHandler<ModalCloseRequestedEventArgs>? CloseRequested;

    protected void RequestClose(object? result) =>
        CloseRequested?.Invoke(this, new ModalCloseRequestedEventArgs(result));
}

/// <summary>对话框请求关闭时携带的返回值。</summary>
public sealed class ModalCloseRequestedEventArgs(object? result) : EventArgs
{
    public object? Result { get; } = result;
}
