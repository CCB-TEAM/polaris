# `Polaris.TypeSystem/TypeMap`

**职责**：C# 类型 ⇄ FProperty 的双向映射；唯一的「新类型」扩展点（`List<T>`、`Dictionary<K,V>`、蓝图结构、枚举、委托）。

> **现状（2026-12）**：**读侧**（UE 类型 → C# 声明）已在 [`SdkEmitter.Names`](../Polaris.SdkGen/SdkGenerator.md) 实现；
> **写侧**（C# → FProperty）仍只有设计，本文件的三个函数都是 🚧。
**对外入口**：`ToProperty` / `ToCSharp` / `CreateDefault`。
**内部结构**：`TypeMap` 是注册表：每个 `IPropertyMapper` 负责一族类型（标量 / 对象 / 结构 / 容器 / 枚举 / 委托），按 C# 类型或 usmap schema 匹配。
**依赖**：UAssetAPI（`FProperty` 子类、`FPackageIndex`）、`UsmapCatalog`（结构成员）。
**取舍**：优先从 **usmap/jmap 的类型描述**生成，而不是从 CLR 反射猜；CLR 类型只在用户新写代码时作为入口（`List<int>` → `ArrayProperty(IntProperty)`）。

### `ToProperty(asset: UAsset, type: TypeSyntax, model: SemanticModel): FProperty`

**状态：🚧 未实现**

- **签名与契约**：把 C# 声明类型映射成 `FProperty`（含 Import 补齐）；不支持的类型抛 `UnsupportedTypeException`，不静默退回 `KStruct`。
- **输入输出**：支持 `int/long/float/double/bool/string/Name/FName`、`T`（对象，`KRef<UClass>`）、usmap 结构类型、`KArray<T>`/`List<T>`、`KDictionary<K,V>`、`KSet<T>`、枚举；`int?` 等可空类型**不支持**。
- **关键分支（伪代码）**：
  ```
  t = model.GetTypeInfo(type)
  if t 命中标量表: return Scalar(t)
  if t 是 KRef<X>: return ObjectProperty(EnsureImport(X))
  if usmap 有同名 schema(结构): return StructProperty(schema, 成员递归)
  if t 是 List/Array 泛型: return ArrayProperty(ToProperty(T))
  if t 是 Dictionary/Set: return MapProperty/SetProperty
  throw Unsupported(t)
  ```
- **边界与失败**：蓝图定义的结构（`S_*`）不在 usmap 里——遇到 `UserDefinedStruct` 需要从**资产**读成员（bmap 提供），M2 前直接报错；结构体成员类型粒度不足（usmap 只给 `StructProperty`）由 `UsmapCatalog` 的 `StructType/InnerType` 补（gap 分析已确认数据在 `UsmapPropertyData` 里）。
- **调用者与被调用者**：`ClassPatcher.AddMember`、`Transpiler.ScanLocals`、`SdkGenerator` 调用；调用 `ImportIndex.Ensure*`。

### `ToCSharp(property: FProperty): string`

**状态：🚧 未实现**

- **签名与契约**：FProperty → C# 类型名（生成 SDK/基座用）；未知类型返回 `object` 并在注释里标 `// unsupported: <kind>`，绝不猜。
- **输入输出**：输入来自资产 `LoadedProperties` 或 usmap；输出为限定名（含命名空间别名）。
- **关键分支（伪代码）**：
  ```
  switch kind:
    IntProperty → "int"   BoolProperty → "bool"   …
    ArrayProperty → $"KArray<{ToCSharp(Inner)}>"
    StructProperty → usmap 解析全路径 → 已知结构名；否则 "KStruct"
  ```
- **边界与失败**：`UserDefinedStruct` 无 usmap 条目 → 输出 `KStruct` + 注释（读侧可接受，写侧不允许用它建新字段）。
- **调用者与被调用者**：`SdkGenerator`、`CSharpEmitter` 调用。

### `CreateDefault(asset: UAsset, spec: MemberSpec): PropertyData`

**状态：🚧 未实现**

- **签名与契约**：给新成员生成 CDO 默认值；`spec` 未给初值时用类型零值（`0/false/""/None`），对象引用为空。
- **输入输出**：返回可直接塞进 `NormalExport.Data` 的 `PropertyData`；`Name` = 成员名。
- **关键分支（伪代码）**：`var p = NewPropertyData(prop); p.Name = member; p.Value = spec.Init ?? Zero(prop)`。
- **边界与失败**：结构体默认值需要逐成员构造；容器默认值为空容器。写不出来就报错（不带占位）。
- **调用者与被调用者**：`ClassPatcher.AddMember`、`WriteCdoValue` 调用。

## 取舍记录

- **不引入 CLR 反射驱动**：反射在 AOT/裁剪下不可靠，且无法表达 `FFieldPath` 语义。类型来源固定为「源码类型 + usmap/jmap/bmap 描述」。
- **容器先做 `List<T>`，再做 `Dictionary/Set`**：KARDS 实际用到的容器类型由 bmap 统计决定，避免提前实现无用的映射。
