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

    public static void Swap(string currentExe, string newExe) => SwapAsync(currentExe, newExe).GetAwaiter().GetResult();

    /// 先在程序目录完整准备新文件，再改名替换；文件被短暂扫描时异步重试，不阻塞界面。
    public static async Task SwapAsync(string currentExe, string newExe)
    {
        var old = OldPath(currentExe);
        var staged = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(currentExe))!, $".pop-update-{Guid.NewGuid():N}.new");
        try
        {
            await RetrySharingViolationAsync(() =>
            {
                // 重试只清理本次独占的临时文件，不接触当前可运行的版本。
                File.Delete(staged);
                File.Copy(newExe, staged, overwrite: false);
            }).ConfigureAwait(false);
            await RetrySharingViolationAsync(() => File.Delete(old)).ConfigureAwait(false);
            await RetrySharingViolationAsync(() => File.Move(currentExe, old)).ConfigureAwait(false);
            try
            {
                await RetrySharingViolationAsync(() => File.Move(staged, currentExe)).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                try
                {
                    await RetrySharingViolationAsync(() => File.Move(old, currentExe)).ConfigureAwait(false);
                }
                catch (Exception rollback)
                {
                    throw new UpdateException($"替换失败，原版本保留在 {old}，自动恢复失败：{rollback.Message}", e);
                }
                throw new UpdateException($"替换 {Path.GetFileName(currentExe)} 失败，已恢复原版本：{e.Message}", e);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException($"无法替换 {Path.GetFileName(currentExe)}，原版本仍保留。请稍后重试：{e.Message}", e);
        }
        finally
        {
            TryDelete(staged);
        }
    }

    private static async Task RetrySharingViolationAsync(Action operation)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { operation(); return; }
            // 只重试 Windows 的共享/锁冲突；路径、权限和空间等错误立即交给调用方。
            catch (IOException e) when ((e.HResult & 0xffff) is 32 or 33 && attempt < 29)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
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
