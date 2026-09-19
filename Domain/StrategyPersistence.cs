using System.Collections.ObjectModel;
using System.Text.Json;
using arknights_random_team.Models;

namespace arknights_random_team.Domain;

internal static class StrategyPersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Loads a complete snapshot; malformed structure leaves the current target unchanged.</summary>
    public static bool Load(string path, ObservableCollection<RandomStrategyDefinition> target)
    {
        var json = File.ReadAllText(path);
        var source = JsonSerializer.Deserialize<List<StrategyPersistenceDto?>>(json, JsonOptions);
        if (source is null)
            return false;

        var loaded = new List<RandomStrategyDefinition>(source.Count);
        foreach (var dto in source)
        {
            if (dto is null || dto.Rules?.Any(rule => rule is null) == true)
                return false;

            var definition = new RandomStrategyDefinition
            {
                Id = string.IsNullOrEmpty(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                Name = dto.Name ?? ""
            };
            foreach (var ruleDto in dto.Rules ?? [])
            {
                var rule = FromDto(ruleDto!);
                if (rule is null)
                    return false;
                definition.Rules.Add(rule);
            }

            if (!StrategyRules.TryValidate(definition.Rules, out _))
                return false;
            loaded.Add(definition);
        }

        target.Clear();
        foreach (var definition in loaded)
            target.Add(definition);
        return true;
    }

    public static string Serialize(IEnumerable<RandomStrategyDefinition> strategies)
    {
        var snapshot = strategies.ToList();
        foreach (var definition in snapshot)
        {
            if (definition is null)
                throw new InvalidDataException("策略列表中包含空项。");
            if (!StrategyRules.TryValidate(definition.Rules, out var error))
                throw new InvalidDataException(error);
        }

        return JsonSerializer.Serialize(snapshot.Select(ToDto).ToList(), JsonOptions);
    }

    private static StrategyPersistenceDto ToDto(RandomStrategyDefinition d) =>
        new()
        {
            Id = d.Id,
            Name = d.Name,
            Rules = d.Rules.Select(r => new StrategyRuleDto
            {
                Kind = r.Kind switch
                {
                    StrategyRuleKind.Rarity => "Rarity",
                    StrategyRuleKind.CareerRange => "CareerRange",
                    StrategyRuleKind.Career => "Career",
                    StrategyRuleKind.StaffSubsetExact => "StaffSubsetExact",
                    StrategyRuleKind.StaffSubsetRange => "StaffSubsetRange",
                    _ => throw new InvalidDataException("策略中包含未知规则类型。")
                },
                Star = r.Star,
                Career = r.Kind is StrategyRuleKind.Career or StrategyRuleKind.CareerRange
                    ? r.Career.ToString()
                    : null,
                Count = r.Count,
                CountMax = r.Kind is StrategyRuleKind.CareerRange or StrategyRuleKind.StaffSubsetRange
                    ? r.CountMax
                    : 0,
                StaffNames = r.Kind is StrategyRuleKind.StaffSubsetExact or StrategyRuleKind.StaffSubsetRange
                    ? [..r.StaffNames]
                    : null
            }).ToList()
        };

    private static StrategyRule? FromDto(StrategyRuleDto r)
    {
        var max = AppOptions.MaxTeamSize;
        if (r.Count < 0 || r.Count > max || r.CountMax < 0 || r.CountMax > max)
        {
            return null;
        }

        if (string.Equals(r.Kind, "CareerRange", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse(r.Career, out Career career) || !Enum.IsDefined(career))
                return null;
            var lo = r.Count;
            var hi = r.CountMax;
            if (lo > hi || lo < 0)
                return null;
            return new StrategyRule
            {
                Kind = StrategyRuleKind.CareerRange,
                Career = career,
                Count = lo,
                CountMax = hi
            };
        }

        if (string.Equals(r.Kind, "StaffSubsetExact", StringComparison.OrdinalIgnoreCase))
        {
            var names = NormalizeStaffNamesDto(r.StaffNames);
            if (names.Count == 0 || r.Count < 0)
                return null;
            return new StrategyRule
            {
                Kind = StrategyRuleKind.StaffSubsetExact,
                StaffNames = names,
                Count = r.Count
            };
        }

        if (string.Equals(r.Kind, "StaffSubsetRange", StringComparison.OrdinalIgnoreCase))
        {
            var names = NormalizeStaffNamesDto(r.StaffNames);
            var lo = r.Count;
            var hi = r.CountMax;
            if (names.Count == 0 || lo > hi || lo < 0)
                return null;
            return new StrategyRule
            {
                Kind = StrategyRuleKind.StaffSubsetRange,
                StaffNames = names,
                Count = lo,
                CountMax = hi
            };
        }

        if (r.Count <= 0)
            return null;

        if (string.Equals(r.Kind, "Rarity", StringComparison.OrdinalIgnoreCase))
        {
            if (r.Star is < FieldLimits.MinStar or > FieldLimits.MaxStar)
                return null;
            return new StrategyRule { Kind = StrategyRuleKind.Rarity, Star = r.Star, Count = r.Count };
        }

        if (string.Equals(r.Kind, "Career", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse(r.Career, out Career career) || !Enum.IsDefined(career))
                return null;
            return new StrategyRule { Kind = StrategyRuleKind.Career, Career = career, Count = r.Count };
        }

        return null;
    }

    private static List<string> NormalizeStaffNamesDto(List<string?>? raw)
    {
        var list = new List<string>();
        if (raw == null)
            return list;
        foreach (var n in raw)
        {
            if (string.IsNullOrWhiteSpace(n))
                continue;
            var t = n.Trim();
            if (!list.Contains(t))
                list.Add(t);
        }

        return list;
    }
}
