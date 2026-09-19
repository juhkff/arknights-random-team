using System.Collections.ObjectModel;

namespace arknights_random_team.Domain;

public class RandomStrategyDefinition : AutomaticNotify
{
    private string _name = "";

    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public ObservableCollection<StrategyRule> Rules { get; } = [];
}
