# `Polaris.Compiler/KismetDecompiler`

**职责**：字节码 ↔ 自有节点树。反编译（`KismetExpression[]` → `KNode`）与回编译（`KNode` → `KismetExpression[]`），
用「反编译 → 回编译 → 字节比对」确认实现对既有函数无损。
**对外入口**：`KismetDecompiler.Decompile` / `KismetRecompiler.Recompile` / `RoundTrip.Verify`。
**内部结构**：节点类型 `KConst`/`KVar`/`KCall`/`KLet`/`KPersist`/`KReturn`/`KEnd`/`KSeq`/`KIf`；回编译用 `BytecodeBuilder` 重新发指令并回填跳转。
**依赖**：UAssetAPI（表达式类型 + 序列化）。
**取舍**：只覆盖编译器子集；遇到不认识的指令抛 `NotSupportedException`（不静默降级）。`EX_LocalFinalFunction` 与 `EX_FinalFunction` 分开保留（否则回编译字节会变）。

### `Decompile(expressions: IReadOnlyList<KismetExpression>): IReadOnlyList<KNode>`

**状态：✅ 已实现**（`card_event_aans.OnPlayedFromHand` 4 条表达式、if/else 分支程序均往返一致）

- **签名与契约**：纯函数；解析 `EX_JumpIfNot` + `EX_Jump` 的 if/else 模式；其它跳转形态抛 `NotSupportedException`。
- **输入输出**：常量 → `KConst`；`EX_LocalVariable`/`EX_InstanceVariable` → `KVar`；`EX_FinalFunction`/`EX_LocalFinalFunction`/`EX_Context` → `KCall`；`EX_Let` → `KLet`；`EX_LetValueOnPersistentFrame` → `KPersist`；`EX_Return`/`EX_EndOfScript` → `KReturn`/`KEnd`。
- **关键分支（伪代码）**：
  ```
  顶层按 starts[] 解析 JumpIfNot/if-else → KIf(cond, KSeq(then), KSeq(else)/null)
  switch expr:
    IntConst/True/False/String/Nothing/Self → KConst
    LocalVariable/InstanceVariable → KVar(instance?)
    LocalFinalFunction → KCall(IsLocalFinal)
    Context(ContextExpression=LocalFinal/Final) → KCall(receiver)
    Let → KLet(pointer, instance?, value); LetValueOnPersistentFrame → KPersist
    Return → KReturn(value|Nothing); EndOfScript → KEnd
    _ → throw NotSupportedException(token)
  ```
- **边界与失败**：条件跳转/switch/结构体常量暂不支持；错误信息带 token。
- **调用者与被调用者**：`RoundTrip.Verify` 调用；不依赖资产。

### `Recompile(nodes: IReadOnlyList<KNode>, asset: UAsset): KismetExpression[]`

**状态：✅ 已实现**（往返用例）

- **签名与契约**：按节点顺序重新发指令；调用/跳转的偏移重新计算。
- **输入输出**：`KCall` 的 `Callee` 保留原 `FPackageIndex`（不重新解析导入）；`KLet` 同时写 `Value` 指针与 `Variable` 表达式（UAssetAPI 的 `EX_Let` 两处都要）。
- **关键分支（伪代码）**：
  ```
  foreach node: Emit(node)
  KCall → EX_Context{Offset=Measure(inner)} 或 EX_FinalFunction/EX_LocalFinalFunction
  KLet → EX_Let{Value=pointer, Variable=var, Expression}
  KPersist → EX_LetValueOnPersistentFrame
  ```
- **边界与失败**：节点种类与反编译一一对应；未覆盖种类抛 `NotSupportedException`。
- **调用者与被调用者**：`RoundTrip.Verify` 调用；调用 `BytecodeBuilder`。

### `Verify(expressions: IReadOnlyList<KismetExpression>, asset: UAsset): RoundTripReport`

**状态：✅ 已实现**（真实函数 `OnPlayedFromHand` 与编译产物均通过）

- **签名与契约**：序列化原指令 → 反编译 → 回编译 → 序列化 → `SequenceEqual`；异常捕获为报告，不抛出。
- **输入输出**：`Ok=true` 表示字节完全一致；失败时 `Error` 给出「回编译字节不一致」或异常消息。
- **关键分支（伪代码）**：
  ```
  nodes = Decompile(expressions)
  rebuilt = Recompile(nodes, asset)
  return Serialize(expressions) == Serialize(rebuilt)
  ```
- **边界与失败**：只证明「反编译/回编译对当前子集无损」，不等于运行语义正确。
- **调用者与被调用者**：测试与未来 `polaris check` 调用。
