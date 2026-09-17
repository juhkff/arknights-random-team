using arknights_random_team.Domain;
using arknights_random_team.Models;

namespace arknights_random_team.Views;

/// <summary>数据库同步的共用入口。列表页与录入页走同一套对话框和写入逻辑。</summary>
internal static class OperatorSyncFlow
{
    public static async Task<SyncUiResult> RunAsync(Action? onStarted = null)
    {
        var dialog = new OperatorSyncDialog();
        var accepted = await AppHost.ShowAsync<bool>(dialog);
        if (!accepted || dialog.Selection is not { } selection)
            return SyncUiResult.Cancelled;

        AppState.OperatorSyncSettings.SelectedStars = selection.SelectedStars.ToHashSet();
        AppState.SaveOperatorSyncSettings();
        onStarted?.Invoke();

        try
        {
            var result = await new OperatorSyncService().SyncAsync(
                AppState.StaffList,
                AppState.OperatorSyncSettings,
                selection.SelectedStars);
            AppState.SaveOperatorData();
            return SyncUiResult.Ok($"新增 {result.Added} 名，校正 {result.Updated} 名，跳过 {result.Unchanged} 名。");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            return SyncUiResult.Fail($"同步失败：{ex.Message}");
        }
    }

    public readonly record struct SyncUiResult(bool Ran, string Message, bool IsError)
    {
        public static SyncUiResult Cancelled => new(false, "", false);

        public static SyncUiResult Ok(string message) => new(true, message, false);

        public static SyncUiResult Fail(string message) => new(true, message, true);
    }
}
