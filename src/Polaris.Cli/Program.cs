using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Polaris.Assets;
using Polaris.Compiler;
using Polaris.Pak;
using Polaris.SdkGen;
using UAssetAPI.UnrealTypes;

return Cli.Run(args);

internal static class Cli
{
    private const string Usage = """
        polaris <verb> [options]

        verbs:
          cards list   --asset <uasset> --usmap <usmap>
          cards set    --asset <uasset> --usmap <usmap> --set name=value [--set ...] --out <dir> [--pak <mod.pak>]
          cards reset  --asset <uasset> --usmap <usmap> --reset name [--reset ...] --out <dir> [--pak <mod.pak>]
          pak          --pak <mod.pak> --file <abs path> [--file ...] [--content-root <dir>] [--mount <point>]
          sdk          --jmap <x.jmap> --out <dir> [--filter kards,Engine]
             （或旧输入：--usmap <usmap> --catalog <functions.json>）
          apply        --mod <Mod.cs> --content <ContentRoot> --usmap <usmap> --out <dir>
                       [--sdk <GeneratedSdk.dll>] [--pak <mod.pak>]
          check        --asset <uasset> | --dir <目录> [--limit N] --usmap <usmap> [--json <file>]
          decompile    --asset <uasset> | --dir <目录> --usmap <usmap> [--function <name>] [--strict] [--out <file|dir>]
          diff         --before <uasset> --after <uasset> --usmap <usmap>   比较 CDO 与函数字节
          ubergraph    --asset <uasset> --usmap <usmap>                    诊断入口/段偏移

        global options:
          --work <dir>         工作目录（usmap 副本与字节码校验用），默认 .polaris-work
          --content-root <dir> pak 内相对路径的根（默认从源路径的 /Content/ 段推断）
          --mount <point>      pak mount point，默认 ../../../kards/Content/
        """;

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            if (args[0] == "sdk")
            {
                return Sdk(Parse(args.Skip(1).ToArray()));
            }

            if (args[0] == "apply")
            {
                return Apply(Parse(args.Skip(1).ToArray()));
            }

            if (args[0] == "check")
            {
                return Check(Parse(args.Skip(1).ToArray()));
            }

            if (args[0] == "decompile")
            {
                return Decompile(Parse(args.Skip(1).ToArray()));
            }

            if (args[0] == "diff")
            {
                return Diff(Parse(args.Skip(1).ToArray()));
            }

            if (args[0] == "ubergraph")
            {
                return Ubergraph(Parse(args.Skip(1).ToArray()));
            }

            if (args[0] == "offsets")
            {
                return Offsets(Parse(args.Skip(1).ToArray()));
            }

            if (args[0] == "pak")
            {
                return Pak(Parse(args.Skip(1).ToArray()));
            }

            if (args[0] != "cards")
            {
                Console.Error.WriteLine($"未知 verb: {args[0]}");
                Console.Error.WriteLine(Usage);
                return 2;
            }

