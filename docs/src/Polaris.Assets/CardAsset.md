# `Polaris.Assets/CardAsset`

**职责**：把一张卡的 `.uasset` 打开成「类 + CDO + 原生父类 schema」的只读模型，供 CDO 覆盖使用。
**对外入口**：`Open` / `ResolveMember` / `Snapshot` / `Save`。
**内部结构**：`Open` 打开资产 → 找 `ClassExport` 与 CDO 导出 → 沿 `SuperStruct` 走到原生父类 → 从 usmap 取 schema 链。
**依赖**：UAssetAPI 1.1.0（`UAsset`/`ClassExport`/`NormalExport`/`Usmap`）。
**取舍**：M2 只支持**原生父类**成员（父类 import 指向 `/Script/*`）；父类是 `/Game` 蓝图资产时明确报错，不猜。

### `Open(uassetPath: string, usmapPath: string, workDir: string?): CardAsset`

**状态：✅ 已实现**（`tests/Polaris.Tests` 用 `card_event_aans` / `card_unit_t_70eot` 实测通过）

- **签名与契约**：读资产与 usmap；失败抛 `NotACardAssetException`（没有 `ClassExport`）、`IOException`（文件缺失）。只读，不写盘。
- **输入输出**：必须存在同名 `.uexp`；usmap 由 UAssetAPI 以读写模式打开，实现里先复制到临时目录。返回对象持有 `UAsset`、`ClassExport`、`NormalExport Cdo`、usmap。
- **关键分支（伪代码）**：
  ```
  if !File.Exists(uasset) or !File.Exists(uexp): throw
  usmap = new Usmap(copyToWritable(usmapPath))
  asset = new UAsset(uasset, VER_UE5_6, usmap)
  cls = asset.Exports.OfType<ClassExport>().FirstOrDefault() else throw NotACardAsset
  cdo = asset.Exports.OfType<NormalExport>().FirstOrDefault(RF_ClassDefaultObject) else throw
  if cls.ClassDefaultObject 指向的不是 cdo: throw（模型不一致）
  ```
- **边界与失败**：实测卡资产是 `Export[0]=class, Export[1]=CDO`，但实现**不**用硬编码下标；`BP_CardFunctions` 这类非卡类只要也有 `ClassExport`+CDO 也能打开（成员解析取决于父类）。
- **调用者与被调用者**：`Polaris.Cli cards` 与测试调用；调用 `ResolveNativeParent`、`Usmap.Schemas`。

### `ResolveMember(name: string): NativeMember`

**状态：✅ 已实现**（`attack/kredits/hasBlitz` 等实测；未知名字报错并列出候选）

- **签名与契约**：在本类 `LoadedProperties` 与原生父类 schema 链里找成员，返回 `Name/PropertyType/DeclaringClass`；找不到抛 `MemberNotFoundException`（列出该链上所有成员）。
- **输入输出**：`PropertyType` 是 usmap 的类型名（`IntProperty`/`BoolProperty`/…）；本类属性（如 `tempCard`）也算成员，但 M2 只允许改原生父类的。
- **关键分支（伪代码）**：
  ```
  own = cls.LoadedProperties.FirstOrDefault(Name==name)  // 本类
  if own: return (name, own.GetType, cls.ObjectName)
  schema = ResolveNativeParent()          // /Script/xxx.Parent
  for s in [schema, schema.SuperType...]:
      p = s.Properties.FirstOrDefault(Name==name)
      if p: return (name, p.Type, s.Name)
  throw MemberNotFound(candidates)
  ```
- **边界与失败**：父类若是 `/Game` BP → 抛 `UnsupportedParentException`（M2 不做跨包 BP）。名字大小写敏感（与 UE 一致）。
- **调用者与被调用者**：`CdoEditor.Set/Reset` 调用；调用 `Usmap.Schemas`。

### `Snapshot(): IReadOnlyDictionary<string, string>`

**状态：✅ 已实现**

- **签名与契约**：把 CDO 当前所有属性渲染成 `名字 → 字符串`（int/bool/float/enum 带类型值），用于写后比对。
- **输入输出**：只读；值渲染保持稳定（bool 用 `True/False`，enum 用 FName 文本）。
- **关键分支（伪代码）**：`foreach d in Cdo.Data: map[d.Name] = Render(d)`。
- **边界与失败**：结构体/数组渲染成类型名 + 元素数（M2 不改它们）；顺序按 `Data` 顺序，用于发现意外重排。
- **调用者与被调用者**：`CdoEditor.Verify`、测试与 CLI `cards list` 调用。

### `Save(outDir: string, fileName: string?): SavedAsset`

**状态：✅ 已实现**

- **签名与契约**：写到 `outDir/<原文件名>`（两半），**永不覆盖输入**；返回路径与字节。
- **输入输出**：`outDir` 必须存在或可创建；若解析后等于输入目录且同名 → 拒绝。
- **关键分支（伪代码）**：`Directory.CreateDirectory(outDir); asset.Write(outPath); return bytes`。
- **边界与失败**：写失败抛 `IOException`；写后校验由 `CdoEditor.Verify` 负责，不在本方法里偷偷做。
- **调用者与被调用者**：`CdoEditor.Apply` 与 CLI 调用；调用 `UAsset.Write`。
