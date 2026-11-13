# `Polaris.SdkGen/UsmapReader`

**职责**：解析 `.usmap` 二进制（magic `0x30C4`，KARDS 为 version 4），供 SDK 生成与后续类型映射使用。
**对外入口**：`UsmapReader.Read(byte[])`。
**内部结构**：`Reader` 顺序读 header → 名字表 → 枚举 → schema → CEXT 扩展（`PPTH` 补模块路径、`EATR` 补 struct kind）。
**依赖**：无（不用 UAssetAPI，源生成器要能带着它进编译管线）。
**取舍**：只保留 SDK 需要的字段（名字、模块、super、成员类型）；不解析 property flags 之外的内容，不支持压缩的 usmap。

### `Read(data: byte[]): UsmapFile`

**状态：✅ 已实现**（真实 KARDS usmap：9704 schema / 1875 enum / version 4，`/Script/kards.BaseCardObject` 128 成员）

- **签名与契约**：返回 `UsmapFile { Names, Enums, Schemas, Version }`；格式错误抛 `FormatException`，压缩格式抛 `NotSupportedException`。
- **输入输出**：`Schemas` 以 **全路径**（`/Script/Engine.Actor`）为键；无模块路径的 schema 用名字；`Enums` 同时登记短名与全路径。
- **关键分支（伪代码）**：
  ```
  magic == 0x30C4 else throw
  version >= 1: hasVersioning → 跳过 UE4/UE5/custom/NetCL
  compression != 0 → NotSupported
  names: i32 count；version>=2 用 u16 长度，否则 u8；ASCII
  enums: name(i32 名字索引) + 计数 + (version>=4: i64 值+name / 否则序号+name)
  schemas: name super u16 numProps u16 serializable；prop: u16 idx u8 dims name propData
  CEXT: PPTH → 模块路径；EATR → struct kind(1=结构,2=类)
  ```
- **边界与失败**：名字索引越界抛 `FormatException`（带位置）；`ArraySize > 1` 的成员按 UAssetAPI 行为展开成多条（SDK 生成时去重）；`OptionalProperty` 当数组处理。
- **调用者与被调用者**：`SdkEmitter.Emit`、`SdkSourceGenerator`、测试调用；不调用别的项目代码。
