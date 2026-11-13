# ADR 0006：UHT 风格 SDK；字节码层自研，不再参考 Solstice 系列

- 状态：**已接受**（用户拍板，2026-12）
- 取代：[ADR 0001](0001-ir-roslyn-source.md) 中「IR = `KNode`/`KArg`」的部分

## 背景

第一版 SDK 生成按 cskismet 的思路走：`KRef`/`KStruct` 标记类 + `throw SdkStub.Call()` 的 getter，
字节码层计划复用 `Solstice.Kismet`（`KArg`/`KNode` 保形模型、`KismetWorkspace.VerifyAll`）。
用户明确否决：

1. **不要 `KArg` 那一套**——它把「UE 的形状」编码进一个私有中间模型，SDK 与编译器都被它绑住；
2. **SDK 要 UHT 风格**——生成的 C# 应像 UHT 产出的头文件：真实继承、`[UProperty]` 字段、`[UFunction]` 方法；
3. **不建议参考 Solstice 系列项目**。

## 决定

### 1. SDK 采用 UHT 风格（已实现）

```csharp
[UClass("/Script/Engine.AISystemBase")]
public partial class AISystemBase : global::Polaris.Sdk.CoreUObject.Object
{
    [UProperty(0x0048000002044201)]
    public global::Polaris.Sdk.CoreUObject.SoftClassPath AISystemClassName;

    [UFunction("FUNC_Final | FUNC_Native | ...")]
    public static int Abs_Int(int A) => throw UhtStub.Call();
}
```

- 类/结构/枚举**继承真实父类**（usmap `super` 链），不再用 `KRef`/`KStruct` 当统一基类；
- 成员是**字段** + `[UProperty(flags)]`（flags 来自 usmap EATR），不是 `=> throw` 的 getter；
- 方法保留 `[UFunction(原始 flags)]`，参数方向 `in/out` 由 jmap `dir` 决定；
- 运行时只有 `UObject`/`UStruct`/`TArray<T>`/`TMap<K,V>`/`FName`/`FText`/`UhtStub` 与四个特性。

### 2. 字节码层自研（进行中）

- 不再引用 `Solstice.Kismet`：`CdoEditor.Verify` 已改成「用 UAssetAPI 把每个函数重新序列化后比 SHA-256」，
  这一步不依赖任何外部字节码库；
- M3/M4 的编译器与反编译器**自己写**：输入是 UHT 风格的 C#（Roslyn `SyntaxTree` + `SemanticModel`），
  输出是 C# 侧的 UHT 声明模型 + 字节码；`KArg`/`KNode` 这类私有 IR **不引入**；
- `UAssetAPI` 仍是资产与 `KismetExpression` 序列化的底座（它不是 Solstice 系列，且 5.6 实测可用）。

### 3. 不参考 Solstice 的具体含义

| 不再做 | 替代 |
|---|---|
| 引用 `Solstice.Kismet.dll` / `KNode` / `KArg` | 自己的 UHT 声明模型 + 编译器 |
| `VerifyAll()` 做「反编译→回编译」 | 函数字节 SHA-256 比对（当前 CDO 路径）；M3 起自己的往返校验 |
| 照搬 `CallShape`/`ContextKind` 等枚举 | 由 UHT 声明（flags/参数方向/归属）直接推导 |
| 照抄其 ubergraph 展开与重烘 | 自己按「存根入口表 + 段」重建（M4） |

## 代价

- **主动放弃 8115/8115 的现成背书**：新字节码层必须重新建立「读→写→逐字节一致」的基准，
  这部分工作量沉到 M3/M4；
- UHT 风格字段会让同名成员/父类成员隐藏产生 CS0108 警告（当前 4 条），可接受；
- 字节码层没有现成反编译器，M3 需要先写自己的指令覆盖与偏移回填。

## 验收

- SDK：485 文件 / 4357 类 / 5349 结构 / 1875 枚举 / 12617 方法 / 33754 成员，`samples/SdkConsumer` 编译**零错误**；
- 生成器：`samples/SdkGenConsumer` 编译期生成同一份并编译**零错误**；
- CDO：`Polaris.Tests` 9 个用例全绿（含函数字节不变的 SHA-256 断言）。
