# 当前数据流向（2026-12）

> 本文只画**已经落地**的链路（✅）和明确未做的部分（🚧），不画设想。

## 1. 输入

```
D:\files\projects\.tmp\polaris\kards.usmap     1.8 MB  9704 schema / 1875 enum
D:\files\projects\jmap-out\functions.json      5.1 MB  1216 owner / 13064 函数
D:\devenv\fmodel\...\Blueprints\*              2137 个资产（每资产 .uasset + .uexp）
```

## 2. 链路 A：SDK 生成（✅）

```
usmap ──► UsmapReader ─┐
                        ├─► SdkEmitter ──► 485 文件 / 4357 类 / 5349 结构 /
functions.json ─► JmapReader ─┘              1875 枚举 / 12617 方法 / 33754 字段
                                         │
                          ┌──────────────┴──────────────┐
                     CLI `polaris sdk`          SdkSourceGenerator
                     （落盘 .tmp/sdk）           （编译期 AdditionalFiles）
                          └──────────────┬──────────────┘
                                         ▼
                        UHT 风格 C# SDK + Polaris.Sdk 运行时
                        （真实继承 / [UProperty] 字段 / [UFunction] 方法）
```

## 3. 链路 B：mod C# → 字节码 → 资产 → pak（✅ 到写回，🚧 运行期）

```
用户 C#（继承 SDK 类，调 SDK 方法/字段）
        │  Roslyn SyntaxTree + SemanticModel
        ▼
  Translator（静态调用 / 实例调用 EX_Context / if-else / return / 字面量）
        │  直接构造 UAssetAPI 的 KismetExpression —— 没有 KArg/KNode 中间层
        ▼
  BytecodeBuilder（标签 → 函数内绝对字节偏移；长度用「序列化一次」测量）
        ▼
  BytecodeCheck（序列化 → 读回 → 指令起始/跳转目标校验）
        ▼
  AssetImportTable（/Script/Module.Class + 函数名 → FPackageIndex；
                    缺失时克隆同类模板追加到 Import 表尾）
        ▼
  FunctionWriter（替换 UFunction.ScriptBytecode）
        ▼
  CardAsset.Save（写 .uasset/.uexp 到新目录，永不覆盖输入）
        ▼
  ModPak（v3/store，mount ../../../kards/Content/；读回 + 确定性重建自检）
        ▼
  游戏内验证（🚧 尚未做）
```

实测锚点：替换 `BP_CardFunctions.AddToBattleLog`（5 → 4 条表达式），其余 **293 个函数 SHA-256 不变**；
实例调用生成 `EX_Context`（字段接收者 → `EX_InstanceVariable`，参数接收者 → `EX_LocalVariable`，`Offset` 非零）。

## 4. 链路 C：CDO 改值（✅ 静态，🚧 运行期）

```
CardAsset.Open（按 RF_ClassDefaultObject 找 CDO；沿 SuperStruct 到 /Script 原生父类）
        │
CdoEditor.Set / Reset（有则改、无则补差量条目、Reset 删条目）
        │
CardAsset.Save ──► 重开比对（字段值）
        │
FunctionBytes.Hashes（每函数字节码序列化后 SHA-256，必须全部不变）
        │
ModPak / 游戏内验证
```

## 5. 模块状态

| 模块 | 状态 |
|---|---|
| `Polaris.Assets`（CardAsset/CdoEditor/AssetDiff/FunctionBytes/FunctionWriter） | ✅ |
| `Polaris.SdkGen`（UsmapReader/JmapReader/SdkEmitter/Json） | ✅ |
| `Polaris.SourceGen`（增量生成器） | ✅ |
| `Polaris.Sdk`（UHT 运行时声明） | ✅ |
| `Polaris.Compiler`（Translator/BytecodeBuilder/BytecodeCheck/AssetImportTable） | ✅ 子集 |
| `Polaris.Pak`（ModPak） | ✅ |
| bmap 扫描 / 蓝图类基座 / 变量读写 / ubergraph 重排 / BP→C# | 🚧 |

## 6. 关键不变量

1. **偏移**：`EX_Jump*` 的操作数是函数内**绝对字节偏移**；长度必须按序列化实测，不能用 UAssetAPI 的 `GetSize`（5.6 的 `FPackageIndex` 算成 8 字节会错）。
2. **导入**：只**追加**到 Import 表尾，既有负索引不变。
3. **最小改动**：只替换目标函数；其余函数字节 SHA-256 必须不变。
4. **输出**：永远写新目录；运行期正确性只由游戏内验证决定。
