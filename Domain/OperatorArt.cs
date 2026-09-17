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
    private const string Repo = "gh/yuanyan3060/ArknightsGameResource@master";

    /// <summary>职业图标所在的资源仓库（与立绘不是同一个仓库）。</summary>
    private const string IconRepo = "gh/Aceship/Arknight-Images@master";

    private static readonly string[] Mirrors =
    [
        "https://gcore.jsdelivr.net",
        "https://cdn.jsdelivr.net",
        "https://testingcf.jsdelivr.net"
    ];

    /// <summary>头像小图（约 50KB，180×180），列表卡片默认用它。</summary>
    public static IReadOnlyList<Uri> Avatar(string? sourceId) => Build("avatar", sourceId, "");

    /// <summary>半身立绘大图，用于切换后的展示。</summary>
    public static IReadOnlyList<Uri> Portrait(string? sourceId) => Build("portrait", sourceId, "_1");

    /// <summary>
    /// 官方职业图标。文件名沿用游戏/社区资源的英文职业名，
    /// 与 <see cref="Models.Career"/> 一一对应（先锋=vanguard、近卫=guard、狙击=sniper、
    /// 重装=defender、医疗=medic、辅助=supporter、术师=caster、特种=specialist）。
    /// 图标很小（3~8KB），不依赖干员的 SourceId，所有干员共用。
    /// </summary>
    public static IReadOnlyList<Uri> CareerIcon(string careerSlug)
    {
        if (string.IsNullOrWhiteSpace(careerSlug) ||
            !careerSlug.All(ch => char.IsAsciiLetterOrDigit(ch)))
        {
            return [];
        }

        var path = $"{IconRepo}/classes/class_{careerSlug}.png";
        return Mirrors.Select(m => new Uri($"{m}/{path}")).ToArray();
    }

    private static IReadOnlyList<Uri> Build(string folder, string? sourceId, string suffix)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return [];

        // charId 来自外部数据，只允许字母数字下划线，避免拼出意外路径
        var id = sourceId.Trim();
        if (!id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
            return [];

        var path = $"{Repo}/{folder}/{id}{suffix}.png";
        return Mirrors.Select(m => new Uri($"{m}/{path}")).ToArray();
    }
}
