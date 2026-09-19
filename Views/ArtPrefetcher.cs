using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using arknights_random_team.Domain;
using arknights_random_team.Models;
using Avalonia.Threading;

namespace arknights_random_team.Views;

/// <summary>
/// One coordinator per shell. Model subscriptions and snapshots belong to the UI thread;
/// the sequential background worker only sees immutable source identifiers and elite flags.
/// </summary>
internal sealed class ArtPrefetcher
{
    /// <summary>Process-wide coordinator so warming can start before the shell control exists.</summary>
    internal static readonly ArtPrefetcher Shared = new();

    private readonly Dictionary<Staff, int> _subscriptions = new(ReferenceEqualityComparer.Instance);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _cancellation;
    private ImmutableArray<(string SourceId, bool Elite2)> _snapshot;
    private Task _worker = Task.CompletedTask;
    private bool _attached;
    private bool _bulkDirty;

    public ArtPrefetcher() => _debounce.Tick += OnDebounceElapsed;

    public void Attach()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_attached)
            return;

        _attached = true;
        AppState.StaffList.CollectionChanged += OnCollectionChanged;
        AppState.BulkUpdateCompleted += OnBulkUpdateCompleted;
        foreach (var staff in AppState.StaffList)
            Subscribe(staff);
        RequestUpdate(immediate: true);
    }

    public void Detach()
    {
        Dispatcher.UIThread.VerifyAccess();
        _attached = false;
        _bulkDirty = false;
        _debounce.Stop();
        AppState.StaffList.CollectionChanged -= OnCollectionChanged;
        AppState.BulkUpdateCompleted -= OnBulkUpdateCompleted;
        ClearSubscriptions();
        CancelWorker();
        _snapshot = default;
        // Keep _worker: a rapid reattach must still await the old worker's shutdown.
    }

    private void Subscribe(Staff staff)
    {
        if (_subscriptions.TryGetValue(staff, out var count))
            _subscriptions[staff] = count + 1;
        else
        {
            _subscriptions.Add(staff, 1);
            staff.PropertyChanged += OnStaffChanged;
        }
    }

    private void Unsubscribe(Staff staff)
    {
        if (!_subscriptions.TryGetValue(staff, out var count))
            return;
        if (count > 1)
            _subscriptions[staff] = count - 1;
        else
        {
            _subscriptions.Remove(staff);
            staff.PropertyChanged -= OnStaffChanged;
        }
    }

    private void ClearSubscriptions()
    {
        foreach (var staff in _subscriptions.Keys)
            staff.PropertyChanged -= OnStaffChanged;
        _subscriptions.Clear();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // Reset does not carry OldItems; explicitly release all previous models.
            ClearSubscriptions();
            foreach (var staff in AppState.StaffList)
                Subscribe(staff);
        }
        else if (e.Action != NotifyCollectionChangedAction.Move)
        {
            if (e.OldItems is not null)
                foreach (Staff staff in e.OldItems)
                    Unsubscribe(staff);
            if (e.NewItems is not null)
                foreach (Staff staff in e.NewItems)
                    Subscribe(staff);
        }
        RequestUpdate();
    }

    private void OnStaffChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName is nameof(Staff.SourceId) or nameof(Staff.Level) or nameof(Staff.EliteLabel))
            RequestUpdate();
    }

    private void OnBulkUpdateCompleted()
    {
        if (!_bulkDirty)
            return;
        _bulkDirty = false;
        RequestUpdate();
    }

    private void RequestUpdate(bool immediate = false)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_attached)
            return;

        // Coalesce roster edits on a timer. Do not cancel an in-flight warmup here:
        // opening the operator list can fire bindings, and aborting would make the
        // first visit look like loading only starts after navigation.
        _debounce.Stop();
        if (AppState.IsBulkUpdating)
        {
            _bulkDirty = true;
            return;
        }
        if (immediate)
            QueueSnapshot();
        else
            _debounce.Start();
    }

    private void CancelWorker()
    {
        var cancellation = _cancellation;
        _cancellation = null;
        if (cancellation is null)
            return;
        try
        {
            cancellation.Cancel();
        }
        catch (Exception ex)
        {
            AppState.LogTrace($"取消图片预取失败：{ex}");
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void OnDebounceElapsed(object? sender, EventArgs e)
    {
        _debounce.Stop();
        QueueSnapshot();
    }

    private void QueueSnapshot()
    {
        if (!_attached)
            return;
        if (AppState.IsBulkUpdating)
        {
            // Preserve a previously requested snapshot if a bulk scope spans the timer tick.
            _bulkDirty = true;
            return;
        }

        try
        {
            // Materialize on the UI thread, with no Staff/Level references escaping it.
            var snapshot = AppState.StaffList
                .Where(staff => !string.IsNullOrWhiteSpace(staff.SourceId))
                .Select(staff => (SourceId: staff.SourceId!, Elite2: staff.Level.EliteLevel >= FieldLimits.MaxElite))
                .Distinct()
                .ToImmutableArray();
            if (!_snapshot.IsDefault &&
                _snapshot.SequenceEqual(snapshot) &&
                _cancellation is { IsCancellationRequested: false })
                return;

            CancelWorker();
            _snapshot = snapshot;
            _cancellation = new CancellationTokenSource();
            _worker = RunAfterAsync(_worker, snapshot, _cancellation.Token);
        }
        catch (Exception ex)
        {
            AppState.LogTrace($"生成图片预取快照失败：{ex}");
        }
    }

    private static async Task RunAfterAsync(
        Task previous,
        ImmutableArray<(string SourceId, bool Elite2)> snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            // Do not cancel this wait: even a canceled predecessor must finish first.
            await previous.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(async () =>
            {
                // Both pages use the same half-body images; prepare these before table avatars.
                var groups = snapshot.Select(staff => OperatorArt.Portrait(staff.SourceId, staff.Elite2))
                    .Concat(snapshot.Select(staff => OperatorArt.Avatar(staff.SourceId, staff.Elite2)))
                    .Where(group => group.Count > 0)
                    .ToArray();
                AppState.LogTrace($"后台预取图片 {groups.Length} 张");
                ArtImage.SetRosterSources(groups);
                var progress = Stopwatch.StartNew();
                await PublishAsync(false);
                foreach (var group in groups)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ArtImage.RememberPreparedSource(group))
                        continue;
                    if (ArtImage.PreloadCapacityReached)
                        break;
                    await ArtImage.PrefetchAsync(group, cancellationToken).ConfigureAwait(false);
                    if (progress.ElapsedMilliseconds >= 250)
                    {
                        await PublishAsync(false);
                        progress.Restart();
                    }
                }
                await PublishAsync(true);

                async Task PublishAsync(bool finished)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!cancellationToken.IsCancellationRequested)
                            ArtImage.ReportPreloadProgress(finished);
                    }, DispatcherPriority.Background);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Replaced snapshot or detached shell.
        }
        catch (Exception ex)
        {
            AppState.LogTrace($"后台图片预取失败：{ex}");
        }
    }
}
