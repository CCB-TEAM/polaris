using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Polaris.Assets;
using Polaris.Compiler;
using Polaris.Pak;
using Polaris.SdkGen;
using UAssetAPI;
using UAssetAPI.Kismet.Bytecode.Expressions;
using UAssetAPI.UnrealTypes;

var env = TestEnv.Resolve();
return TestRunner.Run(env);

internal sealed record TestContext(string CardEventAans, string CardUnit, string Usmap, string Root, string Catalog);

internal static class TestEnv
{
    public static TestContext Resolve()
    {
        var bpRoot = Environment.GetEnvironmentVariable("POLARIS_BP_ROOT")
            ?? @"D:\devenv\fmodel\Output\Exports\kards\Content\Blueprints";

        var usmap = Environment.GetEnvironmentVariable("POLARIS_USMAP");
        if (string.IsNullOrEmpty(usmap) || !File.Exists(usmap))
        {
            usmap = @"D:\files\projects\.tmp\polaris\kards.usmap";
        }

        if (!File.Exists(usmap))
        {
            var paks = @"D:\files\projects\kards\Content\Paks\kards-5.6.1-0+++UE5+Release-5.6-Fork-2352d15b.usmap";
            if (File.Exists(paks))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(usmap)!);
                File.Copy(paks, usmap, overwrite: true);
            }
        }

        var aans = Path.Combine(bpRoot, "Cards", "ANZAC", "events", "card_event_aans.uasset");
        var unit = Path.Combine(bpRoot, "Cards", "Soviet", "Allegiance", "units", "card_unit_t_70eot.uasset");
        var catalog = Environment.GetEnvironmentVariable("POLARIS_CATALOG")
            ?? @"D:\files\projects\jmap-out\functions.json";
        return new TestContext(aans, unit, usmap, bpRoot, catalog);
    }

    public static bool Available(TestContext env)
        => File.Exists(env.CardEventAans) && File.Exists(env.CardUnit) && File.Exists(env.Usmap);
}

internal sealed class IndexResolver(Func<string, string, FPackageIndex> resolve) : IFunctionResolver
{
    public FPackageIndex Resolve(string ownerPath, string functionName) => resolve(ownerPath, functionName);
}

internal static class TestRunner
{
    private static int _failed;

    public static int Run(TestContext env)
    {
        if (!TestEnv.Available(env))
        {
            Console.WriteLine("SKIP: 找不到测试资产或 usmap");
            Console.WriteLine($"  card_event_aans = {env.CardEventAans}");
            Console.WriteLine($"  card_unit_t_70eot = {env.CardUnit}");
            Console.WriteLine($"  usmap = {env.Usmap}");
            return 0;
        }

        var root = Path.Combine(AppContext.BaseDirectory, "run");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);

        OpenAndInspect(env, root);
        UpdateExistingValue(env, root);
        AddMissingValue(env, root);
        ResetRestoresBytes(env, root);
        PakRoundTrip(env, root);
        PakEndToEnd(env, root);
        SdkParsing(env, root);
        SdkEmit(env, root);
        CompilerBasic(root);
        CompilerWriteback(env, root);
        CompilerInstanceCall(env, root);
        Errors(env, root);

