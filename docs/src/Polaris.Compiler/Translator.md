# `Polaris.Compiler/Translator`

**职责**：把 C# 方法体直接编译成 Kismet 字节码（`KismetExpression[]`）。**不经过 KArg/KNode 之类的私有 IR**。
**对外入口**：`Translator.Compile(compilation, method, asset, resolver)`。
**内部结构**：Roslyn `SemanticModel` 解析语句/调用 → `BytecodeBuilder` 发指令 → 标签解析成字节偏移；实例调用包一层 `EX_Context`。
**依赖**：Roslyn（SDK 自带）、UAssetAPI（`KismetExpression` 类型与序列化）、`Polaris.Sdk`（`[UClass]`/`[UFunction]` 元数据）。
**取舍**：覆盖「静态调用 / 实例调用 / if-else / return / 字面量」；变量读写（`FFieldPath` 的 `EX_LocalVariable`/`EX_InstanceVariable` 只做了接收者）、算术留给下一批。**total 翻译**：不支持的构造抛 `CompileException`（带行列），不静默降级。

### `Compile(compilation: Compilation, method: IMethodSymbol, asset: UAsset, resolver: IFunctionResolver): KismetExpression[]`

**状态：✅ 已实现**（测试用例「编译器：if/静态调用/return → 字节码 + 自校验」「变量读写 + 往返」「局部变量声明：写回 + 重开往返」通过）

- **签名与契约**：纯函数；返回顶层表达式数组（以 `EX_Return` + `EX_EndOfScript` 收尾）。方法必须有源码方法体；无 `[UClass]`/`[UFunction]` 元数据时抛 `CompileException`。
- **输入输出**：`resolver` 把「UE 类全路径 + 函数名」映射成目标资产的 `FPackageIndex`；`asset` 用于 `FName`/序列化测量。
- **关键分支（伪代码）**：
  ```
  syntax = method 的 MethodDeclarationSyntax（无 body → 抛）
  foreach statement:
    if: cond → EmitJumpIfNot(else); then; [EmitJump(end); Mark(else); else; Mark(end)]
    return; → EX_Return(EX_Nothing)，终止
    调用语句 → EX_FinalFunction(StackNode=resolver(...), Parameters=字面量)
    其它 → 抛 CompileException
  末尾补 EX_Return(EX_Nothing) + EX_EndOfScript
  ```
- **边界与失败**：条件只接受 `true/false`；实参支持 int/bool/string/float/double 字面量与变量；算术/结构体常量仍报错。
- **调用者与被调用者**：`Polaris.Cli apply`（后续）与测试调用；调用 `BytecodeBuilder`、`IFunctionResolver`。

## 已验证的字节码形态

输入：

```csharp
if (true) { MathLib.Print(1); } else { MathLib.Log("x"); }
return;
```

生成 7 条顶层表达式（`JumpIfNot` → 调用 → `Jump` → 调用 → `Return` → `EndOfScript` 等），
序列化后回读：跳转目标正好落在指令起始（`BytecodeCheck` 自校验通过）。

### `ScanLocals(method: IMethodSymbol): IReadOnlyList<LocalVar>`

**状态：🚧 未实现**

- **签名与契约**：收集方法体局部变量（含 `out var`/`var` 推断）与它们的 `FProperty` 类型；同时收集 `CallFunc_*` 临时变量。
- **输入输出**：按出现顺序返回（决定 `_1/_2` 后缀与声明顺序）；类型写不出即报错。
- **关键分支（伪代码）**：`foreach ILocalSymbol → ToProperty(type)`；`foreach 被使用的返回引脚 → CallFunc_<函数>_<引脚>`。
- **边界与失败**：名字冲突优先保留用户变量；UE 临时变量加数字后缀。
- **调用者与被调用者**：`Compile` 未来调用；调用类型映射。

### `ResolveVariable(symbol: ISymbol, isOut: bool): PointerArg`

**状态：🚧 未实现**

- **签名与契约**：C# 标识符 → `FFieldPath` 指针：局部/参数 → `EX_LocalVariable`；成员 → `EX_InstanceVariable`（`ResolvedOwner` 正确）。
- **输入输出**：出参槽用调用方局部变量令牌（与既有资产同形：`EX_LocalVariable` 为主）；`EX_LocalOutVariable` 只在读回时保留。
- **关键分支（伪代码）**：`参数/局部 → PointerArg(Instance:false)`；`字段 → PointerArg(Instance:true, owner=DeclaringType)`。
- **边界与失败**：`isOut` 判错会写坏调用方 `FFrame.Locals`，必须有单测。
- **调用者与被调用者**：`Compile` 未来调用；依赖 `CardAsset` 的导出索引。

### `EmitCall(call: InvocationExpressionSyntax, model: SemanticModel): KismetExpression`

**状态：✅ 已实现（静态 + 实例调用；变量参数仍 🚧）**

- **签名与契约**：一次调用 → 对应形态：本资产导出 → `EX_LocalVirtualFunction`；`FUNC_Static` → `EX_FinalFunction`；非静态 → `EX_Context`。
- **输入输出**：静态 → `EX_FinalFunction`；实例 → `EX_Context` 包 `EX_FinalFunction`，接收者支持 `this`（`EX_Self`）、字段（`EX_InstanceVariable`）、参数/局部（`EX_LocalVariable`）；`out`/`ref` 实参同样发变量令牌（`EX_LocalVariable`）；`Offset` = 内层调用的序列化长度。
- **关键分支（伪代码）**：`flags 含 FUNC_Static ? Final : (本资产导出 ? Virtual : Context)`。
- **边界与失败**：名字歧义报错列候选；占位参数必须显式给出。
- **调用者与被调用者**：`Compile` 调用；依赖 `IFunctionResolver`。