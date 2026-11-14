# G1 游戏内验证：`card_unit_t_70eot`（苏联 / Allegiance / units）

**目的**：验证 Polaris 产出的 mod pak 在真实游戏里同时生效两类改动：
1. **CDO**：`kredits` 2 → 0（卡牌费用变 0）
2. **字节码**：`OnPlayedFromHand` 被替换为 `targetCard.CampaignAddAttack(5, false)`（打出时攻击 +5）

## 1. 产物

| 文件 | 说明 |
|---|---|
| `D:\files\projects\.tmp\polaris\g1\card_unit_t_70eot.pak` | 7650 字节 / 2 条目（uasset + uexp） |
| `D:\files\projects\.tmp\polaris\g1\card_unit_t_70eot.uasset` | 写回后的资产（便于 diff/复查） |\n| `D:\files\projects\.tmp\polaris\g1\mod.json` | 清单（资产/函数/patch） |
| `samples/G1Mod.cs` | 生成它的 mod 源码（已入库） |

pak 内路径：`Blueprints/Cards/Soviet/Allegiance/units/card_unit_t_70eot.uasset` / `.uexp`
mount：`../../../kards/Content/`

## 2. 安装

1. 关闭游戏。
2. 把 `card_unit_t_70eot.pak` 复制到游戏的 `Paks` 目录（与 `kards-Win64-Shipping` 同级惯例位置）：
   `<游戏安装目录>\KARDS\Content\Paks\`
3. 重新启动游戏。

## 3. 观察点

### 主观察（CDO，确定性高）

- 在**卡组/收藏**里找到该卡（苏联 / Allegiance / 70eot），费用显示应为 **0**。
- 若原来费用为 2、现在为 0，说明 CDO 写入生效。

### 次观察（字节码，注意 Campaign 语义）

- 在**对局**中打出该卡；
- 观察该单位攻击力是否比基础值高 **5**。
- 若没有变化，可能是 `CampaignAddAttack` 在非战役模式不生效——这属于「函数语义选择」问题，不是字节码没写进去；请把现象反馈给我，我换一个通用函数再测。

## 4. 回滚

删除 `KARDS\Content\Paks\card_unit_t_70eot.pak`，重启游戏即可。

## 5. 反馈什么

- 主观察是否成立（费用 0 / 不是 0 / 游戏崩溃/无法启动）
- 次观察现象（攻击变化值）
- 若有异常：游戏日志 + 截图；以及是否出现「pak 无法加载」类错误

'## 6. 实测结果（2026-11）

**✅ 通过**（用户实测：pak 进游戏后生效）。这说明：

- UAssetAPI 写回 + v3/store pak 打包 + mount 路径，在真实游戏里可用；
- 我们生成的 `OnPlayedFromHand` 字节码（含 `EX_Context` + `EX_FinalFunction`）运行期可执行；
- CDO 差量写入（`kredits` 2 → 0）运行期生效。

'## 7. G1b：语言特性验证（待测）

产物：`D:\files\projects\.tmp\polaris\g1b\card_unit_t_70eot_g1b.pak`（8088 字节），源码 [samples/G1bMod.cs](../../samples/G1bMod.cs)。

同一张卡，函数体换成：

```csharp
int x = 1;
x = x + 2;              // 3
if (x > 2) { x = x + 1; } else { x = x + 2; }   // 4
targetCard.CampaignAddAttack(x, false);
```

**预期**：打出该卡时攻击 **+4**（G1 是 +5，可区分）。

步骤：
1. 先删掉 G1 的 `card_unit_t_70eot.pak`（同资产两个 pak 会冲突）；
2. 放入 `card_unit_t_70eot_g1b.pak`，启动游戏；
3. 对局打出该卡，观察攻击加成是否为 **+4**。

这一包同时验证：局部变量（`EX_Let` + 新 FProperty）、算术（`EX_CallMath`）、分支（`EX_JumpIfNot`/`EX_Jump`）、参数传递（`EX_Context`）。

'## 8. G1d：搬别的卡的逻辑（待测）

参考 `card_event_spirit_of_rome.CanPlayFromHand` 的写法：先用 `IsInfantry(out bool)` 判断目标，再按结果走不同分支。

产物：`D:\files\projects\.tmp\polaris\g1d\card_unit_t_70eot_g1d.pak`（7893 字节），源码 [samples/G1dMod.cs](../../samples/G1dMod.cs)。

```csharp
bool infantry;
targetCard.IsInfantry(out infantry);
if (infantry) { targetCard.CampaignAddAttack(3, false); }
else          { targetCard.CampaignAddAttack(1, false); }
```

**预期**：打出该卡时，若它是步兵则攻击 +3，否则 +1。

Flora 侧也应能正常反编译（跳转已改为 Visit 坐标系：`JumpIfNot → 98`、`Jump → 136`，均 `landedFlora=True`）。

'## 9. G1f：指向性卡牌（CDO 要求，待测）

对比 `card_event_spirit_of_rome`（指向性事件）的 CDO：`selectTargetOnPlayedFromHand = true`；我们的测试卡没有这个字段值。

产物：`D:\files\projects\.tmp\polaris\g1f\card_unit_t_70eot_g1f.pak`（8172 字节），源码 [samples/G1fMod.cs](../../samples/G1fMod.cs)。

```csharp
[PolarisPatch("selectTargetOnPlayedFromHand", "true")]   // CDO：变成指向性
...
bool canIt; string reason, reasonParam1, reasonParam2;
targetCard.CanBeTargetted(out canIt, out reason, out reasonParam1, out reasonParam2, targetCard, true);
if (canIt) { targetCard.CampaignAddAttack(2, false); }
```

**预期**：打出该卡时游戏要求选一个目标；对目标攻击 +2。

Flora：`JumpIfNot → 130`（`landedFlora=True`），CDO 多出 `selectTargetOnPlayedFromHand = true`。

'## 10. G1g：通过 `cardFunction` 调 CardFunctionsStub（待测）

`BaseCardObject` 上有成员 `cardFunction`（类型 `CardFunctionsStub`，jmap 的 `property_class`），真卡的抽牌/转换/回手都是通过它调的。

产物：`D:\files\projects\.tmp\polaris\g1g\card_unit_t_70eot_g1g.pak`（7883 字节），源码 [samples/G1gMod.cs](../../samples/G1gMod.cs)。

```csharp
targetCard.cardFunction.MoveUnitFromBoardToOwnersHand(targetCard, 0);
targetCard.cardFunction.DrawSpecificCardFromDeckBySide(0, targetCard.CardID, ESideEnum.left, true);
```

**预期**：打出该卡后它立刻被收回手牌（`MoveUnitFromBoardToOwnersHand`）；抽指定 ID 的牌在 ID 不存在时无效果。

`DrawCardsFromDeckBySide` 这类带 `out TArray<int>` 的抽牌函数还需要数组局部变量支持（见 PLAN）。

## 11. 已知风险'''''

- 该资产的跳转/入口偏移与解析布局不一致（见 `docs/PLAN.md §9`），但我们替换的是**平坦函数** `OnPlayedFromHand`，不触碰 ubergraph 段重排；写回后已通过 `RoundTrip.Verify`。
- 若游戏启动即崩，优先怀疑 pak 格式/挂载路径；可用 `polaris pak` 重新核对条目路径。
