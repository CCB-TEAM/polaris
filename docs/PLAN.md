# Polaris 实施计划

**状态：`PROPOSED`。** 所有工期是区间估计，不是承诺；每个里程碑必须有**可演示的产出**才算完成。

## 1. 已拍板的路线（用户决定，不再重开）

| # | 决定 | 落点 |
|---|---|---|
| 1 | 写入范围：字节码 + CDO 默认值 + 新成员 + 新函数（注入到**既有卡类**） | [ADR 0005](decisions/0005-scope-existing-class-injection.md) |
| 2 | 改过的事件走**规范化重烘 + 回填存根**，段仍全在 ubergraph 内 | [ADR 0002](decisions/0002-ubergraph-canonical-rebake.md) |
| 3 | 编辑面与 IR：C# 源码（Roslyn）+ **UHT 风格声明模型**（自研，不用 `KArg`/`KNode`） | [ADR 0006](decisions/0006-uht-style-sdk-own-bytecode.md) |
| 4 | SDK 全部走 Roslyn `IIncrementalGenerator` | [ADR 0004](decisions/0004-sdk-via-incremental-generator.md) |
| 5 | 验收：未改函数逐字节；改过函数自校验 + 游戏内抽验 | 本文 §6 |
| 6 | 有游戏内验证手段（能换文件、能看结果） | 本文 §7 |

## 2. 里程碑

| 里程碑 | 内容 | 验收（可演示） | 估计 |
|---|---|---|---|
| **M0** 资产地基（读） | `polaris scan` → `bmap.json`：类/父链/成员/函数/CDO/入口表（[AssetIndex](src/Polaris.Assets/AssetIndex.md)） | 三个样本类模型打印完整（`card_event_aans` / `card_unit_t_70eot` / `BP_BaseCard`）；**2137 个资产、9303 个带字节码函数、1691 个 ubergraph、4716 个入口**全扫不中断；「`tempCard` 本类、`cardFunction` 父类」对得上 | 5–8 人日 |
| **M1** 类型系统 + SDK 生成器 | [UsmapReader](src/Polaris.SdkGen/UsmapReader.md) + [JmapReader](src/Polaris.SdkGen/JmapReader.md) + [SdkEmitter](src/Polaris.SdkGen/SdkGenerator.md) + [SdkSourceGenerator](src/Polaris.SourceGen/SdkSourceGenerator.md) | **原生 SDK ✅ 已实现**：485 文件 / 4357 类 / 5349 结构 / 1875 枚举 / 12617 方法（13064 里 447 个包级 `__DelegateSignature` 暂跳过）；`samples/SdkConsumer` 全量编译零错误；生成器消费 AdditionalFiles 后同样零错误，干净构建 ~31 s、增量 ~2 s。**蓝图类基座仍 🚧（等 bmap）** | 已完成原生部分；bmap 后补基座 |
| **M2** 改数值（写 CDO） | `[PolarisClass("card_event_aans")] partial class … { int kredits = 3; }`；含**补/删差量条目**（缺省字段改 true 要新增，见 [card-fields](card-fields.md) §3） | **闸 G0**：写盘→重开值对→未改资产逐字节一致→**游戏内卡面/属性变**。静态半场 **✅ 已实现并测试**（`Polaris.Assets` + 5 个用例：改值 1 字节；补 `hasBlitz` +3/+17 字节；Reset 后 `.uexp` 完全还原、`.uasset` 只剩 +17 的 name map 残留）；**运行期未验** | 3–5 人日（静态已完成） |
| **M2.5** 快速导出 mod pak | 继承 `solstice-pak`（v3/store）→ `ModPak`（路径规范化/读回/确定性自检）；`cards set --pak` 一条命令出包 | **✅ 已实现**：用 vendored writer 重建真实 `wide-mount.pak`（66,543 B / 2 条目）**逐字节一致**；改 `hasBlitz` 后产出 7,615 B mod pak | 1 人日 |
| **M3** 改逻辑（平坦函数） |' [Transpiler](src/Polaris.Compiler/Transpiler.md) 接 `KNode`；出参令牌按实测口径（`EX_LocalVariable` 35454 处 / `EX_LocalOutVariable` 524 处）；补结构体常量（消掉占位） | **闸 G1**：`BP_CardFunctions` 里选一个小函数整函数重生成；四道闸全绿；**游戏内行为变** | 10–15 人日 |
| **M4** 事件体 + 重烘回填 | [UbergraphBuilder](src/Polaris.Compiler/UbergraphBuilder.md)：一段一事件 → 重烘 → 回填所有存根 | **闸 G2**：单事件卡（`card_event_aans`）整段重写；多事件卡（`card_unit_t_70eot` / `BP_Chat`）入口对账全绿；**游戏内事件仍触发且行为变** | 12–18 人日 |
| **M5** BP→C# 基座 | [CSharpEmitter](src/Polaris.Decompile/CSharpEmitter.md)：入口表命名段 + `goto` 兜底 | **闸 G3**：`BP_CardFunctions`（294 函数）生成的 C# 可编译；逐函数标注「可回译/不可回译」清单，可回译部分一致率 ≥95% | 10–15 人日 |
| **M6** 注入新函数 | [ClassPatcher.AddFunction](src/Polaris.Assets/ClassPatcher.md)：追加导出 + `Children` + `FuncMap` + 存根 + 段 | **闸 G4**：给一张卡加一个空函数 → 资产可开 → 游戏内 `FindFunctionByName` 能找到并能调用 | 10–15 人日 |
| **M7** 新成员 + 默认值 | [ClassPatcher.AddMember](src/Polaris.Assets/ClassPatcher.md)：`LoadedProperties` + CDO | **闸 G5**：新成员写盘→重开读回→游戏内可读该字段 | 5–10 人日 |

