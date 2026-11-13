# Polaris 架构

**状态：`PROPOSED`（提议）。** 本文描述目标架构与数据流；函数级契约见 `docs/src/**`，路线与闸门见 [PLAN.md](PLAN.md)。

## 1. 一句话

把 KARDS（UE5.6）蓝图**类的结构与逻辑**表达成 C# 类（字段 = 蓝图成员，方法 = 蓝图函数），
用 Roslyn 编译回 Kismet 字节码与资产字段，再用 UAssetAPI 写回 `.uasset`/`.uexp`。

## 2. 四类输入（这是本项目最重要的现实）

| 输入 | 覆盖 | 不覆盖 | 证据 |
|---|---|---|---|
| `usmap` | 原生 `/Script` 类与结构（含 super 链、成员类型） | **`/Game` 蓝图类 0 条** | [facts §1](../../polaris-research/verified-facts.md) |
| `functions.json`（jmap） | `/Script` 函数签名、flags、参数方向 | `/Game` 0 条；`objects.json` 的 `super` 恒 null | 同上、[eval §2.2](../../polaris-research/polaris-project-eval.md) |
| 资产（`.uasset`+`.uexp`） | 蓝图类自身的 `SuperStruct`、成员、`LoadedProperties`、CDO、字节码 | 不含名字以外的跨包语义 | [facts §3](../../polaris-research/verified-facts.md) |
| **bmap**（本项目新增） | 资产扫描产物：类清单、父链、成员、函数签名、CDO 默认值、ubergraph 入口表 | — | 见 [ADR 0003](decisions/0003-four-inputs-bmap.md) |

`objects.json` 里已经有 **2426 个 `BlueprintGeneratedClass`** 的名字与 outer（[eval §2.1](../../polaris-research/polaris-project-eval.md)），
所以「类清单」不必重新发明；bmap 只补 **super 链 / 成员 / 签名 / CDO / 入口表**这五样。

## 3. 分层

```
┌────────────────────────────────────────────────────────────┐
│ 用户 C#（唯一编辑面）                                       │
│   partial class card_event_aans : BaseCardObject_C {        │
│       int kredits = 3;                                      │
│       override void OnPlayedFromHand(...) { ... }           │
│   }                                                         │
└───────────────┬────────────────────────────────────────────┘
                │ Roslyn SyntaxTree + SemanticModel
┌───────────────▼────────────────────────────────────────────┐
│ Polaris.Compiler                                            │
│   Transpiler   : UHT 声明 + 语句/表达式 → 字节码            │
│   VarScanner   : 局部变量 / 成员 / CallFunc_* 临时变量      │
│   UberBuilder  : 事件体 → 规范化 ubergraph 段 + 入口表      │
│   ClassPatcher : 成员/CDO/新函数 → UAssetAPI 对象           │
└───────────────┬────────────────────────────────────────────┘
                │ 自己的 UHT 声明模型（类/字段/函数 + flags）
┌───────────────▼────────────────────────────────────────────┐
│ Polaris.Bytecode（自研，M3/M4）                              │
│   指令读写 / 跳转偏移回填 / ubergraph 段与存根 / 往返校验     │
└───────────────┬────────────────────────────────────────────┘
                │ UAssetAPI（KismetExpression 序列化 + 导出表/属性）
┌───────────────▼────────────────────────────────────────────┐
│ .uasset + .uexp                                             │
└────────────────────────────────────────────────────────────┘
```

读方向共用同一套模型：

```
资产 ──bmap──► BlueprintModel ──CSharpEmitter──► 可读 C#（partial 基座）
```

## 4. 两条数据流

### 4.1 读（资产 → C#）

1. `polaris scan` 遍历资产目录，产出 `bmap.json`（类/父链/成员/函数/CDO/入口表）。
2. `Polaris.SdkGen` 消费 `jmap + usmap + bmap`，生成：
   - native SDK：`BaseCardObject_C`、`CardFunctionsStub` 等原生类；
   - 蓝图类基座：`card_event_aans_Base`（成员 + 全部函数签名 + 反编译体）。
3. 用户写 `partial class card_event_aans : card_event_aans_Base { ... }` 覆盖要改的方法。

### 4.2 写（C# → 资产）

1. Roslyn 语义模型给出：方法体、字段初始化、`out/ref` 实参、局部变量类型。
2. `Transpiler` 产出自己的字节码指令序列（UHT 声明直接推导）；`VarScanner` 汇总本次用到的局部变量 → `CallFunc_<函数>_<引脚>[_n]`
   临时变量与 `FProperty` 声明。
3. 平坦函数：整函数重生成。事件方法：`UberBuilder` 生成规范化段，重烘 ubergraph，
   然后**改写所有存根里的 `EX_IntConst` 入口常量**。
4. 字段：写 CDO 的 `PropertyData`；新字段：写类 `LoadedProperties` + `Children`（见 M7）。
5. 写盘前四道闸（见 §5）；写盘后重读校验。\n6. 需要直接进游戏时，`ModPak` 把产物按 `Content/` 相对路径打进 v3 mod pak（mount `../../../kards/Content/`），\n   读回校验 + 确定性重建自检。

## 5. 安全模型（不许静默）

| 闸 | 检查 | 失败后果 |
|---|---|---|
| 闸 1 | 未触碰函数：反编译 → 回编译 → **逐字节一致** | 拒绝写盘 |
| 闸 2 | 改过函数：重编译后反编译 → 结构 diff 与预期一致 | 拒绝写盘 |
| 闸 3 | ubergraph：每个入口常量落在**指令起点**，`CheckUbergraphEntryPoints` 全绿 | 回滚编辑 |
| 闸 4 | 写盘后重开资产：目标函数/属性读回正确，其余函数仍逐字节一致 | 报错并保留旧文件 |

运行时正确性**不在闸内**——由用户在游戏内抽验（协议见 PLAN §6）。

## 6. 明确不做

1. 不做完整蓝图语言：`switch`/`try`/`latent`/委托多播/接口分发遇到就报错（沿用 `BodyTranslator` 的 total 规则）。
2. 不做新资产（新卡需要贴图/本地化/卡池注册，是另一个项目）。
3. 不做可视化编辑器：编辑面就是 C# 源码。
4. 不用表达式树当 IR（[ADR 0001](decisions/0001-ir-roslyn-source.md)）。
5. **字节码层自研**（[ADR 0006](decisions/0006-uht-style-sdk-own-bytecode.md)）：不引入 `KArg`/`KNode` 中间模型，UAssetAPI 只做 `KismetExpression` 序列化。

## 7. 依赖边界

- `Polaris.Assets` 依赖 UAssetAPI；**唯一**允许碰 `Export`/`PropertyData` 的层。
- `Polaris.Compiler` 只依赖自己的 UHT 声明模型与 `BlueprintModel`，不依赖 UAssetAPI。
- `Polaris.SdkGen` 只读 bmap/jmap/usmap，产出源码文本。
- `Polaris.Cli` 编排；`Polaris.SourceGen` 是 `SdkGen` 的增量包装（[ADR 0004](decisions/0004-sdk-via-incremental-generator.md)）。
