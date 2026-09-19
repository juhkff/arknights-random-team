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

    /// <summary>1–2 星不能精英，3 星最高精一，4–6 星可精二。</summary>
    public static int MaxEliteForStar(int star) => ClampStar(star) switch
    {
        1 or 2 => 0,
        3 => 1,
        _ => MaxElite
    };

    public static int ClampEliteForStar(int elite, int star) =>
        Math.Clamp(elite, MinElite, MaxEliteForStar(star));

    /// <summary>当前稀有度与精英阶段下的等级上限，与游戏本体一致。</summary>
    public static int MaxRankFor(int star, int elite)
    {
        star = ClampStar(star);
        elite = ClampEliteForStar(elite, star);
        return (star, elite) switch
        {
            (1 or 2, _) => 30,
            (3, 0) => 40,
            (3, _) => 55,
            (4, 0) => 50,
            (4, 1) => 60,
            (4, _) => 70,
            (5, 0) => 50,
            (5, 1) => 70,
            (5, _) => 80,
            (_, 0) => 50,
            (_, 1) => 80,
            _ => 90
        };
    }

    public static int ClampRankFor(int rank, int star, int elite) =>
        Math.Clamp(rank, MinRank, MaxRankFor(star, elite));

    public static string FormatElite(int elite) => elite switch
    {
        1 => "精一",
        2 => "精二",
        _ => "精零"
    };
}
