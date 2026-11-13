# `Polaris.Pak/ModPak`

**职责**：把编辑产物打成 KARDS 能读的 mod `.pak`（v3 / store），并做读回校验。
**对外入口**：`RelativeContentPath` / `Build` / `BuildAssetPair`。
**内部结构**：路径规范化 → 按名字排序 → `PakWriter.Build`（vendored）→ `PakArchive.Read` 校验 → 确定性重建自检 → 写盘。
**依赖**：`Solstice.Pak`（vendored，见 [VENDORED.md](../../../src/Polaris.Pak/VENDORED.md)）。
**取舍**：**继承而不重写** solstice-pak——它已对真实 pak 做过逐字节复现；本层只补「目录/路径/读回」。
格式证据见下文 §1。

## 1. 实测的 KARDS mod pak 形态

| 项 | 值 | 来源 |
|---|---|---|
| magic / version | `0x5A6F12E1` / **3** | `kards\Content\Paks\wide-mount.pak`、`BP_AsyncLoader.pak` footer |
| mount point | `../../../kards/Content/` | 同上 index |
| 压缩 | 0（store） | 全部条目 `compressionMethod=0` |
| 加密/签名 | 无 | footer 只有 index hash |
| 条目命名 | 相对 mount 的路径，如 `Blueprints/Cards/…/card.uexp` | 同上 |

我们用 vendored `PakWriter` 重建 `wide-mount.pak`（66,543 字节 / 2 条目）**逐字节一致**，见测试。

## 2. 函数

### `RelativeContentPath(filePath: string, contentRoot: string?): string`

**状态：✅ 已实现**

- **签名与契约**：绝对路径 → pak 内相对路径（正斜杠、无前导 `/`）；非法路径抛 `PakPathException`。
- **输入输出**：给了 `contentRoot` 用 `Path.GetRelativePath`；没给则找路径里的 `/Content/` 段取其后缀。返回如 `Blueprints/Cards/ANZAC/events/card_event_aans.uasset`。
- **关键分支（伪代码）**：
  ```
  if contentRoot: rel = GetRelativePath(root, full); if rel 以 .. 开头 → 抛
  else: idx = full.IndexOf("/Content/"); if 无 → 抛; rel = full[idx+9..]
  return Normalize(rel)
  ```
- **边界与失败**：含 `..`/空段/无扩展名的路径一律拒绝；Windows 反斜杠统一成正斜杠（pak 格式要求）。
- **调用者与被调用者**：CLI `cards set --pak` 与 `pak --file` 调用；不调用其他业务代码。

### `Build(files: IEnumerable<PakFile>, pakPath: string, mountPoint: string): BuiltPak`

**状态：✅ 已实现**（生成 `mod.pak` 后读回，条目名/字节一致）

- **签名与契约**：打包 → 读回 → 确定性自检 → 写盘；返回 `BuiltPak(路径, 字节, 归档)`。空集合或重复路径抛 `PakPathException`。
- **输入输出**：`files` 的 `RelativePath` 必须已规范化；`pakPath` 目录自动创建；`mountPoint` 默认 `../../../kards/Content/`。
- **关键分支（伪代码）**：
  ```
  entries = files.Select(Normalize).OrderBy(Name)
  if 空/重复 → 抛
  bytes = PakWriter.Build(mount, entries)
  archive = PakArchive.Read(bytes)                    // 校验 SHA-1/边界
  if PakWriter.Build(archive.MountPoint, archive.Entries) != bytes → 抛（非确定性）
  File.WriteAllBytes(pakPath, bytes)
  ```
- **边界与失败**：只支持 store 模式（KARDS mod 实测如此）；要压缩/加密得换 writer，本层不支持。
- **调用者与被调用者**：CLI、`BuildAssetPair`、测试调用；调用 `Solstice.Pak.PakWriter` / `PakArchive`。

### `BuildAssetPair(sourceUassetPath: string, contentRoot: string, uasset: byte[], uexp: byte[], pakPath: string, mountPoint: string): BuiltPak`

**状态：✅ 已实现**（测试用例「改值后直接出 mod pak」）

- **签名与契约**：一个资产的 `.uasset`+`.uexp` → 按源路径相对 `contentRoot` 打包；.uexp 名字由 `.uasset` 改扩展名得到。
- **输入输出**：源路径不要求在 `contentRoot` 下存在（只用来算相对路径）；`uasset/uexp` 是**改后的字节**。
- **关键分支（伪代码）**：`rel = RelativeContentPath(source, root); entries = [(rel, uasset), (ChangeExt(rel,".uexp"), uexp)]`。
- **边界与失败**：源路径不在 root 下 → `PakPathException`；字节为空的条目允许（但会记录长度 0）。
- **调用者与被调用者**：CLI 未来用；调用 `Build`。

## 3. 怎么用（CLI）

```powershell
# 改值 + 直接出 pak（一条命令）
polaris cards set --asset <卡.uasset> --usmap <kards.usmap> `
  --set kredits=4 --out out --pak mod.pak

# 已有产物 → 打包
polaris pak --pak mod.pak --file <绝对路径> [--file ...] [--content-root <...\kards\Content>]
```

输出里的 `mount` 与条目名就是 pak 里真实写的东西。
