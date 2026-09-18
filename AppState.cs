using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using arknights_random_team.Domain;
using arknights_random_team.Models;

namespace arknights_random_team;

public static class AppState
{
    public static ObservableCollection<Staff> StaffList { get; } = [];

    public static ObservableCollection<RandomStrategyDefinition> Strategies { get; } = [];

    public static OperatorSyncSettings OperatorSyncSettings { get; private set; } = new();

    /// <summary>界面偏好（视图选择、表格紧凑开关），桌面端退出时落盘。</summary>
    public static UiPreferences UiPreferences { get; private set; } = new();

    public static string? OperatorSyncSettingsError { get; private set; }

    /// <summary>
    /// 数据文件目录。调试 / <c>dotnet run</c> 时进程是 dotnet.exe，不能用 <see cref="Environment.ProcessPath"/>。
    /// </summary>
    public static string DataDirectory { get; } = ResolveDataDirectory();

    /// <summary>
    /// 浏览器（WebAssembly）里没有文件系统，数据只保存在内存中，刷新页面即重置。
    /// 桌面端始终为 true。
    /// </summary>
    public static bool UseFileStorage => !OperatingSystem.IsBrowser();

    private static string ResolveDataDirectory()
    {
        if (OperatingSystem.IsBrowser())
            return "";

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var hostName = Path.GetFileNameWithoutExtension(processPath);
            if (!hostName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    return dir;
            }
        }

        return Path.GetFullPath(AppContext.BaseDirectory);
    }

    /// <summary>启动阶段日志：浏览器控制台里排查 WebAssembly 启动问题用。</summary>
    public static void LogTrace(string message)
    {
        Console.WriteLine($"[ARK] {message}");
        System.Diagnostics.Debug.WriteLine($"[ARK] {message}");
    }

    private static string SafeCombine(string name) =>
        string.IsNullOrEmpty(DataDirectory) ? "" : Path.Combine(DataDirectory, name);

    private static string StaffPath => SafeCombine("StaffList.xml");

    private static string StrategyPath => SafeCombine("RandomStrategies.json");

    private static string OperatorSyncSettingsPath => SafeCombine("OperatorSyncSettings.json");

    private static string UiPreferencesPath => SafeCombine("UiPreferences.json");

    public static void Initialize()
    {
        LoadStaff();
        StrategyPersistence.Load(StrategyPath, Strategies);
        LoadOperatorSyncSettings();
        LoadUiPreferences();
    }

    public static void Save()
    {
        if (!UseFileStorage)
            return;

        Directory.CreateDirectory(DataDirectory);
        SaveStaff();
        StrategyPersistence.Save(StrategyPath, Strategies);
        SaveUiPreferences();
        if (OperatorSyncSettings.SelectedStars is { Count: > 0 })
            SaveOperatorSyncSettings();
    }

    public static void SaveOperatorData()
    {
        if (!UseFileStorage)
            return;

        Directory.CreateDirectory(DataDirectory);
        SaveStaff();
        SaveOperatorSyncSettings();
    }

    public static void SaveOperatorSyncSettings()
    {
        ValidateOperatorSyncSettings(OperatorSyncSettings);

        if (!UseFileStorage)
        {
            OperatorSyncSettingsError = null;
            return;
        }

        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(OperatorSyncSettings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(OperatorSyncSettingsPath, json);
        OperatorSyncSettingsError = null;
    }

    public static HashSet<string> GetNameSet() => StaffList.Select(staff => staff.Name).ToHashSet();

    private static void LoadStaff()
    {
        StaffList.Clear();
        if (!UseFileStorage || !File.Exists(StaffPath))
            return;

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
                    IsSelected = int.TryParse(each.Element("selected")?.Value, out var selected) && selected != 0,
                    SourceId = NullIfWhiteSpace(each.Element("sourceId")?.Value)
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

    private static void LoadOperatorSyncSettings()
    {
        OperatorSyncSettings = new OperatorSyncSettings();
        OperatorSyncSettingsError = null;
        if (!UseFileStorage || !File.Exists(OperatorSyncSettingsPath))
            return;

        try
        {
            var settings = JsonSerializer.Deserialize<OperatorSyncSettings>(
                File.ReadAllText(OperatorSyncSettingsPath));
            if (settings == null)
                throw new InvalidDataException("同步设置内容为空");
            if (settings.SelectedStars == null)
                throw new InvalidDataException("同步设置缺少 SelectedStars 字段");
            if (settings.SelectedStars.Count == 0)
                throw new InvalidDataException("同步设置中的 SelectedStars 至少需要一个稀有度");
            if (settings.SelectedStars.Any(star => star is < 1 or > 6))
                throw new InvalidDataException("同步设置中的 SelectedStars 只能包含 1 到 6");

            OperatorSyncSettings = settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            OperatorSyncSettingsError = $"无法加载 {Path.GetFileName(OperatorSyncSettingsPath)}：{ex.Message}";
        }
    }

    private static void LoadUiPreferences()
    {
        UiPreferences = new UiPreferences();
        if (!UseFileStorage || !File.Exists(UiPreferencesPath))
            return;

        try
        {
            var preferences = JsonSerializer.Deserialize<UiPreferences>(
                File.ReadAllText(UiPreferencesPath), UiPreferencesJson);
            if (preferences is not null)
                UiPreferences = preferences;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // 偏好文件损坏不值得打断启动：回到默认视图即可。
            UiPreferences = new UiPreferences();
        }
    }

    private static void SaveUiPreferences()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(
            UiPreferencesPath,
            JsonSerializer.Serialize(UiPreferences, UiPreferencesJson));
    }

    private static readonly JsonSerializerOptions UiPreferencesJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static void ValidateOperatorSyncSettings(OperatorSyncSettings settings)    {
        if (settings.SelectedStars == null)
            throw new InvalidDataException("同步设置缺少 SelectedStars 字段");
        if (settings.SelectedStars.Count == 0)
            throw new InvalidDataException("请至少选择一个需要同步的稀有度");
        if (settings.SelectedStars.Any(star => star is < 1 or > 6))
            throw new InvalidDataException("同步稀有度只能是 1 到 6");
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

        Directory.CreateDirectory(Path.GetDirectoryName(StaffPath)!);
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
        if (!string.IsNullOrWhiteSpace(staff.SourceId))
        {
            var sourceIdElement = file.CreateElement("sourceId");
            sourceIdElement.InnerText = staff.SourceId;
            staffElement.AppendChild(sourceIdElement);
        }
        parentNode.AppendChild(staffElement);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
