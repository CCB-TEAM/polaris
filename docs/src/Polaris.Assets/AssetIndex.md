# `Polaris.Assets/AssetIndex`

**职责**：把资产目录扫成 `bmap.json`（类清单、父链、成员、函数、CDO、ubergraph 入口表）。
**对外入口**：`AssetIndex.Scan(IReadOnlyList<string> assetPaths, string usmapPath) → AssetIndexResult`。
**内部结构**：`Scan` 遍历资产 → `ReadClass` 读导出表 → `ReadCdo` 读默认值 → `ReadUbergraphEntries` 扫存根；
结果按 `ObjectPath` 归并，跨包父链递归。
**依赖**：UAssetAPI 1.1.0、`Solstice.Kismet`（只读反编译与入口扫描）。
**取舍**：bmap 是**快照**而非实时视图——生成器要可缓存的纯输入；过期由 `polaris check` 的内容哈希兜底（[ADR 0003](../decisions/0003-four-inputs-bmap.md)）。

### `Scan(assetPaths: IReadOnlyList<string>, usmapPath: string): AssetIndexResult`

**状态：🚧 未实现**

- **签名与契约**：逐资产读 `.uasset`+`.uexp`，返回 `{Classes, Members, Functions, CdoValues, UbergraphEntries, Errors}`；无写操作。
- **输入输出**：资产路径必须成对存在；usmap 会被 UAssetAPI 以读写模式打开，实现里先复制到工作目录。返回的 `Errors` 是 `(asset, stage, message)` 列表，**不吞异常**。
- **关键分支（伪代码）**：
  ```
  for each asset:
      if 没有同名 .uexp: error("缺 uexp"); continue
      try open → 找 ClassExport
      if 无 ClassExport: 记 NonClass（贴图/枚举/结构），continue
      读 SuperStruct（负数=Import，正数=本包导出）
      读本类 LoadedProperties、每个 FunctionExport、CDO 导出
      扫 ExecuteUbergraph_* 的调用点得 入口表
      若 SuperStruct 指向 /Game 且未扫过: 递归 Scan(父包)
  ```
- **边界与失败**：2053+28 个资产里已有 4 个无字节码函数（`E_PreGameStates` 等）与数据型蓝图，必须正常产出空函数表；assets 只读目录可接受（UAssetAPI 只对 usmap 有写需求）。
- **调用者与被调用者**：`Polaris.Cli scan` / MSBuild target 调用；它调用 `ReadClass`、`ReadCdo`、`ReadUbergraphEntries`、`Solstice.Kismet.KismetWorkspace.Open`。

### `ReadUbergraphEntries(ws: KismetWorkspace, className: string): IReadOnlyDictionary<string,int>`

**状态：🚧 未实现**

- **签名与契约**：返回「存根函数名 → 入口字节偏移」；只读，不修改资产。
- **输入输出**：`className` 形如 `card_unit_t_70eot_C`；ubergraph 名由存根字节码里的 `FunctionRef` 反查，返回空表表示该类没有 ubergraph。
- **关键分支（伪代码）**：
  ```
  for each FunctionExport f where f.ScriptBytecode 含 LocalFinal(ExecuteUbergraph_*):
      if 实参是 EX_IntConst n: map[f.Name] = (uint)n
      else: warn("存根偏移不是常量，跳过")     // 理论上不存在
  ```
- **边界与失败**：实测单事件卡入口 = 段首（`card_event_aans` 10），多事件卡入口落在段内（`card_unit_t_70eot` 272/388）——**不要**假设入口 = 段首，也不要假设入口是表达式下标的倍数。
- **调用者与被调用者**：`Scan` 调用；内部调用 `KismetWorkspace.UbergraphCallSites` 或直接反编译存根。

### `ReadCdo(asset: UAsset, classExport: ClassExport): CdoSnapshot`

**状态：🚧 未实现**

- **签名与契约**：读 `ClassDefaultObject` 指向的导出，返回「属性名 → 类型 + 值」快照；只读。
- **输入输出**：CDO 导出带 `RF_ClassDefaultObject`（实测 `Default__card_unit_t_70eot_C` 31 条属性）；无 CDO 时返回空快照并记 `NonCdo`。`TextPropertyData`/`SoftObjectPropertyData`/容器原样保留为字符串描述，不做解释。
- **关键分支（伪代码）**：
  ```
  if classExport.ClassDefaultObject is null: return empty
  cdo = asset.Exports[idx-1]
  if cdo is not NormalExport: return empty + error("CDO 不是 NormalExport")
  for p in cdo.Data: add(p.Name, p.GetType().Name, Render(p))
  ```
- **边界与失败**：CDO 的字节是**无版本属性块**（无属性名），UAssetAPI 已解析成 `PropertyData`；写回时 `FUnversionedHeader` 会重算。**未解码的位布局**风险见 [ue-baking-semantics §附录 B](../../../polaris-research/ue-baking-semantics.md)。
- **调用者与被调用者**：`Scan` 调用；被 `ClassPatcher.WriteCdoValue` 用来做基线比对。

### `Dump(bmap: AssetIndexResult, path: string): void`

**状态：🚧 未实现**

- **签名与契约**：把结果写成确定性 JSON（键排序、无时间戳），相同输入必得相同字节。
- **输入输出**：`path` 可写；失败抛 `IOException` 由 CLI 转成非 0 退出码。
- **关键分支（伪代码）**：`Serialize(sortedKeys) → WriteAllText`；`Debug` 模式下同时写 `bmap.stats.json`。
- **边界与失败**：大于 100 MB 时拒绝写（防止把整张地图的资产误扫进来）。
- **调用者与被调用者**：`scan` 命令调用；被 `polaris check` 读回做哈希对比。
