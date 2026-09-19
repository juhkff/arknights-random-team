using arknights_random_team.Domain;
using arknights_random_team.Models;

namespace arknights_random_team.Views;

/// <summary>数据库同步的共用入口。列表页与录入页走同一套对话框和写入逻辑。</summary>
internal static class OperatorSyncFlow
{
    /// <summary>Process-wide 0/1 guard shared by both sync entry points.</summary>
    private static int _running;

    public static async Task<SyncUiResult> RunAsync(Action? onStarted = null)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return SyncUiResult.Fail("已有同步任务正在进行，请等待完成。");

        try
        {
            var dialog = new OperatorSyncDialog();
            var accepted = await AppHost.ShowAsync<bool>(dialog);
            if (!accepted || dialog.Selection is not { } selection)
                return SyncUiResult.Cancelled;

            AppState.OperatorSyncSettings.SelectedStars = selection.SelectedStars.ToHashSet();

            // Settings persistence is advisory; the immutable selection still drives this sync.
            var settingsWarning = AppState.SaveOperatorSyncSettings()
                ? null
                : AppState.OperatorSyncSettingsError ?? AppState.LastSaveError ?? "同步设置未能保存。";

            onStarted?.Invoke();

            var result = await new OperatorSyncService(renameStaff: AppState.RenameStaff).SyncAsync(
                AppState.StaffList,
                AppState.OperatorSyncSettings,
                selection.SelectedStars);

            // 同步结果不因落盘失败而回滚；明确告诉用户数据目前只在内存中即可。
            if (!AppState.SaveOperatorData(includeStrategyReferences: true))
            {
                return SyncUiResult.Fail(
                    $"{AppState.LastSaveError ?? "同步结果保存失败。"}部分同步结果可能仅在内存中，请确认磁盘可写后重试。");
            }

            // SyncAsync updates LastSuccessfulSync; persist that timestamp after the operator data succeeds.
            settingsWarning = AppState.SaveOperatorSyncSettings()
                ? null
                : AppState.OperatorSyncSettingsError ?? AppState.LastSaveError ?? "同步设置未能保存。";

            var summary = $"新增 {result.Added} 名，校正 {result.Updated} 名，跳过 {result.Unchanged} 名。";
            return SyncUiResult.Ok(settingsWarning is null ? summary : $"{summary}（注意：{settingsWarning}）");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            return SyncUiResult.Fail($"同步失败：{ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    public readonly record struct SyncUiResult(bool Ran, string Message, bool IsError)
    {
        public static SyncUiResult Cancelled => new(false, "", false);

        public static SyncUiResult Ok(string message) => new(true, message, false);

        public static SyncUiResult Fail(string message) => new(true, message, true);

    }
}
