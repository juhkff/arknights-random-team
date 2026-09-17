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

    public Staff()
    {
        // 字段初始化的 Level 不会走 setter，这里补订阅，
        // 表格里改「精二」才能立刻换成精二立绘。
        _level.PropertyChanged += Level_PropertyChanged;
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public int Star
    {
        get => _star;
        set
        {
            if (!SetProperty(ref _star, value))
                return;

            OnPropertyChanged(nameof(StarGlyphs));
            OnPropertyChanged(nameof(RarityBrush));
            OnPropertyChanged(nameof(RaritySoftBrush));
        }
    }

    public Career Career
    {
        get => _career;
        set
        {
            if (!SetProperty(ref _career, value))
                return;

            OnPropertyChanged(nameof(CareerBrush));
            OnPropertyChanged(nameof(CareerSoftBrush));
            OnPropertyChanged(nameof(CareerName));
            OnPropertyChanged(nameof(CareerIconUris));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                OnPropertyChanged(nameof(RosterStatus));
        }
    }

    public Level Level
    {
        get => _level;
        set
        {
            if (ReferenceEquals(_level, value))
                return;

            // 换对象时先退订旧的，避免残留订阅
            _level.PropertyChanged -= Level_PropertyChanged;
            if (!SetProperty(ref _level, value))
            {
                _level.PropertyChanged += Level_PropertyChanged;
                return;
            }

            _level.PropertyChanged += Level_PropertyChanged;

            // 精英阶段决定用默认立绘还是精二立绘
            RaiseArtChanged();
            OnPropertyChanged(nameof(LevelDigits));
            OnPropertyChanged(nameof(EliteLabel));
        }
    }

    /// <summary>
    /// 精英阶段被就地改动时（表格里直接编辑精英列就是这种情况），
    /// 精二立绘要跟着换，所以转发一次图源通知。
    /// </summary>
    private void Level_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(LevelDigits));
        OnPropertyChanged(nameof(EliteLabel));
        if (e.PropertyName == nameof(Models.Level.EliteLevel))
            RaiseArtChanged();
    }

    /// <summary>外部数据源中的稳定标识；手工录入的干员为空。</summary>
    public string? SourceId
    {
        get => _sourceId;
        set
        {
            if (!SetProperty(ref _sourceId, value))
                return;

            // 立绘地址由 SourceId 拼出，同步改写它之后要让缓存失效
            InvalidateArtCache();
            RaiseArtChanged();
        }
    }
}
