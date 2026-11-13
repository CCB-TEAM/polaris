# `Polaris.Compiler/UbergraphBuilder`

**职责**：把「事件方法体 → 规范化 ubergraph 段」烘进字节码，并回填所有存根入口常量。
**对外入口**：`Build` / `RewriteStubEntries` / `VerifyEntries`。
**内部结构**：`Build` 产出 `UberGraphNode`（一段一事件）+ 入口表 → `BakeUbergraph` → `RewriteStubEntries` 改存根 → `VerifyEntries`。
**依赖**：`Solstice.Kismet`（`UberGraphNode`、`BakeUbergraph`、`UbergraphCallSites`）、`ClassPatcher.RewriteStubEntry`。
**取舍**：见 [ADR 0002](../decisions/0002-ubergraph-canonical-rebake.md)——放弃 UE 的共享尾部/蹦床，换取可写的事件体。

### `Build(events: IReadOnlyList<EventMethod>, ctx: BlueprintContext): UberGraphNode`

**状态：🚧 未实现**

- **签名与契约**：按**事件声明顺序**生成段：每段 = 方法体的 `KNode` 列表；段尾 = `JumpToEpilogue`；头部 = `ComputedJump(EntryPoint)`（`bUseFlowStack` 时前面加 `PushExecutionFlow(尾声)`）。返回节点树，不烘焙。
- **输入输出**：`events` 至少 1 个；每个事件必须有同名存根（否则报错，提示先注入存根）；`ctx` 提供 ubergraph 名与事件参数（`K2Node_Event_<参数>` 的持久帧变量）。
- **关键分支（伪代码）**：
  ```
  sections = []
  for ev in events:
      body = Translate(ev.Method)                 // 含 K2Node_Event_* 读取
      sections.Add(UberSectionNode(StartOffset: 0, Nodes: body, Terminator: JumpToEpilogue))
  return UberGraphNode(Dispatch: LocalArg("EntryPoint"), PushesEpilogue: ctx.UseFlowStack, sections)
  ```
- **边界与失败**：`StartOffset` 只是占位，`BakeUbergraph` 会按新布局重算并返回 `EntryOffsets`；事件数 0 时返回 `null`（该类没有 ubergraph）。
- **调用者与被调用者**：`Polaris.Cli apply` 调用；调用 `Transpiler.Translate`、`KismetCompiler.BakeUbergraph`。

### `RewriteStubEntries(ws: KismetWorkspace, uberName: string, entries: IReadOnlyList<uint>, stubNames: IReadOnlyList<string>): RewriteReport`

**状态：🚧 未实现**

- **签名与契约**：把 `entries[i]` 写进 `stubNames[i]` 的 `EX_IntConst`；数量不等直接报错。返回每个存根的新旧偏移。
- **输入输出**：`entries` 顺序必须与 `stubNames` 一一对应（由事件声明顺序决定）；改写后每个常量必须落在指令起点。
- **关键分支（伪代码）**：
  ```
  if entries.Count != stubNames.Count: fail("入口表与存根数不一致")
  for i: 校验 entries[i] 是目标 ubergraph 的指令起点 else fail
  for i: RewriteStubEntry(stubNames[i], entries[i])
  ```
- **边界与失败**：改写会让存根字节变化（虽然只改常量）——必须记录旧字节用于回滚；**未验证**点：`EX_IntConst` 是否被任何缓存/哈希引用（G2 闸用游戏内验证）。
- **调用者与被调用者**：`Polaris.Cli apply` 调用；调用 `ClassPatcher.RewriteStubEntry`。

### `VerifyEntries(ws: KismetWorkspace, uberName: string): IReadOnlyList<EntryCheck>`

**状态：🚧 未实现**

- **签名与契约**：独立校验「每个存根常量都落在目标 ubergraph 的指令边界上」，并与重烘返回的 `EntryOffsets` 对账。
- **输入输出**：返回每项的 `(stub, offset, ok, 落点 opcode)`；任何一项失败即整体失败。
- **关键分支（伪代码）**：
  ```
  for stub in 扫到的存根:
      off = 常量; if !IsInstructionStart(uber, off): bad
      if 期望表里有该 stub 且期望值 != off: bad("未回填")
  ```
- **边界与失败**：现有 `CheckUbergraphEntryPoints` 只比「同偏移同 opcode 名」，**比不出操作数改动**——所以本函数必须额外比对重烘后的新表，而不是只跟原始快照比。
- **调用者与被调用者**：`apply`/`Save` 调用；调用 `UbergraphCallSites` 与 `BakeUbergraph` 的结果。
