# `Polaris.Compiler/AssetImportTable`

**职责**：把「UE 类全路径 + 函数名」解析成目标资产的局部 `FPackageIndex`；缺失时按模板克隆补导入（自研，不依赖其他项目）。
**对外入口**：`Resolve` / `EnsureClass` / `EnsurePackage` / `EnsureFunction`。
**内部结构**：线性查 Import 表；模板克隆保留 `ClassPackage/ClassName`，只替换 `ObjectName/OuterIndex`。
**依赖**：UAssetAPI（`Import`/`FName`/`FPackageIndex`）。
**取舍**：追加到 Import 表尾部（负数索引 1 基，追加不影响既有引用）；**不猜** `ClassPackage`——必须克隆同类模板。

### `Resolve(ownerPath: string, functionName: string): FPackageIndex`

**状态：✅ 已实现**（写回用例：`/Script/Engine.KismetMathLibrary` + `Abs_Int` 成功解析/补导入）

- **签名与契约**：`ownerPath` 形如 `/Script/Engine.KismetMathLibrary`；返回负索引（Import）。结构不合法抛 `InvalidOperationException`。
- **输入输出**：先 `EnsureClass`（顺带 `EnsurePackage`），再查/补 `Function` 导入（`OuterIndex` = 类索引）。
- **关键分支（伪代码）**：
  ```
  class = EnsureClass(ownerPath)
  find Import(ClassName=Function, ObjectName=name, OuterIndex=class)? → 返回
  else clone(Function 模板) with ObjectName=name, OuterIndex=class → 追加 → 返回 -Count
  ```
- **边界与失败**：资产里没有同类模板时抛错（不合成 `ClassPackage`）；路径无 `.` 抛错。
- **调用者与被调用者**：`Translator` 的 `IFunctionResolver` 实现；调用 `EnsureClass`。

### `EnsureClass(ownerPath: string): FPackageIndex`

**状态：✅ 已实现**

- **签名与契约**：确保 `Class` 导入存在（outer = 包导入），返回其负索引。
- **输入输出**：拆分 `/Script/Module` 与 `Class`；已存在则复用。
- **关键分支（伪代码）**：`package = EnsurePackage(path); find Class(outer=package)?; else clone(Class 模板)`。
- **边界与失败**：无 Class 模板抛错；同包同名类复用同一条。
- **调用者与被调用者**：`Resolve`、`EnsureFunction` 调用；调用 `EnsurePackage`。

### `EnsurePackage(packagePath: string): FPackageIndex`

**状态：✅ 已实现**

- **签名与契约**：确保 `Package` 导入存在（outer=0），返回负索引。
- **输入输出**：`packagePath` 形如 `/Script/Engine`；已存在复用。
- **关键分支（伪代码）**：`find Package(ObjectName=path, outer=0)?; else clone(Package 模板, outer=0)`。
- **边界与失败**：无 Package 模板抛错。
- **调用者与被调用者**：`EnsureClass` 调用。

### `EnsureFunction(ownerPath: string, functionName: string): FPackageIndex`

**状态：✅ 已实现**

- **签名与契约**：确保函数导入存在（outer = 类导入），返回负索引。
- **输入输出**：同名同 outer 复用；否则克隆 `Function` 模板并追加。
- **关键分支（伪代码）**：`class = EnsureClass(owner); find Function(outer=class)?; else clone`。
- **边界与失败**：无 Function 模板抛错。
- **调用者与被调用者**：`Resolve` 调用；调用 `EnsureClass`。
