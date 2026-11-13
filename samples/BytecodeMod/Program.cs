// 字节码 mod 示例：读 Mod.cs → Roslyn 编译 → 翻译成 Kismet 字节码 → 写回资产 → 出 mod pak。
//
// 用法：
//   dotnet run --project samples/BytecodeMod -- <资产.uasset> <usmap> <outDir> [目标函数名]
//
// 默认目标函数是 BP_CardFunctions 的 AddToBattleLog（一个平坦函数，适合做写回演示）。
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Polaris.Assets;
using Polaris.Compiler;
using Polaris.Pak;
using UAssetAPI.UnrealTypes;

var assetPath = args.Length > 0
    ? args[0]
    : @"D:\devenv\fmodel\Output\Exports\kards\Content\Blueprints\Cards\BP_CardFunctions.uasset";
var usmapPath = args.Length > 1 ? args[1] : @"D:\files\projects\.tmp\polaris\kards.usmap";
var outDir = args.Length > 2 ? args[2] : Path.Combine(AppContext.BaseDirectory, "out");
var targetFunction = args.Length > 3 ? args[3] : "AddToBattleLog";

var modSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Mod.cs"));

// ① Roslyn：把 Mod.cs 编成一个编译单元（引用 TPA + 运行时 + 生成的 SDK）
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

var method = compilation.GetTypeByMetadataName("AansMod")?
    .GetMembers("OnPlayed").OfType<IMethodSymbol>().SingleOrDefault()
    ?? throw new InvalidOperationException("Mod.cs 里没有 AansMod.OnPlayed");

// ② 打开目标资产，编译方法体
var card = CardAsset.Open(assetPath, usmapPath, Path.Combine(outDir, "_work"));
var before = FunctionBytes.Hashes(card.Asset);
var imports = new AssetImportTable(card.Asset);

// 目标函数的导出索引（参数/局部变量的 FFieldPath owner）与类的导出索引（成员变量 owner）
var targetExport = card.Asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>()
    .FirstOrDefault(f => f.ObjectName.ToString() == targetFunction)
    ?? throw new InvalidOperationException($"资产里没有函数 {targetFunction}");
var classExport = card.Asset.Exports.OfType<UAssetAPI.ExportTypes.ClassExport>().First();
var functionIndex = new FPackageIndex(card.Asset.Exports.IndexOf(targetExport) + 1);
var classIndex = new FPackageIndex(card.Asset.Exports.IndexOf(classExport) + 1);

var bytecode = Translator.Compile(
    compilation,
    method,
    card.Asset,
    imports,
    functionIndex,
    classIndex);

var check = BytecodeCheck.Run(bytecode, card.Asset);
if (!check.Ok)
{
    Console.Error.WriteLine("字节码自校验失败：" + string.Join("; ", check.Errors));
    return 1;
}

// ③ 写回 + 保存
var oldCount = FunctionWriter.Replace(card.Asset, targetFunction, bytecode);
var saved = card.Save(outDir);

// ④ 重开验证：目标函数变了、其它函数字节不变
var reopened = CardAsset.Open(saved.UAssetPath, usmapPath, Path.Combine(outDir, "_verify"));
var after = FunctionBytes.Hashes(reopened.Asset);
foreach (var (name, hash) in before)
{
    if (name != targetFunction && after[name] != hash)
    {
        Console.Error.WriteLine($"其他函数被改动: {name}");
        return 1;
    }
}

var reopenedCheck = BytecodeCheck.Run(
    reopened.Asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>()
        .First(f => f.ObjectName.ToString() == targetFunction).ScriptBytecode!,
    reopened.Asset);
if (!reopenedCheck.Ok)
{
    Console.Error.WriteLine("写回后自校验失败：" + string.Join("; ", reopenedCheck.Errors));
    return 1;
}

// ⑤ 出 mod pak
var relative = ModPak.RelativeContentPath(assetPath);
var pak = ModPak.Build(
    [
        new PakFile(relative, saved.UAsset),
        new PakFile(Path.ChangeExtension(relative, ".uexp"), saved.UExp),
    ],
    Path.Combine(outDir, "mod.pak"));

Console.WriteLine($"mod      {targetFunction} {oldCount} -> {bytecode.Length} 条表达式");
Console.WriteLine($"字节码   {string.Join(", ", bytecode.Select(e => e.Token.ToString()))}");
Console.WriteLine($"校验     {(reopenedCheck.Ok ? "OK" : "FAIL")}（其余函数 {after.Count - 1} 个字节不变）");
Console.WriteLine($"资产     {saved.UAssetPath}");
Console.WriteLine($"pak      {pak.PakPath}（{pak.Bytes.Length} 字节 / {pak.Archive.Entries.Count} 条目）");
Console.WriteLine($"mount    {pak.Archive.MountPoint}");
return 0;
