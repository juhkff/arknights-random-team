namespace arknights_random_team.Models;

public enum StaffViewMode
{
    Table = 0,
    // Retained for reading preferences saved before the half-body view was introduced.
    Avatar = 1,
    Portrait = 2,
    HalfBody = 3
}

public enum RosterViewMode
{
    List,
    Formation
}

/// <summary>Desktop preferences are saved on exit; browser preferences last for the session.</summary>
public sealed class UiPreferences
{
    public StaffViewMode ViewMode { get; set; } = StaffViewMode.Table;
    public bool IsCompactTable { get; set; }
    public RosterViewMode RosterViewMode { get; set; } = RosterViewMode.List;
}
