# 运行时验证记录

> 每次 `polaris apply` / `cards set` 产出资产或 mod pak 并塞回游戏后填一行。静态闸全绿 **不算**通过——本表是唯一运行期判据。

模板：

```
## YYYY-MM-DD <资产> / <里程碑>
- 改动：<函数/字段 + 新值>
- 静态闸：闸1 ✅ 闸2 ✅ 闸3 ✅ 闸4 ✅（pak：读回 + 逐字节自检）
- 游戏内预期：<看到什么>
- 实际：<看到什么；截图/录像路径>
- 结论：通过 / 不通过（原因）
```

## 记录

### 2026-12 `card_event_aans` / M2-G0 静态半场（**通过**）

- 改动：CDO `kredits` 3 → 4
- 静态闸：`.uasset` 0 字节变化；`.uexp` 仅 1 字节（offset 336）；重开读值 = 4；两个函数仍逐字节一致
- 游戏内预期：卡面/属性显示 4 费（或对应规则生效）
- 实际：（待用户在游戏内验证）
- 结论：**静态通过，运行期待验**

> 证据与复现命令：[..\..\polaris-research\cdo-write-experiment.md](..\..\polaris-research\cdo-write-experiment.md)

### 2026-12 `card_unit_t_70eot` / M2-G0 差量补条目（**静态通过，运行期待验**）

- 改动：CDO 原本**没有** `hasBlitz`，新增 `BoolPropertyData(true)`
- 静态闸：`.uexp` 2804 → 2807（+3）；`.uasset` 4365 → 4382（+17，导出表偏移后移）；重开读值 True；3 个函数仍逐字节一致
- 游戏内预期：该单位获得闪击
- 实际：（待用户在游戏内验证；这是 G0 唯一还没证的一半）
- 结论：**静态通过，运行期待验**

### 2026-12 M2 实现落地（`Polaris.Assets` + `Polaris.Cli`，**静态闸全绿**）

- 实现：`CardAsset`（打开/成员解析/快照/写盘）+ `CdoEditor`（Set/Reset/Apply/Verify）+ `AssetDiff` + `polaris cards list/set/reset`
- 通过方式：`.\run.ps1 verify` → 7 个用例全 PASS
  1. 打开 `card_event_aans`：类名、原生父类 `/Script/kards.BaseCardObject`、CDO 17 条、`kredits`=IntProperty、`tempCard` 判为本类属性
  2. `kredits` 3→4：`.uasset` **0 字节**、`.uexp` **1 字节**，重开值 4，其余字段不变，2 个函数逐字节一致
  3. `hasBlitz=true`（补条目）：`.uexp` **+3**、`.uasset` **+17**，重开 true，3 个函数逐字节一致
  4. 补完再 `Reset`：`.uexp` **完全还原**（0 字节差异）；`.uasset` 只剩 **+17** 的 name map 残留（`hasBlitz` 名字不回收）
  5. **真实 KARDS mod pak 逐字节重建**：`wide-mount.pak`（66,543 B / 2 条目）读入→重建→**0 字节差异**
  6. 改值后直出 pak：`Blueprints/Cards/ANZAC/events/card_event_aans.{uasset,uexp}` 两条目、mount `../../../kards/Content/`，条目字节与保存的资产一致
  7. 错误路径：未知成员 / enum 类型 / 本类属性 / 覆盖输入 都按预期抛错
- 结论：**静态全部通过；G0 运行期仍待游戏内抽验**（`hasBlitz` 那条优先）
