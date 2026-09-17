namespace arknights_random_team.Domain;

/// <summary>
/// 干员立绘地址。
///
/// 应用本身不自带图片，立绘来自公开的明日方舟资源仓库，靠 <see cref="Models.Staff.SourceId"/>
/// （即游戏数据里的 charId，形如 char_128_plosis）拼出地址。
/// 只有通过「同步干员」录入的干员才有 SourceId，手动录入的没有，卡片上会显示占位。
///
/// 用 jsDelivr 而不是 raw.githubusercontent.com：后者在国内经常不可达，
/// 同步功能本来也是拿 jsDelivr 当备用源的。
///
/// 需要注意的是 jsDelivr 的各镜像表现并不一致，实测（同一时间点）：
///   gcore.jsdelivr.net      200，CDN 命中，约 0.6s   ← 最稳，放在首位
///   cdn.jsdelivr.net        时好时坏，可能数十秒无响应
///   fastly.jsdelivr.net     301 重定向到 raw.githubusercontent，等于没用上 CDN
/// 所以这里给出一组候选地址，由展示层按顺序回退。
/// </summary>
public static class OperatorArt
{
    private const string GameRepo = "gh/yuanyan3060/ArknightsGameResource@master";

    /// <summary>
    /// 持续更新的全身 AVG 图源（Aceship 原仓库已停更，新干员 404）。
    /// </summary>
    private const string FullArtRepo = "gh/PuppiizSunniiz/Arknight-Images@main";

    /// <summary>职业图标所在的资源仓库（与立绘不是同一个仓库）。</summary>
    private const string IconRepo = "gh/Aceship/Arknight-Images@master";

    private static readonly string[] Mirrors =
    [
        "https://gcore.jsdelivr.net",
        "https://cdn.jsdelivr.net",
        "https://testingcf.jsdelivr.net"
    ];

    /// <summary>
    /// 头像小图（180×180），表格与编队名牌用。
    /// 精二优先 <c>_2</c>，没有专属头像时回退默认文件（无后缀）。
    /// </summary>
    public static IReadOnlyList<Uri> Avatar(string? sourceId, bool elite2 = false)
    {
        IReadOnlyList<Uri> game = elite2
            ? [.. Build(GameRepo, "avatar", sourceId, "_2"), .. Build(GameRepo, "avatar", sourceId, "")]
            : Build(GameRepo, "avatar", sourceId, "");
        IReadOnlyList<Uri> mirror = elite2
            ? [.. Build(FullArtRepo, "avatars", sourceId, "_2"), .. Build(FullArtRepo, "avatars", sourceId, "")]
            : Build(FullArtRepo, "avatars", sourceId, "");

        return [.. Primary(game), .. Primary(mirror), .. Rest(game), .. Rest(mirror)];
    }

    /// <summary>
    /// 抽卡半身像（约 180×360）。全身立绘缺失时作为「立绘」模式的回退。
    /// </summary>
    public static IReadOnlyList<Uri> Portrait(string? sourceId, bool elite2 = false) =>
        elite2
            ? [.. Build(GameRepo, "portrait", sourceId, "_2"), .. Build(GameRepo, "portrait", sourceId, "_1")]
            : Build(GameRepo, "portrait", sourceId, "_1");

    /// <summary>
    /// 全身立绘。卡片「立绘」模式优先用这一张。
    /// 精二优先 <c>_2</c>，没有再回退 <c>_1</c>，再没有才用抽卡半身像。
    /// 全身图只先试首选镜像，没有就改用半身像，避免连打多个 404。
    /// </summary>
    public static IReadOnlyList<Uri> Illustration(string? sourceId, bool elite2 = false)
    {
        IReadOnlyList<Uri> full = elite2
            ? [.. Build(FullArtRepo, "characters", sourceId, "_2"),
               .. Build(FullArtRepo, "characters", sourceId, "_1")]
            : Build(FullArtRepo, "characters", sourceId, "_1");

        return
        [
            .. Primary(full),
            .. Portrait(sourceId, elite2),
            .. Rest(full)
        ];
    }

    /// <summary>
    /// 官方职业图标的嵌入资源地址。
    ///
    /// 职业图标只有 8 个、合计约 40KB，而且所有干员共用同一套，
    /// 所以直接随程序打包，不走网络：刷新即用、离线可用，也不受 CDN 可用性影响。
    /// 文件名沿用游戏/社区资源的英文职业名，与 <see cref="Models.Career"/> 一一对应
    /// （先锋=vanguard、近卫=guard、狙击=sniper、重装=defender、
    ///  医疗=medic、辅助=supporter、术师=caster、特种=specialist）。
    /// </summary>
    public static IReadOnlyList<Uri> CareerIcon(string careerSlug)
    {
        if (string.IsNullOrWhiteSpace(careerSlug) ||
            !careerSlug.All(ch => char.IsAsciiLetterOrDigit(ch)))
        {
            return [];
        }

        var embedded = new Uri(
            $"avares://arknights-random-team.Core/Assets/ClassIcons/class_{careerSlug}.png");

        var path = $"{IconRepo}/classes/class_{careerSlug}.png";
        return [embedded, .. Mirrors.Select(m => new Uri($"{m}/{path}"))];
    }

    private static IReadOnlyList<Uri> Build(string repo, string folder, string? sourceId, string suffix)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return [];

        var id = sourceId.Trim();
        if (!id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
            return [];

        var path = $"{repo}/{folder}/{id}{suffix}.png";
        return Mirrors.Select(m => new Uri($"{m}/{path}")).ToArray();
    }

    private static IReadOnlyList<Uri> Primary(IReadOnlyList<Uri> uris)
    {
        if (uris.Count == 0)
            return uris;

        var taken = new List<Uri>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var uri in uris)
        {
            var file = uri.AbsolutePath;
            var slash = file.LastIndexOf('/');
            var name = slash >= 0 ? file[(slash + 1)..] : file;
            if (!seen.Add(name))
                continue;

            taken.Add(uri);
        }

        return taken;
    }

    private static IReadOnlyList<Uri> Rest(IReadOnlyList<Uri> uris)
    {
        var primary = Primary(uris);
        if (primary.Count == 0)
            return [];

        var skip = new HashSet<Uri>(primary);
        return uris.Where(uri => !skip.Contains(uri)).ToArray();
    }
}
