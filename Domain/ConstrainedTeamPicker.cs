using arknights_random_team.Models;

namespace arknights_random_team.Domain;

/// <summary>
/// 在满足稀有度、职业、指定干员子集人数等约束的前提下，从候选池无放回随机组队。
/// </summary>
public static class ConstrainedTeamPicker
{
    private const int MaxAttempts = 8000;

    public static bool TryPick(
        IReadOnlyList<Staff> pool,
        int k,
        Dictionary<int, int> rarityReq,
        Dictionary<Career, int> careerExact,
        Dictionary<Career, (int lo, int hi)> careerRange,
        IReadOnlyList<StaffSubsetConstraint> staffSubsets,
        Random random,
        out List<Staff> team)
    {
        team = null!;
        if (pool.Count == 0 || k <= 0 || k > pool.Count)
            return false;

        rarityReq = rarityReq.Count == 0 ? new Dictionary<int, int>() : rarityReq;
        careerExact = careerExact.Count == 0 ? new Dictionary<Career, int>() : careerExact;
        careerRange = careerRange.Count == 0 ? new Dictionary<Career, (int lo, int hi)>() : careerRange;
        staffSubsets ??= [];

        foreach (var kv in rarityReq)
        {
            if (kv.Value < 0 || kv.Key is < 1 or > 6)
                return false;
        }

        foreach (var kv in careerExact)
        {
            if (kv.Value < 0)
                return false;
        }

        foreach (var kv in careerRange)
        {
            if (kv.Value.lo > kv.Value.hi || kv.Value.lo < 0)
                return false;
        }

        foreach (var kv in careerExact)
        {
            if (careerRange.TryGetValue(kv.Key, out var rg) && (kv.Value < rg.lo || kv.Value > rg.hi))
                return false;
        }

        foreach (var c in staffSubsets)
        {
            if (c.Names.Count == 0)
                return false;
            if (c.IsExact)
            {
                if (c.ExactOrLo < 0 || c.ExactOrLo > k)
                    return false;
            }
            else if (c.ExactOrLo > c.Hi || c.ExactOrLo < 0 || c.Hi > k)
            {
                return false;
            }
        }

        if (rarityReq.Values.Sum() > k || careerExact.Values.Sum() > k)
            return false;

        if (MinCareerSlotsRequired(careerExact, careerRange) > k)
            return false;

        if (!PoolHasCapacity(pool, k, rarityReq, careerExact, careerRange, staffSubsets))
            return false;

        var starCap = new int[7];
        for (var s = 1; s <= 6; s++)
            starCap[s] = rarityReq.TryGetValue(s, out var need) ? need : k;

        var order = Enumerable.Range(0, pool.Count).ToList();
        var picked = new List<Staff>(k);
        var used = new bool[pool.Count];
        var starCnt = new int[7];
        var careerCnt = new Dictionary<Career, int>();
        foreach (Career c in Enum.GetValues<Career>())
            careerCnt[c] = 0;
        var subsetCnt = new int[staffSubsets.Count];

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            Shuffle(order, random);
            picked.Clear();
            Array.Clear(starCnt, 0, starCnt.Length);
            foreach (Career c in Enum.GetValues<Career>())
                careerCnt[c] = 0;
            Array.Clear(subsetCnt, 0, subsetCnt.Length);
            Array.Fill(used, false);

            if (Dfs(pool, order, k, rarityReq, careerExact, careerRange, staffSubsets, starCap, picked, used, starCnt, careerCnt, subsetCnt))
            {
                team = [..picked];
                return true;
            }
        }

