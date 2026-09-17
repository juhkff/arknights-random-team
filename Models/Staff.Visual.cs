using Avalonia.Media;

namespace arknights_random_team.Models;

/// <summary>
/// 干员的展示派生属性（职业徽记与配色、稀有度符号）。
/// 只做呈现层映射，不参与数据存储，因此与 <see cref="Staff"/> 的可序列化字段分开维护。
/// </summary>
public partial class Staff
{
    private static readonly IBrush CareerVanguard = BrushOf("#5FD0E8");
    private static readonly IBrush CareerGuard = BrushOf("#E8734A");
    private static readonly IBrush CareerSniper = BrushOf("#E8C15F");
    private static readonly IBrush CareerDefender = BrushOf("#C08CE0");
    private static readonly IBrush CareerMedic = BrushOf("#65E6A8");
    private static readonly IBrush CareerSupporter = BrushOf("#7FA8F0");
    private static readonly IBrush CareerCaster = BrushOf("#E06E9C");
    private static readonly IBrush CareerSpecialist = BrushOf("#9BB0C4");
    private static readonly IBrush CareerUnknown = BrushOf("#7C93A8");

    private static readonly IBrush CareerVanguardSoft = BrushOf("#335FD0E8");
    private static readonly IBrush CareerGuardSoft = BrushOf("#33E8734A");
    private static readonly IBrush CareerSniperSoft = BrushOf("#33E8C15F");
    private static readonly IBrush CareerDefenderSoft = BrushOf("#33C08CE0");
    private static readonly IBrush CareerMedicSoft = BrushOf("#3365E6A8");
    private static readonly IBrush CareerSupporterSoft = BrushOf("#337FA8F0");
    private static readonly IBrush CareerCasterSoft = BrushOf("#33E06E9C");
    private static readonly IBrush CareerSpecialistSoft = BrushOf("#339BB0C4");
    private static readonly IBrush CareerUnknownSoft = BrushOf("#337C93A8");

    private static readonly IBrush Rarity6 = BrushOf("#FFB45A");
    private static readonly IBrush Rarity5 = BrushOf("#F3C969");
    private static readonly IBrush Rarity4 = BrushOf("#C48AE8");
    private static readonly IBrush Rarity3 = BrushOf("#5EB0F0");
    private static readonly IBrush Rarity2 = BrushOf("#7DCF8A");
    private static readonly IBrush Rarity1 = BrushOf("#8A9AAB");

    private static readonly IBrush Rarity6Soft = BrushOf("#3D2A12");
    private static readonly IBrush Rarity5Soft = BrushOf("#3A2F17");
    private static readonly IBrush Rarity4Soft = BrushOf("#2E1F3A");
    private static readonly IBrush Rarity3Soft = BrushOf("#163044");
    private static readonly IBrush Rarity2Soft = BrushOf("#163226");
    private static readonly IBrush Rarity1Soft = BrushOf("#1C2730");

    /// <summary>职业配色：用于结果卡片的左侧导轨与徽章。</summary>
    public IBrush CareerBrush => Career switch
    {
        Career.先锋 => CareerVanguard,
        Career.近卫 => CareerGuard,
        Career.狙击 => CareerSniper,
        Career.重装 => CareerDefender,
        Career.医疗 => CareerMedic,
        Career.辅助 => CareerSupporter,
        Career.术师 => CareerCaster,
        Career.特种 => CareerSpecialist,
        _ => CareerUnknown
    };

    /// <summary>职业色的低透明度底，用于头像框、名牌等衬底。</summary>
    public IBrush CareerSoftBrush => Career switch
    {
        Career.先锋 => CareerVanguardSoft,
        Career.近卫 => CareerGuardSoft,
        Career.狙击 => CareerSniperSoft,
        Career.重装 => CareerDefenderSoft,
        Career.医疗 => CareerMedicSoft,
        Career.辅助 => CareerSupporterSoft,
        Career.术师 => CareerCasterSoft,
        Career.特种 => CareerSpecialistSoft,
        _ => CareerUnknownSoft
    };

    /// <summary>稀有度主色，对齐游戏干员卡框：6 金、5 黄、4 紫、3 蓝、2 绿、1 灰。</summary>
    public IBrush RarityBrush => Star switch
    {
        6 => Rarity6,
        5 => Rarity5,
        4 => Rarity4,
        3 => Rarity3,
        2 => Rarity2,
        _ => Rarity1
    };

    /// <summary>稀有度软底，用于名牌与表格徽标。</summary>
    public IBrush RaritySoftBrush => Star switch
    {
        6 => Rarity6Soft,
        5 => Rarity5Soft,
        4 => Rarity4Soft,
        3 => Rarity3Soft,
        2 => Rarity2Soft,
        _ => Rarity1Soft
    };

    /// <summary>稀有度符号，例如 6 星显示为 ★★★★★★。</summary>
    public string StarGlyphs => new string('★', Math.Clamp(Star, 0, 6));

