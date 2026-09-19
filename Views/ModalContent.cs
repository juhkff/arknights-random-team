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
    /// <summary>
    /// 小屏（方案 §5.1 的 &lt;600 档）是否占满可用空间。
    /// 复杂编辑面板（干员详情、策略编辑器）打开时为 true：小屏上侧栏式两栏布局没法用，
    /// 全屏铺开比挤在一个居中小卡片里更好操作。默认 false，保持普通提示框的形态。
    /// </summary>
    public virtual bool PreferFullScreenOnPhone => false;

    /// <summary>请求关闭当前对话框；<paramref name="result"/> 会作为宿主 <c>IModalPresenter.ShowAsync</c> 的返回值。</summary>
    public event EventHandler<ModalCloseRequestedEventArgs>? CloseRequested;

    protected void RequestClose(object? result) =>
        CloseRequested?.Invoke(this, new ModalCloseRequestedEventArgs(result));
}

/// <summary>对话框请求关闭时携带的返回值。</summary>
public sealed class ModalCloseRequestedEventArgs(object? result) : EventArgs
{
    public object? Result { get; } = result;
}
