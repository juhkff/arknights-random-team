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
        set
        {
            if (SetProperty(ref _eliteLevel, value))
                SetProperty(ref _description, Format(value, Rank), nameof(Description));
        }
    }

    public int Rank
    {
        get => _rank;
        set
        {
            if (SetProperty(ref _rank, value))
                SetProperty(ref _description, Format(EliteLevel, value), nameof(Description));
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (!TryParse(value, out var elite, out var rank))
                return;

            _eliteLevel = elite;
            _rank = rank;
            SetProperty(ref _description, Format(elite, rank));
            OnPropertyChanged(nameof(EliteLevel));
            OnPropertyChanged(nameof(Rank));
        }
    }

    public Level(int eliteLevel, int rank)
    {
        _eliteLevel = eliteLevel;
        _rank = rank;
        _description = Format(eliteLevel, rank);
    }

    public static Level GenerateDefaultLevel() => new(2, 1);

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

        if (!int.TryParse(match.Groups[2].Value, out rank) || rank is < 1 or > 90)
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