    /// <summary>精英阶段与等级的数字读数，例如 2-80。</summary>
    public string LevelDigits => $"{Level.EliteLevel}-{Level.Rank:00}";

    /// <summary>精英阶段短标签，用于卡片角标。</summary>
    public string EliteLabel => Level.EliteLevel switch
    {
        2 => "精二",
        1 => "精一",
        _ => "精零"
    };

    /// <summary>是否编入随机池的短状态。</summary>
    public string RosterStatus => IsSelected ? "编入" : "待命";

    // ---- 卡片视图用的立绘 ----

    private bool _usePortrait;
    private IReadOnlyList<Uri>? _avatarUris;
    private bool _avatarUrisElite2;
    private IReadOnlyList<Uri>? _portraitUris;
    private bool _portraitUrisElite2;

    /// <summary>精英二才换精二立绘；与卡片「头像 / 半身像」裁切无关。</summary>
    private bool UseElite2Art => Level.EliteLevel >= 2;

    /// <summary>
    /// 卡片是否按半身像区域显示。只改裁切，不换图：
    /// 图源始终是 <see cref="PortraitUris"/>，精二与否看精英等级。
    /// </summary>
    public bool UsePortrait
    {
        get => _usePortrait;
        set => SetProperty(ref _usePortrait, value);
    }

    /// <summary>
    /// 头像小图（表格、编队名牌）。精二优先 <c>_2</c>。
    /// 按精英阶段缓存：编译绑定靠对象引用判断变化，每次新建 List 会反复下载。
    /// </summary>
    public IReadOnlyList<Uri> AvatarUris
    {
        get
        {
            var elite2 = UseElite2Art;
            if (_avatarUris is { } cached && _avatarUrisElite2 == elite2)
                return cached;

            _avatarUris = Domain.OperatorArt.Avatar(SourceId, elite2);
            _avatarUrisElite2 = elite2;
            return _avatarUris;
        }
    }

    /// <summary>
    /// 半身立绘（180×360）。精二用 <c>_2</c>，否则 <c>_1</c>；取不到再回退。
    /// 卡片头像模式裁切上半，半身像模式铺满，共用这一组地址。
    /// </summary>
    public IReadOnlyList<Uri> PortraitUris
    {
        get
        {
            var elite2 = UseElite2Art;
            if (_portraitUris is { } cached && _portraitUrisElite2 == elite2)
                return cached;

            _portraitUris = Domain.OperatorArt.Portrait(SourceId, elite2);
            _portraitUrisElite2 = elite2;
            return _portraitUris;
        }
    }

    /// <summary>卡片图源：始终用半身立绘，头像/半身像只改显示区域。</summary>
    public IReadOnlyList<Uri> DisplayArtUris => PortraitUris;

    /// <summary>头像小图，给表格等需要方图的地方预载。</summary>
    public IReadOnlyList<Uri> AlternateArtUris => AvatarUris;

    /// <summary>清掉已缓存的候选列表（SourceId 变更后地址会变）。</summary>
    private void InvalidateArtCache()
    {
        _avatarUris = null;
        _portraitUris = null;
    }

    /// <summary>通知界面重新取图源（SourceId 或精英阶段变化时用）。</summary>
    public void RaiseArtChanged()
    {
        OnPropertyChanged(nameof(DisplayArtUris));
        OnPropertyChanged(nameof(AlternateArtUris));
        OnPropertyChanged(nameof(HasArt));
        OnPropertyChanged(nameof(AvatarUris));
        OnPropertyChanged(nameof(PortraitUris));
    }

    /// <summary>是否有立绘可显示。</summary>
    public bool HasArt => DisplayArtUris.Count > 0;

    /// <summary>职业名，用于卡片与表格上的文字标识。</summary>
    public string CareerName => Career switch
    {
        Career.先锋 => "先锋",
        Career.近卫 => "近卫",
        Career.狙击 => "狙击",
        Career.重装 => "重装",
        Career.医疗 => "医疗",
        Career.辅助 => "辅助",
        Career.术师 => "术师",
        Career.特种 => "特种",
        _ => "未知"
    };

    /// <summary>官方职业图标的资源名（与游戏内英文职业名对应）。</summary>
    private string CareerSlug => Career switch
    {
        Career.先锋 => "vanguard",
        Career.近卫 => "guard",
        Career.狙击 => "sniper",
        Career.重装 => "defender",
        Career.医疗 => "medic",
        Career.辅助 => "supporter",
        Career.术师 => "caster",
        Career.特种 => "specialist",
        _ => ""
    };

    /// <summary>官方职业图标地址（多个镜像，按顺序回退）。所有干员按职业共用同一套图标。</summary>
    public IReadOnlyList<Uri> CareerIconUris => Domain.OperatorArt.CareerIcon(CareerSlug);

    private static IBrush BrushOf(string hex) => new SolidColorBrush(Color.Parse(hex));
}
