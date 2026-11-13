# `Polaris.Compiler/BytecodeCheck`

**职责**：编译器自校验：序列化 → 用 UAssetAPI 读回 → 比对条数 → 检查跳转目标落在指令起始。
**对外入口**：`BytecodeCheck.Run(expressions, asset)`。
**内部结构**：一次写出 + 一次读回，记录每条指令的起始偏移与所有 `EX_Jump*` 的目标。
**依赖**：UAssetAPI（`ExpressionSerializer`/`AssetBinaryReader`）。
**取舍**：不做语义校验（不判断指令是否「合理」），只保证字节级自洽；真正的行为验证在游戏内。

### `Run(expressions: IReadOnlyList<KismetExpression>, asset: UAsset): CheckReport`

**状态：✅ 已实现**（编译器用例调用；跳转 2 处全部命中）

- **签名与契约**：返回 `(Ok, Errors, Count, Starts, JumpTargets)`；串行化/读回失败记入 `Errors`，不抛异常。
- **输入输出**：`expressions` 为顶层指令；`asset` 提供名字表与序列化上下文。
- **关键分支（伪代码）**：
  ```
  serialize all → bytes
  read back until EOF: 记录 start 与 token
  if 读回条数 != 生成条数 → 错误
  foreach Jump/JumpIfNot: target ∈ Starts? else 错误
  ```
- **边界与失败**：只收集**顶层**跳转（当前编译器不在嵌套表达式里生成跳转）；`Count` 是读回条数，可与生成条数对账。
- **调用者与被调用者**：测试与未来的 `apply` 调用；调用 UAssetAPI 序列化器。
