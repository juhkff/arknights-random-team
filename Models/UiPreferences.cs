namespace arknights_random_team.Models;

/// <summary>干员列表的展示方式。</summary>
public enum StaffViewMode
{
    /// <summary>表格：编辑与批量维护用，默认视图。</summary>
    Table,

    /// <summary>头像：日常认人、快速维护随机池。</summary>
    Avatar,

    /// <summary>立绘：浏览欣赏。</summary>
    Portrait
}

/// <summary>
/// 界面偏好。
///
/// 桌面端退出时随其它数据一起写入 UiPreferences.json；网页端没有文件系统，
/// 只在内存里生效（刷新页面回到默认），与该端「体验模式，刷新后数据重置」的提示一致。
/// 方案把「视图偏好记忆」列为独立增强，这里只保存视图选择与表格紧凑开关两项。
/// </summary>
public sealed class UiPreferences
{
    /// <summary>上次使用的干员列表视图。</summary>
    public StaffViewMode ViewMode { get; set; } = StaffViewMode.Table;

    /// <summary>表格是否使用紧凑行高。</summary>
    public bool IsCompactTable { get; set; }
}
