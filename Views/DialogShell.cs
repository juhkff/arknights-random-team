namespace arknights_random_team.Views;

/// <summary>
/// 对话框外壳规格：桌面窗口与浏览器叠加层共用同一套标题和最小尺寸。
/// </summary>
internal static class DialogShell
{
    public static Spec For(Type contentType) =>
        Specs.TryGetValue(contentType, out var known) ? known : Spec.Unknown;

    private static readonly Dictionary<Type, Spec> Specs = new()
    {
        [typeof(AlertDialog)] = new("提示", Resizable: false, MinWidth: 0, MinHeight: 0),
        [typeof(ConfirmDialog)] = new("确认操作", Resizable: false, MinWidth: 0, MinHeight: 0),
        [typeof(StaffPickDialog)] = new("选择干员", Resizable: false, MinWidth: 420, MinHeight: 460),
        [typeof(StaffDetailDialog)] = new("干员详情", Resizable: false, MinWidth: 0, MinHeight: 0),
        [typeof(OperatorSyncDialog)] = new("同步干员", Resizable: false, MinWidth: 0, MinHeight: 0),
        [typeof(StrategyEditorDialog)] = new("编辑随机策略", Resizable: true, MinWidth: 760, MinHeight: 600)
    };

    internal readonly record struct Spec(string Title, bool Resizable, double MinWidth, double MinHeight)
    {
        public static Spec Unknown { get; } = new("对话框", Resizable: false, MinWidth: 0, MinHeight: 0);
    }
}
