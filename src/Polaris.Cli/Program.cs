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
          check        --asset <uasset> --usmap <usmap>     反编译/回编译往返覆盖率

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

        var refs = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        refs.Add(MetadataReference.CreateFromFile(typeof(Polaris.Sdk.UObject).Assembly.Location));
        if (opts.Values.TryGetValue("sdk", out var sdkDll) && File.Exists(sdkDll))
        {
            refs.Add(MetadataReference.CreateFromFile(sdkDll));
        }

        var compilation = CSharpCompilation.Create(
            "PolarisApply",
            [CSharpSyntaxTree.ParseText(source, path: modPath)],
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
        if (targets.Count == 0)
        {
            Console.Error.WriteLine("没有 [PolarisAsset]/[PolarisReplace] 标注");
            return 1;
        }

        var replacedTotal = 0;
        foreach (var group in targets.GroupBy(t => ResolveAsset(content, t.AssetPath)))
        {
            var assetPath = group.Key;
            if (!File.Exists(assetPath))
            {
                Console.Error.WriteLine("找不到资产: " + assetPath);
                return 1;
            }

            var card = CardAsset.Open(assetPath, usmap, Path.Combine(opts.Work, "_apply"));
            var before = FunctionBytes.Hashes(card.Asset);
            var imports = new AssetImportTable(card.Asset);
            var replaced = new HashSet<string>(StringComparer.Ordinal);

            foreach (var target in group)
            {
                var function = card.Asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>()
                    .FirstOrDefault(f => f.ObjectName.ToString() == target.FunctionName)
                    ?? throw new InvalidOperationException($"资产里没有函数 {target.FunctionName}");
                var classExport = card.Asset.Exports.OfType<UAssetAPI.ExportTypes.ClassExport>().First();

                var bytecode = Translator.Compile(
                    compilation,
                    target.Method,
                    card.Asset,
                    imports,
                    new FPackageIndex(card.Asset.Exports.IndexOf(function) + 1),
                    new FPackageIndex(card.Asset.Exports.IndexOf(classExport) + 1));

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

            if (opts.Values.TryGetValue("pak", out var pakPath))
            {
                var relative = ModPak.RelativeContentPath(assetPath);
                var built = ModPak.Build(
                    [
                        new PakFile(relative, saved.UAsset),
                        new PakFile(Path.ChangeExtension(relative, ".uexp"), saved.UExp),
                    ],
                    pakPath,
                    opts.Mount);
                Console.WriteLine($"pak      {built.PakPath}（{built.Bytes.Length} 字节 / {built.Archive.Entries.Count} 条目）");
            }

            replacedTotal += replaced.Count;
        }

        Console.WriteLine($"ok       {replacedTotal} 个函数写回");
        return 0;
    }

    private static int Check(Options opts)
    {
        var assetPath = opts.Require("asset");
        var card = CardAsset.Open(assetPath, opts.Require("usmap"), opts.Work);
        var ok = 0;
        var total = 0;
        var failures = new List<string>();

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
                failures.Add($"{function.ObjectName}: {report.Error}");
            }
        }

        Console.WriteLine($"roundtrip {ok}/{total}（{(total == 0 ? 100.0 : 100.0 * ok / total):F1}%）");
        foreach (var failure in failures.Take(10))
        {
            Console.WriteLine("  " + failure);
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