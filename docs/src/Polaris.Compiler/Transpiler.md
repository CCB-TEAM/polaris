# `Polaris.Compiler/Transpiler`

**职责**：C# 方法体 → `KNode` 列表；扫描局部变量/成员变量 → `CallFunc_*` 临时变量与 `FProperty` 声明。
**对外入口**：`Translate(method: IMethodSymbol, ctx: BlueprintContext) → TranslationResult`。
**内部结构**：`Translate` 递归语句 → `ScanLocals`/`ResolveVariable` → `EmitCall`；所有产出的节点都带来源行号。
**依赖**：Roslyn、`Solstice.Kismet` 的 `KNode`/`KArg`、`Polaris.TypeSystem`。
**取舍**：**total 翻译**——任何没实现的构造抛带行列号的 `TranslationException`，绝不产出「看起来能跑」的节点（沿用 `BodyTranslator` 规矩）。

### `Translate(method: IMethodSymbol, ctx: BlueprintContext): TranslationResult`

**状态：🚧 未实现**

- **签名与契约**：把方法体翻成 `IReadOnlyList<KNode>` + 变量声明表 + 诊断；不做写盘。
- **输入输出**：`method` 必须是 `partial class` 里 `override`/新增的函数；`ctx` 提供目标类、成员表、原生签名、`KClass` 元数据。返回的 `Warnings` 包含占位参数（如结构体常量）——**存在占位时默认拒绝写盘**。
- **关键分支（伪代码）**：
  ```
  for stmt in method.Body.Statements:
      switch stmt:
        LocalDeclaration  → 记录类型（var 从初始化表达式推断）后 EmitExpression
        ExpressionStatement → EmitCall/Assign
        If/While/For      → IfNode/LoopNode（for 先降成 while，注释保留）
        Return            → ReturnNode（仅平坦函数合法）
        default           → throw TranslationException(行列, "不支持的构造")
  ```
- **边界与失败**：`switch`/`try`/`await`/`latent`/`delegate` 多播一律报错；`out var` 必须落成「先声明变量、再把变量名当实参」（发射 `EX_LocalVariable`，见下方 `ResolveVariable`）。
- **调用者与被调用者**：`Polaris.Cli apply`、`ClassPatcher` 流程调用；调用 `EmitCall`、`ScanLocals`、`ResolveVariable`、`KismetWorkspace.CreateCall`。

### `ScanLocals(method: IMethodSymbol): IReadOnlyList<LocalVar>`

**状态：🚧 未实现**

- **签名与契约**：收集方法体里的局部变量（含 `out var`、`var` 推断结果）与它们的 `FProperty` 类型；同时收集本函数用到的 `CallFunc_*` 临时变量。
- **输入输出**：返回按出现顺序的列表（顺序决定临时变量 `_1/_2` 后缀与声明顺序）；类型由 Roslyn 语义模型给出，写不出类型即报错。
- **关键分支（伪代码）**：
  ```
  for each ILocalSymbol in method:
      t = ToProperty(symbol.Type)          // 失败 → 报错，不猜
      add(LocalVar(symbol.Name, t))
  for each 调用返回引脚被使用:
      add(CallFunc(name, pin))             // 重名时 _1/_2 递增（实测规则）
  ```
- **边界与失败**：临时变量名冲突时按 UE 规则加数字后缀（实测 `CallFunc_CustomName2HasAttribute_doesIt_1`）；若用户手写的局部变量与 UE 名字冲突，**优先保留用户的**并给临时变量改后缀。
- **调用者与被调用者**：`Translate` 调用；结果交给 `ClassPatcher`/`KismetWorkspace.DeclareLocalVariable`。

### `ResolveVariable(symbol: ISymbol, isOut: bool): PointerArg`

**状态：🚧 未实现**

- **签名与契约**：把 C# 标识符解析成变量指针：局部/参数 → `EX_LocalVariable`（出参槽也用它，见下）；成员 → `EX_InstanceVariable`（`ResolvedOwner` = 本类或父类导出）；`EX_LocalOutVariable` 只在读入既有字节码时原样保留。
- **输入输出**：`resolvedOwner` 必须正确：本类成员用本类导出索引，继承成员用父类导入索引（实测 `cardFunction` 的 owner 是负数 Import）。
- **关键分支（伪代码）**：
  ```
  if symbol 是参数/局部: return PointerArg(Instance:false)      // 出参也是 EX_LocalVariable
  if symbol 是字段:      return PointerArg(Instance:true, owner=DeclaringType)
  // isOut 只影响语义校验（该槽必须可写），不改变发射的令牌
  ```
- **边界与失败**：令牌由**变量属性**决定，不由「这个槽是不是出参」决定——UE 编译器只在属性带 `CPF_OutParm` 时发 `EX_LocalOutVariable`（编译侧源码见 [ue-baking-semantics](../../../../polaris-research/ue-baking-semantics.md) 的 `EmitTermExpr`）；调用点传的是调用方自己的局部变量（无 `OutParm`），所以是 `EX_LocalVariable`。全量实测 `EX_LocalOutVariable` 只有 524 次，与之一致。`isOut` 判断错会写坏调用方的 `FFrame.Locals`：必须有单测覆盖「出参槽 = 调用方变量」。
- **调用者与被调用者**：`EmitCall`、`Translate` 调用；依赖 `KismetWorkspace.EnsureVariable`。

### `EmitCall(call: InvocationExpressionSyntax, ctx: BlueprintContext): KNode`

**状态：🚧 未实现**

- **签名与契约**：一次调用 → 对应形态节点；形态由「被调函数签名 + 是否本资产导出」决定，**不由用户指定**。
- **输入输出**：支持 `Receiver.Method(...)`（`EX_Context`）与静态调用；参数按签名逐个转换，`out/ref` 实参必须是变量。
- **关键分支（伪代码）**：
  ```
  fn = ResolveFunction(receiver?.Type.Name, method.Name)   // 歧义 → 报错列候选
  shape = 本资产导出 ? Virtual : (FUNC_Static ? Final : Context)
  args = method.Parameters.Select(p => p.IsOut ? ResolveVariable(arg,true) : ToArg(arg))
  ```
- **边界与失败**：占位参数（text/struct/array）必须由用户显式给出，否则报错；`k.Log` 之类便捷 API 由编译器展开成 `PrintString`（结构体常量用 `StructConstArg`，不再占位）。
- **调用者与被调用者**：`Translate` 调用；调用 `KismetWorkspace.ResolveFunction`、`CreateCall`。
