using System.Security.Cryptography;
using System.Text;

namespace arknights_random_team.Views;

/// <summary>Best-effort disk cache for downloaded operator art. Browser builds use memory only.</summary>
internal static class LocalImageStore
{
    // Encoded avatar + illustration files for a full roster need more room than visible-only caching.
    private static readonly long DiskBudget =
        long.TryParse(Environment.GetEnvironmentVariable("ARTIMAGE_DISK_BYTES"), out var budget) && budget > 0
            ? budget : 2L * 1024 * 1024 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan TrimInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TempMaxAge = TimeSpan.FromDays(1);
    private static readonly string? Folder = ResolveFolder();

    private static int _hits;
    private static int _trimming;
    private static int _trimScheduled;
    private static long _lastTrimTicks;

    public static int Hits => Volatile.Read(ref _hits);

    public static bool IsEnabled => Folder is not null;

    public static bool Contains(Uri uri)
    {
        if (Folder is null)
            return false;
        return TryInspect(PathFor(uri)) is { Size: > 0 } file &&
               DateTime.UtcNow - file.LastWriteUtc <= MaxAge;
    }

    private static string? ResolveFolder()
    {
        if (OperatingSystem.IsBrowser() || AppState.IsStorageReadOnly ||
            string.IsNullOrEmpty(AppState.DataDirectory))
            return null;

        try
        {
            var folder = Path.Combine(AppState.DataDirectory, "ArtCache");
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch
        {
            return null;
        }
    }

    private static string PathFor(Uri uri)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri));
        return Path.Combine(Folder!, Convert.ToHexString(hash) + ".bin");
    }

    public static async Task<byte[]?> TryReadAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (Folder is null)
            return null;

        ScheduleTrim();

        var path = PathFor(uri);
        try
        {
            if (!File.Exists(path))
                return null;

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0)
                return null;

            Interlocked.Increment(ref _hits);
            TouchIfStale(path);
            return bytes;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public static async Task WriteAsync(Uri uri, byte[] bytes)
    {
        if (Folder is null || bytes.Length == 0)
            return;

        var path = PathFor(uri);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
            TrimIfNeeded();
        }
        catch
        {
            // Caching is optional; network-loaded art remains usable.
        }
        finally
        {
            TryDelete(temp);
        }
    }

    public static void Invalidate(Uri uri)
    {
        if (Folder is not null)
            TryDelete(PathFor(uri));
    }

    private static void TouchIfStale(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromDays(1))
                info.LastWriteTimeUtc = DateTime.UtcNow;
        }
        catch
        {
            // Access-time bookkeeping must not turn a cache hit into a failure.
        }
    }

    private static void ScheduleTrim()
    {
        if (DateTime.UtcNow.Ticks - Volatile.Read(ref _lastTrimTicks) < TrimInterval.Ticks ||
            Interlocked.CompareExchange(ref _trimScheduled, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                TrimIfNeeded();
            }
            finally
            {
                Volatile.Write(ref _trimScheduled, 0);
            }
        });
    }

    private static void TrimIfNeeded()
    {
        if (Folder is null)
            return;

        var now = DateTime.UtcNow;
        if (now.Ticks - Volatile.Read(ref _lastTrimTicks) < TrimInterval.Ticks ||
            Interlocked.CompareExchange(ref _trimming, 1, 0) != 0)
        {
            return;
        }

        try
        {
            now = DateTime.UtcNow;
            if (now.Ticks - Volatile.Read(ref _lastTrimTicks) < TrimInterval.Ticks)
                return;

            Volatile.Write(ref _lastTrimTicks, now.Ticks);
            TrimCache(now);
        }
        finally
        {
            Volatile.Write(ref _trimming, 0);
        }
    }

    private static void TrimCache(DateTime now)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(Folder!, "*.tmp"))
            {
                if (TryInspect(path) is { } file && now - file.LastWriteUtc > TempMaxAge)
                    TryDelete(path);
            }

            var files = Directory.EnumerateFiles(Folder!, "*.bin")
                .Select(TryInspect)
                .OfType<CacheFile>()
                .OrderBy(file => file.LastWriteUtc)
                .ToList();
            var total = files.Sum(file => file.Size);

            foreach (var file in files)
            {
                if (now - file.LastWriteUtc <= MaxAge && total <= DiskBudget)
                    break;

                if (TryDelete(file.Path))
                    total -= file.Size;
            }
        }
        catch
        {
            // A failed cleanup should never affect image loading.
        }
    }

    private static CacheFile? TryInspect(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return new CacheFile(path, info.Length, info.LastWriteTimeUtc);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct CacheFile(string Path, long Size, DateTime LastWriteUtc);
}
