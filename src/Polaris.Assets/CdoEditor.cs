using System.Globalization;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace Polaris.Assets;

/// <summary>CDO 写入值不合法（类型不支持/解析失败/类型不符）。</summary>
public sealed class CdoValueException(string message) : Exception(message);

/// <summary>写后校验失败（重开比对或函数字节校验）。</summary>
public sealed class VerificationFailedException(VerifyReport report)
    : Exception("写后校验失败：" + string.Join("；", report.Failures))
{
    public VerifyReport Report { get; } = report;
}

public enum PatchAction
{
    NoOp,
    Updated,
    Added,
    Removed,
}

/// <summary>一次 CDO 改动。</summary>
public sealed record CdoPatch(string Name, PatchAction Action, string? Before, string? After);

/// <summary>一次请求：<c>Value == null</c> 表示 Reset（删条目 = 回原生默认）。</summary>
public sealed record PatchSpec(string Name, string? Value)
{
    public static PatchSpec Set(string name, string value) => new(name, value);

    public static PatchSpec Reset(string name) => new(name, null);
}

/// <summary>写后校验结果。</summary>
public sealed record VerifyReport(
    bool Ok,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> CdoDiffs,
    IReadOnlyList<string> FunctionFailures,
    int FunctionCount);

/// <summary>一次编辑的完整报告。</summary>
public sealed record EditReport(
    IReadOnlyList<CdoPatch> Patches,
    SavedAsset Saved,
    VerifyReport Verify,
    ByteDiff DiffUAsset,
    ByteDiff DiffUExp);

/// <summary>
/// 按「原生父类成员覆盖」语义改 CDO：有则改、无则补、Reset 则删。
///
/// <para>
/// 原生父类的**默认值**在二进制里，离线拿不到，所以本类不猜「是否等于默认」：
/// 等于默认由 <see cref="Reset"/>（删条目）显式表达。
/// </para>
/// </summary>
public static class CdoEditor
{
    private static readonly HashSet<string> SupportedTypes =
    [
        "IntProperty",
        "BoolProperty",
        "FloatProperty",
    ];

    public static CdoPatch Set(CardAsset card, string name, string value)
    {
        var member = card.ResolveMember(name);
        if (member.IsOwnProperty)
        {
            throw new CdoValueException(
                $"{name} 是本 BP 类自己的属性（{member.PropertyType}）；M2 只覆盖原生父类成员");
        }

        if (!SupportedTypes.Contains(member.PropertyType))
        {
            throw new CdoValueException(
                $"{name} 的类型是 {member.PropertyType}；M2 只支持 {string.Join("/", SupportedTypes)}");
        }

        var existing = card.Cdo.Data.FirstOrDefault(d =>
            string.Equals(d.Name.ToString(), name, StringComparison.Ordinal));

        if (existing is null)
        {
            var created = CreateProperty(card, member, value);
            card.Cdo.Data.Add(created);
            return new CdoPatch(name, PatchAction.Added, null, Render(created));
        }

        var existingType = NormalizeDataType(existing.GetType().Name);
        if (!string.Equals(existingType, member.PropertyType, StringComparison.Ordinal))
        {
            throw new CdoValueException(
                $"{name} 在 CDO 里的类型是 {existingType}，schema 说是 {member.PropertyType}");
        }

        var before = Render(existing);
        UpdateInPlace(existing, member.PropertyType, value);
        return new CdoPatch(name, PatchAction.Updated, before, Render(existing));
    }

    public static CdoPatch Reset(CardAsset card, string name)
    {
        // 删条目也要求名字在类链上存在，避免手滑删掉别的字段
        _ = card.ResolveMember(name);
        var existing = card.Cdo.Data.FirstOrDefault(d =>
            string.Equals(d.Name.ToString(), name, StringComparison.Ordinal));
        if (existing is null)
        {
            return new CdoPatch(name, PatchAction.NoOp, null, null);
        }

        var before = Render(existing);
        card.Cdo.Data.Remove(existing);
        return new CdoPatch(name, PatchAction.Removed, before, null);
    }

    public static EditReport Apply(
        CardAsset card,
        IReadOnlyList<PatchSpec> patches,
        string outDir,
        string workDir)
    {
        var before = card.Snapshot();
        var applied = new List<CdoPatch>(patches.Count);
        foreach (var spec in patches)
        {
            applied.Add(spec.Value is null
                ? Reset(card, spec.Name)
                : Set(card, spec.Name, spec.Value));
        }

        var saved = card.Save(outDir);
        var verify = Verify(card, before, patches, saved, workDir);

        var diffUAsset = AssetDiff.Compare(card.OriginalUAssetBytes, saved.UAsset);
        var diffUExp = AssetDiff.Compare(card.OriginalUExpBytes, saved.UExp);

        var report = new EditReport(applied, saved, verify, diffUAsset, diffUExp);
        if (!verify.Ok)
        {
            throw new VerificationFailedException(verify);
        }

        return report;
    }