        return false;
    }

    private static int MinCareerSlotsRequired(
        Dictionary<Career, int> careerExact,
        Dictionary<Career, (int lo, int hi)> careerRange)
    {
        var sum = 0;
        foreach (Career c in Enum.GetValues<Career>())
        {
            if (careerExact.TryGetValue(c, out var ex))
                sum += ex;
            else if (careerRange.TryGetValue(c, out var rg))
                sum += rg.lo;
        }

        return sum;
    }

    private static int CountInSubset(IReadOnlyList<Staff> pool, HashSet<string> names) =>
        pool.Count(s => names.Contains(s.Name));

    private static int CountOutsideSubset(IReadOnlyList<Staff> pool, HashSet<string> names) =>
        pool.Count(s => !names.Contains(s.Name));

    private static bool PoolHasCapacity(
        IReadOnlyList<Staff> pool,
        int k,
        Dictionary<int, int> rarityReq,
        Dictionary<Career, int> careerExact,
        Dictionary<Career, (int lo, int hi)> careerRange,
        IReadOnlyList<StaffSubsetConstraint> staffSubsets)
    {
        foreach (var kv in rarityReq)
        {
            if (pool.Count(s => s.Star == kv.Key) < kv.Value)
                return false;
        }

        foreach (var kv in careerExact)
        {
            if (pool.Count(s => s.Career == kv.Key) < kv.Value)
                return false;
        }

        foreach (var kv in careerRange)
        {
            if (careerExact.ContainsKey(kv.Key))
                continue;
            if (pool.Count(s => s.Career == kv.Key) < kv.Value.lo)
                return false;
        }

        foreach (var c in staffSubsets)
        {
            var inS = CountInSubset(pool, c.Names);
            var outS = CountOutsideSubset(pool, c.Names);
            if (c.IsExact)
            {
                var n = c.ExactOrLo;
                if (n > inS || n > k)
                    return false;
                if (n == 0 && outS < k)
                    return false;
            }
            else if (c.ExactOrLo > inS)
            {
                return false;
            }
        }

        return true;
    }

    private static int MaxAdditionalFromSubset(IReadOnlyList<Staff> pool, bool[] used, HashSet<string> names)
    {
        var t = 0;
        for (var i = 0; i < pool.Count; i++)
        {
            if (!used[i] && names.Contains(pool[i].Name))
                t++;
        }

        return t;
    }

    private static bool Dfs(
        IReadOnlyList<Staff> pool,
        List<int> order,
        int k,
        Dictionary<int, int> rarityReq,
        Dictionary<Career, int> careerExact,
        Dictionary<Career, (int lo, int hi)> careerRange,
        IReadOnlyList<StaffSubsetConstraint> staffSubsets,
        int[] starCap,
        List<Staff> picked,
        bool[] used,
        int[] starCnt,
        Dictionary<Career, int> careerCnt,
        int[] subsetCnt)
    {
        if (picked.Count == k)
            return FinalOk(starCnt, careerCnt, k, rarityReq, careerExact, careerRange, staffSubsets, subsetCnt);

        var remaining = k - picked.Count;
        if (!PartialOk(pool, used, starCnt, careerCnt, remaining, subsetCnt, rarityReq, careerExact, careerRange, staffSubsets))
            return false;

        foreach (var idx in order)
        {
            if (used[idx])
                continue;

            var s = pool[idx];
            var ns = starCnt[s.Star] + 1;
            if (ns > starCap[s.Star])
                continue;

            var nc = careerCnt[s.Career] + 1;
            if (careerExact.TryGetValue(s.Career, out var cneed))
            {
                if (nc > cneed)
                    continue;
            }
            else if (careerRange.TryGetValue(s.Career, out var rg) && nc > rg.hi)
            {
                continue;
            }

            var skipStaff = false;
            for (var i = 0; i < staffSubsets.Count; i++)
            {
                var c = staffSubsets[i];
                var ni = subsetCnt[i] + (c.Names.Contains(s.Name) ? 1 : 0);
                var maxAllowed = c.IsExact ? c.ExactOrLo : c.Hi;
                if (ni > maxAllowed)
                {
                    skipStaff = true;
                    break;
                }
            }

            if (skipStaff)
                continue;

            used[idx] = true;
            picked.Add(s);
            starCnt[s.Star]++;
            careerCnt[s.Career]++;
            for (var i = 0; i < staffSubsets.Count; i++)
            {
                if (staffSubsets[i].Names.Contains(s.Name))
                    subsetCnt[i]++;
            }

            if (Dfs(pool, order, k, rarityReq, careerExact, careerRange, staffSubsets, starCap, picked, used, starCnt, careerCnt, subsetCnt))
                return true;

            for (var i = 0; i < staffSubsets.Count; i++)
            {
                if (staffSubsets[i].Names.Contains(s.Name))
                    subsetCnt[i]--;
            }

            careerCnt[s.Career]--;
            starCnt[s.Star]--;
            picked.RemoveAt(picked.Count - 1);
            used[idx] = false;
        }

        return false;
    }

    private static bool PartialOk(
        IReadOnlyList<Staff> pool,
        bool[] used,
        int[] starCnt,
        Dictionary<Career, int> careerCnt,
        int remaining,
        int[] subsetCnt,
        Dictionary<int, int> rarityReq,
        Dictionary<Career, int> careerExact,
        Dictionary<Career, (int lo, int hi)> careerRange,
        IReadOnlyList<StaffSubsetConstraint> staffSubsets)
    {
        foreach (var kv in rarityReq)
        {
            var have = starCnt[kv.Key];
            if (have > kv.Value || kv.Value - have > remaining)
                return false;
        }

        foreach (var kv in careerExact)
        {
            var have = careerCnt[kv.Key];
            if (have > kv.Value || kv.Value - have > remaining)
                return false;
        }

        foreach (var kv in careerRange)
        {
            if (careerExact.ContainsKey(kv.Key))
                continue;
            var have = careerCnt[kv.Key];
            if (have > kv.Value.hi || have + remaining < kv.Value.lo)
                return false;
        }

        for (var i = 0; i < staffSubsets.Count; i++)
        {
            var c = staffSubsets[i];
            var have = subsetCnt[i];
            var maxAllowed = c.IsExact ? c.ExactOrLo : c.Hi;
            if (have > maxAllowed)
                return false;
            var minReq = c.ExactOrLo;
            var maxAdd = MaxAdditionalFromSubset(pool, used, c.Names);
            if (have + maxAdd < minReq)
                return false;
        }

        return true;
    }

    private static bool FinalOk(
        int[] starCnt,
        Dictionary<Career, int> careerCnt,
        int k,
        Dictionary<int, int> rarityReq,
        Dictionary<Career, int> careerExact,
        Dictionary<Career, (int lo, int hi)> careerRange,
        IReadOnlyList<StaffSubsetConstraint> staffSubsets,
        int[] subsetCnt)
    {
        var sum = 0;
        for (var i = 1; i <= 6; i++)
            sum += starCnt[i];
        if (sum != k)
            return false;

        foreach (var kv in rarityReq)
        {
            if (starCnt[kv.Key] != kv.Value)
                return false;
        }

        foreach (var kv in careerExact)
        {
            if (careerCnt[kv.Key] != kv.Value)
                return false;
        }

        foreach (var kv in careerRange)
        {
            var have = careerCnt[kv.Key];
            if (have < kv.Value.lo || have > kv.Value.hi)
                return false;
        }

        for (var i = 0; i < staffSubsets.Count; i++)
        {
            var c = staffSubsets[i];
            var h = subsetCnt[i];
            if (c.IsExact)
            {
                if (h != c.ExactOrLo)
                    return false;
            }
            else if (h < c.ExactOrLo || h > c.Hi)
            {
                return false;
            }
        }

        return true;
    }

    private static void Shuffle(List<int> order, Random rng)
    {
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
    }

    public static void MergeRules(
        RandomStrategyDefinition def,
        out Dictionary<int, int> rarityReq,
        out Dictionary<Career, int> careerExact,
        out Dictionary<Career, (int lo, int hi)> careerRange,
        out List<StaffSubsetConstraint> staffSubsets)
    {
        rarityReq = new Dictionary<int, int>();
        careerExact = new Dictionary<Career, int>();
        careerRange = new Dictionary<Career, (int lo, int hi)>();
        staffSubsets = [];

        if (def.Rules.Count == 0)
            return;

        foreach (var r in def.Rules)
        {
            if (r.Kind == StrategyRuleKind.Rarity && r.Star is >= 1 and <= 6 && r.Count > 0)
            {
                rarityReq.TryGetValue(r.Star, out var prev);
                rarityReq[r.Star] = prev + r.Count;
            }
            else if (r.Kind == StrategyRuleKind.Career && r.Count > 0)
            {
                careerExact.TryGetValue(r.Career, out var prev);
                careerExact[r.Career] = prev + r.Count;
            }
            else if (r.Kind == StrategyRuleKind.CareerRange)
            {
                var lo = r.Count;
                var hi = r.CountMax;
                if (lo > hi || lo < 0)
                    continue;
                if (!careerRange.TryGetValue(r.Career, out var prev))
                    careerRange[r.Career] = (lo, hi);
                else
                {
                    var nl = Math.Max(prev.lo, lo);
                    var nh = Math.Min(prev.hi, hi);
                    careerRange[r.Career] = nl > nh ? (1, 0) : (nl, nh);
                }
            }
            else if (r.Kind == StrategyRuleKind.StaffSubsetExact)
            {
                var names = NormalizeStaffNames(r.StaffNames);
                if (names.Count == 0 || r.Count < 0)
                    continue;
                staffSubsets.Add(new StaffSubsetConstraint
                {
                    Names = names,
                    IsExact = true,
                    ExactOrLo = r.Count,
                    Hi = 0
                });
            }
            else if (r.Kind == StrategyRuleKind.StaffSubsetRange)
            {
                var names = NormalizeStaffNames(r.StaffNames);
                var lo = r.Count;
                var hi = r.CountMax;
                if (names.Count == 0 || lo > hi || lo < 0)
                    continue;
                staffSubsets.Add(new StaffSubsetConstraint
                {
                    Names = names,
                    IsExact = false,
                    ExactOrLo = lo,
                    Hi = hi
                });
            }
        }
    }

    private static HashSet<string> NormalizeStaffNames(List<string>? raw)
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
}