合计 **68–106 人日**（含 M2.5 的 1 人日）。M0+M2+M2.5 是「最早能玩的东西」（11–17 人日）。表中数字来源：[kards-asset-facts.md](../../polaris-research/kards-asset-facts.md)（2137 资产 / 9303 函数 / 1691 ubergraph / 4716 入口 / 8688 段）。

## 3. 垂直切片（固定样本，回归基准）

| 样本 | 为什么选它 | 关键数字 |
|---|---|---|
| `card_event_aans` | 最小卡：1 事件、1 段、CDO 17 条 | 存根 36 B / ubergraph 255 B / 入口 10 |
| `card_unit_t_70eot` | 多事件、入口落在段内、有循环 | 存根 2 个、ubergraph 528 B、段 10/82/202、入口 272/388 |
| `BP_Chat` | 多入口 + 蹦床（最难 ubergraph） | 4 段 / 6 入口 / 尾声 1565 |
| `BP_CardFunctions` | 294 个函数、46 个 props 的复杂签名、`out` 参数 | 导出 299 条、children 294 |
| `BP_BaseCard` | 99 个成员、109 个函数、父类原生 | 导出 139 条、CDO 30 条 |

## 4. 熔断闸（先写死判据，再开工）

| 闸 | 判据 | 不通过怎么办 |
|---|---|---|
| **G0** | CDO 写盘可重读 + 未改资产逐字节一致 + 游戏内可见（静态半场 **✅ 已实现并测试**）。**运行期对「补条目」单独抽验**：缺省字段补成 true 后游戏里真的生效 | 整个「写回」路线重估（UAssetAPI 写回不可信），砍到只读工具 |
| **G1** | 平坦函数重生成后四闸绿 + 游戏内行为变 | 字节码写入降级为「只改常量/参数」，不做整函数 |
| **G2** | 重烘后入口全落在指令起点 + 游戏内事件仍触发 | 放弃规范化重烘，退回布局冻结（只段内替换/段尾追加） |
| **G3** | 生成 C# 可编译 + 回译率 ≥95% | BP→C# 降级为「只出可读代码」，不回译 |
| **G4** | 新函数游戏内可被找到并调用 | 砍掉 M6，Polaris 止步于「改既有 + CDO」 |
| **G5** | 新成员游戏内可读 | 砍掉 M7 |

## 5. 四道闸（每次 `apply` 必跑）

1. **未改函数**：反编译 → 回编译 → 逐字节一致（8115/8115 基线）。
2. **改过函数**：重编译 → 反编译 → 与预期结构 diff 一致（无意外差异）。
3. **ubergraph**：每个入口常量落在指令起点，且与新烘的入口表逐项相等。
4. **写盘后**：重开资产 → 目标函数/属性读回正确 → 其余函数仍逐字节一致。

任何一闸失败：**回滚内存改动、不写盘、保留输入**。

## 6. 运行时验证协议（每次里程碑收尾）

1. `polaris apply --out out/<资产>`（永不覆盖输入）。
2. 用现有打包流程把 `out/` 塞回游戏（用户执行，Polaris 不碰 pak）。
3. 游戏内按「差异清单」逐项看：
   - M2：卡面/属性数值；
   - M3：该函数的行为（打日志/看效果）；
   - M4：事件是否触发、行为是否按新逻辑；
   - M6/M7：新函数可调用、新字段可读。
4. 结果记进 `docs/verification-log.md`（模板：资产 / 改动 / 预期 / 实际 / 结论）。

**静态闸全绿 ≠ 运行正确**：本项目历史上有 3 次「四闸全绿、游戏里错」的事故，运行期结果才是最终判据。

## 7. 风险登记（按「会不会静默错」排序）

