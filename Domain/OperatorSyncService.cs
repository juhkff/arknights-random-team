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

    /// <summary>
    /// 进程内共享的 HTTP 客户端。
    /// 原来每次同步都 <c>new OperatorSyncService()</c>、各自建一份 HttpClient：
    /// 每次都要重建连接池，反复同步（或两个入口先后触发）时会持续 churn。
    /// 服务本身不持有可释放资源，共享客户端不会互相干扰。
    /// </summary>
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<Uri> _sources;
    private readonly Action<Staff, string, string> _renameStaff;

    public OperatorSyncService(
        HttpClient? httpClient = null,
        IReadOnlyList<Uri>? sources = null,
        Action<Staff, string, string>? renameStaff = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _sources = sources ?? DefaultSources;
        _renameStaff = renameStaff ?? AssignName;
    }

    private static void AssignName(Staff staff, string _, string name) => staff.Name = name;

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

                // 合并是一次批量写入：包进更新事务，让列表在结束后统一刷新一次，
                // 而不是每新增一名干员就重算一遍筛选与排序。
                using var bulkUpdate = AppState.BeginBulkUpdate();
                var result = Merge(localStaff, remoteOperators);
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

    private OperatorSyncResult Merge(
        ObservableCollection<Staff> localStaff,
        IReadOnlyList<RemoteOperator> remoteOperators)
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
            var previousSourceId = local.SourceId;
            var synchronizedName = byName.TryGetValue(remote.Name, out var nameOwner) &&
                                   !ReferenceEquals(nameOwner, local)
                ? previousName
                : remote.Name;
            var changed = previousSourceId != remote.SourceId ||
                          previousName != synchronizedName ||
                          local.Star != remote.Star ||
                          local.Career != remote.Career;

            if (!string.IsNullOrWhiteSpace(previousSourceId) &&
                previousSourceId != remote.SourceId &&
                bySourceId.TryGetValue(previousSourceId, out var sourceOwner) &&
                ReferenceEquals(sourceOwner, local))
            {
                bySourceId.Remove(previousSourceId);
            }

            local.SourceId = remote.SourceId;
            _renameStaff(local, previousName, synchronizedName);
            local.Star = remote.Star;
            local.Career = remote.Career;
            bySourceId[remote.SourceId] = local;
            if (previousName != synchronizedName)
                byName.Remove(previousName);
            byName[synchronizedName] = local;

            if (changed)
                updated++;
            else
                unchanged++;
        }

        return new OperatorSyncResult(added, updated, unchanged);
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
               star is >= FieldLimits.MinStar and <= FieldLimits.MaxStar;
    }
}

public sealed record RemoteOperator(string SourceId, string Name, int Star, Career Career);

public sealed record OperatorSyncResult(int Added, int Updated, int Unchanged);
