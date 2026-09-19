using arknights_random_team.Models;

namespace arknights_random_team.Domain;

/// <summary>
/// 策略条目合并与保存期冲突校验。同维固定值必须一致（不再相加）；范围取交集。
/// </summary>
public static class StrategyRules
{
    public sealed class Merged
    {
        public Dictionary<int, int> RarityExact { get; init; } = new();
        public Dictionary<Career, int> CareerExact { get; init; } = new();
        public Dictionary<Career, (int lo, int hi)> CareerRange { get; init; } = new();
        public List<StaffSubsetConstraint> StaffSubsets { get; init; } = [];

        public bool HasAny =>
            RarityExact.Count > 0 || CareerExact.Count > 0 || CareerRange.Count > 0 || StaffSubsets.Count > 0;
    }

    public static bool TryValidate(IEnumerable<StrategyRule> rules, out string error)
    {
        if (!TryMerge(rules, out var merged, out error))
            return false;

        var max = AppOptions.MaxTeamSize;
        if (merged.RarityExact.Values.Sum(value => (long)value) > max)
        {
            error = $"策略中各稀有度固定人数之和超过了随机数量上限（{max}）。";
            return false;
        }

        if (MinCareerSlots(merged.CareerExact, merged.CareerRange) > max)
        {
            error = $"策略中各职业数量（及范围下限）之和超过了随机数量上限（{max}）。";
            return false;
        }

        foreach (var c in merged.StaffSubsets)
        {
            if (c.ExactOrLo > max)
            {
                error = $"「某些干员总数」的下限或固定值超过了随机数量上限（{max}）。";
                return false;
            }
        }

        error = "";
        return true;
    }

    public static bool TryMerge(IEnumerable<StrategyRule> rules, out Merged merged, out string error)
    {
        var rarityExact = new Dictionary<int, int>();
        var careerExact = new Dictionary<Career, int>();
        var careerRange = new Dictionary<Career, (int lo, int hi)>();
        var staffSubsets = new List<StaffSubsetConstraint>();
        merged = new Merged();
        error = "";

        var max = AppOptions.MaxTeamSize;
        foreach (var r in rules)
        {
            if (r is null)
            {
                error = "策略中包含空规则。";
                return false;
            }

            if (r.Kind == StrategyRuleKind.Rarity)
            {
                if (r.Star is < FieldLimits.MinStar or > FieldLimits.MaxStar || r.Count <= 0 || r.Count > max)
                {
                    error = "稀有度规则的星级或人数超出合法范围。";
                    return false;
                }
                if (rarityExact.TryGetValue(r.Star, out var prev) && prev != r.Count)
                {
                    error = $"策略中对 {r.Star} 星同时要求了 {prev} 人和 {r.Count} 人，互相冲突。";
                    return false;
                }

                rarityExact[r.Star] = r.Count;
            }
            else if (r.Kind == StrategyRuleKind.Career)
            {
                if (!Enum.IsDefined(r.Career) || r.Count <= 0 || r.Count > max)
                {
                    error = "职业规则的职业或人数超出合法范围。";
                    return false;
                }
                if (!TryApplyCareerExact(careerExact, careerRange, r.Career, r.Count, out error))
                    return false;
            }
            else if (r.Kind == StrategyRuleKind.CareerRange)
            {
                if (!Enum.IsDefined(r.Career) || r.Count > r.CountMax || r.Count < 0 || r.CountMax > max)
                {
                    error = $"策略中「{r.Career}」的职业无效，或数量范围超出合法范围。";
                    return false;
                }

                if (!TryApplyCareerRange(careerExact, careerRange, r.Career, r.Count, r.CountMax, out error))
                    return false;
            }
            else if (r.Kind == StrategyRuleKind.StaffSubsetExact)
            {
                var names = NormalizeStaffNames(r.StaffNames);
                if (names.Count == 0 || r.Count < 0 || r.Count > max)
                {
                    error = "「某些干员总数」的名单为空，或固定人数超出合法范围。";
                    return false;
                }
                if (r.Count > names.Count)
                {
                    error = "「某些干员总数」的固定人数大于名单中的干员种类数。";
                    return false;
                }

                if (!TryApplyStaffSubset(staffSubsets, names, isExact: true, lo: r.Count, hi: r.Count, out error))
                    return false;
            }
            else if (r.Kind == StrategyRuleKind.StaffSubsetRange)
            {
                var names = NormalizeStaffNames(r.StaffNames);
                if (names.Count == 0)
                {
                    error = "「某些干员总数」的名单不能为空。";
                    return false;
                }
                if (r.Count > r.CountMax || r.Count < 0 || r.CountMax > max)
                {
                    error = "「某些干员总数」的数量范围超出合法范围。";
                    return false;
                }

                if (r.CountMax > names.Count)
                {
                    error = "「某些干员总数」的范围上限大于名单中的干员种类数。";
                    return false;
                }

                if (!TryApplyStaffSubset(staffSubsets, names, isExact: false, lo: r.Count, hi: r.CountMax, out error))
                    return false;
            }
            else
            {
                error = "策略中包含未知规则类型。";
                return false;
            }
        }

        merged = new Merged
        {
            RarityExact = rarityExact,
            CareerExact = careerExact,
            CareerRange = careerRange,
            StaffSubsets = staffSubsets
        };
        return true;
    }

