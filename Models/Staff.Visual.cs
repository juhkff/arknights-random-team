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
    /// 卡片图源的候选地址（多个 jsDelivr 镜像，按顺序回退）。
    /// 手动录入的干员没有 <see cref="SourceId"/>，这里为空，卡片会显示职业徽记作为占位。
    ///
    /// 首选规格取不到时会回退到另一种：头像与半身像的收录并不一致，
    /// 有的干员只有其中一种，回退一下能明显减少「没图」的情况。
    /// </summary>
    public IReadOnlyList<Uri> ArtUris
    {
        get
        {
            var primary = _usePortrait
                ? Domain.OperatorArt.Portrait(SourceId)
                : Domain.OperatorArt.Avatar(SourceId);
            var secondary = _usePortrait
                ? Domain.OperatorArt.Avatar(SourceId)
                : Domain.OperatorArt.Portrait(SourceId);

            return [.. primary, .. secondary];
        }
    }

    /// <summary>通知界面重新取 <see cref="ArtUris"/>（切换头像/立绘时用）。</summary>
    public void RaiseArtChanged() => OnPropertyChanged(nameof(ArtUris));

    /// <summary>是否有立绘可显示。</summary>
    public bool HasArt => ArtUris.Count > 0;

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
