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

    public static UiPreferences UiPreferences { get; private set; } = new();

    public static string? OperatorSyncSettingsError { get; private set; }

    public static string? LastSaveError { get; private set; }

    private static readonly List<string> StartupNoticeList = [];
    private static string _staffSnapshot = "";
    private static string _strategySnapshot = "";
    private static string _settingsSnapshot = "";
    private static string _preferencesSnapshot = "";
    private static string? _storageReadOnlyReason;

    /// <summary>本次启动的数据恢复与读取提示。</summary>
    public static IReadOnlyList<string> StartupNotices => StartupNoticeList;

    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static bool UseFileStorage => !OperatingSystem.IsBrowser();

    private static readonly HashSet<string> WriteBlocked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前会话处于只读状态或无法安全覆盖的文件。</summary>
    public static IReadOnlyList<string> WriteBlockedFiles => WriteBlocked
        .Select(Path.GetFileName)
        .OfType<string>()
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string ResolveDataDirectory()
    {
        if (OperatingSystem.IsBrowser())
            return "";

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            !Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            Path.GetDirectoryName(processPath) is { Length: > 0 } directory)
        {
            return directory;
        }

        return Path.GetFullPath(AppContext.BaseDirectory);
    }

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

    public static bool IsStorageReadOnly => _storageReadOnlyReason is not null;

    public static void SetStorageReadOnly(string reason) => _storageReadOnlyReason = reason;

    public static void Initialize()
    {
        StartupNoticeList.Clear();
        WriteBlocked.Clear();

        LoadStaff();
        LoadStrategies();
        LoadOperatorSyncSettings();
        LoadUiPreferences();
        CaptureSnapshots();

        if (_storageReadOnlyReason is { } reason)
        {
            WriteBlocked.UnionWith([StaffPath, StrategyPath, OperatorSyncSettingsPath, UiPreferencesPath]);
            AddStartupNotice(reason);
        }
    }

    private enum ReadResult
    {
        Missing,
        Success,
        Invalid,
        Unavailable
    }

    /// <summary>Loads one file while preserving content that could not be read or parsed.</summary>
    private static string? LoadWithRecovery(string path, Func<ReadResult> read)
    {
        if (!UseFileStorage)
            return null;

        var name = Path.GetFileName(path);
        var backup = path + ".bak";
        var result = read();
        if (IsStorageReadOnly)
        {
            if (result is ReadResult.Invalid or ReadResult.Unavailable ||
                result == ReadResult.Missing && FileStore.Probe(backup) != FileProbeResult.Missing)
            {
                AddStartupNotice($"{name} 未能正常读取；只读模式下未进行恢复或隔离，原始数据未被修改。");
            }

            return null;
        }

        var restored = false;

        if (result == ReadResult.Missing)
        {
            switch (FileStore.TryRestoreBackup(path))
            {
                case BackupRestoreResult.Missing:
                    return null;
                case BackupRestoreResult.Failed:
                    return BlockWrite(path, $"检测到或无法访问 {name}.bak，但无法恢复 {name}；本次不会写回该文件。");
                case BackupRestoreResult.Restored:
                    restored = true;
                    result = read();
                    break;
            }
        }

        if (result == ReadResult.Success)
        {
            if (restored)
                AddStartupNotice($"已从 {name}.bak 恢复 {name}，请检查数据是否完整。");
            return null;
        }

        if (result is ReadResult.Unavailable or ReadResult.Missing)
            return BlockWrite(path, $"当前无法稳定读取 {name}。为避免覆盖磁盘数据，本次会话不会写回该文件。");

        if (restored)
            return PreserveInvalidBackup(path, backup, name, $"{name} 的备份内容无效，已保留诊断副本，本次使用默认数据。");

        var quarantine = FileStore.TryQuarantine(path, out var quarantined);
        if (quarantine == QuarantineResult.Missing)
            return BlockWrite(path, $"{name} 在读取后又消失。为避免覆盖外部更新，本次会话不会写回它。");
        if (quarantine == QuarantineResult.Failed)
        {
            if (FileStore.Probe(backup) != FileProbeResult.Missing)
                return BlockWrite(path, $"{name} 内容无效且无法移走。为保护现有或无法确认的备份，本次不会写回它。");

            var unquarantined = $"{name} 内容无效且暂时无法移走，但没有现有备份；仍可尝试保存，新数据提交时旧内容会成为 .bak。";
            AddStartupNotice(unquarantined);
            return unquarantined;
        }

        switch (FileStore.TryRestoreBackup(path))
        {
            case BackupRestoreResult.Restored:
            {
                var backupResult = read();
                if (backupResult == ReadResult.Success)
                {
                    AddStartupNotice($"{name} 内容无效，已从备份恢复；原文件保留为 {Path.GetFileName(quarantined)}。");
                    return null;
                }

                if (backupResult is ReadResult.Unavailable or ReadResult.Missing)
                    return BlockWrite(path, $"{name} 已从备份复制，但当前无法稳定读取；本次不会写回该文件。");

                return PreserveInvalidBackup(
                    path,
                    backup,
                    name,
                    $"{name} 与其备份均无法解析，已分别保留诊断副本，本次使用默认数据。");
            }
            case BackupRestoreResult.Failed:
                return BlockWrite(path, $"{name} 内容无效，且现有备份无法恢复；本次不会写回该文件。");
            default:
            {
                var message = $"{name} 内容无效，已保留为 {Path.GetFileName(quarantined)}，本次使用默认数据。";
                AddStartupNotice(message);
                return message;
            }
        }
    }

    private static string PreserveInvalidBackup(string path, string backup, string name, string message)
    {
        // Both files contain invalid data. Moving both avoids deleting a path that an external sync tool
        // may have replaced between parsing and recovery.
        if (FileStore.TryQuarantine(path, out var pathCopy) != QuarantineResult.Moved ||
            FileStore.TryQuarantine(backup, out var backupCopy) != QuarantineResult.Moved)
        {
            return BlockWrite(path, $"{name} 的备份内容无效且无法安全移走；本次不会写回该文件。");
        }

        FileStore.DeleteIfDuplicate(pathCopy!, backupCopy!);
        AddStartupNotice(message);
        return message;
    }

    private static string BlockWrite(string path, string message)
    {
        WriteBlocked.Add(path);
        AddStartupNotice(message);
        return message;
    }

    private static void AddStartupNotice(string message)
    {
        StartupNoticeList.Add(message);
        LogTrace(message);
    }

    public static bool Save()
    {
        if (!UseFileStorage)
            return true;

        LastSaveError = null;
        var saved = TrySaveState(StaffPath, SerializeStaff, ref _staffSnapshot, out _);
        saved &= TrySaveState(StrategyPath, () => StrategyPersistence.Serialize(Strategies), ref _strategySnapshot, out _);
        saved &= TrySaveState(UiPreferencesPath, SerializeUiPreferences, ref _preferencesSnapshot, out _);
        saved &= SaveOperatorSyncSettingsCore(out _);
        return saved;
    }

    /// <summary>
    /// Saves the operator list. A sync can also rename operators, so its caller includes any migrated
    /// strategy references in the same save attempt. Sync settings are persisted separately.
    /// </summary>
    public static bool SaveOperatorData(bool includeStrategyReferences = false)
    {
        if (!UseFileStorage)
            return true;

        LastSaveError = null;
        var saved = TrySaveState(StaffPath, SerializeStaff, ref _staffSnapshot, out _);
        if (includeStrategyReferences)
            saved &= TrySaveState(StrategyPath, () => StrategyPersistence.Serialize(Strategies), ref _strategySnapshot, out _);
        return saved;
    }

    public static bool SaveOperatorSyncSettings()
    {
        LastSaveError = null;
        return SaveOperatorSyncSettingsCore(out _);
    }

    private static bool SaveOperatorSyncSettingsCore(out string? error)
    {
        if (!UseFileStorage)
        {
            OperatorSyncSettingsError = null;
            error = null;
            return true;
        }

        OperatorSyncSettings.SelectedStars = OperatorSyncSettings.SelectedStars?
            .Where(star => star is >= FieldLimits.MinStar and <= FieldLimits.MaxStar)
            .ToHashSet();
        if (OperatorSyncSettings.SelectedStars is { Count: 0 })
            OperatorSyncSettings.SelectedStars = null;

        var ok = TrySaveState(
            OperatorSyncSettingsPath,
            SerializeOperatorSyncSettings,
            ref _settingsSnapshot,
            out error);
        OperatorSyncSettingsError = error;
        return ok;
    }

    private static bool TrySaveState(
        string path,
        Func<string> serialize,
        ref string snapshot,
        out string? error)
    {
        string current;
        try
        {
            current = serialize();
        }
        catch (Exception ex) when (IsPersistenceError(ex))
        {
            error = $"保存 {Path.GetFileName(path)} 失败：{ex.Message}";
            RecordSaveError(error);
            return false;
        }

        if (current == snapshot)
        {
            error = null;
            return true;
        }

        if (!TrySaveFile(path, () => FileStore.WriteAllTextAtomic(path, current), out error))
            return false;

        snapshot = current;
        return true;
    }

    private static bool TrySaveFile(string path, Action save, out string? error)
    {
        if (WriteBlocked.Contains(path))
        {
            error = $"未保存 {Path.GetFileName(path)}：文件不可安全写入或当前处于只读模式，已跳过写入以保护磁盘数据。";
            RecordSaveError(error);
            return false;
        }

        try
        {
            Directory.CreateDirectory(DataDirectory);
            save();
            error = null;
            return true;
        }
        catch (Exception ex) when (IsPersistenceError(ex))
        {
            error = $"保存 {Path.GetFileName(path)} 失败：{ex.Message}";
            RecordSaveError(error);
            return false;
        }
    }

    private static void RecordSaveError(string error)
    {
        LastSaveError ??= error;
        LogTrace(error);
    }

    private static bool IsPersistenceError(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or InvalidDataException
            or XmlException
            or JsonException
            or NotSupportedException;

    public static HashSet<string> GetNameSet() => StaffList.Select(staff => staff.Name).ToHashSet();

    public static void RenameStaff(Staff staff, string previousName, string newName)
    {
        newName = newName.Trim();
        if (StaffValidator.ValidateName(newName, StaffList, staff) is { } error)
            throw new InvalidOperationException(error);

        staff.Name = newName;
        if (string.Equals(previousName, newName, StringComparison.Ordinal))
            return;

        foreach (var rule in Strategies.SelectMany(strategy => strategy.Rules))
        {
            if (rule.Kind is StrategyRuleKind.StaffSubsetExact or StrategyRuleKind.StaffSubsetRange)
                rule.RenameStaff(previousName, newName);
        }
    }

    private static int _bulkUpdateDepth;

    public static bool IsBulkUpdating => _bulkUpdateDepth > 0;

    public static event Action? BulkUpdateCompleted;

    public static IDisposable BeginBulkUpdate()
    {
        _bulkUpdateDepth++;
        return new BulkUpdateScope();
    }

    private sealed class BulkUpdateScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _bulkUpdateDepth--;
            if (_bulkUpdateDepth == 0)
                BulkUpdateCompleted?.Invoke();
        }
    }

    private static void LoadStaff()
    {
        StaffList.Clear();
        LoadWithRecovery(StaffPath, TryReadStaff);
    }

    private static ReadResult TryReadStaff()
    {
        try
        {
            var document = XDocument.Load(StaffPath);
            if (document.Root?.Name != "staffList" ||
                document.Root.Elements().Any(element => element.Name != "career"))
            {
                return ReadResult.Invalid;
            }

            var loaded = new List<Staff>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var career in document.Root.Elements("career"))
            {
                if (!Enum.TryParse(career.Attribute("type")?.Value, out Career careerType) ||
                    !Enum.IsDefined(careerType) ||
                    career.Elements().Any(element => element.Name != "staff"))
                {
                    return ReadResult.Invalid;
                }

                foreach (var element in career.Elements("staff"))
                {
                    var allowed = new HashSet<XName> { "name", "star", "level", "selected", "sourceId" };
                    if (element.Elements().Any(child => !allowed.Contains(child.Name)) ||
                        element.Elements().GroupBy(child => child.Name).Any(group => group.Count() > 1))
                    {
                        return ReadResult.Invalid;
                    }

                    var name = element.Element("name")?.Value?.Trim() ?? "";
                    var sourceId = NullIfWhiteSpace(element.Element("sourceId")?.Value);
                    if (name.Length == 0 || !names.Add(name) ||
                        sourceId is not null && !sourceIds.Add(sourceId))
                    {
                        return ReadResult.Invalid;
                    }

                    var starElement = element.Element("star");
                    if (starElement is not null && !int.TryParse(starElement.Value, out _))
                        return ReadResult.Invalid;
                    var star = starElement is null ? FieldLimits.MinStar : int.Parse(starElement.Value);

                    var selectedElement = element.Element("selected");
                    if (selectedElement is not null && !int.TryParse(selectedElement.Value, out _))
                        return ReadResult.Invalid;
                    var selected = selectedElement is not null && int.Parse(selectedElement.Value) != 0;

                    var level = Level.GenerateDefaultLevel();
                    if (element.Element("level") is { } levelElement)
                    {
                        var parts = levelElement.Value.Split(';');
                        if (parts.Length != 2 ||
                            !int.TryParse(parts[0], out var elite) ||
                            !int.TryParse(parts[1], out var rank))
                        {
                            return ReadResult.Invalid;
                        }
                        level = new Level(elite, rank);
                    }

                    loaded.Add(new Staff
                    {
                        Name = name,
                        Star = FieldLimits.ClampStar(star),
                        Career = careerType,
                        IsSelected = selected,
                        SourceId = sourceId,
                        Level = level
                    });
                }
            }

            StaffList.Clear();
            foreach (var staff in loaded)
                StaffList.Add(staff);
            return ReadResult.Success;
        }
        catch (FileNotFoundException)
        {
            return ReadResult.Missing;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        {
            return ReadResult.Invalid;
        }
        catch (Exception ex) when (IsReadUnavailable(ex))
        {
            return ReadResult.Unavailable;
        }
    }

    private static void LoadStrategies()
    {
        Strategies.Clear();
        LoadWithRecovery(StrategyPath, TryReadStrategies);
    }

    private static ReadResult TryReadStrategies()
    {
        try
        {
            return StrategyPersistence.Load(StrategyPath, Strategies)
                ? ReadResult.Success
                : ReadResult.Invalid;
        }
        catch (FileNotFoundException)
        {
            return ReadResult.Missing;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return ReadResult.Invalid;
        }
        catch (Exception ex) when (IsReadUnavailable(ex))
        {
            return ReadResult.Unavailable;
        }
    }

    private static void LoadOperatorSyncSettings()
    {
        OperatorSyncSettings = new OperatorSyncSettings();
        OperatorSyncSettingsError = LoadWithRecovery(OperatorSyncSettingsPath, TryReadOperatorSyncSettings);
    }

    private static ReadResult TryReadOperatorSyncSettings()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<OperatorSyncSettings>(
                File.ReadAllText(OperatorSyncSettingsPath));
            if (settings?.SelectedStars is not { Count: > 0 } stars ||
                stars.Any(star => star is < FieldLimits.MinStar or > FieldLimits.MaxStar))
            {
                return ReadResult.Invalid;
            }

            OperatorSyncSettings = settings;
            return ReadResult.Success;
        }
        catch (FileNotFoundException)
        {
            return ReadResult.Missing;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            OperatorSyncSettings = new OperatorSyncSettings();
            return ReadResult.Invalid;
        }
        catch (Exception ex) when (IsReadUnavailable(ex))
        {
            OperatorSyncSettings = new OperatorSyncSettings();
            return ReadResult.Unavailable;
        }
    }

    private static void LoadUiPreferences()
    {
        UiPreferences = new UiPreferences();
        LoadWithRecovery(UiPreferencesPath, TryReadUiPreferences);
    }

    private static ReadResult TryReadUiPreferences()
    {
        try
        {
            var preferences = JsonSerializer.Deserialize<UiPreferences>(
                File.ReadAllText(UiPreferencesPath), UiPreferencesJson);
            if (preferences is null || !Enum.IsDefined(preferences.ViewMode) ||
                !Enum.IsDefined(preferences.RosterViewMode))
                return ReadResult.Invalid;

            if (preferences.ViewMode is StaffViewMode.Avatar or StaffViewMode.Portrait)
                preferences.ViewMode = StaffViewMode.HalfBody;
            UiPreferences = preferences;
            return ReadResult.Success;
        }
        catch (FileNotFoundException)
        {
            return ReadResult.Missing;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            UiPreferences = new UiPreferences();
            return ReadResult.Invalid;
        }
        catch (Exception ex) when (IsReadUnavailable(ex))
        {
            UiPreferences = new UiPreferences();
            return ReadResult.Unavailable;
        }
    }

    private static bool IsReadUnavailable(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static readonly JsonSerializerOptions UiPreferencesJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions OperatorSyncSettingsJson = new()
    {
        WriteIndented = true
    };

    private static void CaptureSnapshots()
    {
        _staffSnapshot = SerializeStaff();
        _strategySnapshot = StrategyPersistence.Serialize(Strategies);
        _settingsSnapshot = SerializeOperatorSyncSettings();
        _preferencesSnapshot = SerializeUiPreferences();
    }

    private static string SerializeUiPreferences() =>
        JsonSerializer.Serialize(UiPreferences, UiPreferencesJson);

    private static string SerializeOperatorSyncSettings() =>
        JsonSerializer.Serialize(OperatorSyncSettings, OperatorSyncSettingsJson);

    private static string SerializeStaff()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var staff in StaffList)
        {
            var name = staff.Name.Trim();
            var sourceId = NullIfWhiteSpace(staff.SourceId);
            if (name.Length == 0 || !names.Add(name))
                throw new InvalidDataException("干员名称为空或存在重名。");
            if (sourceId is not null && !sourceIds.Add(sourceId))
                throw new InvalidDataException("存在重复的数据源标识。");
            if (!Enum.IsDefined(staff.Career))
                throw new InvalidDataException($"干员「{staff.Name}」的职业值无效。");
            VerifyXmlText(staff.Name, "干员名称");
            if (staff.SourceId is not null)
                VerifyXmlText(staff.SourceId, "数据源标识");
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(
                "staffList",
                StaffList.GroupBy(staff => staff.Career).Select(group =>
                    new XElement(
                        "career",
                        new XAttribute("type", group.Key),
                        group.Select(staff =>
                            new XElement(
                                "staff",
                                new XElement("name", staff.Name.Trim()),
                                new XElement("star", staff.Star),
                                new XElement("level", $"{staff.Level.EliteLevel};{staff.Level.Rank}"),
                                new XElement("selected", Convert.ToInt32(staff.IsSelected)),
                                string.IsNullOrWhiteSpace(staff.SourceId)
                                    ? null
                                    : new XElement("sourceId", staff.SourceId.Trim())))))));

        return document.ToString();
    }

    private static void VerifyXmlText(string value, string field)
    {
        try
        {
            XmlConvert.VerifyXmlChars(value);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"{field}包含无法保存的控制字符。", ex);
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
