# `Polaris.Assets/ClassPatcher`

**职责**：唯一的资产写入口——CDO 默认值、类成员、新 UFunction、存根入口常量。
**对外入口**：`WriteCdoValue` / `AddFunction` / `AddMember` / `RewriteStubEntry` / `Save`。
**内部结构**：所有方法都遵守「先改内存 → 再自校验 → 最后 `Save` 到新目录」。
**依赖**：UAssetAPI（`NormalExport.Data`、`ClassExport.Children`、`ImportIndex`）。
**取舍**：追加导出/导入一律放表尾，避免移动既有 `FPackageIndex`（负数为 1 基、正数为 0 基）。

### `WriteCdoValue(asset: UAsset, className: string, member: string, value: PropertyData): PatchResult`

**状态：🚧 未实现**

- **签名与契约**：把 CDO 里同名 `PropertyData` 替换为 `value`；找不到抛 `MemberNotFound`，类型不符抛 `TypeMismatch`。返回改了几条、旧值是什么。
- **输入输出**：`member` 必须已存在于本类或父类 CDO（新增成员走 `AddMember`）；`value` 的 `PropertyType` 必须与旧值一致（`IntPropertyData` 对 `IntPropertyData`）。
- **关键分支（伪代码）**：
  ```
  cdo = ResolveCdo(className)            // 沿 SuperStruct 跨包找
  if cdo is null: fail("类没有 CDO")
  idx = cdo.Data.FindIndex(p => p.Name == member)
  if idx < 0: fail("CDO 里没有该成员")   // 新增字段必须先 AddMember
  if type(value) != type(cdo.Data[idx]): fail("类型不匹配")
  old = cdo.Data[idx]; cdo.Data[idx] = value
  ```
- **边界与失败**：容器/文本/软引用类型**只允许改同类新值**（不做结构重建）；写盘前必须走闸 4（重开读值）。
- **调用者与被调用者**：`Polaris.Compiler` 的字段应用阶段调用；调用 `ResolveCdo`、`Save`。

### `AddFunction(asset: UAsset, className: string, spec: FunctionSpec): FPackageIndex`

**状态：🚧 未实现**

- **签名与契约**：在既有 `BlueprintGeneratedClass` 上追加一个 `FunctionExport`：`Outer = 类导出`、`FunctionFlags` 按 `spec`、`LoadedProperties` = 参数 + 局部变量；挂进类的 `Children`，并在 `ClassExport.FuncMap` 加一条。返回新导出的 `FPackageIndex`。
- **输入输出**：`spec` 至少含名字、flags、参数表、字节码；名字必须在 Name 表里（不存在则 `new FName(asset, name)` 追加，**最后才做**）。
- **关键分支（伪代码）**：
  ```
  if 类里已有同名 UFunction: fail("重名")
  size = 类尾部 cooked 字段是否依赖导出数  // M6 实测；先按追加尾部处理
  e = new FunctionExport(asset, extras: 8 字节 0) { OuterIndex = 类导出, ObjectName = name, ... }
  asset.Exports.Add(e); idx = new FPackageIndex(asset.Exports.Count)
  classExport.Children = classExport.Children.Append(idx)
  classExport.FuncMap.Add(name, idx)
  写导出的 SerializedSize / ScriptBytecodeSize（由 UAssetAPI 的 Write 决定）
  ```
- **边界与失败**：**未验证**运行时 `FindFunctionByName` 是否认 `FuncMap`/`Children` 之一（G4 闸）。若必须改类尾部 cooked 字段（`NumReplicatedProperties` 等），本函数要扩展；届时回滚策略 = 保留原资产副本。
- **调用者与被调用者**：`polaris inject` 与 `Transpiler`（事件方法需要新存根时）调用；调用 `Save`、`ImportIndex.Ensure*`。

### `AddMember(asset: UAsset, className: string, spec: MemberSpec): void`

**状态：🚧 未实现**

- **签名与契约**：把 `FProperty` 追加到类 `LoadedProperties`，并在 CDO 写默认值；`Children` 同步（`UStruct` 的属性链）。
- **输入输出**：成员名不得与父类/本类重复；类型走 `TypeMap` 生成（支持标量/对象/结构/`List<T>` 等容器）。
- **关键分支（伪代码）**：
  ```
  if 同名成员存在于自身或父链: fail("重名")
  prop = TypeMap.CreateProperty(asset, spec.Type)   // 会补 Import
  classExport.LoadedProperties = ...Append(prop)
  cdo.Data.Add(TypeMap.CreateDefault(spec))
  ```
- **边界与失败**：**未验证**点在 `SuperStruct` 的属性偏移/`PropertyLink` 链是否需要重排；M7 才做，闸门 = 重开后 `LoadedProperties` 读回 + 游戏内可读该字段。
- **调用者与被调用者**：`Transpiler` 应用字段时调用；调用 `TypeMap.CreateProperty`、`WriteCdoValue`。

### `RewriteStubEntry(asset: UAsset, stubName: string, newOffset: uint): void`

**状态：🚧 未实现**

- **签名与契约**：把存根函数里 `ExecuteUbergraph_*(<EX_IntConst>)` 的常量改成 `newOffset`；只改这一个字的字节，函数长度不变。
- **输入输出**：`stubName` 必须是含该调用的函数；`newOffset` 必须落在目标 ubergraph 的指令起点（由 `VerifyEntries` 保证）。
- **关键分支（伪代码）**：
  ```
  node = 反编译(stubName) 里唯一的 LocalFinalCallNode(ExecuteUbergraph_*)
  if 实参不是 IntArg: fail("形态不符")
  用新常量重建该节点 → Generate(stubName) → 比对改动仅此一处
  ```
- **边界与失败**：整函数重生成会改变字节序（虽然语义等价），因此必须校验「除常量外结构 diff 为空」；失败则放弃并保留原字节。
- **调用者与被调用者**：`UbergraphBuilder.RewriteStubEntries` 调用；调用 `KismetWorkspace.Generate`。

### `Save(asset: UAsset, outDir: string, fileName: string): SavedAsset`

**状态：🚧 未实现**

- **签名与契约**：写 `.uasset` + `.uexp` 两半到**新目录**（永不覆盖输入）；写前跑闸 1–3，写后重开跑闸 4。
- **输入输出**：`outDir` 必须可写且不是输入目录；返回两半字节与路径。
- **关键分支（伪代码）**：`闸1(未改函数) ∧ 闸3(入口) → Write → 重开 → 闸4 → 返回`。
- **边界与失败**：任一闸失败则删除已写的输出并抛异常；成功也只代表「字节/结构正确」，运行期由用户验证。
- **调用者与被调用者**：`Polaris.Cli apply` 调用；调用 `Verify` 系列与 `UAsset.Write`。
