# ADR 0002：改过的事件走「规范化重烘 + 回填存根」

- 状态：**已接受**（用户拍板，2026-12）
- 关联：[UE 烘培语义](../../../polaris-research/ue-baking-semantics.md)、[事实 §3.2](../../../polaris-research/verified-facts.md)

## 背景

实测三条互相矛盾的事实：

1. 入口偏移是 ubergraph `Script` 的**绝对字节偏移**，烘在**调用点**的 `EX_IntConst` 里
   （`LocalFinal ExecuteUbergraph_X(<offset>)`），不在 ubergraph 自己的字节码里。
2. 入口偏移**不等于段起始**：`card_unit_t_70eot` 段起点 10/82/202，入口 272/388；
   `BP_Chat` 段起点 10/83/648/1267，入口 107/721/726/731/938/1295（含蹦床）。
   全量口径（1691 个 ubergraph / 8688 段 / 4716 个入口）：**只有 19.8% 的入口落在段起始上，
   68.9% 的段没有任何入口**（[facts §8](../../../polaris-research/kards-asset-facts.md)）。
3. 事件体之间会共享尾部（一个段被多个入口/回边引用），所以「一段 = 一个事件」不成立。
4. 段尾终止符：`PopExecutionFlow` 34.9%、`JumpToOtherSection` 31.4%、`FallsThrough` 27.2%、
   `JumpToEpilogue` 仅 6.1%——UE5.6 大量用执行流栈（latent/协程）而不是跳公共尾声。

现有 `solstice-kismet` 的 `BakeUbergraph` 只能**重烘**并返回入口表，**没有任何代码回填调用点**；
`ModApplier` 明确拒绝 `Replace` 打 ubergraph。

## 决定

Polaris 自己拥有 ubergraph 的布局：

1. **规范化**：一个事件方法 = 一个段，段首即入口；正常结束的段尾发 `EX_Jump` 到唯一尾声。
   不再模仿 UE 的共享尾部/蹦床（那是优化结果，不是语义）。
   **例外**：方法体里调用了延迟/协程节点时，段尾必须保留 `PopExecutionFlow` 语义——这类节点首期
   **直接报错**（`Transpiler` 不支持 latent），所以规范化段尾只有 `JumpToEpilogue` 与空体两种。
2. 重烘后**改写所有存根**里的 `EX_IntConst`：存根名 → 新入口偏移。
3. 入口表从存根扫出来（`UbergraphCallSites`），与新布局的段起始逐一对应后写回。
4. 写盘前用 `CheckUbergraphEntryPoints` 校验「每个常量都落在指令起点」，并与新烘的入口表逐项相等。
5. **未改动的函数一个字节不动**；只重生成被用户覆盖的方法所属的 ubergraph 与存根。
6. 头部 `PushExecutionFlow(尾声)`：按是否需要流栈决定（原资产 43.5% 有），由 `Build` 显式传入。

## 否决的方案

- **布局保真（保留段序/蹦床，只改段内表达式）**：C# 方法体写不全，用户被迫退回 KNode 级编辑，
  与「C# 是唯一编辑面」冲突。
- **绕开 ubergraph（事件体编成普通 UFunction）**：不碰入口表，但依赖 BP VM 的 PersistentFrame 行为，
  且会改变事件绑定语义，未验证。

## 代价

- **改过的类字节必然与原始不同**（含存根）。验收因此分两档：未改函数逐字节；改过函数自校验
  （见 PLAN §5 闸 2/3）。
- 多入口段的原始形状（如 `BP_Chat` 6 入口 4 段）会被规范化成 6 段；需要回归样本覆盖。
- 新增一条链：**扫描 → 重烘 → 回填 → 校验**，评估估 12–18 人日（[eval §2.4](../../../polaris-research/polaris-project-eval.md)）。
