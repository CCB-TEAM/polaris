# Polaris

**用 C# 写 KARDS（UE 5.6）蓝图**：字段 = 蓝图成员，方法 = 蓝图函数；Roslyn 编译回 Kismet 字节码，
经 UAssetAPI 写回 `.uasset` / `.uexp`，再打成 mod `.pak`。

Polaris 是「**自研编译器 + UHT 风格 SDK**」的组合：

- **SDK 是 UHT 风格**：真实继承、`[UProperty]` 字段、`[UFunction]` 方法——C# 版 UHT 头文件
- **不经过私有中间模型**：没有 `KArg`/`KNode`，直接从 C# 语法树发射 `KismetExpression`
- **字节码层自研**：指令/跳转偏移/导入表/写回全部本仓库实现，UAssetAPI 只做资产与表达式序列化
- **可验证**：每次写盘都做「序列化→读回→跳转落点」自校验 + 其余函数字节 SHA-256 不变

> 内部项目，仅供 CCB-TEAM 使用。不包含任何游戏资产、密钥或映射文件。

---

## 现状（2026-12）

| 能力 | 状态 |
|---|---|
| **纯 jmap** → **UHT 风格 C# SDK**（484 文件 / 4356 类 / 5349 结构 / 1876 枚举 / 12617 方法） | ✅ |
| Roslyn **增量源生成器**（编译期自动生成 SDK） | ✅ |
| CDO 改值（原生父类成员：`kredits`/`attack`/`has*`…；改值 / 补差量条目 / Reset） | ✅ |
| mod `.pak` 导出（v3 / store / mount `../../../kards/Content/`，对真实 mod pak 逐字节验证） | ✅ |
| 编译器：静态/实例调用、`if-else`、`return`、`out/ref`、int/bool/string/float/double 字面量 | ✅ |
| 写回既有函数 + 导入解析（其余函数字节不变） | ✅ |
| `polaris apply`：注解 mod 一条命令写回（函数 + CDO 字段）+ 出 pak；多资产时每卡一个 pak | ✅ |
| **数据注解**：`[PolarisAsset]`/`[PolarisReplace]`/`[PolarisPatch]` 放进 mod 源码 | ✅ |
| 变量读写（赋值 / 变量作实参 / `out`、`ref` 实参） | ✅ |
| `return` 带返回值（`return 5;` → `EX_Return(IntConst)`） | ✅ |
| 局部变量声明（`int x = 1;` → 补 FProperty + `EX_Let`，写回可重开） | ✅ |
| 算术/比较/位运算/字符串/逻辑/一元表达式（含 `%`、`& \| ^ << >>`、`&& \|\|`、字符串拼接与比较） | ✅ |
| 条件分支（`if (x > 1)` → 比较 + `EX_JumpIfNot`） | ✅ |
| `out`/`ref`/`const` 参数（从纯 jmap 的 CPF flags 推出） | ✅ |
| 反编译 → 回编译字节一致（全量 Cards 语料 **6492/6492 函数**） | ✅ |
| ubergraph 重排、蓝图类基座、BP→C# | 🚧 |

## 数据流

```
jmap（纯）──► JmapFileReader ──► SdkEmitter ──► UHT 风格 SDK
                                                       ├─ CLI `polaris sdk`
                                                       └─ SdkSourceGenerator（编译期）

用户 C# mod ──► Roslyn ──► Translator ──► BytecodeBuilder ──► BytecodeCheck
                                              │
                                              ├─ AssetImportTable（类/函数 → FPackageIndex）
                                              ├─ FunctionWriter（替换 ScriptBytecode）
                                              ├─ CardAsset.Save（.uasset/.uexp）
                                              └─ ModPak（mod.pak）
```

细节与不变量见 [docs/dataflow.md](docs/dataflow.md)。

## 快速开始

```powershell
# 构建（.NET 10；离线环境依赖仓库外的 NuGet 缓存，见 docs/PLAN.md §8）
.\run.ps1 build

# 测试：13 个用例 + 两道编译闸（全量 SDK 编译、源生成器编译）
.\run.ps1 verify

# 生成 SDK 到 ..\.tmp\polaris\sdk-all
.\run.ps1 sdk
```

## 示例：修改一张卡（card_xx）

以 `card_unit_t_70eot`（Soviet / Allegiance / units）为例。

### 1) 改数值（CDO）——`samples/CardModDemo`

```powershell
dotnet run --project samples/CardModDemo -- `
  <Blueprints>/Cards/Soviet/Allegiance/units/card_unit_t_70eot.uasset `
  <kards.usmap> out
```