    public static long MinCareerSlots(
        IReadOnlyDictionary<Career, int> careerExact,
        IReadOnlyDictionary<Career, (int lo, int hi)> careerRange)
    {
        long sum = 0;
        foreach (Career c in Enum.GetValues<Career>())
        {
            if (careerExact.TryGetValue(c, out var ex))
                sum += ex;
            else if (careerRange.TryGetValue(c, out var rg))
                sum += rg.lo;
        }

        return sum;
    }

    internal static HashSet<string> NormalizeStaffNames(IEnumerable<string>? raw)
    {
        var set = new HashSet<string>();
        if (raw == null)
            return set;
        foreach (var n in raw)
        {
            if (!string.IsNullOrWhiteSpace(n))
                set.Add(n.Trim());
        }

        return set;
    }

    private static bool TryApplyCareerExact(
        Dictionary<Career, int> careerExact,
        Dictionary<Career, (int lo, int hi)> careerRange,
        Career career,
        int n,
        out string error)
    {
        error = "";
        if (careerExact.TryGetValue(career, out var prev) && prev != n)
        {
            error = $"策略中对「{career}」同时要求了 {prev} 人和 {n} 人，互相冲突。";
            return false;
        }

        if (careerRange.TryGetValue(career, out var rg) && (n < rg.lo || n > rg.hi))
        {
            error = $"策略中「{career}」固定为 {n} 人，与范围 {rg.lo}–{rg.hi} 冲突。";
            return false;
        }

        careerExact[career] = n;
        careerRange.Remove(career);
        return true;
    }

    private static bool TryApplyCareerRange(
        Dictionary<Career, int> careerExact,
        Dictionary<Career, (int lo, int hi)> careerRange,
        Career career,
        int lo,
        int hi,
        out string error)
    {
        error = "";
        if (careerExact.TryGetValue(career, out var ex))
        {
            if (ex < lo || ex > hi)
            {
                error = $"策略中「{career}」固定为 {ex} 人，与范围 {lo}–{hi} 冲突。";
                return false;
            }

            return true;
        }

        if (!careerRange.TryGetValue(career, out var prev))
        {
            careerRange[career] = (lo, hi);
            return true;
        }

        var nl = Math.Max(prev.lo, lo);
        var nh = Math.Min(prev.hi, hi);
        if (nl > nh)
        {
            error = $"策略中对「{career}」的数量范围交集为空（{prev.lo}–{prev.hi} 与 {lo}–{hi}）。";
            return false;
        }

        careerRange[career] = (nl, nh);
        return true;
    }

    private static bool TryApplyStaffSubset(
        List<StaffSubsetConstraint> staffSubsets,
        HashSet<string> names,
        bool isExact,
        int lo,
        int hi,
        out string error)
    {
        error = "";
        foreach (var existing in staffSubsets)
        {
            if (!existing.Names.SetEquals(names))
                continue;

            if (existing.IsExact && isExact)
            {
                if (existing.ExactOrLo != lo)
                {
                    error = $"策略中对同一批指定干员同时要求了 {existing.ExactOrLo} 人和 {lo} 人，互相冲突。";
                    return false;
                }

                return true;
            }

            if (existing.IsExact)
            {
                if (existing.ExactOrLo < lo || existing.ExactOrLo > hi)
                {
                    error = $"策略中对同一批指定干员固定为 {existing.ExactOrLo} 人，与范围 {lo}–{hi} 冲突。";
                    return false;
                }

                return true;
            }

            if (isExact)
            {
                if (lo < existing.ExactOrLo || lo > existing.Hi)
                {
                    error = $"策略中对同一批指定干员固定为 {lo} 人，与范围 {existing.ExactOrLo}–{existing.Hi} 冲突。";
                    return false;
                }

                existing.IsExact = true;
                existing.ExactOrLo = lo;
                existing.Hi = 0;
                return true;
            }

            var nl = Math.Max(existing.ExactOrLo, lo);
            var nh = Math.Min(existing.Hi, hi);
            if (nl > nh)
            {
                error = $"策略中对同一批指定干员的数量范围交集为空（{existing.ExactOrLo}–{existing.Hi} 与 {lo}–{hi}）。";
                return false;
            }

            if (nl == nh)
            {
                existing.IsExact = true;
                existing.ExactOrLo = nl;
                existing.Hi = 0;
            }
            else
            {
                existing.ExactOrLo = nl;
                existing.Hi = nh;
            }

            return true;
        }

        staffSubsets.Add(new StaffSubsetConstraint
        {
            Names = names,
            IsExact = isExact,
            ExactOrLo = lo,
            Hi = isExact ? 0 : hi
        });
        return true;
    }
}
