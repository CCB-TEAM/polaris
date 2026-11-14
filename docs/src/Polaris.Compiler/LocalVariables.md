# `Polaris.Compiler/LocalVariables`

**职责**：为函数补局部变量 `FProperty`（写回模式下编译器声明 `int x = 1;` 时调用）。
**对外入口**：`Ensure(function, asset, name, propertyType, elementSize)`。
**内部结构**：先查 `function.LoadedProperties` 是否已有同名属性；否则在同资产其它函数里找同 `SerializedType` 的属性模板，克隆其 flags/ElementSize；没有模板时按类型给默认值；`BoolProperty` 用 `FBoolProperty`（FieldSize/ByteMask 等必须写对，否则后续数据会错位）。
**依赖**：UAssetAPI（`FGenericProperty` / `FName`）。
**取舍**：**不猜**类型布局——优先用真实资产里的同类型属性模板；局部变量追加到 `LoadedProperties` 末尾，写回时 `StructExport.Write` 会重算属性表。

### `Ensure(function: FunctionExport, asset: UAsset, name: string, propertyType: string, elementSize: int): FProperty`

**状态：✅ 已实现**（`card_event_aans.OnPlayedFromHand` 加 `int x` 后写回，重开往返一致）

- **签名与契约**：已有同名属性直接复用；否则创建并追加，返回属性对象。
- **输入输出**：`propertyType` 为 UE 属性类型名（`IntProperty`/`BoolProperty`/`FloatProperty`/`DoubleProperty`/`Int64Property`）；`elementSize` 是回退值。
- **关键分支（伪代码）**：
  ```
  if LoadedProperties 有 name: return it
  template = 同资产 Functions 的所有 LoadedProperties 里 SerializedType==propertyType 的第一个
  prop = FGenericProperty { SerializedType, Name=name, Flags/ArrayDim/ElementSize/PropertyFlags 来自 template 或默认 }
  LoadedProperties += prop
  ```
- **边界与失败**：没有模板时用 `CPF_ZeroConstructor|CPF_IsPlainOldData|CPF_NoDestructor` 默认 flags；布尔局部变量用 1 字节。
- **调用者与被调用者**：`Translator.EmitLocalDeclaration` 调用；不直接写盘。
