# `Polaris.SdkGen/JmapReader`

**职责**：解析 jmap `functions.json` 的 `objects` 树 → owner（原生类）→ 函数 → 参数/返回。
**对外入口**：`JmapReader.Read(string json)`。
**内部结构**：`Json.Parse` → 遍历 `objects` → `functions` → `params`，`JmapType.FromJson` 递归处理 `inner/key/value`。
**依赖**：`Json`（自带最小解析器，netstandard 可用、无 NuGet）。
**取舍**：本 catalog 没有 `ref`/`const`（老 extractor），参数方向只能靠 `dir`；`class` 字段兼作对象类路径与结构体路径。

### `Read(json: string): IReadOnlyDictionary<string, JmapOwner>`

**状态：✅ 已实现**（真实数据：1216 owner / 13064 函数；447 个包级 owner 是 `__DelegateSignature`）

- **签名与契约**：返回以 owner 全路径（`/Script/Engine.KismetMathLibrary`）为键的字典；JSON 结构不对抛 `FormatException`。
- **输入输出**：每个 `JmapFunction` 保留 `Name/Flags/Params`；`JmapParam` 保留 `Name/Dir/Type`（`ClassPath/EnumPath/Inner/Key/Value`）。
- **关键分支（伪代码）**：
  ```
  root.objects 不存在 → throw
  foreach (path, ownerJson): owner = {Path, Type, Super, Functions}
  foreach (name, fnJson): fn = {Name, Flags, Params}
  foreach param: {Name, Dir, Type: FromJson(param)}
  ```
- **边界与失败**：`functions` 缺失 → 空函数表；`params` 缺失 → 空参数；`type` 未知保留原字符串（渲染时退 `KStruct`）；owner 路径不带 `.`（包级容器）由 emitter 跳过。
- **调用者与被调用者**：`SdkEmitter.Emit`、`SdkSourceGenerator`、测试调用；调用 `Json.Parse`。
