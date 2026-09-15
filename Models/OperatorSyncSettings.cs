namespace arknights_random_team.Models;

public sealed class OperatorSyncSettings
{
    public HashSet<int>? SelectedStars { get; set; }

    public DateTimeOffset? LastSuccessfulSync { get; set; }
}
