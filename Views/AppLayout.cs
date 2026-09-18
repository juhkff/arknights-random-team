namespace arknights_random_team.Views;

/// <summary>
/// 按应用可用逻辑宽度划分的断点。列数与工具栏换行都看内容宽度，不看设备名。
/// </summary>
public static class AppLayout
{
    /// <summary>宽屏阈值：方案 §5.1 要求 ≥1200 出完整侧栏，默认窗口宽度按此对齐（1240）。</summary>
    public const double WideWidth = 1200;
    public const double MediumWidth = 900;
    public const double PhoneWidth = 600;

    public static double ShellWidth { get; private set; }

    public static bool IsWide => ShellWidth >= WideWidth;

    public static bool UseIconNav => ShellWidth is >= MediumWidth and < WideWidth;

    public static bool UseDrawerNav => ShellWidth > 0 && ShellWidth < MediumWidth;

    public static bool IsNarrow => ShellWidth > 0 && ShellWidth < MediumWidth;

    public static bool IsPhone => ShellWidth > 0 && ShellWidth < PhoneWidth;

    public static double PagePadding => IsNarrow ? 16 : 24;

    public static double SidebarWidth =>
        UseDrawerNav ? 0 : IsWide ? 208 : 72;

    public static double HeaderHeight => IsPhone ? 56 : 64;

    public static event Action? Changed;

    public static void Update(double width)
    {
        if (width <= 0 || Math.Abs(ShellWidth - width) < 0.5)
            return;

        ShellWidth = width;
        Changed?.Invoke();
    }
}
