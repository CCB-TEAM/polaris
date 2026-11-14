# `Polaris.SdkGen/JmapFileReader`

**职责**：直接读游戏进程 dump 出来的 **纯 jmap**（`.jmap`），取出类/结构/枚举/函数签名，产出与 usmap 相同的内存模型。
**对外入口**：`ReadFile(path) → Result(Usmap, Owners)`。
**内部结构**：`System.Text.Json` 流式读 `objects` 字典 → 按 `type` 分派：Class/Struct/ScriptStruct → schema，Function → owner 函数，Enum → 枚举。
**依赖**：System.Text.Json（net10，无第三方 JSON 库）。
**取舍**：不再依赖 usmap 或 jmap extractor 的中间产物；`super_struct`/`properties`/`function_flags`/CPF flags 全部来自 jmap。类型映射保持与 usmap 模型一致，SdkEmitter 不用改。

### `ReadFile(path: string): Result`

**状态：✅ 已实现**（66 MB 的 `kards1.60_No_UE4SS.jmap`：9705 schema / 1876 enum / 1216 owner / 13064 函数，生成 484 文件后全量编译 0 错误）

- **签名与契约**：纯函数（读文件）；返回 `UsmapFile` + `Dictionary<string,JmapOwner>`；文件缺 `objects` 抛 `FormatException`。
- **输入输出**：`Class` → StructKind=2，`Struct/ScriptStruct` → 1；`super_struct` 取短名/模块；函数参数方向由 `CPF_ReturnParm`/`CPF_OutParm` 推出，`ref`/`const` 由 `CPF_ReferenceParm`/`CPF_ConstParm` 推出。
- **关键分支（伪代码）**：
  ```
  parse doc → objects
  foreach (path, obj):
    switch obj.type:
      Class/Struct/ScriptStruct → schema{super, properties[]}
      Function → owner[outer].Add({name, function_flags, params})
      Enum → enums[module.name] = names[]
  ```
- **边界与失败**：`outer` 为空的包级函数跳过；未知属性类型映射为 `Unknown`（生成侧退化为 `UStruct`）；CPF flags 未收录的位忽略（仅元数据，不影响编译）。
- **调用者与被调用者**：`polaris sdk --jmap`、`SdkSourceGenerator`（AdditionalFiles 里的 `.jmap`）调用；输出给 `SdkEmitter`。
