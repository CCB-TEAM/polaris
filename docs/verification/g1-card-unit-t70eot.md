# G1 游戏内验证：`card_unit_t_70eot`（苏联 / Allegiance / units）

**目的**：验证 Polaris 产出的 mod pak 在真实游戏里同时生效两类改动：
1. **CDO**：`kredits` 2 → 0（卡牌费用变 0）
2. **字节码**：`OnPlayedFromHand` 被替换为 `targetCard.CampaignAddAttack(5, false)`（打出时攻击 +5）

## 1. 产物

| 文件 | 说明 |
|---|---|
| `D:\files\projects\.tmp\polaris\g1\card_unit_t_70eot.pak` | 7650 字节 / 2 条目（uasset + uexp） |
| `D:\files\projects\.tmp\polaris\g1\card_unit_t_70eot.uasset` | 写回后的资产（便于 diff/复查） |
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

## 6. 已知风险

- 该资产的跳转/入口偏移与解析布局不一致（见 `docs/PLAN.md §9`），但我们替换的是**平坦函数** `OnPlayedFromHand`，不触碰 ubergraph 段重排；写回后已通过 `RoundTrip.Verify`。
- 若游戏启动即崩，优先怀疑 pak 格式/挂载路径；可用 `polaris pak` 重新核对条目路径。
