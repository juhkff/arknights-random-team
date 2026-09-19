namespace arknights_random_team.Models;

/// <summary>
/// 干员字段的合法区间，集中一处。
///
/// 这些值决定数组下标（如约束求解器里的 <c>_unusedStar[s.Star]</c>）与显示格式，
/// 越界数据会让生成阵容时下标越界崩溃，或让 <c>Level.Format</c> 抛异常。
/// 存档解析、同步合并、界面编辑三处都必须走同一份区间，不能各写一套。
/// </summary>
public static class FieldLimits
{
    public const int MinStar = 1;
    public const int MaxStar = 6;
    public const int MinElite = 0;
    public const int MaxElite = 2;
    public const int MinRank = 1;
    public const int MaxRank = 90;

    public static int ClampStar(int star) => Math.Clamp(star, MinStar, MaxStar);

    public static int ClampElite(int elite) => Math.Clamp(elite, MinElite, MaxElite);

    public static int ClampRank(int rank) => Math.Clamp(rank, MinRank, MaxRank);
}
