# ADR 0003：输入是四件套，第四件是 bmap

- 状态：**已接受**（2026-12，主代理实测 + 子代理复核）

## 背景

原设想的数据源是 `jmap`（函数签名）+ `usmap`（结构/类 schema）。实测覆盖如下：

| 输入 | `/Script` | `/Game` |
|---|---|---|
| `functions.json` | 13696 个函数，1216 个 owner 全在 `/Script` | **0** |
| `functions.kards.json` | 1043 | **0** |
| `usmap` | 原生类/结构（实测 19408 个 schema） | **0**（字符串 `card_event_aans` / `BP_BaseCard` 均不命中） |
| `objects.json` | 有 | **17086 条**，其中 `BlueprintGeneratedClass` 2426 条，但 `super` 恒 null |

蓝图类的**继承链、成员、函数签名、CDO 默认值**都不可能来自 jmap/usmap。

## 决定

Polaris 读四件套：

1. `usmap` —— 原生类/结构 schema（含 super 链）。
2. `jmap functions.json` —— 原生函数签名、参数方向（`dir`/`ref`/`const`）、flags。
3. `jmap objects.json` —— 资产清单与类名的交叉对账（不自作权威，`super` 不可信）。
4. **`bmap.json`（新）** —— 资产扫描产物：
   - 类：`ObjectPath`、本类导出索引、`SuperStruct`（跨包时继续追）
   - 成员：本类 `LoadedProperties`（名字 + FProperty 描述），继承成员从父类（多数是原生）取
   - 函数：每个 `UFunction` 的 `LoadedProperties`（参数/局部变量/`CallFunc_*`）、`FunctionFlags`、字节码尺寸
   - CDO：`Default__<Class>_C` 导出的 `PropertyData` 列表（名字 + 值 + 类型）
   - ubergraph：存根名 → 入口偏移；段起始偏移

bmap 由 `polaris scan` 产出（MSBuild target 或手动），**不进**源生成器的资产 IO 路径。

## 否决的方案

- 只用三件套：蓝图类模型缺失，等于放弃「类 = C# 类」的核心表达。
- 从 `objects.json` 推断 super：实测 47311 条 `super` 全为 null。
- 从 usmap 找 BP 类：0 命中。

## 代价

- bmap 是**版本绑定**的：游戏更新后要重扫（耗时约十几分钟，需实测）。
- bmap 与资产不同步时，生成器会产生过期代码 → 用内容哈希 + `polaris check` 报警。
- bmap 体积与格式要控制（见 PLAN M0 验收）。
