using arknights_random_team.Views;

namespace arknights_random_team;

/// <summary>
/// 对话框入口。具体「画在哪里」由启动时按平台注册的 <see cref="IModalPresenter"/> 决定：
/// 桌面端是原生窗口，浏览器端是应用内叠加层。
/// 各页面只通过这里请求对话框，不关心实现，因此两端共用同一份业务代码。
/// </summary>
public static class AppHost
{
    /// <summary>当前平台注册的对话框展示方式，在 <c>App.OnFrameworkInitializationCompleted</c> 里设置。</summary>
    public static IModalPresenter? Presenter { get; set; }

    /// <summary>显示确认框，用户确认返回 <c>true</c>。</summary>
    public static Task<bool> ConfirmAsync(string message, string title = "确认操作")
    {
        var presenter = Presenter;
        return presenter is null
            ? Task.FromResult(false)
            : presenter.ShowAsync(new ConfirmDialog(title, message));
    }

    /// <summary>显示提示框。</summary>
    public static async Task AlertAsync(string message, string title = "提示")
    {
        if (Presenter is not { } presenter)
            return;

        await presenter.ShowAsync(new AlertDialog(title, message));
    }

    /// <summary>显示任意对话框，返回它关闭时给出的结果。</summary>
    public static Task<T?> ShowAsync<T>(ModalContent dialog)
    {
        var presenter = Presenter;
        return presenter is null
            ? Task.FromResult<T?>(default)
            : presenter.ShowAsync<T>(dialog);
    }
}
