# `Polaris.SdkGen/Json`

**职责**：最小 JSON 解析（object/array/string/number/bool/null），供 `JmapReader` 在无依赖的生成器里用。
**对外入口**：`Json.Parse(text)`。
**内部结构**：`JsonValue` 家族（`JsonObject`/`JsonArray`/`JsonString`/`JsonNumber`/`JsonBool`/`JsonNull`）+ 单遍递归下降解析器。
**依赖**：无（不用 System.Text.Json / Newtonsoft，保证源生成器零依赖）。
**取舍**：只读、不保序字典（`Dictionary` 插入序在 .NET 上稳定但不是契约）、不校验重复键；数字同时保留 `long` 与 `double`。

### `Parse(text: string): JsonValue`

**状态：✅ 已实现**（解析 5.1 MB `functions.json`，13064 个函数全量）

- **签名与契约**：返回根值；语法错误抛 `FormatException`（带位置）。
- **输入输出**：`text` 为完整 JSON 文本；不支持的形状没有——只接受标准 JSON。
- **关键分支（伪代码）**：
  ```
  SkipWhitespace
  '{' → object: 循环 "key": value
  '[' → array:  循环 value
  '"' → string（处理 \" \\ \/ \b \f \n \r \t \uXXXX）
  t/f/n → true/false/null
  其它 → number（long 或 double）
  ```
- **边界与失败**：末尾多余内容不报错（`Parse` 只读一个值）；嵌套深度未设上限（本输入 ~4 层）。
- **调用者与被调用者**：`JmapReader.Read` 调用；不调用别人。
