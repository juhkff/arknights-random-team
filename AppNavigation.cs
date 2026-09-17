namespace arknights_random_team;

public enum AppPage
{
    Generate,
    Input,
    List,
    Strategy
}

/// <summary>
/// 子页面空状态按钮跳转用。外壳 <c>MainView</c> 订阅后切页，
/// 各页不必去找祖先控件。
/// </summary>
public static class AppNavigation
{
    public static event Action<AppPage>? Requested;

    public static void GoTo(AppPage page) => Requested?.Invoke(page);
}
