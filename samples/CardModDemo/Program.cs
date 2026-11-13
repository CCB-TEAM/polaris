// 一个「现在的 Polaris mod」：C# 程序 + 库 API（还没有 partial class 模型）。
//
// 用法：
//   dotnet run --project samples/CardModDemo -- <卡.uasset> <kards.usmap> [outDir]
//
// 做的事：改 CDO 三个值（两个已有、一个差量补条目）→ 写资产 → 出 mod pak。

using Polaris.Assets;
using Polaris.Pak;

var cardPath = args.Length > 0
    ? args[0]
    : @"D:\devenv\fmodel\Output\Exports\kards\Content\Blueprints\Cards\Soviet\Allegiance\units\card_unit_t_70eot.uasset";
var usmapPath = args.Length > 1
    ? args[1]
    : @"D:\files\projects\.tmp\polaris\kards.usmap";
var outDir = args.Length > 2 ? args[2] : Path.Combine(AppContext.BaseDirectory, "out");

var work = Path.Combine(outDir, "_work");
Directory.CreateDirectory(outDir);

// ① 打开卡（类 + CDO + 原生父类 schema）
var card = CardAsset.Open(cardPath, usmapPath, work);
Console.WriteLine($"类       {card.ClassExport.ObjectName}");
Console.WriteLine($"原生父类 {card.NativeParentPath}");

// ② 声明这个 mod 想改什么：已有值直接改；CDO 里没有的（= 原生默认）会自动补一条
var patches = new[]
{
    PatchSpec.Set("kredits", "3"),      // 费用 2 -> 3（改已有值）
    PatchSpec.Set("defense", "4"),      // 防御 2 -> 4
    PatchSpec.Set("hasBlitz", "true"),  // 闪击：CDO 原本没有 -> 补差量条目
};

// ③ 改 → 写盘 → 重开比对 + 全部函数逐字节校验（失败会抛异常，不落半成品）
var report = CdoEditor.Apply(card, patches, outDir, Path.Combine(work, "verify"));
foreach (var p in report.Patches)
{
    Console.WriteLine($"  {p.Action,-8} {p.Name,-12} {p.Before ?? "-"} -> {p.After ?? "(删)"}");
}

Console.WriteLine($"差异     .uasset {report.DiffUAsset.ChangedBytes} 字节 / .uexp {report.DiffUExp.ChangedBytes} 字节");
Console.WriteLine($"校验     {(report.Verify.Ok ? "OK" : "FAIL")}（函数 {report.Verify.FunctionCount} 个逐字节一致）");

// ④ 直接出 mod pak（mount ../../../kards/Content/，v3/store）
var relative = ModPak.RelativeContentPath(cardPath);
var pak = ModPak.Build(
    [
        new PakFile(relative, report.Saved.UAsset),
        new PakFile(Path.ChangeExtension(relative, ".uexp"), report.Saved.UExp),
    ],
    Path.Combine(outDir, "mod.pak"));

Console.WriteLine($"pak      {pak.PakPath}（{pak.Bytes.Length} 字节 / {pak.Archive.Entries.Count} 条目）");
Console.WriteLine($"mount    {pak.Archive.MountPoint}");
