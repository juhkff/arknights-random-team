using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using arknights_random_team.Models;

namespace arknights_random_team.Domain;

public sealed class OperatorSyncService
{
    private static readonly Uri[] DefaultSources =
    [
        new("https://raw.githubusercontent.com/Kengxxiao/ArknightsGameData/master/zh_CN/gamedata/excel/character_table.json"),
        new("https://cdn.jsdelivr.net/gh/Kengxxiao/ArknightsGameData@master/zh_CN/gamedata/excel/character_table.json")
    ];

    private static readonly IReadOnlyDictionary<string, Career> ProfessionMap =
        new Dictionary<string, Career>(StringComparer.Ordinal)
        {
            ["PIONEER"] = Career.先锋,
            ["WARRIOR"] = Career.近卫,
            ["SNIPER"] = Career.狙击,
            ["TANK"] = Career.重装,
            ["MEDIC"] = Career.医疗,
            ["SUPPORT"] = Career.辅助,
            ["CASTER"] = Career.术师,
            ["SPECIAL"] = Career.特种
        };

    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<Uri> _sources;

    public OperatorSyncService(HttpClient? httpClient = null, IReadOnlyList<Uri>? sources = null)
    {
        _httpClient = httpClient ?? CreateHttpClient();
        _sources = sources ?? DefaultSources;
    }

    public async Task<OperatorSyncResult> SyncAsync(
        ObservableCollection<Staff> localStaff,
        OperatorSyncSettings settings,
        IReadOnlySet<int> selectedStars,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        foreach (var source in _sources)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, source);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var allRemoteOperators = await ParseOperatorsAsync(stream, cancellationToken);
                var remoteOperators = allRemoteOperators
                    .Where(item => selectedStars.Contains(item.Star))
                    .ToList();
                if (remoteOperators.Count == 0)
                    throw new InvalidDataException("数据源中没有找到符合所选稀有度的正式干员");

                var result = Merge(localStaff, remoteOperators, source.AbsoluteUri);
                settings.LastSuccessfulSync = DateTimeOffset.UtcNow;
                return result;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidDataException)
            {
                errors.Add($"{source.Host}: {ex.Message}");
            }
        }

        throw new InvalidOperationException($"所有数据源均不可用。{string.Join("；", errors)}");
    }

    public static async Task<IReadOnlyList<RemoteOperator>> ParseOperatorsAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("干员数据根节点不是 JSON 对象");

        var operators = new List<RemoteOperator>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!property.Name.StartsWith("char_", StringComparison.Ordinal) ||
                property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var value = property.Value;
            if (value.TryGetProperty("isNotObtainable", out var unavailable) &&
                unavailable.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            if (!TryGetString(value, "name", out var name) ||
                !TryGetString(value, "profession", out var profession) ||
                !ProfessionMap.TryGetValue(profession, out var career) ||
                !TryGetString(value, "rarity", out var rarity) ||
                !TryParseStar(rarity, out var star))
            {
                continue;
            }

            operators.Add(new RemoteOperator(property.Name, name.Trim(), star, career));
        }

        return operators
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .Select(group => group.OrderBy(item => item.SourceId, StringComparer.Ordinal).First())
            .ToList();
    }

    public static OperatorSyncResult Merge(
        ObservableCollection<Staff> localStaff,
        IReadOnlyList<RemoteOperator> remoteOperators,
        string sourceUrl)
    {
        var bySourceId = localStaff
            .Where(item => !string.IsNullOrWhiteSpace(item.SourceId))
            .GroupBy(item => item.SourceId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var byName = localStaff
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var added = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var remote in remoteOperators)
        {
            if (!bySourceId.TryGetValue(remote.SourceId, out var local) &&
                !byName.TryGetValue(remote.Name, out local))
            {
                local = new Staff
                {
                    SourceId = remote.SourceId,
                    Name = remote.Name,
                    Star = remote.Star,
                    Career = remote.Career,
                    Level = Level.GenerateMaxLevel(remote.Star),
                    IsSelected = false
                };
                localStaff.Add(local);
                bySourceId[remote.SourceId] = local;
                byName[remote.Name] = local;
                added++;
                continue;
            }

            var previousName = local.Name;
            var changed = local.SourceId != remote.SourceId ||
                          previousName != remote.Name ||
                          local.Star != remote.Star ||
                          local.Career != remote.Career;
            local.SourceId = remote.SourceId;
            local.Name = remote.Name;
            local.Star = remote.Star;
            local.Career = remote.Career;
            bySourceId[remote.SourceId] = local;
            if (previousName != remote.Name)
                byName.Remove(previousName);
            byName[remote.Name] = local;

            if (changed)
                updated++;
            else
                unchanged++;
        }

        return new OperatorSyncResult(
            RemoteCount: remoteOperators.Count,
            Added: added,
            Updated: updated,
            Unchanged: unchanged,
            SourceUrl: sourceUrl);
    }

    private static HttpClient CreateHttpClient()
    {
        // 浏览器（WebAssembly）的 BrowserHttpHandler 不支持 AutomaticDecompression——
        // 解压由浏览器自己处理，设置了会直接抛 PlatformNotSupportedException。
        var handler = new HttpClientHandler();
        if (!OperatingSystem.IsBrowser())
            handler.AutomaticDecompression = DecompressionMethods.All;

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("arknights-random-team/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static bool TryGetString(JsonElement value, string propertyName, out string result)
    {
        result = "";
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        result = property.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(result);
    }

    private static bool TryParseStar(string rarity, out int star)
    {
        const string prefix = "TIER_";
        star = 0;
        return rarity.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(rarity.AsSpan(prefix.Length), out star) &&
               star is >= 1 and <= 6;
    }
}

public sealed record RemoteOperator(string SourceId, string Name, int Star, Career Career);

public sealed record OperatorSyncResult(
    int RemoteCount,
    int Added,
    int Updated,
    int Unchanged,
    string SourceUrl);
