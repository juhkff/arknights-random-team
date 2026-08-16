namespace arknights_random_team.Models;

public static class AppOptions
{
    public static IReadOnlyList<Career> Careers { get; } = Enum.GetValues<Career>();

    public static IReadOnlyList<int> Stars { get; } = [1, 2, 3, 4, 5, 6];
}
