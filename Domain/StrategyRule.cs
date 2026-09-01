using arknights_random_team.Models;

namespace arknights_random_team.Domain;

public class StrategyRule
{
    public StrategyRuleKind Kind { get; set; }

    /// <summary>1–6，仅当 <see cref="Kind"/> 为 <see cref="StrategyRuleKind.Rarity"/> 时有效。</summary>
    public int Star { get; set; }

    public Career Career { get; set; }

    public int Count { get; set; }

    /// <summary>区间上界，仅 <see cref="StrategyRuleKind.CareerRange"/> 与 <see cref="StrategyRuleKind.StaffSubsetRange"/> 使用。</summary>
    public int CountMax { get; set; }

    /// <summary>干员名列表，仅 <see cref="StrategyRuleKind.StaffSubsetExact"/> / <see cref="StrategyRuleKind.StaffSubsetRange"/> 使用。</summary>
    public List<string> StaffNames { get; set; } = [];

    public string SummaryLine =>
        Kind switch
        {
            StrategyRuleKind.Rarity => $"某星干员总数：{Star} 星 × {Count}",
            StrategyRuleKind.CareerRange => $"某职业总数：{Career} {Count}–{CountMax}",
            StrategyRuleKind.Career => $"某职业总数：{Career} × {Count}",
            StrategyRuleKind.StaffSubsetExact => $"某些干员总数：从 {FormatStaffNames()} 中固定 {Count} 个",
            StrategyRuleKind.StaffSubsetRange => $"某些干员总数：从 {FormatStaffNames()} 中范围 {Count}–{CountMax} 个",
            _ => ""
        };

    private string FormatStaffNames()
    {
        if (StaffNames.Count == 0)
            return "（未指定）";
        var head = string.Join("、", StaffNames.Take(4));
        return StaffNames.Count > 4 ? head + "…" : head;
    }
}
