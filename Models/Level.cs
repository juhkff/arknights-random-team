using System.Text.RegularExpressions;
using arknights_random_team.Domain;

namespace arknights_random_team.Models;

public class Level : AutomaticNotify
{
    private static readonly Regex DescriptionRegex = new(@"^精([零一二])(\d{1,2})级$", RegexOptions.Compiled);

    private int _eliteLevel;
    private int _rank;
    private string _description = "";

    public int EliteLevel
    {
        get => _eliteLevel;
        // 夹取到合法区间：越界值会让 Format 抛异常，而调用方（存档解析、表格编辑）
        // 都不处理异常。夹取后最坏情况是「按合法值显示」，而不是崩溃。
        set
        {
            var clamped = FieldLimits.ClampElite(value);
            if (SetProperty(ref _eliteLevel, clamped))
                SetProperty(ref _description, Format(clamped, Rank), nameof(Description));
        }
    }

    public int Rank
    {
        get => _rank;
        set
        {
            var clamped = FieldLimits.ClampRank(value);
            if (SetProperty(ref _rank, clamped))
                SetProperty(ref _description, Format(EliteLevel, clamped), nameof(Description));
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (!TryParse(value, out var elite, out var rank))
                return;

            var eliteChanged = _eliteLevel != elite;
            var rankChanged = _rank != rank;
            _eliteLevel = elite;
            _rank = rank;
            SetProperty(ref _description, Format(elite, rank));
            if (eliteChanged)
                OnPropertyChanged(nameof(EliteLevel));
            if (rankChanged)
                OnPropertyChanged(nameof(Rank));
        }
    }

    public Level(int eliteLevel, int rank)
    {
        _eliteLevel = FieldLimits.ClampElite(eliteLevel);
        _rank = FieldLimits.ClampRank(rank);
        _description = Format(_eliteLevel, _rank);
    }

    public static Level GenerateDefaultLevel() => new(2, 1);

    public static Level GenerateMaxLevel(int star) => star switch
    {
        1 or 2 => new Level(0, 30),
        3 => new Level(1, 55),
        4 => new Level(2, 70),
        5 => new Level(2, 80),
        6 => new Level(2, 90),
        _ => throw new ArgumentOutOfRangeException(nameof(star), "稀有度越界")
    };

    public static bool TryParse(string? text, out int eliteLevel, out int rank)
    {
        eliteLevel = 0;
        rank = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = DescriptionRegex.Match(text.Trim());
        if (!match.Success)
            return false;

        eliteLevel = match.Groups[1].Value switch
        {
            "零" => 0,
            "一" => 1,
            "二" => 2,
            _ => -1
        };
        if (eliteLevel < 0)
            return false;

        if (!int.TryParse(match.Groups[2].Value, out rank) ||
            rank < FieldLimits.MinRank || rank > FieldLimits.MaxRank)
            return false;

        return true;
    }

    private static string Format(int eliteLevel, int rank)
    {
        var elite = eliteLevel switch
        {
            0 => "零",
            1 => "一",
            2 => "二",
            _ => throw new ArgumentOutOfRangeException(nameof(eliteLevel), "精英等级越界")
        };
        return $"精{elite}{rank}级";
    }
}
