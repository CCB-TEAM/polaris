# `Polaris.Assets/FunctionBytes`

**职责**：把资产里每个 `UFunction` 的字节码重新序列化后取 SHA-256，用来断言「改数据没动任何函数」。
**对外入口**：`FunctionBytes.Hashes(UAsset)`。
**内部结构**：遍历 `FunctionExport` → `ExpressionSerializer.WriteExpression` 逐条写入内存流 → SHA-256。
**依赖**：UAssetAPI（`KismetExpression` + `AssetBinaryWriter`）。**不依赖任何字节码库**（[ADR 0006](../decisions/0006-uht-style-sdk-own-bytecode.md)）。
**取舍**：只做「有没有变」的比对，不做反编译；空字节码函数跳过（本来就没有可执行内容）。

### `Hashes(asset: UAsset): IReadOnlyDictionary<string, string>`

**状态：✅ 已实现**（`CdoEditor.Verify` 用它做闸 4；9 个用例全绿）

- **签名与契约**：纯函数；返回「函数名 → 十六进制 SHA-256」；同一资产两次调用结果相同。
- **输入输出**：只看有 `ScriptBytecode` 的函数；序列化失败抛异常（由调用方记入失败项）。
- **关键分支（伪代码）**：
  ```
  foreach FunctionExport with ScriptBytecode.Length > 0:
      ms = new MemoryStream(); w = new AssetBinaryWriter(ms, asset)
      foreach expr: ExpressionSerializer.WriteExpression(expr, w)
      map[name] = SHA256(ms.ToArray())
  ```
- **边界与失败**：`KismetExpression` 解析失败时 `ScriptBytecode` 为 null（UAssetAPI 落回 `ScriptBytecodeRaw`），此时该函数被跳过——这是已知盲区，M3 自研字节码层后消除。
- **调用者与被调用者**：`CdoEditor.Verify` 调用；调用 UAssetAPI 序列化器。
