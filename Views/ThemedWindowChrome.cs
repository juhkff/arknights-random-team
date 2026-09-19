using Avalonia.Controls;

namespace arknights_random_team.Views;

/// <summary>
/// 去掉系统标题栏，只留边框与缩放，标题栏改由 <see cref="ThemedTitleBar"/> 画在客户区里。
/// 不能继续用 <see cref="WindowDecorations.Full"/>：Win32 仍会绘制最小/最大化按钮并抢走拖动命中。
/// </summary>
internal static class ThemedWindowChrome
{
    public const double TitleBarHeight = ThemedTitleBar.BarHeight;

    public static void Apply(Window window)
    {
        window.ExtendClientAreaToDecorationsHint = true;
        window.WindowDecorations = WindowDecorations.BorderOnly;
    }
}