    public static VerifyReport Verify(
        CardAsset card,
        IReadOnlyDictionary<string, string> before,
        IReadOnlyList<PatchSpec> patches,
        SavedAsset saved,
        string workDir)
    {
        var failures = new List<string>();
        var cdoDiffs = new List<string>();

        var reopened = CardAsset.Open(saved.UAssetPath, card.UsmapPath, workDir);
        var after = reopened.Snapshot();

        var touched = new HashSet<string>(patches.Select(p => p.Name), StringComparer.Ordinal);

        // ① 没动的字段必须原样
        foreach (var (key, value) in before)
        {
            if (touched.Contains(key))
            {
                continue;
            }

            if (!after.TryGetValue(key, out var now))
            {
                cdoDiffs.Add($"{key}: 消失");
                continue;
            }

            if (!string.Equals(now, value, StringComparison.Ordinal))
            {
                cdoDiffs.Add($"{key}: {value} -> {now}");
            }
        }

        // ② 没被 patch 的新字段（意外新增）也算错
        foreach (var key in after.Keys)
        {
            if (!before.ContainsKey(key) && !touched.Contains(key))
            {
                cdoDiffs.Add($"{key}: 意外新增 = {after[key]}");
            }
        }

        // ③ 目标值必须落地
        foreach (var spec in patches)
        {
            if (spec.Value is null)
            {
                if (after.ContainsKey(spec.Name))
                {
                    cdoDiffs.Add($"{spec.Name}: Reset 后仍存在 = {after[spec.Name]}");
                }

                continue;
            }

            if (!after.TryGetValue(spec.Name, out var now))
            {
                cdoDiffs.Add($"{spec.Name}: 目标值未写入（缺失）");
                continue;
            }

            var expected = RenderExpected(reopened, spec);
            if (!string.Equals(now, expected, StringComparison.Ordinal))
            {
                cdoDiffs.Add($"{spec.Name}: 期望 {expected}，实际 {now}");
            }
        }

        if (cdoDiffs.Count > 0)
        {
            failures.Add("CDO 重开比对不一致：" + string.Join("；", cdoDiffs));
        }

        // ④ 全部函数的字节码必须逐字节不变（不依赖任何外部字节码库：
        //    用 UAssetAPI 把每个函数重新序列化后比 SHA-256）
        var functionFailures = new List<string>();
        var beforeHashes = FunctionBytes.Hashes(card.Asset);
        var functionCount = beforeHashes.Count;
        try
        {
            var afterHashes = FunctionBytes.Hashes(reopened.Asset);
            if (afterHashes.Count != beforeHashes.Count)
            {
                functionFailures.Add($"函数数量 {beforeHashes.Count} -> {afterHashes.Count}");
            }

            foreach (var (name, hash) in beforeHashes)
            {
                if (!afterHashes.TryGetValue(name, out var now))
                {
                    functionFailures.Add($"{name}: 重开后消失");
                }
                else if (!string.Equals(hash, now, StringComparison.Ordinal))
                {
                    functionFailures.Add($"{name}: 字节码发生变化");
                }
            }
        }
        catch (Exception ex)
        {
            functionFailures.Add($"字节码校验无法执行: {ex.Message}");
        }

        if (functionFailures.Count > 0)
        {
            failures.Add("函数字节码校验失败：" + string.Join("；", functionFailures));
        }

        return new VerifyReport(failures.Count == 0, failures, cdoDiffs, functionFailures, functionCount);
    }

    // ---------- 内部 ----------

    private static PropertyData CreateProperty(CardAsset card, NativeMember member, string value) => member.PropertyType switch
    {
        "IntProperty" => new IntPropertyData(new FName(card.Asset, member.Name))
        {
            Value = int.Parse(value, CultureInfo.InvariantCulture),
        },
        "BoolProperty" => new BoolPropertyData(new FName(card.Asset, member.Name))
        {
            Value = ParseBool(value),
        },
        "FloatProperty" => new FloatPropertyData(new FName(card.Asset, member.Name))
        {
            Value = float.Parse(value, CultureInfo.InvariantCulture),
        },
        _ => throw new CdoValueException($"{member.Name}: 不支持的属性类型 {member.PropertyType}"),
    };

    private static void UpdateInPlace(PropertyData data, string propertyType, string value)
    {
        switch (propertyType)
        {
            case "IntProperty" when data is IntPropertyData i:
                i.Value = int.Parse(value, CultureInfo.InvariantCulture);
                break;
            case "BoolProperty" when data is BoolPropertyData b:
                b.Value = ParseBool(value);
                break;
            case "FloatProperty" when data is FloatPropertyData f:
                f.Value = float.Parse(value, CultureInfo.InvariantCulture);
                break;
            default:
                throw new CdoValueException($"{data.Name}: 无法把 {propertyType} 写进 {data.GetType().Name}");
        }
    }

    private static bool ParseBool(string value) => value.ToLowerInvariant() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => throw new CdoValueException($"不是合法 bool: {value}"),
    };

    private static string RenderExpected(CardAsset reopened, PatchSpec spec)
    {
        var data = reopened.Cdo.Data.First(d =>
            string.Equals(d.Name.ToString(), spec.Name, StringComparison.Ordinal));
        return Render(data);
    }

    private static string Render(PropertyData p) => p switch
    {
        IntPropertyData i => i.Value.ToString(CultureInfo.InvariantCulture),
        BoolPropertyData b => b.Value ? "true" : "false",
        FloatPropertyData f => f.Value.ToString("R", CultureInfo.InvariantCulture),
        EnumPropertyData e => e.Value.ToString(),
        _ => $"{p.GetType().Name}:{p.RawValue}",
    };

    private static string NormalizeDataType(string typeName)
        => typeName.EndsWith("PropertyData", StringComparison.Ordinal)
            ? typeName[..^"Data".Length]
            : typeName;
}