            var opts = Parse(args.Skip(1).ToArray());
            var action = opts.Positional.FirstOrDefault();
            return action switch
            {
                "list" => List(opts),
                "set" => Edit(opts),
                "reset" => Edit(opts),
                _ => Fail($"未知 cards 动作: {action ?? "(空)"}"),
            };
        }
        catch (VerificationFailedException ex)
        {
            Console.Error.WriteLine("✗ " + ex.Message);
            foreach (var diff in ex.Report.CdoDiffs)
            {
                Console.Error.WriteLine("  CDO  " + diff);
            }

            foreach (var f in ex.Report.FunctionFailures)
            {
                Console.Error.WriteLine("  函数 " + f);
            }

            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ {ex.GetType().Name}: {ex.Message}");
            if (Environment.GetEnvironmentVariable("POLARIS_DEBUG") == "1")
            {
                Console.Error.WriteLine(ex.StackTrace);
            }

            return 1;
        }
    }

    private static int List(Options opts)
    {
        var card = CardAsset.Open(opts.Require("asset"), opts.Require("usmap"), opts.Work);
        Console.WriteLine($"资产     {Path.GetFileName(card.SourcePath)}");
        Console.WriteLine($"类       {card.ClassExport.ObjectName}");
        Console.WriteLine($"原生父类 {card.NativeParentPath}");
        Console.WriteLine($"CDO 属性 {card.Cdo.Data.Count} 条");
        Console.WriteLine();
        Console.WriteLine($"{"名字",-36} {"类型",-20} {"值",-28} 声明类");
        foreach (var p in card.Cdo.Data.OrderBy(d => d.Name.ToString(), StringComparer.Ordinal))
        {
            var name = p.Name.ToString();
            string declaring;
            try
            {
                declaring = card.ResolveMember(name).DeclaringClass;
            }
            catch (MemberNotFoundException)
            {
                declaring = "<类链上没有>";
            }

            Console.WriteLine($"{name,-36} {p.GetType().Name,-20} {Render(p),-28} {declaring}");
        }

        return 0;
    }

    private static int Edit(Options opts)
    {
        var asset = opts.Require("asset");
        var usmap = opts.Require("usmap");
        var outDir = opts.Require("out");
        var card = CardAsset.Open(asset, usmap, opts.Work);

        var patches = opts.Patches;
        if (patches.Count == 0)
        {
            return Fail("没有 --set / --reset");
        }

        var report = CdoEditor.Apply(card, patches, outDir, opts.Work);

        Console.WriteLine($"资产  {Path.GetFileName(card.SourcePath)}");
        Console.WriteLine($"输出  {report.Saved.UAssetPath}");
        foreach (var p in report.Patches)
        {
            Console.WriteLine($"  {p.Action,-8} {p.Name,-28} {p.Before ?? "-"} -> {p.After ?? "(删除)"}");
        }

        Console.WriteLine($"差异  .uasset {report.DiffUAsset.BeforeSize} -> {report.DiffUAsset.AfterSize} " +
                          $"({report.DiffUAsset.ChangedBytes} 字节不同)");
        Console.WriteLine($"      .uexp   {report.DiffUExp.BeforeSize} -> {report.DiffUExp.AfterSize} " +
                          $"({report.DiffUExp.ChangedBytes} 字节不同)");
        Console.WriteLine($"校验  {(report.Verify.Ok ? "✅" : "✗")} 函数逐字节一致 {report.Verify.FunctionCount} 个" +
                          $"（失败 {report.Verify.FunctionFailures.Count}）");

        if (opts.Values.TryGetValue("pak", out var pakPath))
        {
            var contentRoot = opts.Values.GetValueOrDefault("content-root");
            var relative = ModPak.RelativeContentPath(card.SourcePath, contentRoot);
            var built = ModPak.Build(
                [
                    new PakFile(relative, report.Saved.UAsset),
                    new PakFile(Path.ChangeExtension(relative, ".uexp"), report.Saved.UExp),
                ],
                pakPath,
                opts.Mount);
            PrintPak(built);
        }

        return report.Verify.Ok ? 0 : 1;
    }

    private static int Apply(Options opts)
    {
        var modPath = opts.Require("mod");
        var content = opts.Require("content");
        var usmap = opts.Require("usmap");
        var outDir = opts.Require("out");
        var source = File.ReadAllText(modPath);
        var dryRun = opts.Values.ContainsKey("dry-run");

        var refs = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        refs.Add(MetadataReference.CreateFromFile(typeof(Polaris.Sdk.UObject).Assembly.Location));
        if (opts.Values.TryGetValue("sdk", out var sdkDll) && File.Exists(sdkDll))
        {
            refs.Add(MetadataReference.CreateFromFile(sdkDll));
        }

        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source, path: modPath) };
        if (opts.Values.TryGetValue("sdk-source", out var sdkSource))
        {
            foreach (var file in Directory.EnumerateFiles(sdkSource, "*.cs", SearchOption.AllDirectories))
            {
                trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file));
            }
        }

        var compilation = CSharpCompilation.Create(
            "PolarisApply",
            trees,
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                Console.Error.WriteLine("  " + error);
            }

            return 1;
        }

        var targets = ModPlanBuilder.FindTargets(compilation);
        var patches = ModPlanBuilder.FindPatches(compilation);
        if (targets.Count == 0 && patches.Count == 0)
        {
            Console.Error.WriteLine("没有 [PolarisAsset]/[PolarisReplace]/[PolarisPatch] 标注");
            return 1;
        }

        var plan = new SortedDictionary<string, (List<ModTarget> Targets, List<ModPatch> Patches)>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var path = ResolveAsset(content, target.AssetPath);
            if (!plan.TryGetValue(path, out var entry))
            {
                entry = (new List<ModTarget>(), new List<ModPatch>());
                plan[path] = entry;
            }

            entry.Targets.Add(target);
        }

        foreach (var patch in patches)
        {
            var path = ResolveAsset(content, patch.AssetPath);
            if (!plan.TryGetValue(path, out var entry))
            {
                entry = (new List<ModTarget>(), new List<ModPatch>());
                plan[path] = entry;
            }

            entry.Patches.Add(patch);
        }

        var replacedTotal = 0;
        var patchedTotal = 0;
        var multiAsset = plan.Count > 1;
        var builtPaths = new List<string>();
        foreach (var (assetPath, entry) in plan)
        {
            if (!File.Exists(assetPath))
            {
                Console.Error.WriteLine("找不到资产: " + assetPath);
                return 1;
            }

            var card = CardAsset.Open(assetPath, usmap, Path.Combine(opts.Work, "_apply"));
            var before = FunctionBytes.Hashes(card.Asset);
            var imports = new AssetImportTable(card.Asset);
            var replaced = new HashSet<string>(StringComparer.Ordinal);

            foreach (var target in entry.Targets)
            {
                var function = card.Asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>()
                    .FirstOrDefault(f => f.ObjectName.ToString() == target.FunctionName)
                    ?? throw new InvalidOperationException($"资产里没有函数 {target.FunctionName}");
                var classExport = card.Asset.Exports.OfType<UAssetAPI.ExportTypes.ClassExport>().First();

                var functionParams = function.LoadedProperties
                    .Where(p => (p.PropertyFlags & UAssetAPI.UnrealTypes.EPropertyFlags.CPF_Parm) != 0)
                    .Select(p => p.Name.ToString())
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var parameter in target.Method.Parameters)
                {
                    if (functionParams.Count > 0 && !functionParams.Contains(parameter.Name))
                    {
                        Console.Error.WriteLine(
                            $"{target.FunctionName}: mod 参数 {parameter.Name} 在目标函数里不存在（现有: {string.Join(", ", functionParams)}）");
                        return 1;
                    }
                }

                var hasReturnParm = function.LoadedProperties.Any(p =>
                    (p.PropertyFlags & UAssetAPI.UnrealTypes.EPropertyFlags.CPF_ReturnParm) != 0);
                if (hasReturnParm && target.Method.ReturnsVoid)
                {
                    Console.Error.WriteLine($"{target.FunctionName}: 目标函数有返回值，mod 方法必须返回同类型值");
                    return 1;
                }

                if (!hasReturnParm && !target.Method.ReturnsVoid)
                {
                    Console.Error.WriteLine($"{target.FunctionName}: 目标函数没有返回值，mod 方法应返回 void");
                    return 1;
                }

                var outParams = function.LoadedProperties
                    .Where(p => (p.PropertyFlags & UAssetAPI.UnrealTypes.EPropertyFlags.CPF_OutParm) != 0)
                    .Select(p => p.Name.ToString())
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var outParam in outParams)
                {
                    if (!target.Method.Parameters.Any(p => p.Name == outParam))
                    {
                        Console.Error.WriteLine($"{target.FunctionName}: 目标函数有 out 参数 {outParam}，mod 方法必须声明同名参数");
                        return 1;
                    }
                }

                var bytecode = Translator.Compile(
                    compilation,
                    target.Method,
                    card.Asset,
                    imports,
                    new FPackageIndex(card.Asset.Exports.IndexOf(function) + 1),
                    new FPackageIndex(card.Asset.Exports.IndexOf(classExport) + 1),
                    function);

                var check = BytecodeCheck.Run(bytecode, card.Asset);
                if (!check.Ok)
                {
                    Console.Error.WriteLine($"{target.FunctionName}: " + string.Join("; ", check.Errors));
                    return 1;
                }

                FunctionWriter.Replace(card.Asset, target.FunctionName, bytecode);
                replaced.Add(target.FunctionName);
                Console.WriteLine($"mod      {Path.GetFileName(assetPath)} :: {target.FunctionName}（{bytecode.Length} 条）");
            }

            foreach (var patch in entry.Patches)
            {
                if (patch.Value.Length == 0)
                {
                    CdoEditor.Reset(card, patch.Field);
                    Console.WriteLine($"patch    {Path.GetFileName(assetPath)} :: {patch.Field} = (reset)");
                }
                else
                {
                    CdoEditor.Set(card, patch.Field, patch.Value);
                    Console.WriteLine($"patch    {Path.GetFileName(assetPath)} :: {patch.Field} = {patch.Value}");
                }

                patchedTotal++;
            }

            if (dryRun)
            {
                Console.WriteLine($"plan     {Path.GetFileName(assetPath)}：{replaced.Count} 个函数 / {entry.Patches.Count} 个 CDO 字段（dry-run，不写盘）");
                replacedTotal += replaced.Count;
                continue;
            }

            var saved = card.Save(outDir);
            var reopened = CardAsset.Open(saved.UAssetPath, usmap, Path.Combine(opts.Work, "_apply_verify"));
            var after = FunctionBytes.Hashes(reopened.Asset);
            foreach (var (name, hash) in before)
            {
                if (!replaced.Contains(name) && after[name] != hash)
                {
                    Console.Error.WriteLine("其他函数被改动: " + name);
                    return 1;
                }
            }

            foreach (var name in replaced)
            {
                var written = reopened.Asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>()
                    .FirstOrDefault(f => f.ObjectName.ToString() == name);
                if (written?.ScriptBytecode is null)
                {
                    Console.Error.WriteLine("重开后找不到函数: " + name);
                    return 1;
                }

                var roundtrip = RoundTrip.Verify(written.ScriptBytecode, reopened.Asset);
                if (!roundtrip.Ok)
                {
                    Console.Error.WriteLine($"{name}: 写回后往返失败 " + roundtrip.Error);
                    return 1;
                }
            }

            var snapshot = reopened.Snapshot();
            foreach (var patch in entry.Patches)
            {
                var present = snapshot.ContainsKey(patch.Field);
                if (patch.Value.Length == 0 ? present : !present)
                {
                    Console.Error.WriteLine($"CDO 字段校验失败: {patch.Field}");
                    return 1;
                }
            }

            if (opts.Values.TryGetValue("pak", out var pakPath))
            {
                var relative = ModPak.RelativeContentPath(assetPath);
                var targetPak = pakPath;
                if (multiAsset)
                {
                    Directory.CreateDirectory(pakPath);
                    targetPak = Path.Combine(pakPath, Path.GetFileNameWithoutExtension(assetPath) + ".pak");
                }

                var built = ModPak.Build(
                    [
                        new PakFile(relative, saved.UAsset),
                        new PakFile(Path.ChangeExtension(relative, ".uexp"), saved.UExp),
                    ],
                    targetPak,
                    opts.Mount);
                Console.WriteLine($"pak      {built.PakPath}（{built.Bytes.Length} 字节 / {built.Archive.Entries.Count} 条目）");
                builtPaths.Add(built.PakPath);
            }

            replacedTotal += replaced.Count;
        }

        if (!dryRun)
        {
            var manifest = new
            {
                tool = "polaris apply",
                mod = Path.GetFileName(modPath),
                content,
                usmap,
                assets = plan.Keys.Select(p => new
                {
                    asset = Path.GetFileName(p),
                    functions = plan[p].Targets.Select(t => t.FunctionName).ToArray(),
                    patches = plan[p].Patches.Select(patch => new { field = patch.Field, value = patch.Value }).ToArray(),
                }).ToArray(),
                paks = builtPaths.ToArray(),
                createdAt = DateTimeOffset.UtcNow.ToString("O"),
            };
            var manifestPath = Path.Combine(outDir, "mod.json");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"manifest {manifestPath}");
        }

        Console.WriteLine($"ok       {replacedTotal} 个函数写回，{patchedTotal} 个 CDO 字段");
        return 0;
    }

    private static int Offsets(Options opts)
    {
        var card = CardAsset.Open(opts.Require("asset"), opts.Require("usmap"), opts.Work);
        var name = opts.Require("function");
        var function = card.Asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>()
            .FirstOrDefault(f => f.ObjectName.ToString() == name);
        if (function?.ScriptBytecode is not { Length: > 0 })
        {
            Console.Error.WriteLine("找不到函数或函数没有字节码: " + name);
            return 1;
        }

        var block = SerializeExpressions(function.ScriptBytecode, card.Asset);
        var fileStarts = new HashSet<int>();
        var floraStarts = new HashSet<int>();
        var rows = new List<(int Index, int FileOffset, int FloraOffset, string Token, uint? Target)>();
        int fileOffset = 0, floraOffset = 0;
        for (var i = 0; i < function.ScriptBytecode.Length; i++)
        {
            var expression = function.ScriptBytecode[i];
            var raw = SerializeExpressions([expression], card.Asset).Length;
            fileStarts.Add(fileOffset);
            floraStarts.Add(floraOffset);
            uint? target = expression switch
            {
                UAssetAPI.Kismet.Bytecode.Expressions.EX_Jump jump => jump.CodeOffset,
                UAssetAPI.Kismet.Bytecode.Expressions.EX_JumpIfNot jumpIfNot => jumpIfNot.CodeOffset,
                _ => null,
            };
            rows.Add((i, fileOffset, floraOffset, expression.Token.ToString(), target));
            fileOffset += raw;
            int floraSize;
            try
            {
                floraSize = Polaris.Compiler.FloraLayout.Size(expression);
            }
            catch (NotSupportedException)
            {
                floraSize = raw + 4 * CountNames(expression);
            }

            floraOffset += floraSize;
        }

        foreach (var row in rows)
        {
            var line = $"  #{row.Index} file@{row.FileOffset} flora@{row.FloraOffset} {row.Token}";
            if (row.Target is uint target)
            {
                line += $" -> {target} landedFile={fileStarts.Contains((int)target)} landedFlora={floraStarts.Contains((int)target)}";
            }

            Console.WriteLine(line);
        }

        Console.WriteLine($"bytes      {block.Length}  fileStarts={fileStarts.Count} floraStarts={floraStarts.Count}");
        return 0;
    }

    private static int CountNames(object? value)
    {
        switch (value)
        {
            case null:
                return 0;
            case UAssetAPI.Kismet.Bytecode.KismetPropertyPointer pointer:
                return pointer.New?.Path?.Length ?? 1;
            case UAssetAPI.Kismet.Bytecode.KismetExpression expression:
            {
                var total = 0;
                foreach (var field in expression.GetType().GetFields(
                             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    total += CountNames(field.GetValue(expression));
                }

                return total;
            }
            case System.Collections.IEnumerable enumerable when value is not string:
            {
                var total = 0;
                foreach (var item in enumerable)
                {
                    total += CountNames(item);
                }

                return total;
            }
            default:
                return 0;
        }
    }

    private static int Ubergraph(Options opts)
    {
        var card = CardAsset.Open(opts.Require("asset"), opts.Require("usmap"), opts.Work);
        var ubergraph = card.Asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>()
            .FirstOrDefault(f => f.ObjectName.ToString().StartsWith("ExecuteUbergraph"));
        if (ubergraph?.ScriptBytecode is not { Length: > 0 })
        {
            Console.Error.WriteLine("资产里没有 ExecuteUbergraph_* 函数");
            return 1;
        }

        var block = SerializeExpressions(ubergraph.ScriptBytecode, card.Asset);
        var starts = ParseStarts(block, card.Asset);
        Console.WriteLine($"ubergraph {ubergraph.ObjectName}: expr={ubergraph.ScriptBytecode.Length} bytes={block.Length} starts={starts.Count}");

        var computed = ubergraph.ScriptBytecode
            .OfType<UAssetAPI.Kismet.Bytecode.Expressions.EX_ComputedJump>()
            .FirstOrDefault();
        Console.WriteLine($"  computedJump operand={computed?.CodeOffsetExpression?.Token}");

        string NameOf(UAssetAPI.UnrealTypes.FPackageIndex index)
        {
            if (index.Index > 0 && index.Index <= card.Asset.Exports.Count)
            {
                return card.Asset.Exports[index.Index - 1].ObjectName.ToString();
            }

            if (index.Index < 0 && -index.Index <= card.Asset.Imports.Count)
            {
                return card.Asset.Imports[-index.Index - 1].ObjectName.ToString();
            }

            return "idx:" + index.Index;
        }

        var stubs = 0;
        var stubItems = new List<object>();
        foreach (var function in card.Asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>())
        {
            if (ReferenceEquals(function, ubergraph) || function.ScriptBytecode is not { Length: > 0 })
            {
                continue;
            }

            foreach (var call in function.ScriptBytecode.OfType<UAssetAPI.Kismet.Bytecode.Expressions.EX_LocalFinalFunction>())
            {
                if (NameOf(call.StackNode) != ubergraph.ObjectName.ToString())
                {
                    continue;
                }

                foreach (var entry in call.Parameters.OfType<UAssetAPI.Kismet.Bytecode.Expressions.EX_IntConst>())
                {
                    stubs++;
                    var landed = starts.Contains((uint)entry.Value);
                    var next = starts.FirstOrDefault(s => s >= entry.Value, (uint)block.Length);
                    Console.WriteLine($"  {function.ObjectName}: entry={entry.Value} landed={landed} next={next} delta={next - entry.Value}");
                    stubItems.Add(new
                    {
                        function = function.ObjectName.ToString(),
                        entry = entry.Value,
                        landed,
                        next,
                        delta = next - entry.Value,
                    });
                }
            }
        }

        Console.WriteLine($"stubs      {stubs}");

        if (opts.Values.TryGetValue("json", out var jsonPath))
        {
            var payload = new
            {
                tool = "polaris ubergraph",
                asset = Path.GetFileName(opts.Require("asset")),
                ubergraph = ubergraph.ObjectName.ToString(),
                expressions = ubergraph.ScriptBytecode.Length,
                bytes = block.Length,
                starts = starts.Count,
                computedJumpOperand = computed?.CodeOffsetExpression?.Token.ToString(),
                stubs = stubItems.ToArray(),
            };
            var dir = Path.GetDirectoryName(Path.GetFullPath(jsonPath));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(payload,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"json       {jsonPath}");
        }
        return 0;
    }

    private static byte[] SerializeExpressions(
        IReadOnlyList<UAssetAPI.Kismet.Bytecode.KismetExpression> expressions,
        UAssetAPI.UAsset asset)
    {
        var stream = new MemoryStream();
        var writer = new UAssetAPI.AssetBinaryWriter(stream, asset);
        foreach (var expression in expressions)
        {
            UAssetAPI.Kismet.Bytecode.ExpressionSerializer.WriteExpression(expression, writer);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static List<uint> ParseStarts(byte[] block, UAssetAPI.UAsset asset)
    {
        var starts = new List<uint>();
        var reader = new UAssetAPI.AssetBinaryReader(new MemoryStream(block), asset);
        while (reader.BaseStream.Position < block.Length)
        {
            starts.Add((uint)reader.BaseStream.Position);
            UAssetAPI.Kismet.Bytecode.ExpressionSerializer.ReadExpression(reader);
        }

        return starts;
    }

    private static int Diff(Options opts)
    {
        var usmap = opts.Require("usmap");
        var before = CardAsset.Open(opts.Require("before"), usmap, Path.Combine(opts.Work, "_diff_before"));
        var after = CardAsset.Open(opts.Require("after"), usmap, Path.Combine(opts.Work, "_diff_after"));

        var beforeCdo = before.Snapshot();
        var afterCdo = after.Snapshot();
        var cdoDiffs = new List<string>();
        foreach (var key in beforeCdo.Keys.Union(afterCdo.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var hasBefore = beforeCdo.TryGetValue(key, out var oldValue);
            var hasAfter = afterCdo.TryGetValue(key, out var newValue);
            if (!hasBefore)
            {
                cdoDiffs.Add($"+ {key} = {newValue}");
            }
            else if (!hasAfter)
            {
                cdoDiffs.Add($"- {key}（原 {oldValue}）");
            }
            else if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                cdoDiffs.Add($"~ {key}: {oldValue} -> {newValue}");
            }
        }

        var beforeHashes = FunctionBytes.Hashes(before.Asset);
        var afterHashes = FunctionBytes.Hashes(after.Asset);
        var functionDiffs = new List<string>();
        foreach (var name in beforeHashes.Keys.Union(afterHashes.Keys).OrderBy(n => n, StringComparer.Ordinal))
        {
            var hasBefore = beforeHashes.TryGetValue(name, out var oldHash);
            var hasAfter = afterHashes.TryGetValue(name, out var newHash);
            if (!hasBefore)
            {
                functionDiffs.Add($"+ {name}");
            }
            else if (!hasAfter)
            {
                functionDiffs.Add($"- {name}");
            }
            else if (!string.Equals(oldHash, newHash, StringComparison.Ordinal))
            {
                functionDiffs.Add($"~ {name}");
            }
        }

        var uassetDiff = AssetDiff.Compare(before.OriginalUAssetBytes, after.OriginalUAssetBytes);
        var uexpDiff = AssetDiff.Compare(before.OriginalUExpBytes, after.OriginalUExpBytes);

        Console.WriteLine($"cdo       {cdoDiffs.Count} 处变化");
        foreach (var line in cdoDiffs)
        {
            Console.WriteLine("  " + line);
        }

        Console.WriteLine($"functions {functionDiffs.Count} 处变化");
        foreach (var line in functionDiffs)
        {
            Console.WriteLine("  " + line);
        }

        Console.WriteLine($"uasset    {uassetDiff.ChangedBytes} 字节变化（{uassetDiff.BeforeSize} -> {uassetDiff.AfterSize}）");
        Console.WriteLine($"uexp      {uexpDiff.ChangedBytes} 字节变化（{uexpDiff.BeforeSize} -> {uexpDiff.AfterSize}）");
        return 0;
    }

    private static int Decompile(Options opts)
    {
        var usmap = opts.Require("usmap");
        var only = opts.Values.TryGetValue("function", out var name) ? name : null;
        var strict = opts.Values.ContainsKey("strict");

        if (opts.Values.TryGetValue("dir", out var sourceDir))
        {
            var outDir = opts.Values.TryGetValue("out", out var dir) ? dir : Path.Combine(opts.Work, "decompiled");
            Directory.CreateDirectory(outDir);
            var assets = 0;
            var failed = 0;
            foreach (var path in Directory.EnumerateFiles(sourceDir, "*.uasset", SearchOption.AllDirectories))
            {
                CardAsset assetCard;
                try
                {
                    assetCard = CardAsset.Open(path, usmap, opts.Work);
                }
                catch (Exception)
                {
                    continue;
                }

                var (text, _, bad) = DecompileAsset(assetCard, only);
                if (bad > 0)
                {
                    failed += bad;
                }

                var relative = Path.GetRelativePath(sourceDir, path);
                var flat = relative.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
                File.WriteAllText(Path.Combine(outDir, flat + ".txt"), text);
                assets++;
            }

            Console.WriteLine($"out      {outDir}（{assets} 个资产，{failed} 个函数往返失败）");
            return strict && failed > 0 ? 1 : 0;
        }

        var card = CardAsset.Open(opts.Require("asset"), usmap, opts.Work);
        var (body, shown, failures) = DecompileAsset(card, only);
        if (only is not null && shown == 0)
        {
            Console.Error.WriteLine("找不到函数: " + only);
            return 1;
        }

        if (strict && failures > 0)
        {
            Console.Error.WriteLine($"{failures} 个函数往返不一致");
            return 1;
        }

        if (opts.Values.TryGetValue("out", out var file))
        {
            var fileDir = Path.GetDirectoryName(Path.GetFullPath(file));
            if (!string.IsNullOrEmpty(fileDir))
            {
                Directory.CreateDirectory(fileDir);
            }

            File.WriteAllText(file, body);
            Console.WriteLine($"out      {file}（{shown} 个函数）");
        }
        else
        {
            Console.Write(body);
        }

        return 0;
    }

    private static (string Text, int Shown, int Failed) DecompileAsset(CardAsset card, string? only)
    {
        var text = new System.Text.StringBuilder();
        var shown = 0;
        var failed = 0;
        foreach (var function in card.Asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>())
        {
            if (function.ScriptBytecode is not { Length: > 0 })
            {
                continue;
            }

            if (only is not null && function.ObjectName.ToString() != only)
            {
                continue;
            }

            var nodes = KismetDecompiler.Decompile(function.ScriptBytecode, card.Asset, out var decodedControlFlow);
            var roundtrip = RoundTrip.Verify(function.ScriptBytecode, card.Asset);
            text.AppendLine($"// {function.ObjectName}  roundtrip={(roundtrip.Ok ? "ok" : "FAIL: " + roundtrip.Error)}  controlflow={(decodedControlFlow ? "decoded" : "raw")}");
            if (!roundtrip.Ok)
            {
                failed++;
            }

            text.AppendLine(KismetText.Format(nodes, card.Asset));
            text.AppendLine();
            shown++;
        }

        return (text.ToString(), shown, failed);
    }

    private static int Check(Options opts)
    {
        var usmap = opts.Require("usmap");
        var files = new List<string>();
        if (opts.Values.TryGetValue("asset", out var assetPath))
        {
            files.Add(assetPath);
        }
        else if (opts.Values.TryGetValue("dir", out var dir))
        {
            var filesInDir = Directory.EnumerateFiles(dir, "*.uasset", SearchOption.AllDirectories);
            if (opts.Values.TryGetValue("limit", out var limitText) && int.TryParse(limitText, out var limit))
            {
                filesInDir = filesInDir.Take(limit);
            }

            files.AddRange(filesInDir);
        }
        else
        {
            Console.Error.WriteLine("用法: check --asset <uasset> 或 --dir <Blueprints 目录> --usmap <usmap>");
            return 2;
        }

        var ok = 0;
        var total = 0;
        var assets = 0;
        var failures = new List<string>();

        var skipped = 0;
        foreach (var path in files)
        {
            CardAsset card;
            try
            {
                card = CardAsset.Open(path, usmap, opts.Work);
            }
            catch (Exception)
            {
                skipped++;
                continue;
            }

            assets++;
            foreach (var function in card.Asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>())
            {
                if (function.ScriptBytecode is not { Length: > 0 })
                {
                    continue;
                }

                total++;
                var report = RoundTrip.Verify(function.ScriptBytecode, card.Asset);
                if (report.Ok)
                {
                    ok++;
                }
                else
                {
                    failures.Add($"{Path.GetFileName(path)}.{function.ObjectName}: {report.Error}");
                }
            }
        }

        Console.WriteLine($"roundtrip {ok}/{total}（{(total == 0 ? 100.0 : 100.0 * ok / total):F1}%） across {assets} assets，skipped {skipped}");
        foreach (var failure in failures.Take(10))
        {
            Console.WriteLine("  " + failure);
        }

        if (opts.Values.TryGetValue("json", out var jsonPath))
        {
            var payload = new
            {
                tool = "polaris check",
                usmap,
                usmapSha256 = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(usmap))).ToLowerInvariant(),
                ok,
                total,
                assets,
                skipped,
                failures = failures.ToArray(),
            };
            var jsonDir = Path.GetDirectoryName(Path.GetFullPath(jsonPath));
            if (!string.IsNullOrEmpty(jsonDir))
            {
                Directory.CreateDirectory(jsonDir);
            }

            File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(payload,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"json     {jsonPath}");
        }

        return ok == total ? 0 : 1;
    }

    private static string ResolveAsset(string content, string asset)
    {
        if (asset.StartsWith("/Game/", StringComparison.Ordinal))
        {
            return Path.Combine(content, asset["/Game/".Length..].Replace('/', Path.DirectorySeparatorChar));
        }

        if (Path.IsPathRooted(asset))
        {
            return asset;
        }

        return Path.Combine(content, asset.Replace('/', Path.DirectorySeparatorChar));
    }

    private static int Sdk(Options opts)
    {
        var outDir = opts.Require("out");
        UsmapFile usmapFile;
        IReadOnlyDictionary<string, JmapOwner> owners;

        if (opts.Values.TryGetValue("jmap", out var jmapPath))
        {
            var parsed = JmapFileReader.ReadFile(jmapPath);
            usmapFile = parsed.Usmap;
            owners = parsed.Owners;
            Console.WriteLine($"jmap     {Path.GetFileName(jmapPath)}（纯 jmap 输入）");
        }
        else
        {
            var usmapBytes = File.ReadAllBytes(opts.Require("usmap"));
            var catalogText = File.ReadAllText(opts.Require("catalog"));
            usmapFile = UsmapReader.Read(usmapBytes);
            owners = JmapReader.Read(catalogText);
        }
        var filter = opts.Values.TryGetValue("filter", out var f)
            ? f.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;

        var result = SdkEmitter.Emit(usmapFile, owners, new SdkOptions(filter));
        Directory.CreateDirectory(outDir);

        // 清掉上一次的生成物，避免残留的旧 namespace/文件混进编译
        var sdkRoot = Path.Combine(outDir, "Polaris.Sdk");
        if (Directory.Exists(sdkRoot))
        {
            Directory.Delete(sdkRoot, recursive: true);
        }

        foreach (var file in result.Files)
        {
            var path = Path.Combine(outDir, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, file.Text);
        }

        Console.WriteLine($"usmap    {usmapFile.Schemas.Count} schema / {usmapFile.Enums.Count} enum / version {usmapFile.Version}");
        Console.WriteLine($"catalog  {owners.Count} owner / {owners.Values.Sum(o => o.Functions.Count)} 函数");
        Console.WriteLine($"emit     {result.Classes} 类 / {result.Structs} 结构 / {result.Enums} 枚举 / {result.Methods} 方法 / {result.Members} 成员");
        Console.WriteLine($"files    {result.Files.Count} 个 / {result.Files.Sum(x => x.Text.Length) / 1024} KB");
        Console.WriteLine($"out      {outDir}");
        return 0;
    }

    private static int Pak(Options opts)
    {
        if (opts.FileArgs.Count == 0)
        {
            return Fail("没有 --file");
        }

        var contentRoot = opts.Values.GetValueOrDefault("content-root");
        var files = opts.FileArgs.Select(f => new PakFile(
            ModPak.RelativeContentPath(f, contentRoot),
            File.ReadAllBytes(f)));
        var built = ModPak.Build(files, opts.Require("pak"), opts.Mount);
        PrintPak(built);
        return 0;
    }

    private static void PrintPak(BuiltPak built)
    {
        Console.WriteLine($"pak    {built.PakPath}（{built.Bytes.Length} 字节，{built.Archive.Entries.Count} 条目）");
        Console.WriteLine($"mount  {built.Archive.MountPoint}");
        foreach (var e in built.Archive.Entries)
        {
            Console.WriteLine($"  {e.Name,-60} {e.Data.Length,8} 字节");
        }
    }

    private static string Render(UAssetAPI.PropertyTypes.Objects.PropertyData p) => p switch
    {
        UAssetAPI.PropertyTypes.Objects.IntPropertyData i => i.Value.ToString(),
        UAssetAPI.PropertyTypes.Objects.BoolPropertyData b => b.Value ? "true" : "false",
        UAssetAPI.PropertyTypes.Objects.FloatPropertyData f => f.Value.ToString("R"),
        _ => $"{p.GetType().Name}:{p.RawValue}",
    };

    private static int Fail(string message)
    {
        Console.Error.WriteLine("✗ " + message);
        Console.Error.WriteLine(Usage);
        return 2;
    }

    private sealed class Options
    {
        public List<string> Positional { get; } = [];
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public List<string> Sets { get; } = [];
        public List<string> Resets { get; } = [];
        public string Work => Values.TryGetValue("work", out var w) ? w : Path.Combine(Environment.CurrentDirectory, ".polaris-work");

        public string Mount => Values.TryGetValue("mount", out var m) ? m : ModPak.DefaultMountPoint;

        public List<string> FileArgs { get; } = [];

        public IReadOnlyList<PatchSpec> Patches =>
            [.. Sets.Select(s => PatchSpec.Set(s[..s.IndexOf('=')], s[(s.IndexOf('=') + 1)..])),
             .. Resets.Select(r => PatchSpec.Reset(r))];

        public string Require(string key)
            => Values.TryGetValue(key, out var value) ? value : throw new ArgumentException($"缺少 --{key}");
    }

    private static Options Parse(string[] argv)
    {
        var o = new Options();
        for (var i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                o.Positional.Add(a);
                continue;
            }

            var key = a[2..];
            if (key is "set" or "reset")
            {
                if (i + 1 >= argv.Length)
                {
                    throw new ArgumentException($"--{key} 缺值");
                }

                (key == "set" ? o.Sets : o.Resets).Add(argv[++i]);
                continue;
            }

            if (key is "strict" or "dry-run")
            {
                o.Values[key] = "1";
                continue;
            }

            if (key == "file")
            {
                if (i + 1 >= argv.Length)
                {
                    throw new ArgumentException("--file 缺值");
                }

                o.FileArgs.Add(argv[++i]);
                continue;
            }

            if (i + 1 >= argv.Length)
            {
                throw new ArgumentException($"--{key} 缺值");
            }

            o.Values[key] = argv[++i];
        }

        return o;
    }
}