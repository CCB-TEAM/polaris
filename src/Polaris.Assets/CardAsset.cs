using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace Polaris.Assets;

/// <summary>资产里没有 ClassExport（贴图/数据资产等）。</summary>
public sealed class NotACardAssetException(string message) : Exception(message);

/// <summary>父类是 /Game 蓝图资产（M2 只做原生父类）。</summary>
public sealed class UnsupportedParentException(string message) : Exception(message);

/// <summary>成员在类链上找不到。</summary>
public sealed class MemberNotFoundException(string message, IReadOnlyList<string> candidates) : Exception(message)
{
    public IReadOnlyList<string> Candidates { get; } = candidates;
}

/// <summary>类链上的一个成员（M2 只写原生父类的）。</summary>
/// <param name="Name">成员名（大小写敏感）。</param>
/// <param name="PropertyType">归一化类型名，如 <c>IntProperty</c> / <c>BoolProperty</c>。</param>
/// <param name="DeclaringClass">声明它的类（usmap schema 全路径或本类名）。</param>
/// <param name="IsOwnProperty">true = 本 BP 类自己的属性（M2 不允许改）。</param>
public sealed record NativeMember(string Name, string PropertyType, string DeclaringClass, bool IsOwnProperty);

/// <summary>写盘结果（两半）。</summary>
public sealed record SavedAsset(byte[] UAsset, byte[] UExp, string UAssetPath, string UExpPath);

/// <summary>
/// 一张卡的只读模型：类 + CDO + 原生父类 schema。
///
/// <para>
/// 只做「按 <c>RF_ClassDefaultObject</c> 找 CDO」和「沿 <c>SuperStruct</c> 走到 /Script 原生父类」，
/// 不硬编码导出下标（kardseditor 的 <c>Exports[1]</c> 只对卡成立）。
/// </para>
/// </summary>
public sealed class CardAsset
{
    public UAsset Asset { get; }
    public Usmap Usmap { get; }
    public ClassExport ClassExport { get; }
    public NormalExport Cdo { get; }
    public string SourcePath { get; }

    /// <summary>usmap 的可写副本路径（UAssetAPI 以读写模式打开）。</summary>
    public string UsmapPath { get; }

    /// <summary>输入资产的原始字节（diff 基线；UAssetAPI 打开的是可写副本）。</summary>
    public byte[] OriginalUAssetBytes { get; }

    /// <summary>输入 .uexp 的原始字节。</summary>
    public byte[] OriginalUExpBytes { get; }

    /// <summary>原生父类的 usmap 全路径，如 <c>/Script/kards.BaseCardObject</c>。</summary>
    public string NativeParentPath { get; }

    private CardAsset(
        UAsset asset,
        Usmap usmap,
        ClassExport classExport,
        NormalExport cdo,
        string sourcePath,
        string usmapPath,
        string nativeParentPath,
        byte[] originalUAssetBytes,
        byte[] originalUExpBytes)
    {
        Asset = asset;
        Usmap = usmap;
        ClassExport = classExport;
        Cdo = cdo;
        SourcePath = sourcePath;
        UsmapPath = usmapPath;
        NativeParentPath = nativeParentPath;
        OriginalUAssetBytes = originalUAssetBytes;
        OriginalUExpBytes = originalUExpBytes;
    }

