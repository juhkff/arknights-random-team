namespace arknights_random_team.Views;

public enum AppPage
{
    Generate,
    Input,
    List,
    Strategy
}

/// <summary>页面间跳转。由 <see cref="MainView"/> 订阅，列表页的「添加干员」复用录入页流程。</summary>
public static class AppNavigation
{
    public static event Action<AppPage>? Requested;

    public static void Go(AppPage page) => Requested?.Invoke(page);
}
