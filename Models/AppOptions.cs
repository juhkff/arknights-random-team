namespace arknights_random_team.Models;

public static class AppOptions
{
    /// <summary>与阵容生成页随机数量滑条上限一致。</summary>
    public const int MaxTeamSize = 12;

    public static IReadOnlyList<Career> Careers { get; } = Enum.GetValues<Career>();

    public static IReadOnlyList<int> Stars { get; } =
        Enumerable.Range(FieldLimits.MinStar, FieldLimits.MaxStar - FieldLimits.MinStar + 1).ToArray();
}
