using Solstice.Pak;

namespace Polaris.Pak;

/// <summary>要打进 pak 的一个文件（路径相对 mount point，正斜杠）。</summary>
public sealed record PakFile(string RelativePath, byte[] Data);

/// <summary>打包结果（含读回校验后的归档）。</summary>
public sealed record BuiltPak(string PakPath, byte[] Bytes, PakArchive Archive);

public sealed class PakPathException(string message) : Exception(message);

/// <summary>
/// 在 <see cref="PakWriter"/>（vendored，v3/store）之上做「改完直接出 mod pak」：
/// 路径规范化 → 按名字排序 → 打包 → 读回校验（含 SHA-1 与确定性重建）。
///
/// <para>mount point 默认 <c>../../../kards/Content/</c>，实测自 KARDS 的
/// <c>Content/Paks/wide-mount.pak</c>。</para>
/// </summary>
public static class ModPak
{
    public const string DefaultMountPoint = "../../../kards/Content/";

    /// <summary>
    /// 把绝对路径变成 pak 内路径：优先按 <paramref name="contentRoot"/> 取相对路径，
    /// 否则找路径里的 <c>/Content/</c> 段。
    /// </summary>
    public static string RelativeContentPath(string filePath, string? contentRoot = null)
    {
        var full = Path.GetFullPath(filePath);
        string relative;
        if (!string.IsNullOrEmpty(contentRoot))
        {
            var root = Path.GetFullPath(contentRoot);
            relative = Path.GetRelativePath(root, full);
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                throw new PakPathException($"{filePath} 不在 content root {root} 下");
            }
        }
        else
        {
            var normalized = full.Replace('\\', '/');
            const string marker = "/Content/";
            var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                throw new PakPathException($"路径里没有 /Content/ 段，请给 --content-root: {filePath}");
            }

            relative = normalized[(idx + marker.Length)..];
        }

        return Normalize(relative);
    }

    /// <summary>把一组文件打成 pak 并写盘；返回前用 <see cref="PakArchive.Read"/> 校验。</summary>
    public static BuiltPak Build(
        IEnumerable<PakFile> files,
        string pakPath,
        string mountPoint = DefaultMountPoint)
    {
        // 按名字排序：同一组输入必得同一份 pak（便于 diff / 复现）
        var entries = files
            .Select(f => new PakEntry(Normalize(f.RelativePath), f.Data))
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();

        if (entries.Count == 0)
        {
            throw new PakPathException("没有要打包的文件");
        }

        var duplicates = entries.GroupBy(e => e.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new PakPathException("pak 内路径重复：" + string.Join("，", duplicates));
        }

        var bytes = PakWriter.Build(mountPoint, entries);
        var archive = PakArchive.Read(bytes);

        // 确定性自检：读回来的条目重新打包必须逐字节一致
        var rebuilt = PakWriter.Build(archive.MountPoint, archive.Entries);
        if (!rebuilt.AsSpan().SequenceEqual(bytes))
        {
            throw new InvalidOperationException("pak 重建结果与首次输出不一致（写入器非确定性）");
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(pakPath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(pakPath, bytes);
        return new BuiltPak(pakPath, bytes, archive);
    }

    /// <summary>把编辑产物（.uasset + .uexp）按源资产的相对路径打进 pak。</summary>
    public static BuiltPak BuildAssetPair(
        string sourceUassetPath,
        string contentRoot,
        byte[] uasset,
        byte[] uexp,
        string pakPath,
        string mountPoint = DefaultMountPoint)
    {
        var relative = RelativeContentPath(sourceUassetPath, contentRoot);
        var uexpRelative = Path.ChangeExtension(relative, ".uexp").Replace('\\', '/');
        return Build(
            [
                new PakFile(relative, uasset),
                new PakFile(uexpRelative, uexp),
            ],
            pakPath,
            mountPoint);
    }

    private static string Normalize(string path)
    {
        var p = path.Replace('\\', '/').TrimStart('/');
        if (p.Split('/').Any(seg => seg is "" or "." or ".."))
        {
            throw new PakPathException($"非法 pak 内路径: {path}");
        }

        if (!p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            && !p.EndsWith(".uexp", StringComparison.OrdinalIgnoreCase)
            && !p.EndsWith(".ubulk", StringComparison.OrdinalIgnoreCase)
            && !Path.HasExtension(p))
        {
            throw new PakPathException($"pak 内路径没有扩展名: {path}");
        }

        return p;
    }
}
