# ADR 0004：SDK 全部走 Roslyn 增量源生成器

- 状态：**已接受**（用户拍板，2026-12）

## 背景

`jmap → C# SDK` 有多种落地方式。评估阶段曾有两条相反建议：

- cskismet 的 CLI 生成器（生成物入库，`git diff` 可审，最好调试）；
- 评估建议「几十 MB 数据不进编译管线」。

用户明确选择**全部走 `IIncrementalGenerator`**（作为「源生成器」这一特点的落地）。

## 决定

SDK 与蓝图类基座都由 `Polaris.SourceGen`（`IIncrementalGenerator`）在编译期生成，输入为
`AdditionalFiles`：

| AdditionalFile | 体积（实测） | 生成物 |
|---|---|---|
| `kards.usmap` | 1.8 MB | 原生结构/枚举 → C# 类型 |
| `functions.json` | 5.1 MB | 原生类与方法（12478 个） |
| `bmap.json` | 待测（预估 3–8 MB） | 蓝图类基座、成员、事件签名 |

**bmap 的扫描不在生成器里做**：资产 IO 仍由 `polaris scan`（MSBuild `BeforeBuild` target 调 CLI）完成，
生成器只读 JSON。这样生成器保持纯函数式（同样的 AdditionalFiles → 同样的源码），可增量缓存。

## 已知代价与缓解

| 代价 | 缓解 |
|---|---|
| 每次 build 解析 5 MB JSON + 1.8 MB usmap | 生成器按文件内容 hash 建增量管线；实测目标：无改动 build 增量 < 2 s |
| 生成 2 MB C# 源码 | 只生成被引用的命名空间/类？——不行，成员引用需要完整类型；改为**按程序集拆分**（`Polaris.Sdk.Kards` 单独程序集），避免污染用户项目 |
| 生成器内异常难查 | `EmitCompilerGeneratedFiles=true` + `polaris sdk --dump` 用同一个 `SdkEmitter` 落盘到 `obj/generated/` |
| IDE 卡顿 | 首次打开慢；提供 `polaris sdk --write` 把同一份输出写进仓库（可选，二者不共存） |

## 否决的方案

- 纯 CLI：与用户对「源生成器」的要求冲突。
- 生成器里直接读资产：需要在编译进程里跑 UAssetAPI + 扫 2081 个包，慢且无法缓存。

## 验收

- `dotnet build` 后 `obj/generated/` 里能看到完整 SDK。
- 无改动二次 build 增量 < 2 s；改一行用户代码只重跑用户项目。
- 生成物与 `polaris sdk --dump` 逐字节一致（同一 emitter，不允许两套）。
