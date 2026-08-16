using System.Collections.ObjectModel;

namespace arknights_random_team.Domain;

public class RandomStrategyDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = "";

    public ObservableCollection<StrategyRule> Rules { get; } = [];
}
