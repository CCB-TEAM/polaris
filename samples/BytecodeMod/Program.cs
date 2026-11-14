// 字节码 mod 示例：读 Mod.cs → Roslyn 编译 → 从注解得到「目标资产 + 目标函数」
// → 翻译成 Kismet 字节码 → 写回资产 → 出 mod pak。
//
// 用法：
//   dotnet run --project samples/BytecodeMod -- [contentRoot] [usmap] [outDir]
//
// 目标资产/函数来自 Mod.cs 上的 [PolarisAsset]/[PolarisReplace]，CLI 只给环境路径。
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Polaris.Assets;
using Polaris.Compiler;
using Polaris.Pak;
using UAssetAPI.UnrealTypes;

var contentRoot = args.Length > 0
    ? args[0]
    : @"D:\devenv\fmodel\Output\Exports\kards\Content";
var usmapPath = args.Length > 1 ? args[1] : @"D:\files\projects\.tmp\polaris\kards.usmap";
var outDir = args.Length > 2 ? args[2] : Path.Combine(AppContext.BaseDirectory, "out");

var modSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Mod.cs"));

// ① Roslyn：把 Mod.cs 编成编译单元（引用 TPA + 运行时 + 生成的 SDK）
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
    .Split(Path.PathSeparator)
    .Select(p => MetadataReference.CreateFromFile(p))
    .ToList();
references.Add(MetadataReference.CreateFromFile(typeof(Polaris.Sdk.UObject).Assembly.Location));
references.Add(MetadataReference.CreateFromFile(typeof(Polaris.Sdk.Kards.CardFunctionsStub).Assembly.Location));

var compilation = CSharpCompilation.Create(
    "BytecodeMod",
    [CSharpSyntaxTree.ParseText(modSource)],
    references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

var diagnostics = compilation.GetDiagnostics()
    .Where(d => d.Severity == DiagnosticSeverity.Error)
    .ToList();
if (diagnostics.Count > 0)
{
    Console.Error.WriteLine("Mod.cs 编译失败：");
    foreach (var d in diagnostics)
    {
        Console.Error.WriteLine("  " + d);
    }

    return 1;
}

// ② 从数据注解得到「目标资产 + 目标函数」
var targets = ModPlanBuilder.FindTargets(compilation);
if (targets.Count == 0)
{
    Console.Error.WriteLine("Mod.cs 里没有 [PolarisAsset] + [PolarisReplace] 标注");
    return 1;
}

var target = targets[0];
var assetPath = ResolveAsset(contentRoot, target.AssetPath);
Console.WriteLine($"目标     {Path.GetFileName(assetPath)} :: {target.FunctionName}（{targets.Count} 条注解）");

// ③ 打开目标资产，编译方法体
var card = CardAsset.Open(assetPath, usmapPath, Path.Combine(outDir, "_work"));
var before = FunctionBytes.Hashes(card.Asset);
var imports = new AssetImportTable(card.Asset);

var targetExport = card.Asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>()
    .FirstOrDefault(f => f.ObjectName.ToString() == target.FunctionName)
    ?? throw new InvalidOperationException($"资产里没有函数 {target.FunctionName}");
var classExport = card.Asset.Exports.OfType<UAssetAPI.ExportTypes.ClassExport>().First();
var functionIndex = new FPackageIndex(card.Asset.Exports.IndexOf(targetExport) + 1);
var classIndex = new FPackageIndex(card.Asset.Exports.IndexOf(classExport) + 1);

var bytecode = Translator.Compile(
    compilation,
    target.Method,
    card.Asset,
    imports,
    functionIndex,
    classIndex,
    targetExport);

var check = BytecodeCheck.Run(bytecode, card.Asset);
if (!check.Ok)
{
    Console.Error.WriteLine("字节码自校验失败：" + string.Join("; ", check.Errors));
    return 1;
}

// ④ 写回 + 保存
var oldCount = FunctionWriter.Replace(card.Asset, target.FunctionName, bytecode);
var saved = card.Save(outDir);

// ⑤ 重开验证：目标函数变了、其它函数字节不变
var reopened = CardAsset.Open(saved.UAssetPath, usmapPath, Path.Combine(outDir, "_verify"));
var after = FunctionBytes.Hashes(reopened.Asset);
foreach (var (name, hash) in before)
{
    if (name != target.FunctionName && after[name] != hash)
    {
        Console.Error.WriteLine($"其他函数被改动: {name}");
        return 1;
    }
}

var reopenedCheck = BytecodeCheck.Run(
    reopened.Asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>()
        .First(f => f.ObjectName.ToString() == target.FunctionName).ScriptBytecode!,
    reopened.Asset);
if (!reopenedCheck.Ok)
{
    Console.Error.WriteLine("写回后自校验失败：" + string.Join("; ", reopenedCheck.Errors));
    return 1;
}

// ⑥ 出 mod pak
var relative = ModPak.RelativeContentPath(assetPath);
var pak = ModPak.Build(
    [
        new PakFile(relative, saved.UAsset),
        new PakFile(Path.ChangeExtension(relative, ".uexp"), saved.UExp),
    ],
    Path.Combine(outDir, "mod.pak"));

Console.WriteLine($"mod      {target.FunctionName} {oldCount} -> {bytecode.Length} 条表达式");
Console.WriteLine($"字节码   {string.Join(", ", bytecode.Select(e => e.Token.ToString()))}");
Console.WriteLine($"校验     {(reopenedCheck.Ok ? "OK" : "FAIL")}（其余函数 {after.Count - 1} 个字节不变）");
Console.WriteLine($"资产     {saved.UAssetPath}");
Console.WriteLine($"pak      {pak.PakPath}（{pak.Bytes.Length} 字节 / {pak.Archive.Entries.Count} 条目）");
Console.WriteLine($"mount    {pak.Archive.MountPoint}");
return 0;

static string ResolveAsset(string contentRoot, string asset)
{
    if (asset.StartsWith("/Game/", StringComparison.Ordinal))
    {
        return Path.Combine(contentRoot, asset["/Game/".Length..].Replace('/', Path.DirectorySeparatorChar));
    }

    if (Path.IsPathRooted(asset))
    {
        return asset;
    }

    return Path.Combine(contentRoot, asset.Replace('/', Path.DirectorySeparatorChar));
}
