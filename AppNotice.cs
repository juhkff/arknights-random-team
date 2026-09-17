using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Styles.Controls;
using Material.Styles.Models;

namespace arknights_random_team;

/// <summary>底部轻提示。生成失败、录入校验这类可立刻改的问题走这里，不必弹模态。</summary>
public static class AppNotice
{
    public static void Post(string message, double seconds = 2.8)
    {
        var text = new TextBlock
        {
            Text = message,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        SnackbarHost.Post(
            new SnackbarModel(text, TimeSpan.FromSeconds(seconds)),
            "MainSnackbar",
            DispatcherPriority.Normal);
    }
}
