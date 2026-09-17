using System.Security.Cryptography;
using System.Text;

namespace arknights_random_team.Views;

/// <summary>
/// 立绘的本地磁盘缓存，让图片在**重启程序后**也不必重新下载。
///
/// 与浏览器自带的 HTTP 缓存是两件事：这里是自己落盘，
/// 因此清掉浏览器缓存、换网络环境、或 CDN 临时不可达时也仍然有图。
///
/// 浏览器端（WebAssembly）没有文件系统，一律不落盘，只靠内存缓存；
/// 这是平台限制，不是开关。
///
/// 缓存的是下载到的**原始字节**：解码必须重新做（位图无法可靠序列化），
/// 但省掉的是网络往返 —— 那才是耗时的大头。
/// </summary>
internal static class LocalImageStore
{
    private static readonly string? Folder = ResolveFolder();
    private static readonly HashSet<string> Failed = [];

    /// <summary>本地缓存是否可用（浏览器端为 false）。</summary>
    public static bool IsEnabled => Folder is not null;

    /// <summary>本次运行中从本地缓存读出的次数。</summary>
    public static int Hits { get; private set; }

    private static string? ResolveFolder()
    {
        if (OperatingSystem.IsBrowser())
            return null;

        try
        {
            // 放在应用自己的数据目录下，和 StaffList.xml 等文件同级，便于用户清理
            var root = AppState.DataDirectory;
            if (string.IsNullOrEmpty(root))
                return null;

            var dir = Path.Combine(root, "ArtCache");
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch
        {
            // 只读目录等情况：退化为纯内存缓存，不影响功能
            return null;
        }
    }

    private static string PathFor(Uri uri)
    {
        // 用哈希作文件名：地址里可能带 # 等不适合做文件名的字符
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri));
        return Path.Combine(Folder!, Convert.ToHexString(hash) + ".bin");
    }

    /// <summary>尝试读本地缓存；没有就返回 null。</summary>
    public static async Task<byte[]?> TryReadAsync(Uri uri)
    {
        if (Folder is null)
            return null;

        var path = PathFor(uri);
        if (Failed.Contains(path))
            return null;

        try
        {
            if (!File.Exists(path))
                return null;

            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            if (bytes.Length == 0)
                return null;

            Hits++;
            return bytes;
        }
        catch
        {
            Failed.Add(path);
            return null;
        }
    }

    /// <summary>写入本地缓存。失败只记录一次，不反复尝试。</summary>
    public static async Task WriteAsync(Uri uri, byte[] bytes)
    {
        if (Folder is null || bytes.Length == 0)
            return;

        var path = PathFor(uri);
        if (Failed.Contains(path))
            return;

        try
        {
            // 先写临时文件再替换，避免程序中途退出留下半截文件
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);

            if (File.Exists(path))
                File.Delete(path);

            File.Move(temp, path);
        }
        catch
        {
            Failed.Add(path);
        }
    }
}
