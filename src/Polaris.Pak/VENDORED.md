# 来源

`PakEntry.cs` / `PakWriter.cs` / `PakArchive.cs` 逐字复制自 **CCB-TEAM/solstice-pak**（MIT，见 `LICENSE-solstice-pak.txt`）：

- 上游：`D:\files\projects\out-repos\solstice-pak\src\Solstice.Pak\`
- 命名空间保持 `Solstice.Pak`，不改上游代码，方便以后同步
- 上游只支持 **magic `0x5A6F12E1` / version 3 / store 模式**——这正是 KARDS 的 mod pak 格式
  （实测 `kards\Content\Paks\wide-mount.pak` 与 `BP_AsyncLoader.pak` 都是 v3、mount `../../../kards/Content/`、compression=0）

Polaris 在这一层之上加 `ModPak`（本项目自己写）：目录/文件 → 相对路径规范化 → 打包 → 读回校验。
