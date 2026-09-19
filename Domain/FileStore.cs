namespace arknights_random_team.Domain;

internal enum BackupRestoreResult
{
    Missing,
    Restored,
    Failed
}

internal enum QuarantineResult
{
    Moved,
    Missing,
    Failed
}

internal enum FileProbeResult
{
    Missing,
    Present,
    Unavailable
}

/// <summary>Small, shared helpers for atomic writes, backup restore and corrupt-file quarantine.</summary>
internal static class FileStore
{
    public static void WriteAtomic(string path, Action<string> write)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            write(temp);
            if (FilesEqual(temp, path))
                return;

            try
            {
                File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
            }
            catch (FileNotFoundException)
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            TryDelete(temp);
        }
    }

    public static void WriteAllTextAtomic(string path, string contents) =>
        WriteAtomic(path, temp => File.WriteAllText(temp, contents));

    /// <summary>Copies a backup into a missing main path. The backup remains available.</summary>
    public static BackupRestoreResult TryRestoreBackup(string path)
    {
        try
        {
            File.Copy(path + ".bak", path, overwrite: false);
            return BackupRestoreResult.Restored;
        }
        catch (FileNotFoundException)
        {
            return BackupRestoreResult.Missing;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return BackupRestoreResult.Failed;
        }
    }

    private static bool FilesEqual(string left, string right)
    {
        try
        {
            using var leftStream = File.Open(left, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var rightStream = File.Open(right, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (leftStream.Length != rightStream.Length)
                return false;

            Span<byte> leftBuffer = stackalloc byte[4096];
            Span<byte> rightBuffer = stackalloc byte[4096];
            while (true)
            {
                var leftRead = leftStream.Read(leftBuffer);
                var rightRead = rightStream.Read(rightBuffer);
                if (leftRead != rightRead || !leftBuffer[..leftRead].SequenceEqual(rightBuffer[..rightRead]))
                    return false;
                if (leftRead == 0)
                    return true;
            }
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    public static FileProbeResult Probe(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return FileProbeResult.Present;
        }
        catch (FileNotFoundException)
        {
            return FileProbeResult.Missing;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return FileProbeResult.Unavailable;
        }
    }

    public static QuarantineResult TryQuarantine(string path, out string? quarantinedPath)
    {
        quarantinedPath = null;
        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var target = Path.Combine(directory, $"{name}.corrupt-{stamp}-{Guid.NewGuid():N}{extension}");

        try
        {
            File.Move(path, target);
            quarantinedPath = target;
            return QuarantineResult.Moved;
        }
        catch (FileNotFoundException)
        {
            return QuarantineResult.Missing;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return QuarantineResult.Failed;
        }
    }

    public static void DeleteIfDuplicate(string keep, string candidate)
    {
        try
        {
            if (FilesEqual(keep, candidate))
                File.Delete(candidate);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            // Both stable quarantine files remain available if deduplication fails.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            // A stale temporary file is harmless.
        }
    }

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
