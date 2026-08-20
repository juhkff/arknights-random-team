using System.Collections.ObjectModel;
using System.Xml;
using System.Xml.Linq;
using arknights_random_team.Domain;
using arknights_random_team.Models;

namespace arknights_random_team;

public static class AppState
{
    public static ObservableCollection<Staff> StaffList { get; } = [];

    public static ObservableCollection<RandomStrategyDefinition> Strategies { get; } = [];

    private static string DataDirectory
    {
        get
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                var dir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrWhiteSpace(dir))
                    return dir;
            }

            return AppContext.BaseDirectory;
        }
    }

    private static string StaffPath => Path.Combine(DataDirectory, "StaffList.xml");

    private static string StrategyPath => Path.Combine(DataDirectory, "RandomStrategies.json");

    public static void Initialize()
    {
        LoadStaff();
        StrategyPersistence.Load(StrategyPath, Strategies);
    }

    public static void Save()
    {
        SaveStaff();
        StrategyPersistence.Save(StrategyPath, Strategies);
    }

    public static HashSet<string> GetNameSet() => StaffList.Select(staff => staff.Name).ToHashSet();

    private static void LoadStaff()
    {
        StaffList.Clear();
        if (!File.Exists(StaffPath))
        {
            var xmldoc = new XmlDocument();
            xmldoc.AppendChild(xmldoc.CreateXmlDeclaration("1.0", "utf-8", "yes"));
            var rootElement = xmldoc.CreateElement("staffList");
            rootElement.InnerText = "";
            xmldoc.AppendChild(rootElement);
            xmldoc.Save(StaffPath);
            return;
        }

        var xDocument = XDocument.Load(StaffPath);
        foreach (var career in xDocument.Root?.Elements("career") ?? [])
        {
            var typeAttr = career.Attribute("type")?.Value;
            if (string.IsNullOrWhiteSpace(typeAttr) || !Enum.TryParse(typeAttr, out Career careerType))
                continue;

            foreach (var each in career.Elements("staff"))
            {
                var name = each.Element("name")?.Value ?? "";
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var staff = new Staff
                {
                    Name = name,
                    Star = int.TryParse(each.Element("star")?.Value, out var star) ? star : 1,
                    Career = careerType,
                    IsSelected = int.TryParse(each.Element("selected")?.Value, out var selected) && selected != 0
                };

                var levelText = each.Element("level")?.Value;
                if (string.IsNullOrWhiteSpace(levelText))
                    staff.Level = Level.GenerateDefaultLevel();
                else
                {
                    var parts = levelText.Split(';');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out var elite) &&
                        int.TryParse(parts[1], out var rank))
                        staff.Level = new Level(elite, rank);
                    else
                        staff.Level = Level.GenerateDefaultLevel();
                }

                StaffList.Add(staff);
            }
        }
    }

    private static void SaveStaff()
    {
        var xmlDocument = new XmlDocument();
        if (File.Exists(StaffPath))
            xmlDocument.Load(StaffPath);
        else
        {
            xmlDocument.AppendChild(xmlDocument.CreateXmlDeclaration("1.0", "utf-8", "yes"));
            xmlDocument.AppendChild(xmlDocument.CreateElement("staffList"));
        }

        var root = xmlDocument.SelectSingleNode("staffList") ?? xmlDocument.DocumentElement!;
        root.RemoveAll();

        var groupList = StaffList
            .GroupBy(x => x.Career)
            .Select(x => new StaffGroupByCareer { Career = x.Key, StaffList = x.ToList() });

        foreach (var staffsWithCareer in groupList)
        {
            var careerElement = xmlDocument.CreateElement("career");
            careerElement.SetAttribute("type", staffsWithCareer.Career.ToString());
            foreach (var staff in staffsWithCareer.StaffList)
                AddStaff(xmlDocument, careerElement, staff);
            root.AppendChild(careerElement);
        }

        xmlDocument.Save(StaffPath);
    }

    private static void AddStaff(XmlDocument file, XmlElement parentNode, Staff staff)
    {
        var staffElement = file.CreateElement("staff");
        var nameElement = file.CreateElement("name");
        var starElement = file.CreateElement("star");
        var levelElement = file.CreateElement("level");
        var selectedElement = file.CreateElement("selected");
        nameElement.InnerText = staff.Name;
        starElement.InnerText = staff.Star.ToString();
        levelElement.InnerText = $"{staff.Level.EliteLevel};{staff.Level.Rank}";
        selectedElement.InnerText = Convert.ToInt32(staff.IsSelected).ToString();
        staffElement.AppendChild(nameElement);
        staffElement.AppendChild(starElement);
        staffElement.AppendChild(levelElement);
        staffElement.AppendChild(selectedElement);
        parentNode.AppendChild(staffElement);
    }
}