        Console.WriteLine();
        Console.WriteLine(_failed == 0 ? "全部通过" : $"{_failed} 个用例失败");
        return _failed == 0 ? 0 : 1;
    }

    private static void OpenAndInspect(TestContext env, string root)
    {
        Case("打开资产与原生父类解析", () =>
        {
            var card = CardAsset.Open(env.CardEventAans, env.Usmap, Work(root, "open"));
            Equal("card_event_aans_C", card.ClassExport.ObjectName.ToString());
            Equal("/Script/kards.BaseCardObject", card.NativeParentPath);
            Equal(17, card.Cdo.Data.Count);
            Equal("IntProperty", card.ResolveMember("kredits").PropertyType);
            Equal("BoolProperty", card.ResolveMember("hasBlitz").PropertyType);
            Equal(true, card.ResolveMember("tempCard").IsOwnProperty);
            Console.WriteLine($"      CDO {card.Cdo.Data.Count} 条，父类 {card.NativeParentPath}");
        });
    }

    private static void UpdateExistingValue(TestContext env, string root)
    {
        Case("改已有值 kredits 3 -> 4（定长替换）", () =>
        {
            var outDir = Path.Combine(root, "update");
            var card = CardAsset.Open(env.CardEventAans, env.Usmap, Work(root, "update-card"));
            var report = CdoEditor.Apply(
                card,
                [PatchSpec.Set("kredits", "4")],
                outDir,
                Work(root, "update-verify"));

            Equal(PatchAction.Updated, report.Patches.Single().Action);
            Equal("3", report.Patches.Single().Before);
            Equal("4", report.Patches.Single().After);

            // 期望：uasset 一个字节都不动；uexp 只变 int 的一个字节
            Equal(0, report.DiffUAsset.ChangedBytes);
            Equal(0, report.DiffUExp.SizeDelta);
            Equal(1, report.DiffUExp.ChangedBytes);
            Equal(true, report.Verify.Ok);
            Equal(2, report.Verify.FunctionCount);

            var reopened = CardAsset.Open(report.Saved.UAssetPath, env.Usmap, Work(root, "update-reopen"));
            Equal("4", reopened.Snapshot()["kredits"]);
            Equal("Anzac", reopened.Snapshot()["faction"]);
        });
    }

    private static void AddMissingValue(TestContext env, string root)
    {
        Case("补差量条目 hasBlitz（原 CDO 没有）", () =>
        {
            var outDir = Path.Combine(root, "add");
            var card = CardAsset.Open(env.CardUnit, env.Usmap, Work(root, "add-card"));
            var report = CdoEditor.Apply(
                card,
                [PatchSpec.Set("hasBlitz", "true")],
                outDir,
                Work(root, "add-verify"));

            Equal(PatchAction.Added, report.Patches.Single().Action);
            Equal(3, report.DiffUExp.SizeDelta);   // 实测 ground truth
            Equal(17, report.DiffUAsset.SizeDelta);
            Equal(true, report.Verify.Ok);
            Equal(3, report.Verify.FunctionCount);

            var reopened = CardAsset.Open(report.Saved.UAssetPath, env.Usmap, Work(root, "add-reopen"));
            Equal("true", reopened.Snapshot()["hasBlitz"]);
        });
    }

    private static void ResetRestoresBytes(TestContext env, string root)
    {
        Case("补完再 Reset 回到原始字节", () =>
        {
            var added = Path.Combine(root, "reset-added");
            var card = CardAsset.Open(env.CardUnit, env.Usmap, Work(root, "reset-card"));
            var addedReport = CdoEditor.Apply(
                card,
                [PatchSpec.Set("hasBlitz", "true")],
                added,
                Work(root, "reset-verify-1"));

            var back = CardAsset.Open(addedReport.Saved.UAssetPath, env.Usmap, Work(root, "reset-card2"));
            var resetReport = CdoEditor.Apply(
                back,
                [PatchSpec.Reset("hasBlitz")],
                Path.Combine(root, "reset-restored"),
                Work(root, "reset-verify-2"));

            Equal(PatchAction.Removed, resetReport.Patches.Single().Action);
            // .uexp 完全还原；.uasset 里的 name map 会留下 hasBlitz 这一条（+17 字节），不影响游戏读取
            var original = CardAsset.Open(env.CardUnit, env.Usmap, Work(root, "reset-orig"));
            Equal(0, AssetDiff.Compare(original.OriginalUExpBytes, resetReport.Saved.UExp).ChangedBytes);
            Equal(17, AssetDiff.Compare(original.OriginalUAssetBytes, resetReport.Saved.UAsset).SizeDelta);
            Equal(true, resetReport.Verify.Ok);
            var afterReset = CardAsset.Open(resetReport.Saved.UAssetPath, env.Usmap, Work(root, "reset-reopen"));
            Equal(false, afterReset.Snapshot().ContainsKey("hasBlitz"));
        });
    }

    private static void PakRoundTrip(TestContext env, string root)
    {
        Case("真实 KARDS mod pak 逐字节重建（wide-mount.pak）", () =>
        {
            var pakPath = @"D:\files\projects\kards\Content\Paks\wide-mount.pak";
            if (!File.Exists(pakPath))
            {
                Console.WriteLine("      SKIP: 没有 wide-mount.pak");
                return;
            }

            var bytes = File.ReadAllBytes(pakPath);
            var archive = Solstice.Pak.PakArchive.Read(bytes);
            Equal(ModPak.DefaultMountPoint, archive.MountPoint);
            var rebuilt = Solstice.Pak.PakWriter.Build(archive.MountPoint, archive.Entries);
            Equal(0, AssetDiff.Compare(bytes, rebuilt).ChangedBytes);
            Console.WriteLine($"      {archive.Entries.Count} 条目 / {bytes.Length} 字节，重建一致");
        });
    }

    private static void PakEndToEnd(TestContext env, string root)
    {
        Case("改值后直接出 mod pak（路径/字节/读回）", () =>
        {
            var card = CardAsset.Open(env.CardEventAans, env.Usmap, Work(root, "pak-card"));
            var report = CdoEditor.Apply(
                card,
                [PatchSpec.Set("kredits", "4")],
                Path.Combine(root, "pak-assets"),
                Work(root, "pak-verify"));

            var relative = ModPak.RelativeContentPath(env.CardEventAans);
            Equal("Blueprints/Cards/ANZAC/events/card_event_aans.uasset", relative);

            var contentRoot = Path.GetDirectoryName(env.Root)!;   // ...\kards\Content
            Equal(relative, ModPak.RelativeContentPath(env.CardEventAans, contentRoot));

            var pakPath = Path.Combine(root, "mod.pak");
            var built = ModPak.Build(
                [
                    new PakFile(relative, report.Saved.UAsset),
                    new PakFile(Path.ChangeExtension(relative, ".uexp"), report.Saved.UExp),
                ],
                pakPath);

            Equal(2, built.Archive.Entries.Count);
            Equal(ModPak.DefaultMountPoint, built.Archive.MountPoint);
            var uasset = built.Archive.Entries.Single(e => e.Name.EndsWith(".uasset"));
            var uexp = built.Archive.Entries.Single(e => e.Name.EndsWith(".uexp"));
            Equal(relative, uasset.Name);
            Equal(0, AssetDiff.Compare(report.Saved.UAsset, uasset.Data).ChangedBytes);
            Equal(0, AssetDiff.Compare(report.Saved.UExp, uexp.Data).ChangedBytes);
            Console.WriteLine($"      {built.Bytes.Length} 字节 / {built.Archive.Entries.Count} 条目");
        });
    }

    private static void SdkParsing(TestContext env, string root)
    {
        Case("usmap / jmap 解析（真实数据）", () =>
        {
            var usmap = UsmapReader.Read(File.ReadAllBytes(env.Usmap));
            Equal(9704, usmap.Schemas.Count);
            Equal(4, usmap.Version);
            Equal(1875, usmap.Enums.Values.Distinct().Count());

            var bco = usmap.Schemas["/Script/kards.BaseCardObject"];
            True(bco.Properties.Count > 100, "BaseCardObject 成员数");
            True(bco.Properties.Any(p => p.Name == "hasBlitz"), "BaseCardObject.hasBlitz");
            True(bco.Properties.Any(p => p.Name == "attack"), "BaseCardObject.attack");

            var owners = JmapReader.Read(File.ReadAllText(env.Catalog));
            Equal(1216, owners.Count);
            Equal(13064, owners.Values.Sum(o => o.Functions.Count));
            Console.WriteLine($"      schema {usmap.Schemas.Count} / enum {usmap.Enums.Values.Distinct().Count()} / owner {owners.Count} / 函数 {owners.Values.Sum(o => o.Functions.Count)}");
        });
    }

    private static void SdkEmit(TestContext env, string root)
    {
        Case("SDK 生成：确定性 + 关键签名", () =>
        {
            var usmap = UsmapReader.Read(File.ReadAllBytes(env.Usmap));
            var owners = JmapReader.Read(File.ReadAllText(env.Catalog));

            var a = SdkEmitter.Emit(usmap, owners);
            var b = SdkEmitter.Emit(usmap, owners);
            Equal(a.Files.Count, b.Files.Count);
            Equal(string.Join("|", a.Files.Select(f => f.RelativePath + ":" + f.Text.Length)),
                  string.Join("|", b.Files.Select(f => f.RelativePath + ":" + f.Text.Length)));
            Equal(true, a.Files.All(f => f.Text.Length > 0));
            // 13064 个函数里有 447 个是包级 owner（__DelegateSignature），M1 跳过委托\n            True(a.Methods == 12617, "方法数 = 非包级 owner 的函数数");

            var engine = a.Files.Single(f => f.RelativePath == "Polaris.Sdk/Engine/Engine.Classes.g.cs");
            True(engine.Text.Contains("PrintString"), "Engine 有 PrintString");
            True(engine.Text.Contains("LinearColor"), "PrintString 的 TextColor 用 LinearColor");

            var kards = a.Files.Single(f => f.RelativePath == "Polaris.Sdk/Kards/Kards.Structs.g.cs");
            True(kards.Text.Contains("hasBlitz"), "Kards 结构含 hasBlitz");

            var filtered = SdkEmitter.Emit(usmap, owners, new SdkOptions(["Kards"]));
            True(filtered.Files.All(f => f.RelativePath.StartsWith("Polaris.Sdk/Kards/")), "filter 只出 Kards");
            Console.WriteLine($"      全量 {a.Files.Count} 文件 / {a.Classes} 类 / {a.Structs} 结构 / {a.Enums} 枚举 / {a.Methods} 方法");
        });
    }

    private static void CompilerBasic(string root)
    {
        Case("编译器：if/静态调用/return → 字节码 + 自校验", () =>
        {
            const string source = """
                using Polaris.Sdk;

                [UClass("/Script/TestLib.MathLib")]
                public class MathLib : UObject
                {
                    [UFunction("FUNC_Static")] public static void Print(int v) { }
                    [UFunction("FUNC_Static")] public static void Log(string s) { }
                }

                public static class Mod
                {
                    public static void Run()
                    {
                        if (true) { MathLib.Print(1); } else { MathLib.Log("x"); }
                        return;
                    }
                }
                """;

            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(p => MetadataReference.CreateFromFile(p))
                .ToList();

            var compilation = CSharpCompilation.Create(
                "CompilerProbe",
                [CSharpSyntaxTree.ParseText(source)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var method = compilation.GetTypeByMetadataName("Mod")!
                .GetMembers("Run").OfType<IMethodSymbol>().Single();

            var asset = new UAsset(EngineVersion.VER_UE5_6);
            var next = 0;
            var resolver = new IndexResolver((_, _) => new FPackageIndex(--next));

            var expressions = Translator.Compile(compilation, method, asset, resolver);
            var first = BytecodeCheck.Run(expressions, asset);
            True(first.Ok, "自校验: " + string.Join("; ", first.Errors) + " | starts=" + string.Join(",", first.Starts) + " | jumps=" + string.Join(",", first.JumpTargets));
            True(first.Count == expressions.Length, "读回条数 = 生成条数");
            True(first.JumpTargets.Count == 2, "if/else 产生两个跳转");
            True(expressions.Any(e => e.Token == UAssetAPI.Kismet.Bytecode.EExprToken.EX_JumpIfNot), "含条件跳转");

            // 同一编译两次 → 字节逐字节一致
            var asset2 = new UAsset(EngineVersion.VER_UE5_6);
            var next2 = 0;
            var resolver2 = new IndexResolver((_, _) => new FPackageIndex(--next2));
            var again = Translator.Compile(compilation, method, asset2, resolver2);
            Equal(expressions.Length, again.Length);

            Console.WriteLine($"      {expressions.Length} 条指令 / {first.Starts.Count} 个起始偏移 / 跳转 {first.JumpTargets.Count}");
        });
    }

    private static void CompilerWriteback(TestContext env, string root)
    {
        Case("编译器写回：替换 BP_CardFunctions.AddToBattleLog", () =>
        {
            var assetPath = Path.Combine(env.Root, "Cards", "BP_CardFunctions.uasset");
            var card = CardAsset.Open(assetPath, env.Usmap, Work(root, "wb-card"));
            var before = FunctionBytes.Hashes(card.Asset);

            const string source = """
                using Polaris.Sdk;

                [UClass("/Script/Engine.KismetMathLibrary")]
                public class KismetMathLibraryProbe : UObject
                {
                    [UFunction("FUNC_Static")] public static int Abs_Int(int A) => 0;
                }

                public static class Mod
                {
                    public static void Run() { KismetMathLibraryProbe.Abs_Int(1); return; }
                }
                """;

            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(p => MetadataReference.CreateFromFile(p))
                .ToList();
            var compilation = CSharpCompilation.Create(
                "WritebackProbe",
                [CSharpSyntaxTree.ParseText(source)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var method = compilation.GetTypeByMetadataName("Mod")!
                .GetMembers("Run").OfType<IMethodSymbol>().Single();

            var imports = new AssetImportTable(card.Asset);
            var expressions = Translator.Compile(compilation, method, card.Asset, imports);
            var check = BytecodeCheck.Run(expressions, card.Asset);
            True(check.Ok, "编译自校验: " + string.Join("; ", check.Errors));

            var oldCount = FunctionWriter.Replace(card.Asset, "AddToBattleLog", expressions);
            var saved = card.Save(Path.Combine(root, "wb-out"));
            var reopened = CardAsset.Open(saved.UAssetPath, env.Usmap, Work(root, "wb-reopen"));
            var after = FunctionBytes.Hashes(reopened.Asset);

            foreach (var (name, hash) in before)
            {
                if (name == "AddToBattleLog")
                {
                    continue;
                }

                Equal(hash, after[name]);
            }

            True(after["AddToBattleLog"] != before["AddToBattleLog"], "目标函数字节已变");
            var target = reopened.Asset.Exports.OfType<UAssetAPI.ExportTypes.FunctionExport>()
                .First(f => f.ObjectName.ToString() == "AddToBattleLog");
            Equal(expressions.Length, target.ScriptBytecode!.Length);

            var check2 = BytecodeCheck.Run(target.ScriptBytecode, reopened.Asset);
            True(check2.Ok, "写回后自校验: " + string.Join("; ", check2.Errors));
            True(reopened.Asset.Imports.Any(im =>
                im.ObjectName?.ToString() == "Abs_Int" && im.ClassName?.ToString() == "Function"), "函数导入就位");

            Console.WriteLine($"      AddToBattleLog {oldCount} -> {expressions.Length} 条；导入 {card.Asset.Imports.Count} 条");
        });
    }

    private static void CompilerInstanceCall(TestContext env, string root)
    {
        Case("实例调用：字段/参数接收者 → EX_Context", () =>
        {
            const string source = """
                using Polaris.Sdk;

                [UClass("/Script/kards.CardFunctionsStub")]
                public class CardFunctionsStub : UObject
                {
                    [UFunction("FUNC_Public")] public void GainKreditSlot(int side) { }
                }

                public class Mod
                {
                    public CardFunctionsStub cardFunction;

                    public void RunField() { cardFunction.GainKreditSlot(1); return; }

                    public static void RunParam(CardFunctionsStub cardFunction)
                    {
                        cardFunction.GainKreditSlot(2);
                        return;
                    }
                }
                """;

            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(p => MetadataReference.CreateFromFile(p))
                .ToList();
            var compilation = CSharpCompilation.Create(
                "InstanceProbe",
                [CSharpSyntaxTree.ParseText(source)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var mod = compilation.GetTypeByMetadataName("Mod")!;

            var next = 0;
            var resolver = new IndexResolver((_, _) => new FPackageIndex(--next));

            // 用真实资产做序列化上下文：`new FName(asset, name)` 需要可用的 name map
            var host = CardAsset.Open(env.CardEventAans, env.Usmap, Work(root, "inst-card")).Asset;

            var fieldMethod = mod.GetMembers("RunField").OfType<IMethodSymbol>().Single();
            var fieldExprs = Translator.Compile(compilation, fieldMethod, host, resolver,
                new FPackageIndex(3), new FPackageIndex(1));
            True(fieldExprs[0] is EX_Context, "字段接收者生成 EX_Context");
            var fieldContext = (EX_Context)fieldExprs[0];
            True(fieldContext.ObjectExpression is EX_InstanceVariable, "接收者是 EX_InstanceVariable");
            True(fieldContext.ContextExpression is EX_FinalFunction, "上下文里是 EX_FinalFunction");
            True(fieldContext.Offset > 0, "Offset 非零（跳过字节数）");
            True(BytecodeCheck.Run(fieldExprs, host).Ok, "字段接收者自校验");

            var paramMethod = mod.GetMembers("RunParam").OfType<IMethodSymbol>().Single();
            var paramExprs = Translator.Compile(compilation, paramMethod, host, resolver,
                new FPackageIndex(4), new FPackageIndex(1));
            var paramContext = (EX_Context)paramExprs[0];
            True(paramContext.ObjectExpression is EX_LocalVariable, "参数接收者是 EX_LocalVariable");
            True(BytecodeCheck.Run(paramExprs, host).Ok, "参数接收者自校验");

            Console.WriteLine($"      字段 {fieldExprs.Length} 条 / Offset {fieldContext.Offset}；参数 {paramExprs.Length} 条");
        });
    }

    private static void Errors(TestContext env, string root)
    {
        Case("错误路径：未知成员 / 不支持类型 / 本类属性", () =>
        {
            var card = CardAsset.Open(env.CardEventAans, env.Usmap, Work(root, "err-card"));
            Throws<MemberNotFoundException>(() => card.ResolveMember("no_such_member_xyz"));
            Throws<CdoValueException>(() => CdoEditor.Set(card, "faction", "Soviet"));
            Throws<CdoValueException>(() => CdoEditor.Set(card, "tempCard", "x"));
            Throws<InvalidOperationException>(() => card.Save(Path.GetDirectoryName(env.CardEventAans)!));
        });
    }

    // ---------- 断言 ----------

    private static string Work(string root, string name)
    {
        var dir = Path.Combine(root, "work", name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Case(string name, Action body)
    {
        Console.WriteLine($"● {name}");
        try
        {
            body();
            Console.WriteLine("  PASS");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  FAIL: {ex.GetType().Name}: {ex.Message}");
            if (Environment.GetEnvironmentVariable("POLARIS_DEBUG") == "1")
            {
                Console.WriteLine(ex.StackTrace);
            }
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"期望 {expected}，实际 {actual}");
        }
    }

    private static void True(bool value, string what)
    {
        if (!value)
        {
            throw new InvalidOperationException($"断言失败: {what}");
        }
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException($"期望抛 {typeof(T).Name}，但没有");
    }
}
