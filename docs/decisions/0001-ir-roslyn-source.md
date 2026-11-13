# ADR 0001：IR 与编辑面统一走 Roslyn 源码

- 状态：**已接受**（用户拍板，2026-12）
- 关联：[Polaris 评估 §2.6](../../../polaris-research/polaris-project-eval.md)、[表达式树评估](../../../polaris-research/expression-tree-eval.md)

## 背景

候选三种：Roslyn 源码 AST、`System.Linq.Expressions` 表达式树、自定义强类型 AST。
「表达式树」曾被单独立项评估（47 个可复现探针，.NET 10.0.13）。

## 决定

1. **唯一编辑面 = C# 源码**（Roslyn `SyntaxTree` + `SemanticModel`）。
2. **唯一 IR = `KNode`/`KArg`**（solstice-kismet 的保形模型）。
3. 表达式树只允许出现在测试工程里，做「可执行差分测试」（第 5 道闸）。

## 证据（实测，不是判断）

| 事实 | 影响 |
|---|---|
| 34 个 `KArg` 里 22 个要靠侧信道元数据保真（`FFieldPath` 全路径 + `ResolvedOwner`、`Instance/Out/Default`、`CallShape`、`ContextKind`、`AssignKind`） | `Expression` 抽象掉形状，与该职责相反 |
| C# 语法根本转不出语句树：`x => x = 5` → CS0832、`while`/`{return x;}` → CS0834、`out var` → CS8198 | 表达式树**没有人类书写面** |
| `EX_ComputedJump` 的目标是运行时值，`GotoExpression.Target` 必须是 `LabelTarget` | 只能 trampoline 近似，不是等价 |
| KARDS 的 `S_*` 蓝图结构没有 CLR 类型（usmap 不含 `UserDefinedStruct`） | 结构体成员访问在树里断链 |
| `Expression.Switch` 支持非常量 case（原假设被证伪） | 唯一意外的好消息，但不改变结论 |

## 否决的方案

- **表达式树当主 IR**：字节一致契约（8115/8115）会被破坏；会让「反编译→回编译逐字节一致」这道闸失效。
- **自定义 AST**：要重写解析/绑定/诊断，收益低于成本。

## 代价

- 用户必须写 C#（不能写「蓝图风格的伪代码」）——但这正是懒狗 coder 想要的编辑面。
- Roslyn 版本跟随 SDK 自带（本机 nuget 走 schannel 会 `SEC_E_NO_CREDENTIALS`，见 PLAN §8）。
