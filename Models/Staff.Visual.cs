using Avalonia.Media;

namespace arknights_random_team.Models;

/// <summary>
/// 干员的展示派生属性（职业徽记与配色、稀有度符号）。
/// 只做呈现层映射，不参与数据存储，因此与 <see cref="Staff"/> 的可序列化字段分开维护。
/// </summary>
public partial class Staff
{
    /// <summary>职业配色：用于结果卡片的左侧导轨与徽章。</summary>
    public IBrush CareerBrush => Career switch
    {
        Career.先锋 => new SolidColorBrush(Color.Parse("#5FD0E8")),
        Career.近卫 => new SolidColorBrush(Color.Parse("#E8734A")),
        Career.狙击 => new SolidColorBrush(Color.Parse("#E8C15F")),
        Career.重装 => new SolidColorBrush(Color.Parse("#C08CE0")),
        Career.医疗 => new SolidColorBrush(Color.Parse("#65E6A8")),
        Career.辅助 => new SolidColorBrush(Color.Parse("#7FA8F0")),
        Career.术师 => new SolidColorBrush(Color.Parse("#E06E9C")),
        Career.特种 => new SolidColorBrush(Color.Parse("#9BB0C4")),
        _ => new SolidColorBrush(Color.Parse("#7C93A8"))
    };

    /// <summary>稀有度符号，例如 6 星显示为 ★★★★★★。</summary>
    public string StarGlyphs => new string('★', Math.Clamp(Star, 0, 6));

    /// <summary>精英阶段与等级的数字读数，例如 2 / 80。</summary>
    public string LevelDigits => $"{Level.EliteLevel}-{Level.Rank:00}";

    // ---- 卡片视图用的立绘 ----

    private bool _usePortrait;
    private IReadOnlyList<Uri>? _avatarUris;
    private IReadOnlyList<Uri>? _portraitUris;
    private bool _portraitUrisElite2;

    /// <summary>卡片是否使用半身立绘大图（否则用头像小图）。由干员列表统一下发。</summary>
    public bool UsePortrait
    {
        get => _usePortrait;
        set
        {
            if (SetProperty(ref _usePortrait, value))
                RaiseArtChanged();
        }
    }

    /// <summary>
    /// 「头像」规格的候选地址（多个 jsDelivr 镜像，按顺序回退）。
    /// 头像本身不分精英阶段，所以这一组与精英无关。
    /// </summary>
    public IReadOnlyList<Uri> AvatarUris => _avatarUris ??= Domain.OperatorArt.Avatar(SourceId);

    /// <summary>
    /// 「半身像」规格的候选地址。精二用专属立绘（_2），取不到时回退默认立绘（_1）。
    ///
    /// 结果按「精英是否精二」缓存：编译绑定是靠对象引用判断有没有变的，
    /// 每次访问都新建 List 会让它每帧都认为图源变了，白白重发下载。
    /// </summary>
    public IReadOnlyList<Uri> PortraitUris
    {
        get
        {
            var elite2 = Level.EliteLevel >= 2;
            if (_portraitUris is { } cached && _portraitUrisElite2 == elite2)
                return cached;

            _portraitUris = Domain.OperatorArt.Portrait(SourceId, elite2);
            _portraitUrisElite2 = elite2;
            return _portraitUris;
        }
    }

    /// <summary>当前该显示的图源（由干员列表统一下发头像/半身像的开关）。</summary>
    public IReadOnlyList<Uri> DisplayArtUris => _usePortrait ? PortraitUris : AvatarUris;

    /// <summary>另一规格的图源，供展示层预载，切换时才不用重新下载。</summary>
    public IReadOnlyList<Uri> AlternateArtUris => _usePortrait ? AvatarUris : PortraitUris;

    /// <summary>清掉已缓存的候选列表（SourceId 变更后地址会变）。</summary>
    private void InvalidateArtCache()
    {
        _avatarUris = null;
        _portraitUris = null;
    }

    /// <summary>通知界面重新取图源（切换头像/立绘、或精英阶段变化时用）。</summary>
    public void RaiseArtChanged()
    {
        OnPropertyChanged(nameof(DisplayArtUris));
        OnPropertyChanged(nameof(AlternateArtUris));
        OnPropertyChanged(nameof(HasArt));
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
}
