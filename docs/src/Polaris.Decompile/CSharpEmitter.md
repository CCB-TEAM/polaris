# `Polaris.Decompile/CSharpEmitter`

**职责**：把 `KNode` / `UberGraphNode` 输出成可读 C#（蓝图类基座），并用**存根入口表**给段命名。
**对外入口**：`EmitClass` / `EmitMethod` / `NameSectionsByEntries`。
**内部结构**：`EmitClass` 组合成员声明 + 每个函数一个方法；`EmitMethod` 按节点类型发 C# 语句；`NameSectionsByEntries` 用 bmap 入口表替代「猜段首调用」。
**依赖**：`Solstice.Kismet` 反编译结果、bmap。
**取舍**：输出目标是**人能改的 C#**（可编译、可 `goto`），不是与原始字节一一对应（[ADR 0001](../decisions/0001-ir-roslyn-source.md)）。

### `EmitClass(model: BlueprintClassModel): string`

**状态：🚧 未实现**

- **签名与契约**：生成 `abstract partial class <Class>_Base : <Parent>`：字段 = 成员（带默认值注释）、抽象/虚方法 = 每个 `UFunction`（含参数方向）。
- **输入输出**：父类名来自 bmap 的 `SuperStruct`；成员默认值以 `// CDO: 3` 注释保留（避免反编译产物写回 CDO 造成意外改动）；函数体来自 `EmitMethod`，无法反编译的标 `throw new PolarisUnsupported(...)`。
- **关键分支（伪代码）**：
  ```
  emit header(namespace, using, class decl, 父类)
  for m in model.Members: emit 属性声明（默认值注释）
  for f in model.Functions: emit 方法（参数 in/out/ref 按 jmap 判定表）
  ```
- **边界与失败**：`S_*` 蓝图结构没有 usmap schema → 成员类型退成 `KStruct` 并加 `// TODO`；构造函数/`UberGraphFrame` 不进 C#（属资产内部）。
- **调用者与被调用者**：`SdkGenerator` 调用；调用 `EmitMethod`、`TypeMap.ToCSharp`。

### `EmitMethod(fn: FunctionModel): string`

**状态：🚧 未实现**

- **签名与契约**：一个 `UFunction` → 一段 C# 方法体；保证可编译。
- **输入输出**：平坦函数用 if/while/赋值/调用；`BlockGraphNode` 用 `goto` 标号；`UberGraphNode` 的每个段 → 一个 `case`/方法片段（按 `NameSectionsByEntries` 命名）。
- **关键分支（伪代码）**：
  ```
  foreach node:
     IfNode → if (cond) { … } else { … }
     LoopNode/BottomLoopNode → while/do-while
     AssignNode → 目标 = 值（bool 用 AssignBool）
     CallNode → 调用表达式（返回引脚存临时变量）
     BlockGraphNode → label/ goto
  ```
- **边界与失败**：跳转越出片段时保留 `goto`，不硬造 if（条款见 HANDOVER 的「规则一」）；无法表达的节点 `throw new PolarisUnsupported`，绝不静默丢弃。
- **调用者与被调用者**：`EmitClass` 调用；调用 `NameSectionsByEntries`。

### `NameSectionsByEntries(uber: UberGraphNode, entries: IReadOnlyDictionary<string,int>): IReadOnlyList<string>`

**状态：🚧 未实现**

- **签名与契约**：用「入口偏移 → 存根名」给段贴名字；没有入口的段退回 `Section@<offset>`。
- **输入输出**：`entries` 来自 bmap；返回与段等长的名字表。一个段可能对应多个入口（实测 `BP_Chat` 4 段 6 入口），此时名字用 `A|B` 复合。
- **关键分支（伪代码）**：
  ```
  for s in uber.Sections:
      names = entries.Where(kv => 该入口落在 [s.Start, 下一段.Start)).Select(kv => kv.Key)
      label = names.Any() ? names.Join("|") : $"Section@{s.StartOffset}"
  ```
- **边界与失败**：入口落在段中间时（`card_unit_t_70eot` 272/388）按区间归属，不重新切段——切段会改变布局；读侧只负责「可读且不撒谎」。
- **调用者与被调用者**：`EmitMethod` 调用；输入来自 `AssetIndex.ReadUbergraphEntries`。
