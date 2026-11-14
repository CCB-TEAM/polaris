# `Polaris.Compiler/ModPlanBuilder`

**职责**：扫描 mod 编译单元里的数据注解，产出「C# 方法 → 目标资产 UFunction」的绑定列表。
**对外入口**：`FindTargets(compilation)`。
**内部结构**：递归遍历所有命名空间/嵌套类型；类上的 `[PolarisAsset]` 给资产路径，方法上的 `[PolarisReplace]` 给目标函数名。
**依赖**：Roslyn（`Compilation`/`INamedTypeSymbol`）。
**取舍**：一个类一个资产；没写 `[PolarisAsset]` 的类整类跳过，没写 `[PolarisReplace]` 的方法跳过（不猜）。

### `FindTargets(compilation: Compilation): IReadOnlyList<ModTarget>`

**状态：✅ 已实现**（测试「数据注解：[PolarisAsset]/[PolarisReplace] → mod 计划」）

- **签名与契约**：纯函数；按源码顺序返回 `ModTarget(Class, Method, AssetPath, FunctionName)`；无匹配返回空列表。
- **输入输出**：`AssetPath` 原样保留（相对 Content、`/Game/...` 或绝对路径由调用方解析）。
- **关键分支（伪代码）**：
  ```
  foreach type in 全部命名空间/嵌套类型:
      asset = type 上的 [PolarisAsset]
      if asset == null: continue
      foreach method with [PolarisReplace]:
          add ModTarget(type, method, asset, function)
  ```
- **边界与失败**：同名注解重复时按出现顺序都保留；不校验资产是否存在（由写回阶段报错）。
- **调用者与被调用者**：`samples/BytecodeMod`、未来的 `polaris apply` 调用；只读 Roslyn 符号。
