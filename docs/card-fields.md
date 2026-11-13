# 卡属性字段（CDO 侧）

**状态：`✅ 已实现` 数据来源（usmap + 真实 CDO 实测），字段语义按需补全。**

## 1. 数据从哪来

| 信息 | 来源 |
|---|---|
| 字段名与类型 | usmap 的 `/Script/kards.BaseCardObject` schema（原生父类，所有卡都继承它） |
| 某个字段是否出现在某张卡的 CDO 里 | 该卡 `Default__<Class>_C` 导出的 `PropertyData` 列表（差量序列化，见 §3） |
| 字段当前值 | 同上，`PropertyData` 的 `RawValue` |

> 参考实现 `kardseditor` 的字段表把 `hasBlitz` 标成「嫩山鸡」之类的玩梗中文，**不可作为文档**；
> 本表按 schema 重写。

## 2. 常用字段

类型缩写：`int` = `IntProperty`，`bool` = `BoolProperty`，`enum` = `EnumProperty`，
`text`/`name`/`str`/`softobj` 同 UE 类型名。

| 字段 | 类型 | 语义 | CDO 里常见？ |
|---|---|---|---|
| `attack` | int | 攻击力 | ✅ 总是 |
| `defense` | int | 防御力 | ✅ 总是 |
| `range` | int | 移动/射程 | ✅ 总是 |
| `kredits` | int | 费用 | ✅ 总是 |
| `operationCost` | int | 行动费用 | ✅ 总是 |
| `heavyArmor` | int | 重甲 | 仅当非 0 |
| `cipher` | int | 情报 | 仅当非 0 |
| `hasBlitz` | bool | 闪击 | 仅当 true |
| `hasAmbush` | bool | 伏击 | 仅当 true |
| `hasSmokescreen` | bool | 烟幕 | 仅当 true |
| `hasMobilize` | bool | 动员 | 仅当 true |
| `hasAlpine` | bool | 山地 | 仅当 true |
| `hasFury` | bool | 狂怒 | 仅当 true |
| `hasGuard` | bool | 守护 | 仅当 true |
| `hasShock` | bool | 冲击 | 仅当 true |
| `hasCovert` | bool | 隐蔽 | 仅当 true |
| `hasScrying` | bool | 占卜 | 仅当 true |
| `hasPincer` | bool | 钳击 | 仅当 true |
| `hasSalvage` | bool | 抢救 | 仅当 true |
| `Name` | name | 资产名/卡 ID | ✅ 总是 |
| `title` | text | 卡名（FText） | ✅ 总是 |
| `Text` | text | 卡面描述 | ✅ 总是 |
| `flavorText` | text | 风味文本 | ✅ 总是 |
| `Type` | enum | 兵种（tank/infantry/…） | ✅ 总是 |
| `faction` | enum | 国家 | ✅ 总是 |
| `rarity` | enum | 稀有度 | ✅ 总是 |
| `cardSet` | enum | 所属系列 | ✅ 总是 |
| `Image` | softobj | 卡图 | ✅ 总是 |
| `artist` | str | 画师 | 常见 |
| `isDefenseBuff` / `isAttackBuff` / `isKreditsBuff` | bool | 卡面增益标记 | 仅当 true |
| `cardFunction` | obj | 关联的 `BP_CardFunctions` 引用 | 常见 |

完整列表（128 个成员）见 usmap；上表只列「改卡时会动到的」。

## 3. 差量序列化（**最容易踩的坑**）

BP 的 CDO 只序列化**与父类默认值不同**的属性：

- 字段出现在 CDO 里 → 改它的 `Value` 即可（定长替换，实测只变 1 字节）。
- 字段**不在** CDO 里（值 = 父类默认 false/0）→ 想改成非默认值，必须**新增一条 `PropertyData`**，
  不是"改值"。实测：给一张卡补 `hasBlitz=true` 后，`.uexp` +3 字节、`.uasset` +17 字节
  （导出变长导致导出表偏移整体后移），重开读回正确，函数仍逐字节一致。
- 把 true 改回 false：删掉该条目（＝回到父类默认），而不是写 false。

**写侧规则**：值 == 默认 → 删除条目；值 != 默认 → 有则改、无则补。
（默认值从父类 CDO 解析；跨包父类见 [M0 的 bmap](PLAN.md)。）

## 4. 验证要求

| 操作 | 断言 |
|---|---|
| 改已有值 | `.uasset` 0 字节变化；`.uexp` 只允许目标字段的定长字节变化 |
| 补/删条目 | 长度增量符合预期；`.uasset` 的导出表偏移可整体后移；重开读回 + 函数逐字节一致 |
| 任意写入 | 游戏内抽验（见 [verification-log](verification-log.md)） |
