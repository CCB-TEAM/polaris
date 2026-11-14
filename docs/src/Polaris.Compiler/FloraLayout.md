# Polaris.Compiler / FloraLayout

**职责**：按 Kismet 的「内存偏移」（Flora / CUE4Parse 的 `Visit` 模型）计算表达式的布局尺寸，供跳转 `CodeOffset` 使用。

**对外入口**：`Size(KismetExpression)`、`Size(IReadOnlyList<KismetExpression>)`。

**依赖**：UAssetAPI 的表达式类。

**取舍（为什么不用序列化字节数）**

- 跳转 `CodeOffset` 不是「文件里写了几字节」，而是 UE/Flora 的 **in-memory offset**：属性指针按 8 字节、`FPackageIndex`/`StackNode` 按 8 字节、`FName` 按 8 字节计。UAssetAPI `GetSize`（`Visit`）与 CUE4Parse `StatementIndex` 用的是同一套模型。
- 我们最初用「序列化一条再量长度」当偏移，得到的是文件字节位置；写进 `CodeOffset` 后，Flora 反编译时报 `BlockJump` NRE / `Found unreached instructions`（目标不在语句起点）。实测 G1b 的 9 条语句在 Flora 的落点是 `[0,23,65,94,136,141,183,225]`，与我们文件偏移 `[0,39,101,134,196,201,263,317,319]` 一一对应但数值不同；改用本文件后 `JumpIfNot -> 141`、`Jump -> 183` 都落在起点。
- 否决的方案：继续用 UAssetAPI `GetSize` 原样（`EX_Let.Visit` 对指针按 8 计，而 UAssetAPI 1.1.0 的 `GetSize` 有 FPackageIndex 8/4 偏差，且我们无法控制 NuGet 版本）。这里显式列尺寸，行为可测。

## 函数

### `Size(expression: KismetExpression): int`

**状态：✅ 已实现**（G1b 重写后 `polaris offsets` 显示 `landedFlora=True`；`BytecodeCheck` 通过）

- **签名与契约**：返回该表达式及其子表达式的 in-memory 尺寸；遇到未覆盖的 token 抛 `NotSupportedException`。
- **输入输出**：`EX_Let` = 1 + 8 + Variable + Expression；`EX_Context` = 1 + Object + 12 + Context；`EX_FinalFunction`/`CallMath` = 1 + 8 + 参数 + 1；变量类 = 1 + 8；常量按类型（int/float 5、double/int64 9、byte 2、string 1+len+1、unicode 1+2len+2、name 9）；`EX_Jump` 5；`EX_JumpIfNot` 1+4+条件。
- **关键分支（伪代码）**：按 `expression` 类型 switch，递归子表达式求和；未覆盖 → 抛。
- **边界与失败**：只覆盖我们生成的子集；原始资产里其它类型由调用方退回 `BytecodeBuilder.Measure`（仅用于原始函数，不参与标签改写）。
- **调用者与被调用者**：`BytecodeBuilder.Build`、`BytecodeCheck.Run`、`polaris offsets`。

### `Size(expressions: IReadOnlyList<KismetExpression>): int`

**状态：✅ 已实现**

- **签名与契约**：逐条求和；空列表返回 0。
- **调用者与被调用者**：`FloraLayout.Size` 内部的 `EX_FinalFunction.Parameters` 等。