    public static CardAsset Open(string uassetPath, string usmapPath, string? workDir = null)
    {
        if (!File.Exists(uassetPath))
        {
            throw new FileNotFoundException($"资产不存在: {uassetPath}", uassetPath);
        }

        var uexpPath = Path.ChangeExtension(uassetPath, ".uexp");
        if (!File.Exists(uexpPath))
        {
            throw new FileNotFoundException($"缺 .uexp: {uexpPath}", uexpPath);
        }

        if (!File.Exists(usmapPath))
        {
            throw new FileNotFoundException($"usmap 不存在: {usmapPath}", usmapPath);
        }

        // UAssetAPI 用 File.Open(ReadWrite) 打开 usmap：只读位置会抛 UnauthorizedAccessException，
        // 所以先复制一份到可写目录。
        var work = workDir ?? Path.Combine(Path.GetTempPath(), "polaris-usmap");
        Directory.CreateDirectory(work);
        var usmapCopy = Path.Combine(work, Path.GetFileName(usmapPath));
        if (!File.Exists(usmapCopy) || !BytesEqual(File.ReadAllBytes(usmapCopy), File.ReadAllBytes(usmapPath)))
        {
            File.Copy(usmapPath, usmapCopy, overwrite: true);
        }

        // 同理：UAssetAPI 也用 ReadWrite 打开 uasset/uexp，输入目录只读时必须先复制。
        // 子目录按完整路径的短哈希分桶，避免不同目录下同名卡互相覆盖。
        var bucket = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(uassetPath))))[..8];
        var assetDir = Path.Combine(work, "assets", bucket);
        Directory.CreateDirectory(assetDir);
        var copiedUAsset = Path.Combine(assetDir, Path.GetFileName(uassetPath));
        var copiedUExp = Path.ChangeExtension(copiedUAsset, ".uexp");
        var uassetBytes = File.ReadAllBytes(uassetPath);
        var uexpBytes = File.ReadAllBytes(uexpPath);
        if (!File.Exists(copiedUAsset) || !BytesEqual(File.ReadAllBytes(copiedUAsset), uassetBytes))
        {
            File.Copy(uassetPath, copiedUAsset, overwrite: true);
        }

        if (!File.Exists(copiedUExp) || !BytesEqual(File.ReadAllBytes(copiedUExp), uexpBytes))
        {
            File.Copy(uexpPath, copiedUExp, overwrite: true);
        }

        var usmap = new Usmap(usmapCopy);
        var asset = new UAsset(copiedUAsset, EngineVersion.VER_UE5_6, usmap);

        var cls = asset.Exports.OfType<ClassExport>().FirstOrDefault()
            ?? throw new NotACardAssetException($"{Path.GetFileName(uassetPath)} 里没有 ClassExport");

        var cdo = asset.Exports.OfType<NormalExport>()
            .FirstOrDefault(e => e.ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject))
            ?? throw new NotACardAssetException($"{cls.ObjectName} 没有 CDO 导出（RF_ClassDefaultObject）");

        if (cls.ClassDefaultObject is null || cls.ClassDefaultObject.Index <= 0
            || !ReferenceEquals(asset.Exports[cls.ClassDefaultObject.Index - 1], cdo))
        {
            throw new NotACardAssetException(
                $"{cls.ObjectName}.ClassDefaultObject 与 RF_ClassDefaultObject 导出一致性校验失败");
        }

        var parentPath = NativeParentPathOf(asset, cls);
        return new CardAsset(asset, usmap, cls, cdo, uassetPath, usmapCopy, parentPath, uassetBytes, uexpBytes);
    }

    /// <summary>CDO 当前全部属性（名字 → 渲染值）；顺序即 <c>Data</c> 顺序。</summary>
    public IReadOnlyDictionary<string, string> Snapshot()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in Cdo.Data)
        {
            map[p.Name.ToString()] = Render(p);
        }

        return map;
    }

    /// <summary>在本类属性与原生父类 schema 链里找成员。</summary>
    public NativeMember ResolveMember(string name)
    {
        var own = ClassExport.LoadedProperties?
            .FirstOrDefault(p => string.Equals(p.Name.ToString(), name, StringComparison.Ordinal));
        if (own is not null)
        {
            return new NativeMember(name, NormalizeType(own.GetType().Name), ClassExport.ObjectName.ToString(), true);
        }

        var schema = FindSchema(NativeParentPath)
            ?? throw new MemberNotFoundException(
                $"usmap 里没有原生父类 {NativeParentPath}", []);

        for (var s = schema; s is not null; s = FindSchema(s.SuperType))
        {
            foreach (var kv in s.Properties.OrderBy(k => k.Key))
            {
                if (!string.Equals(kv.Value.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                var type = kv.Value.PropertyData?.Type.ToString() ?? "?";
                return new NativeMember(name, NormalizeType(type), SchemaKeyOf(s) ?? NativeParentPath, false);
            }

            if (string.IsNullOrEmpty(s.SuperType))
            {
                break;
            }
        }

        throw new MemberNotFoundException(
            $"{ClassExport.ObjectName} 的类链上没有成员 {name}",
            Candidates());
    }

    private IReadOnlyList<string> Candidates()
    {
        var list = new List<string>();
        list.AddRange(ClassExport.LoadedProperties?.Select(p => p.Name.ToString()) ?? []);
        var schema = FindSchema(NativeParentPath);
        for (var s = schema; s is not null; s = FindSchema(s.SuperType))
        {
            list.AddRange(s.Properties.Values.Select(p => p.Name));
            if (string.IsNullOrEmpty(s.SuperType))
            {
                break;
            }
        }

        return list;
    }

    public SavedAsset Save(string outDir, string? fileName = null)
    {
        Directory.CreateDirectory(outDir);
        var name = fileName ?? Path.GetFileName(SourcePath);
        var outPath = Path.Combine(outDir, name);
        if (string.Equals(
                Path.GetFullPath(outPath),
                Path.GetFullPath(SourcePath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("拒绝覆盖输入资产");
        }

        Asset.Write(outPath);
        var uexp = Path.ChangeExtension(outPath, ".uexp");
        if (!File.Exists(uexp))
        {
            throw new IOException($"写入后没有 .uexp: {uexp}");
        }

        return new SavedAsset(File.ReadAllBytes(outPath), File.ReadAllBytes(uexp), outPath, uexp);
    }

    // ---------- 内部 ----------

    private static string NativeParentPathOf(UAsset asset, ClassExport cls)
    {
        var super = cls.SuperStruct;
        if (super is null || super.Index == 0)
        {
            throw new UnsupportedParentException($"{cls.ObjectName} 没有父类（SuperStruct 为空）");
        }

        if (super.Index > 0)
        {
            var parentExport = asset.Exports[super.Index - 1];
            throw new UnsupportedParentException(
                $"父类是同一资产里的导出 {parentExport.ObjectName}；M2 只支持 /Script 原生父类");
        }

        var import = asset.Imports[-super.Index - 1];
        var path = ImportObjectPath(asset, import);
        if (!path.StartsWith("/Script/", StringComparison.Ordinal))
        {
            throw new UnsupportedParentException(
                $"父类 {path} 不是 /Script 原生类；M2 只支持原生父类成员覆盖");
        }

        return path;
    }

    private static string ImportObjectPath(UAsset asset, Import import)
    {
        var parts = new List<string> { import.ObjectName.ToString() };
        var outer = import.OuterIndex;
        var guard = 0;
        while (outer is not null && outer.Index < 0 && guard++ < 64)
        {
            var oi = asset.Imports[-outer.Index - 1];
            if (string.Equals(oi.ClassName?.ToString(), "Package", StringComparison.Ordinal))
            {
                return $"{oi.ObjectName}.{string.Join(".", parts)}";
            }

            parts.Insert(0, oi.ObjectName.ToString());
            outer = oi.OuterIndex;
        }

        return string.Join(".", parts);
    }

    private UsmapSchema? FindSchema(string? nameOrPath)
    {
        if (string.IsNullOrEmpty(nameOrPath))
        {
            return null;
        }

        if (Usmap.Schemas.TryGetValue(nameOrPath, out var exact))
        {
            return exact;
        }

        var shortName = nameOrPath.Contains('.')
            ? nameOrPath[(nameOrPath.LastIndexOf('.') + 1)..]
            : nameOrPath;

        var key = Usmap.Schemas.Keys.FirstOrDefault(k =>
            string.Equals(k, shortName, StringComparison.Ordinal)
            || k.EndsWith("." + shortName, StringComparison.Ordinal));

        return key is null ? null : Usmap.Schemas[key];
    }

    private string? SchemaKeyOf(UsmapSchema schema)
        => Usmap.Schemas.FirstOrDefault(kv => ReferenceEquals(kv.Value, schema)).Key;

    private static string NormalizeType(string typeName)
        => typeName.StartsWith('F') && typeName.EndsWith("Property", StringComparison.Ordinal)
            ? typeName[1..]
            : typeName;

    private static string Render(UAssetAPI.PropertyTypes.Objects.PropertyData p) => p switch
    {
        UAssetAPI.PropertyTypes.Objects.IntPropertyData i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        UAssetAPI.PropertyTypes.Objects.BoolPropertyData b => b.Value ? "true" : "false",
        UAssetAPI.PropertyTypes.Objects.FloatPropertyData f => f.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        UAssetAPI.PropertyTypes.Objects.EnumPropertyData e => e.Value.ToString(),
        _ => $"{p.GetType().Name}:{p.RawValue}",
    };

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }
}
