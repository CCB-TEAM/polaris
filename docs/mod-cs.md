# 现在的 mod C# 是什么样

**状态：`✅ 已实现`（库 API 形态）。partial class 形态还是 `🚧`。** 最近更新：2026-12。

## 1. 现在能写的：一个 C# 程序调库

Polaris 目前**没有**蓝图类模型，也没有 `[PolarisClass]`。今天的 mod 就是一段普通 C# 程序
（或一条 CLI 命令），编辑面是 `Polaris.Assets` 的 API：

```csharp
using Polaris.Assets;
using Polaris.Pak;

var card = CardAsset.Open(cardPath, usmapPath, work);   // 类 + CDO + 原生父类
var report = CdoEditor.Apply(card,
[
    PatchSpec.Set("kredits", "3"),      // 改已有值（2 -> 3）
    PatchSpec.Set("defense", "4"),      // 改已有值（2 -> 4）
    PatchSpec.Set("hasBlitz", "true"),  // CDO 里没有 = 原生默认 -> 自动补差量条目
], outDir, verifyDir);                  // 写盘 + 重开比对 + 全部函数逐字节校验

var relative = ModPak.RelativeContentPath(cardPath);
ModPak.Build(
[
    new PakFile(relative, report.Saved.UAsset),
    new PakFile(Path.ChangeExtension(relative, ".uexp"), report.Saved.UExp),
], Path.Combine(outDir, "mod.pak"));
```

可直接运行的完整版：[samples/CardModDemo](../../samples/CardModDemo/Program.cs)：

```powershell
dotnet run --project samples/CardModDemo -- <卡.uasset> <kards.usmap> out
```

实测输出：

```
类       card_unit_t_70eot_C
原生父类 /Script/kards.BaseCardObject
  Updated  kredits      2 -> 3
  Updated  defense      2 -> 4
  Added    hasBlitz     - -> true
校验     OK（函数 3 个逐字节一致）
pak      out\mod.pak（7615 字节 / 2 条目）
mount    ../../../kards/Content/
```

等价的 CLI（不用写代码）：

```powershell
polaris cards set --asset <卡.uasset> --usmap <kards.usmap> `
  --set kredits=3 --set defense=4 --set hasBlitz=true `
  --out out --pak mod.pak
```

## 2. 现在能表达什么（边界）

| 能力 | 状态 |
|---|---|
| 改原生父类成员的默认值（int/bool/float） | ✅ |
| 给缺失字段补差量条目（`hasBlitz=true` 这种） | ✅ |
| `Reset`（删条目 = 回原生默认） | ✅ |
| `enum`（`faction`/`rarity`/`type`） | ⛔ 报错，未支持 |
| `text`（`title`/`Text`）、软引用、结构体、容器 | ⛔ 报错，未支持 |
| 本 BP 类自己的成员（如 `tempCard`） | ⛔ 报错（M2 只覆盖原生父类） |
| 改函数逻辑 / 事件体 | ⛔ 未实现（M3/M4） |
| 新增成员 / 新增函数 / 新卡 | ⛔ 未实现（M6/M7） |

## 3. 目标形态（`🚧` 未实现）

```csharp
[PolarisClass("card_event_test")]
public partial class CardEventTest : BaseCardObject_C
{
    int kredits = 3;                              // -> CDO 覆盖（已有成员）

    public override void OnPlayedFromHand(CardFunctionsRef cardFunction, CardRef targetCard)
    {
        cardFunction.GainKreditSlot(this, side);  // -> ubergraph 段
        if (IsValid(tempCard)) cardFunction.ChangeDefense(tempCard, cardID, 3, out var ok);
    }
}
```

要用上它，还缺四件东西（都在 [INDEX](INDEX.md) 的未实现清单里）：

1. **SDK 生成器**（M1）：把 `BaseCardObject_C`、`CardFunctionsRef`、`CardRef` 这些类型生成出来；
2. **Transpiler**（M3）：方法体 → `KNode`（含 `CallFunc_*` 临时变量、`out` 槽）；
3. **UbergraphBuilder**（M4）：事件体 → 段 + 回填存根入口；
4. **新成员/新函数**（M6/M7）：往类上挂 UFunction、加 FProperty。

**在 M4 之前**，`override OnPlayedFromHand` 这种写法编译得过（只是普通 C#），但 Polaris 不会把它变回字节码——
所以现阶段别写它，写「C# 调库」或 CLI。

## 4. 一个已定的语义

partial class 的字段 = **覆盖**（只允许类链上已有的成员，值写进 CDO），不是「新增成员」。
新增成员是 M7 的独立能力。这条写进了 [ADR 0005](decisions/0005-scope-existing-class-injection.md) 与
[card-fields §3](card-fields.md)。
