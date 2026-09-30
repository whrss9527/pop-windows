using System.IO.Compression;

namespace Pop.Core;

public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

/// 下载好的更新包：校验、解压、找出新的 Pop.exe
public static class UpdatePackage
{
    public const string ExecutableName = "Pop.exe";

    /// 校验和不对直接拒绝；对了再解压到 stagingDirectory，返回新 Pop.exe 的路径
    public static string Prepare(string zipPath, string packageName, string checksumsText, string stagingDirectory)
    {
        var sums = Checksums.Parse(checksumsText);
        var actual = Checksums.Sha256File(zipPath);
        if (!sums.ContainsKey(packageName))
            throw new UpdateException($"校验和清单里没有 {packageName}");
        if (!Checksums.Matches(sums, packageName, actual))
            throw new UpdateException("下载的安装包校验和不对，已放弃更新");

        if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        Directory.CreateDirectory(stagingDirectory);
        var root = Path.GetFullPath(stagingDirectory) + Path.DirectorySeparatorChar;
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(stagingDirectory, entry.FullName));
                // 不允许解压到目标文件夹外面（../）
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateException($"安装包里有不安全的路径：{entry.FullName}");
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        var exe = Directory.EnumerateFiles(stagingDirectory, ExecutableName, SearchOption.AllDirectories)
            .OrderBy(p => p.Length)
            .FirstOrDefault();
        return exe ?? throw new UpdateException($"安装包里没有 {ExecutableName}");
    }
}

/// 把正在运行的 Pop.exe 换成新的。Windows 不允许覆盖正在运行的 exe，但允许改名：
/// 先把自己改名成 Pop.exe.old，再把新文件放到原来的位置，下次启动时删掉 .old
public static class ExecutableSwap
{
    public static string OldPath(string currentExe) => currentExe + ".old";

    public static void Swap(string currentExe, string newExe)
    {
        var old = OldPath(currentExe);
        TryDelete(old);
        if (File.Exists(old)) throw new UpdateException($"上一次更新留下的 {Path.GetFileName(old)} 删不掉，请退出所有 Pop 后再试");
        File.Move(currentExe, old);
        try
        {
            File.Copy(newExe, currentExe, overwrite: false);
        }
        catch (Exception e)
        {
            // 放不进去就把原来的改回来，保证 Pop 还能启动
            try
            {
                if (File.Exists(currentExe)) File.Delete(currentExe);
                File.Move(old, currentExe);
            }
            catch (Exception rollback)
            {
                throw new UpdateException($"替换失败，恢复原来的版本也失败了：{rollback.Message}", e);
            }
            throw new UpdateException($"替换 {Path.GetFileName(currentExe)} 失败：{e.Message}", e);
        }
    }

    /// 启动时清理上一次更新留下的旧文件；旧进程可能还没完全退出，删不掉就下次再删
    public static bool CleanUp(string currentExe) => TryDelete(OldPath(currentExe));

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// 能不能在这个文件夹里写文件（装在 Program Files 里时不能）
    public static bool CanWrite(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".pop-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
