namespace Polaris.Assets;

/// <summary>两个字节数组的差异摘要。</summary>
/// <param name="BeforeSize">原长度。</param>
/// <param name="AfterSize">新长度。</param>
/// <param name="ChangedBytes">公共前缀里不同的字节数 + 长度差的绝对值。</param>
/// <param name="FirstOffsets">前 8 个不同位置（诊断用）。</param>
/// <param name="SizeDelta">新长度 - 原长度。</param>
public sealed record ByteDiff(
    int BeforeSize,
    int AfterSize,
    int ChangedBytes,
    IReadOnlyList<int> FirstOffsets)
{
    public int SizeDelta => AfterSize - BeforeSize;
}

public static class AssetDiff
{
    /// <summary>
    /// 逐字节比较两个文件内容。长尾的偏移（插入/删除导致的整体平移）也会计入
    /// <see cref="ByteDiff.ChangedBytes"/>，所以「只变 1 字节」这种断言只对定长替换成立。
    /// </summary>
    public static ByteDiff Compare(byte[] before, byte[] after)
    {
        var n = Math.Min(before.Length, after.Length);
        var offsets = new List<int>(8);
        var changed = 0;
        for (var i = 0; i < n; i++)
        {
            if (before[i] == after[i])
            {
                continue;
            }

            changed++;
            if (offsets.Count < 8)
            {
                offsets.Add(i);
            }
        }

        changed += Math.Abs(before.Length - after.Length);
        return new ByteDiff(before.Length, after.Length, changed, offsets);
    }
}