```csharp
var card = CardAsset.Open(cardPath, usmapPath, work);
var report = CdoEditor.Apply(card,
[
    PatchSpec.Set("kredits", "3"),      // 2 -> 3
    PatchSpec.Set("defense", "4"),      // 2 -> 4
    PatchSpec.Set("hasBlitz", "true"),  // CDO 本来没有 -> 补差量条目
], outDir, verifyWork);
```

实测：

```
Updated  kredits  2 -> 3
Updated  defense  2 -> 4
Added    hasBlitz - -> true
校验     OK（函数 3 个逐字节一致）
pak      mod.pak（7615 字节 / 2 条目）
```

### 2) 改事件逻辑（字节码）——`samples/BytecodeMod`

`samples/BytecodeMod/Mod.cs`（人写的 mod，**不是**本项目的编译源码）——目标资产/函数用**注解**集中声明：

```csharp
using Polaris.Sdk;
using Polaris.Sdk.Kards;

[PolarisAsset("Blueprints/Cards/Soviet/Allegiance/units/card_unit_t_70eot.uasset")]
public class T70Mod
{
    // 参数名必须与 UFunction 的参数名一致（OnPlayedFromHand 的 targetCard）
    [PolarisReplace("OnPlayedFromHand")]
    public void OnPlayed(BaseCardObject targetCard)
    {
        targetCard.CampaignAddAttack(1, false);
        return;
    }
}
```

```powershell
# 方式一：示例程序
dotnet run --project samples/BytecodeMod -- <Content> <kards.usmap> out

# 方式二：CLI（一条命令，带 pak）
dotnet run --project src/Polaris.Cli -- apply `
  --mod samples/BytecodeMod/Mod.cs --content <Content> `
  --usmap <kards.usmap> --out out `
  --sdk samples/SdkConsumer/bin/Debug/net10.0/SdkConsumer.dll --pak out/mod.pak [--dry-run]
```

实测：

```
目标     card_unit_t_70eot.uasset :: OnPlayedFromHand（1 条注解）
mod      OnPlayedFromHand 4 -> 3 条表达式
字节码   EX_Context, EX_Return, EX_EndOfScript
校验     OK（其余函数 2 个字节不变）
pak      mod.pak（7650 字节 / 2 条目）
mount    ../../../kards/Content/
```

> ⚠️ 这个示例整段替换了 `OnPlayedFromHand` 存根：原 ubergraph 逻辑不再执行（演示写回链路用）。
> 保留原逻辑、只改事件体是 M4（ubergraph 段重排 + 存根回填）的内容。

## 目录

```
src/Polaris.SdkGen/     usmap/jmap 解析 + UHT 风格 SDK 生成
src/Polaris.SourceGen/  增量源生成器（AdditionalFiles）
src/Polaris.Sdk/        生成代码引用的运行时声明（UObject/UStruct/TArray/[UClass]…）
src/Polaris.Compiler/   Translator / BytecodeBuilder / BytecodeCheck / AssetImportTable
src/Polaris.Assets/     CardAsset / CdoEditor / FunctionBytes / FunctionWriter
src/Polaris.Pak/        v3 pak 读写（vendored from solstice-pak，MIT）
src/Polaris.Cli/        polaris cards|pak|sdk
docs/                   架构、数据流、ADR、逐函数契约
samples/                CardModDemo（CDO）/ BytecodeMod（字节码）/ SdkConsumer / SdkGenConsumer
tests/                  Polaris.Tests（24 个用例）
```

## 限制

- 只支持 KARDS / UE 5.6 实测过的格式；不保证其他版本。
- 编译器是子集：`out/ref` 实参、struct 常量、ubergraph 事件体还没做。
- 运行期行为**未在游戏内验证**（静态闸全绿不等于运行正确）。
- 不新增资产（不做新卡）；不做可视化编辑器。

## 运行期验证

G1（游戏内）验证包与步骤：[docs/verification/g1-card-unit-t70eot.md](docs/verification/g1-card-unit-t70eot.md)（`kredits` 0 + 打出攻击 +5，pak 在 `..\.tmp\polaris\g1`）。

## 文档

- [docs/INDEX.md](docs/INDEX.md)：模块 / 函数看板 / 未实现清单
- [docs/PLAN.md](docs/PLAN.md)：里程碑与闸门
- [docs/dataflow.md](docs/dataflow.md)：数据流向与不变量
- [docs/decisions/](docs/decisions/)：ADR（UHT 风格 + 自研字节码层、CDO、pak 等）
