using arknights_random_team.Models;

namespace arknights_random_team.Domain;

/// <summary>
/// 在满足稀有度、职业、指定干员子集人数等约束的前提下，从候选池无放回组队。
/// 先把互斥的职业范围实例化为可行配额；填空位时若仍有下限未凑齐，则只从当前最紧的那一条配额对应的合法干员里均匀抽取
/// （不优待「一人占多项」），下限全部满足后再从剩余合法干员里抽。走入死角才回溯，并用节点预算避免卡死。
/// </summary>
public static class ConstrainedTeamPicker
{
    private const int CareerN = 8;
    private const int MaxRestarts = 32;
    private const int MaxNodesPerRestart = 4_000;

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

        var builder = new Builder(pool, k, rarityReq, staffSubsets, random);
        for (var restart = 0; restart < MaxRestarts; restart++)
        {
            if (!TrySampleCareerTargets(pool, k, careerExact, careerRange, random, out var careerTarget))
                continue;

            builder.Reset(careerTarget);
            if (builder.Search())
            {
                team = [.. builder.Picked];
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
            var inS = pool.Count(s => s.Star == kv.Key);
            if (inS < kv.Value || pool.Count - inS < k - kv.Value)
                return false;
        }

        foreach (var kv in careerExact)
        {
            var inS = pool.Count(s => s.Career == kv.Key);
            if (inS < kv.Value || pool.Count - inS < k - kv.Value)
                return false;
        }

        foreach (var kv in careerRange)
        {
            if (careerExact.ContainsKey(kv.Key))
                continue;
            var inS = pool.Count(s => s.Career == kv.Key);
            if (inS < kv.Value.lo || pool.Count - inS < k - kv.Value.hi)
                return false;
        }

        foreach (var c in staffSubsets)
        {
            var inS = 0;
            var outS = 0;
            foreach (var s in pool)
            {
                if (c.Names.Contains(s.Name))
                    inS++;
                else
                    outS++;
            }

            var maxTake = c.IsExact ? c.ExactOrLo : c.Hi;
            var minTake = c.ExactOrLo;
            if (minTake > inS || maxTake > k || outS < k - maxTake)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 职业互斥，可将范围约束随机实例化为一个仍满足全队人数与池容量的精确配额。
    /// </summary>
    private static bool TrySampleCareerTargets(
        IReadOnlyList<Staff> pool,
        int k,
        Dictionary<Career, int> careerExact,
        Dictionary<Career, (int lo, int hi)> careerRange,
        Random rng,
        out Dictionary<Career, int> careerTarget)
    {
        careerTarget = new Dictionary<Career, int>(careerExact);
        var pending = new List<Career>();
        foreach (var kv in careerRange)
        {
            if (!careerExact.ContainsKey(kv.Key))
                pending.Add(kv.Key);
        }

        if (pending.Count == 0)
            return true;

        var poolByCareer = new int[CareerN];
        foreach (var s in pool)
            poolByCareer[(int)s.Career]++;

        var constrained = new bool[CareerN];
        foreach (var kv in careerExact)
            constrained[(int)kv.Key] = true;
        foreach (var c in pending)
            constrained[(int)c] = true;

        var poolUnconstrained = 0;
        for (var c = 0; c < CareerN; c++)
        {
            if (!constrained[c])
                poolUnconstrained += poolByCareer[c];
        }

        var remain = k - careerExact.Values.Sum();
        Shuffle(pending, rng);

        for (var i = 0; i < pending.Count; i++)
        {
            var c = pending[i];
            var (lo, hi) = careerRange[c];
            var cap = poolByCareer[(int)c];

            var sumLoRest = 0;
            var sumHiRest = 0;
            for (var j = i + 1; j < pending.Count; j++)
            {
                var other = pending[j];
                var rg = careerRange[other];
                sumLoRest += rg.lo;
                sumHiRest += Math.Min(rg.hi, poolByCareer[(int)other]);
            }

            var nMin = Math.Max(lo, remain - sumHiRest - poolUnconstrained);
            var nMax = Math.Min(Math.Min(hi, cap), remain - sumLoRest);
            if (nMin > nMax)
                return false;

            var n = rng.Next(nMin, nMax + 1);
            careerTarget[c] = n;
            remain -= n;
        }

        return remain >= 0 && remain <= poolUnconstrained;
    }

    private static void Shuffle<T>(IList<T> order, Random rng)
    {
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
    }

    private sealed class Builder
    {
        public readonly List<Staff> Picked;

        private readonly IReadOnlyList<Staff> _pool;
        private readonly int _k;
        private readonly int[] _starLo = new int[7];
        private readonly int[] _starHi = new int[7];
        private readonly int[] _starCnt = new int[7];
        private readonly int[] _unusedStar = new int[7];
        private readonly int[] _careerLo = new int[CareerN];
        private readonly int[] _careerHi = new int[CareerN];
        private readonly int[] _careerCnt = new int[CareerN];
        private readonly int[] _unusedCareer = new int[CareerN];
        private readonly int[] _subLo;
        private readonly int[] _subHi;
        private readonly int[] _subCnt;
        private readonly int[] _unusedSubset;
        private readonly bool[,] _inSubset;
        private readonly bool[] _used;
        private readonly List<int> _candBuf;
        private readonly List<int> _legalBuf;
        private readonly List<int> _tightQuotaIds;
        private readonly bool[] _mustSub;
        private readonly int[] _starSupply = new int[7];
        private readonly int[] _careerSupply = new int[CareerN];
        private readonly int[] _subSupply;
        private readonly Random _rng;
        private int _nodes;

        public Builder(
            IReadOnlyList<Staff> pool,
            int k,
            Dictionary<int, int> rarityReq,
            IReadOnlyList<StaffSubsetConstraint> staffSubsets,
            Random rng)
        {
            _pool = pool;
            _k = k;
            _rng = rng;
            Picked = new List<Staff>(k);
            _used = new bool[pool.Count];
            _candBuf = new List<int>(pool.Count);
            _legalBuf = new List<int>(pool.Count);
            _tightQuotaIds = new List<int>(6 + CareerN + staffSubsets.Count);

            for (var s = 1; s <= 6; s++)
            {
                if (rarityReq.TryGetValue(s, out var need))
                {
                    _starLo[s] = need;
                    _starHi[s] = need;
                }
                else
                {
                    _starLo[s] = 0;
                    _starHi[s] = k;
                }
            }

            var subN = staffSubsets.Count;
            _subLo = new int[subN];
            _subHi = new int[subN];
            _subCnt = new int[subN];
            _unusedSubset = new int[subN];
            _mustSub = new bool[subN];
            _subSupply = new int[subN];
            _inSubset = new bool[pool.Count, subN];

            for (var i = 0; i < pool.Count; i++)
            {
                var staff = pool[i];
                for (var u = 0; u < subN; u++)
                {
                    if (staffSubsets[u].Names.Contains(staff.Name))
                    {
                        _inSubset[i, u] = true;
                        _unusedSubset[u]++;
                    }
                }
            }

            for (var u = 0; u < subN; u++)
            {
                var c = staffSubsets[u];
                _subLo[u] = c.ExactOrLo;
                _subHi[u] = c.IsExact ? c.ExactOrLo : c.Hi;
            }
        }

        public void Reset(Dictionary<Career, int> careerTarget)
        {
            Picked.Clear();
            Array.Clear(_used);
            Array.Clear(_starCnt);
            Array.Clear(_careerCnt);
            Array.Clear(_subCnt);
            Array.Clear(_unusedStar);
            Array.Clear(_unusedCareer);
            _nodes = 0;

            for (var c = 0; c < CareerN; c++)
            {
                if (careerTarget.TryGetValue((Career)c, out var n))
                {
                    _careerLo[c] = n;
                    _careerHi[c] = n;
                }
                else
                {
                    _careerLo[c] = 0;
                    _careerHi[c] = _k;
                }
            }

            for (var i = 0; i < _pool.Count; i++)
            {
                var s = _pool[i];
                _unusedStar[s.Star]++;
                _unusedCareer[(int)s.Career]++;
            }

            Array.Clear(_unusedSubset);
            for (var i = 0; i < _pool.Count; i++)
            {
                for (var u = 0; u < _subLo.Length; u++)
                {
                    if (_inSubset[i, u])
                        _unusedSubset[u]++;
                }
            }
        }

        public bool Search()
        {
            if (++_nodes > MaxNodesPerRestart)
                return false;

            if (Picked.Count == _k)
                return true;

            if (!CollectCandidates())
                return false;

            Shuffle(_candBuf, _rng);
            var choices = _candBuf.ToArray();
            foreach (var idx in choices)
            {
                Push(idx);
                if (Search())
                    return true;
                Pop(idx);
                if (_nodes > MaxNodesPerRestart)
                    return false;
            }

            return false;
        }

        private bool CollectCandidates()
        {
            _legalBuf.Clear();
            _candBuf.Clear();
            var remaining = _k - Picked.Count;

            var mustStar = -1;
            for (var s = 1; s <= 6; s++)
            {
                var def = _starLo[s] - _starCnt[s];
                if (def > 0 && def == remaining)
                {
                    if (mustStar >= 0)
                        return false;
                    mustStar = s;
                }
            }

            var mustCareer = -1;
            for (var c = 0; c < CareerN; c++)
            {
                var def = _careerLo[c] - _careerCnt[c];
                if (def > 0 && def == remaining)
                {
                    if (mustCareer >= 0)
                        return false;
                    mustCareer = c;
                }
            }

            var anyMustSub = false;
            for (var u = 0; u < _subLo.Length; u++)
            {
                var def = _subLo[u] - _subCnt[u];
                _mustSub[u] = def > 0 && def == remaining;
                anyMustSub |= _mustSub[u];
            }

            for (var i = 0; i < _pool.Count; i++)
            {
                if (_used[i])
                    continue;

                var staff = _pool[i];
                if (mustStar >= 0 && staff.Star != mustStar)
                    continue;
                if (mustCareer >= 0 && (int)staff.Career != mustCareer)
                    continue;
                if (anyMustSub)
                {
                    var skip = false;
                    for (var u = 0; u < _subLo.Length; u++)
                    {
                        if (_mustSub[u] && !_inSubset[i, u])
                        {
                            skip = true;
                            break;
                        }
                    }

                    if (skip)
                        continue;
                }

                if (!FeasibleAfter(i))
                    continue;

                _legalBuf.Add(i);
            }

            if (_legalBuf.Count == 0)
                return false;

            if (!TryRestrictToTightestOpenQuota())
                return false;

            return _candBuf.Count > 0;
        }

        /// <summary>
        /// 仍有下限时，只从「当前最紧配额」的桶里抽人：桶内均匀随机，不优待跨配额干员。
        /// 某条下限在合法集合中已无人可填，则当前状态无解。
        /// 下限都凑齐后，候选为全部剩余合法干员。
        /// </summary>
        private bool TryRestrictToTightestOpenQuota()
        {
            _tightQuotaIds.Clear();
            var best = int.MaxValue;

            void Consider(int quotaId, int count)
            {
                if (count < best)
                {
                    best = count;
                    _tightQuotaIds.Clear();
                    _tightQuotaIds.Add(quotaId);
                }
                else if (count == best)
                {
                    _tightQuotaIds.Add(quotaId);
                }
            }

            var anyOpen = false;
            for (var s = 1; s <= 6; s++)
            {
                if (_starLo[s] <= _starCnt[s])
                    continue;
                anyOpen = true;
                var n = 0;
                for (var k = 0; k < _legalBuf.Count; k++)
                {
                    if (_pool[_legalBuf[k]].Star == s)
                        n++;
                }

                if (n == 0)
                    return false;
                Consider(s, n);
            }

            for (var c = 0; c < CareerN; c++)
            {
                if (_careerLo[c] <= _careerCnt[c])
                    continue;
                anyOpen = true;
                var n = 0;
                for (var k = 0; k < _legalBuf.Count; k++)
                {
                    if ((int)_pool[_legalBuf[k]].Career == c)
                        n++;
                }

                if (n == 0)
                    return false;
                Consider(10 + c, n);
            }

            for (var u = 0; u < _subLo.Length; u++)
            {
                if (_subLo[u] <= _subCnt[u])
                    continue;
                anyOpen = true;
                var n = 0;
                for (var k = 0; k < _legalBuf.Count; k++)
                {
                    if (_inSubset[_legalBuf[k], u])
                        n++;
                }

                if (n == 0)
                    return false;
                Consider(20 + u, n);
            }

            if (!anyOpen)
            {
                _candBuf.AddRange(_legalBuf);
                return true;
            }

            var chosen = _tightQuotaIds[_rng.Next(_tightQuotaIds.Count)];
            foreach (var i in _legalBuf)
            {
                if (MatchesQuota(i, chosen))
                    _candBuf.Add(i);
            }

            return _candBuf.Count > 0;
        }

        private bool MatchesQuota(int idx, int quotaId)
        {
            if (quotaId < 10)
                return _pool[idx].Star == quotaId;
            if (quotaId < 20)
                return (int)_pool[idx].Career == quotaId - 10;
            return _inSubset[idx, quotaId - 20];
        }

        private bool FeasibleAfter(int idx)
        {
            var staff = _pool[idx];
            var star = staff.Star;
            var career = (int)staff.Career;
            var remaining = _k - Picked.Count - 1;

            if (_starCnt[star] + 1 > _starHi[star] || _careerCnt[career] + 1 > _careerHi[career])
                return false;

            for (var u = 0; u < _subLo.Length; u++)
            {
                if (_inSubset[idx, u] && _subCnt[u] + 1 > _subHi[u])
                    return false;
            }

            var starDefSum = 0;
            for (var s = 1; s <= 6; s++)
            {
                var have = _starCnt[s] + (s == star ? 1 : 0);
                var unused = _unusedStar[s] - (s == star ? 1 : 0);
                var def = _starLo[s] - have;
                if (have > _starHi[s] || def > remaining || def > unused)
                    return false;
                if (def > 0)
                    starDefSum += def;
            }

            if (starDefSum > remaining)
                return false;

            var careerDefSum = 0;
            for (var c = 0; c < CareerN; c++)
            {
                var have = _careerCnt[c] + (c == career ? 1 : 0);
                var unused = _unusedCareer[c] - (c == career ? 1 : 0);
                var def = _careerLo[c] - have;
                if (have > _careerHi[c] || def > remaining || def > unused)
                    return false;
                if (def > 0)
                    careerDefSum += def;
            }

            if (careerDefSum > remaining)
                return false;

            for (var u = 0; u < _subLo.Length; u++)
            {
                var hit = _inSubset[idx, u];
                var have = _subCnt[u] + (hit ? 1 : 0);
                var unused = _unusedSubset[u] - (hit ? 1 : 0);
                var def = _subLo[u] - have;
                if (have > _subHi[u] || def > remaining || def > unused)
                    return false;
            }

            return RemainingSupplyOk(idx, star, career);
        }

        /// <summary>
        /// 简单的 unused 计数会把「已被其他上限卡住」的干员算进供给。
        /// 这里按加入当前人选后仍能再选的干员重算各配额剩余供给。
        /// </summary>
        private bool RemainingSupplyOk(int addedIdx, int addedStar, int addedCareer)
        {
            Array.Clear(_starSupply);
            Array.Clear(_careerSupply);
            if (_subSupply.Length > 0)
                Array.Clear(_subSupply);

            for (var i = 0; i < _pool.Count; i++)
            {
                if (!IsStillEligible(i, addedIdx, addedStar, addedCareer))
                    continue;

                var staff = _pool[i];
                _starSupply[staff.Star]++;
                _careerSupply[(int)staff.Career]++;
                for (var u = 0; u < _subLo.Length; u++)
                {
                    if (_inSubset[i, u])
                        _subSupply[u]++;
                }
            }

            for (var s = 1; s <= 6; s++)
            {
                var have = _starCnt[s] + (s == addedStar ? 1 : 0);
                if (_starLo[s] - have > _starSupply[s])
                    return false;
            }

            for (var c = 0; c < CareerN; c++)
            {
                var have = _careerCnt[c] + (c == addedCareer ? 1 : 0);
                if (_careerLo[c] - have > _careerSupply[c])
                    return false;
            }

            for (var u = 0; u < _subLo.Length; u++)
            {
                var have = _subCnt[u] + (_inSubset[addedIdx, u] ? 1 : 0);
                if (_subLo[u] - have > _subSupply[u])
                    return false;
            }

            return true;
        }

        private bool IsStillEligible(int i, int addedIdx, int addedStar, int addedCareer)
        {
            if (_used[i] || i == addedIdx)
                return false;

            var staff = _pool[i];
            var starHave = _starCnt[staff.Star] + (staff.Star == addedStar ? 1 : 0);
            if (starHave >= _starHi[staff.Star])
                return false;

            var career = (int)staff.Career;
            var careerHave = _careerCnt[career] + (career == addedCareer ? 1 : 0);
            if (careerHave >= _careerHi[career])
                return false;

            for (var u = 0; u < _subLo.Length; u++)
            {
                if (!_inSubset[i, u])
                    continue;
                var have = _subCnt[u] + (_inSubset[addedIdx, u] ? 1 : 0);
                if (have >= _subHi[u])
                    return false;
            }

            return true;
        }

        private void Push(int idx)
        {
            var staff = _pool[idx];
            _used[idx] = true;
            Picked.Add(staff);
            _starCnt[staff.Star]++;
            _careerCnt[(int)staff.Career]++;
            _unusedStar[staff.Star]--;
            _unusedCareer[(int)staff.Career]--;
            for (var u = 0; u < _subLo.Length; u++)
            {
                if (!_inSubset[idx, u])
                    continue;
                _subCnt[u]++;
                _unusedSubset[u]--;
            }
        }

        private void Pop(int idx)
        {
            var staff = _pool[idx];
            for (var u = 0; u < _subLo.Length; u++)
            {
                if (!_inSubset[idx, u])
                    continue;
                _subCnt[u]--;
                _unusedSubset[u]++;
            }

            _unusedCareer[(int)staff.Career]++;
            _unusedStar[staff.Star]++;
            _careerCnt[(int)staff.Career]--;
            _starCnt[staff.Star]--;
            Picked.RemoveAt(Picked.Count - 1);
            _used[idx] = false;
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
