# `Polaris.Compiler/KismetText`

**职责**：把 `KNode` 树渲染成可读的 C# 风格伪代码（供人工核对；不参与回编译）。
**对外入口**：`Format(nodes, asset)`。
**内部结构**：按节点类型输出语句/表达式；函数名通过 `StackNode` 在资产导出/导入表里解析；变量名取自 `FFieldPath`。
**依赖**：UAssetAPI（表达式/资产表）。
**取舍**：跳转/未知指令输出为注释（`KRaw` → `// EX_JumpIfNot`），**不假装**还原控制流；持久帧写入显示为 `persist[dest] = value;`。

### `Format(nodes: IReadOnlyList<KNode>, asset: UAsset): string`

**状态：✅ 已实现**（`OnPlayedFromHand` 输出：

```
persist[K2Node_Event_targetCard] = targetCard;
ExecuteUbergraph_card_event_aans(10);
return;
```

）

- **签名与契约**：纯函数；返回多行文本（`Environment.NewLine` 分隔）；不抛异常（未知节点输出占位注释）。
- **输入输出**：`KCall` 输出 `receiver.func(args)` 或 `func(args)`；`KLet` 输出 `var = expr;`；`KIf` 输出 `if (...) { ... } else { ... }`。
- **关键分支（伪代码）**：
  ```
  foreach node: switch node:
    KConst → 常量文本; KVar → 路径名; KCall → 名字 + 参数
    KLet → var = expr; KPersist → persist[dest] = expr
    KReturn → return [expr]; KEnd → 不输出
    KIf/KSeq → 递归缩进; KRaw → // token
  ```
- **边界与失败**：表达式里的未知节点输出 `/* ? */`；跳转原样注释，不还原 if/else（真实资产偏移语义未定）。
- **调用者与被调用者**：`polaris decompile` 与测试调用；调用 `KismetDecompiler.Decompile` 的结果。
