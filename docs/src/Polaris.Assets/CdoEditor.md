# `Polaris.Assets/CdoEditor`

**职责**：按「原生父类成员覆盖」语义改 CDO：有则改、无则补、`Reset` 则删；每次落盘前后都有断言。
**对外入口**：`Set` / `Reset` / `Apply` / `Verify`。
**内部结构**：`Set/Reset` 只改内存；`Apply` 串起来 → `CardAsset.Save` → `Verify` 重开比对 + 函数字节校验。
**依赖**：`CardAsset`、UAssetAPI 的 `PropertyData` 子类。
**取舍**：M2 支持 `int/bool/float`；`enum/text/struct/容器` 明确报错（M7 再做）。**不猜默认值**——原生父类的默认值在二进制里，离线拿不到，所以「等于默认」用 `Reset`（删条目）表达，而不是比较后自动删。

### `Set(card: CardAsset, name: string, value: string): CdoPatch`

**状态：✅ 已实现**（`kredits=4` 改值、`hasBlitz=true` 补条目实测）

- **签名与契约**：改 CDO 里同名属性；不存在则按 schema 类型新增；返回 `(Name, Action=Updated|Added, Before, After)`。类型不符/无法解析抛 `CdoValueException`。
- **输入输出**：`value` 为文本：int 十进制、bool `true/false`（不区分大小写）、float 用不变文化解析。名字必须是 `CardAsset.ResolveMember` 能找到的成员。
- **关键分支（伪代码）**：
  ```
  m = ResolveMember(name)
  if m 类型 not in {Int,Bool,Float}: throw UnsupportedType
  d = Cdo.Data.FirstOrDefault(Name==name)
  if d != null:
      if d 的类型与 m 不符: throw TypeMismatch
      d.Value = Parse(m.Type, value);  action = Updated
  else:
      Cdo.Data.Add(NewProperty(m.Type, name, value)); action = Added
  ```
- **边界与失败**：新增条目会改变导出长度（实测 +3 字节 uexp / +17 字节 uasset），不影响既有字节码；`value` 为空串、`int` 溢出都会抛错而不是截断。
- **调用者与被调用者**：`Apply` 与 CLI `cards set` 调用；调用 `CardAsset.ResolveMember`、`CreateProperty`。

### `Reset(card: CardAsset, name: string): CdoPatch`

**状态：✅ 已实现**（补 `hasBlitz` 后再 `Reset` 回到原字节，见测试）

- **签名与契约**：删掉 CDO 里的该条目（= 回到原生父类默认值）；不存在则 `Action=NoOp`。
- **输入输出**：返回 `(Name, NoOp|Removed, Before, After=default)`。
- **关键分支（伪代码）**：
  ```
  d = Cdo.Data.FirstOrDefault(Name==name)
  if d == null: return NoOp
  Cdo.Data.Remove(d); return Removed
  ```
- **边界与失败**：删掉的是「差量条目」，不会删类上的 `FProperty`；删错名字时 `ResolveMember` 仍会先校验。
- **调用者与被调用者**：`Apply` 与 CLI `cards reset` 调用。

### `Apply(card: CardAsset, patches: IReadOnlyList<PatchSpec>, outDir: string, workDir: string): EditReport`

**状态：✅ 已实现**

- **签名与契约**：按顺序执行 patch → `Save` → `Verify`；任一校验失败抛 `VerificationFailedException`（报告里带失败项），并把输出目录标记为不可用。
- **输入输出**：`EditReport` 含 patches、`SavedAsset`、`VerifyReport`（重开快照 diff、函数校验结果、字节 diff 摘要）。
- **关键分支（伪代码）**：
  ```
  foreach p in patches: Set 或 Reset
  saved = Save(outDir)
  verify = Verify(originalSnapshot, expected, saved)
  if !verify.Ok: throw
  ```
- **边界与失败**：写盘后才发现函数不一致时，**保留输出文件供诊断**（不静默删），但报告明确失败。
- **调用者与被调用者**：CLI `cards set/reset` 调用；调用 `Set`、`Reset`、`CardAsset.Save`、`Verify`。

### `Verify(card: CardAsset, before: IReadOnlyDictionary<string,string>, patches: IReadOnlyList<PatchSpec>, saved: SavedAsset, workDir: string): VerifyReport`

**状态：✅ 已实现**

- **签名与契约**：重开输出资产，检查：①所有未被 patch 的 CDO 字段与原始一致；②被 patch 的字段等于目标值；③**全部函数的字节码 SHA-256 不变**（`FunctionBytes`，不依赖外部字节码库）；④给出字节 diff 摘要。
- **输入输出**：`VerifyReport(Ok, CdoDiffs, FunctionFailures, ByteDiff)`；`Ok` 为三者合取。
- **关键分支（伪代码）**：
  ```
  re = CardAsset.Open(saved, usmap)
  for kv in original: if kv.Key 未 patch 且 re.Snapshot()[k] != v: add diff
  foreach (k,v) in expected: if re.Snapshot()[k] != v: add diff
  before = FunctionBytes.Hashes(card.Asset);   after = FunctionBytes.Hashes(re.Asset)
  if before != after: 失败
  ```
- **边界与失败**：函数校验依赖 UAssetAPI 的表达式序列化；序列化抛异常时进 `Failures`，**不允许**静默跳过这道闸。
- **调用者与被调用者**：`Apply` 调用；调用 `CardAsset.Open`、`KismetWorkspace.VerifyAll`、`AssetDiff.Compare`。
