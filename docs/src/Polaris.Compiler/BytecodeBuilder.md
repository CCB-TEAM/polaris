# `Polaris.Compiler/BytecodeBuilder`

**职责**：扁平 `KismetExpression` 序列 + 标签跳转；Build 时把标签解析成**函数内绝对字节偏移**。
**对外入口**：`NewLabel` / `MarkLabel` / `Emit` / `EmitJump` / `EmitJumpIfNot` / `Build`。
**内部结构**：表达式列表 + 标签表 + 待补丁表；Build 先测每条指令的序列化长度，再回填 `EX_Jump*` 的 `CodeOffset`。
**依赖**：UAssetAPI（表达式类型 + `ExpressionSerializer`）。
**取舍**：**不用 `KismetExpression.GetSize`**——UAssetAPI 的 `Visit` 把 5.6 的 4 字节 `FPackageIndex` 按 8 字节计，会算错跳转；改为「逐条序列化取长度」。

### `Build(asset: UAsset): KismetExpression[]`

**状态：✅ 已实现**（编译器用例：跳转目标全部落在指令起始）

- **签名与契约**：返回补好偏移的表达式数组；标签未定义抛 `InvalidOperationException`。
- **输入输出**：`asset` 只用于 `FName` 与序列化测量；返回值可直接交给 `ExpressionSerializer` 写回。
- **关键分支（伪代码）**：
  ```
  for i: starts[i] = position; position += Measure(expr[i])
  foreach (index→label): target = starts[labelIndex]
      EX_Jump.CodeOffset / EX_JumpIfNot.CodeOffset = target
  ```
- **边界与失败**：只有 `EX_Jump`/`EX_JumpIfNot` 支持标签；跳转目标可以为「整个函数末尾」（末尾标签）。
- **调用者与被调用者**：`Translator`、测试调用；调用 `ExpressionSerializer.WriteExpression` 测长度。
