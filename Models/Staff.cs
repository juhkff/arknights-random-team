using arknights_random_team.Domain;

namespace arknights_random_team.Models;

public partial class Staff : AutomaticNotify
{
    private string _name = "";
    private int _star = 1;
    private Level _level = Level.GenerateDefaultLevel();
    private Career _career;
    private bool _isSelected;
    private string? _sourceId;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public int Star
    {
        get => _star;
        set => SetProperty(ref _star, value);
    }

    public Career Career
    {
        get => _career;
        set => SetProperty(ref _career, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public Level Level
    {
        get => _level;
        set => SetProperty(ref _level, value);
    }

    /// <summary>外部数据源中的稳定标识；手工录入的干员为空。</summary>
    public string? SourceId
    {
        get => _sourceId;
        set => SetProperty(ref _sourceId, value);
    }
}