| 风险 | 触发条件 | 后果 | 缓解 |
|---|---|---|---|
| CDO 无版本属性块写回改坏无关属性 | 任一 CDO 写入 | 卡面/逻辑莫名其妙 | 重开读回全字段 + 字节 diff 只允许目标属性变化 |
| 新增导出/导入移动既有 `FPackageIndex` | 插入而非追加表项 | 引用错乱 | 强制追加表尾；实现里禁止 `Insert` |
| 新函数运行时不可见 | M6 | 白写 | G4 闸；回退方案是只改既有函数 |
| 新成员破坏 UStruct 布局 | M7 | 类加载异常 | G5 闸；先只支持标量成员 |
| 规范化重烘改变事件入口 | M4 多入口卡 | 事件不触发/跳错 | 入口对账 + `BP_Chat`/`70eot` 回归 |
| 事件体含执行流栈（`PopExecutionFlow` 占段尾 34.9%） | M4 重写含 latent/协程的卡 | 段尾语义被换成 `Jump`，协程恢复点丢失 | `Transpiler` 对 latent 节点直接报错；M4 只接受纯同步事件体 |
| 0 体 ubergraph（8 个资产，入口直指 `Return`） | M4 规范化生成空段 | 事件无法寻址 | `Build` 支持「空段 + 跳到尾声」，回归覆盖 `card_unit_jazz_band` |
| jmap/usmap/bmap 版本不一致 | 游戏更新 | 生成代码与资产不匹配 | `polaris check` 内容哈希 + 版本三元组（engine 5.6 / usmap hash / bmap hash） |
| 源生成器拖慢 build | M1 | 开发体验崩 | ADR 0004 的缓解项；实测阈值 2 s |
| 反编译 C# 不可编译 | M5 | 基座不可用 | 不可译函数明确列出，不静默产出坏代码 |

## 8. 环境与构建（已实测的坑）

- `dotnet 10.0.402`；`DOTNET_CLI_HOME`/`NUGET_PACKAGES` 指向工作区（见仓库 `run.ps1`）。
- **nuget.org 在本机不可达**（schannel `SEC_E_NO_CREDENTIALS`）：Roslyn 用 SDK 自带的
  `$(MSBuildToolsPath)\Roslyn\bincore`；UAssetAPI 用本地缓存 1.1.0。
- `UseSharedCompilation=false`：沙箱里 MSBuild 命名管道可能不可用。
- **不要用 `dotnet build Polaris.slnx`**：沙箱里 `_GetProjectReferenceTargetFrameworkProperties` 的嵌套 MSBuild 会报
  MSB4276（`WorkloadAutoImportPropsLocator` 找不到），且显示「0 错误但失败」。用 `.\run.ps1 build`（逐项目构建）。
- 字节码层自研后不再引用 Solstice；`Polaris.Assets` 只用 UAssetAPI 做表达式序列化校验。
- usmap 会被 UAssetAPI 以读写模式打开 → 必须先复制到可写目录（`Usmap.PathToStream` 报 `UnauthorizedAccessException`）。
- 参考源码：`D:\files\projects\ref-uapi`（UAssetAPI）、`polaris-research`（UE 烘培语义实测）、
  `ue-src\KismetCompilerVMBackend.cpp`（烘培语义）。

## 9. 未决问题：ubergraph 跳转偏移基准（M4 前必须查清）

实测 `BP_CardFunctions.AddAttackUntilEndOfTurn`：

- 反序列化后逐条测量/顺序回读得到的指令起点：`0,41,63,109,131,212,271,273`（总长 274）
- 两条 `EX_JumpIfNot` 的 `CodeOffset` 都是 `191`
- `191` 落在 `EX_LocalVirtualFunction`（131 起，长 81）内部；从 191 解析能“碰巧”读出 `EX_ByteConst`，但不是真实语句起点
- 同一资产的**无改动保存** `.uexp` 与原文件逐字节一致（798367/798367），排除了序列化保真问题

全资产统计（BP_CardFunctions 294 个函数 / 212 个含跳转 / 1599 个跳转目标）：只有 **22 个**目标落在指令起点；同一函数内不同目标到最近起点的偏差也不一致（+20/+9 等），说明不是单一基准常量。\n\n补充（Round 5）：`AddAttackUntilEndOfTurn` 的 `191` 恰好是 `ExecuteUbergraph_BP_CardFunctions`（2035 字节）里的一个指令起点，但该函数自身的 274 字节块**不在** ubergraph 字节里；对 `abs`/`start+off`/`start+1+off`/`start+5+off`/`end-off`/`prevStart+off` 六种公式全量统计都只有 1–2% 命中，基本可排除单一线性基准。\n再按 token 分开：`EX_Jump` 绝对 8 / 相对 2；`EX_JumpIfNot` 绝对 14 / 相对 11 —— 两者都没有稳定基准，说明问题不在“Jump 和 JumpIfNot 用了不同公式”。\n\nUE 源码 `KismetCompilerVMBackend.cpp::PerformFixups` 写的是**绝对** statement 偏移
（`CommitSkip(OffsetToFix, TargetStatementOffset)`），与实测不一致；怀疑与 ubergraph
的脚本缓冲区分段/入口表有关。M4 之前需要：
1. 找到该函数导出在 `.uexp` 中的真实脚本起点（当前按 SerialOffset 开窗没搜到字节块，说明窗定位还不准）；
2. 对照 `JumpTargetFixupMap` 的 ubergraph 分支与 `StatementLabelMap` 的构建，确认 191 的基准。
