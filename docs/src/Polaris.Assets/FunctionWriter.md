# `Polaris.Assets/FunctionWriter`

**职责**：整体替换一个 `UFunction` 的 `ScriptBytecode`（不碰其它导出）；写盘仍走 `CardAsset.Save`。
**对外入口**：`Replace` / `Count`。
**内部结构**：按名字找 `FunctionExport`，替换表达式数组并清掉 raw 回退字段。
**依赖**：UAssetAPI（`FunctionExport`/`KismetExpression`）。
**取舍**：不手工改 `ScriptBytecodeSize`——`StructExport.Write` 会重算 iCode 总长与磁盘大小。

### `Replace(asset: UAsset, functionName: string, bytecode: IReadOnlyList<KismetExpression>): int`

**状态：✅ 已实现**（写回用例：`BP_CardFunctions.AddToBattleLog` 5 → 4 条，其余 293 个函数字节不变）

- **签名与契约**：返回替换前的表达式条数；函数不存在抛 `InvalidOperationException`。
- **输入输出**：`bytecode` 是编译产物（顶层表达式）；替换后 `ScriptBytecodeRaw=null`、`ScriptBytecodeSize=0`。
- **关键分支（伪代码）**：
  ```
  fn = Exports.OfType<FunctionExport>().First(name)
  old = fn.ScriptBytecode?.Length ?? 0
  fn.ScriptBytecode = bytecode; fn.ScriptBytecodeRaw = null; fn.ScriptBytecodeSize = 0
  ```
- **边界与失败**：只换字节码，不改属性表/参数/flags；函数不存在直接报错。
- **调用者与被调用者**：`Polaris.Cli apply`（后续）与测试调用；写盘由 `CardAsset.Save`。

### `Count(asset: UAsset, functionName: string): int?`

**状态：✅ 已实现**

- **签名与契约**：返回函数字节码条数；函数不存在返回 `null`。
- **输入输出**：只读。
- **关键分支（伪代码）**：`FirstOrDefault(name)?.ScriptBytecode?.Length`。
- **边界与失败**：无异常路径。
- **调用者与被调用者**：测试与诊断调用。
