# `Polaris.SdkGen/SdkEmitter`

**职责**：把「usmap schema + jmap 函数表」渲染成 **UHT 风格** C# SDK（真实继承 + `[UProperty]` 字段 + `[UFunction]` 方法 + 枚举），同一份输入必得同一份输出。
**对外入口**：`SdkEmitter.Emit(usmap, owners, options)`。
**内部结构**：`Names` 负责命名/命名空间/类型解析（唯一的类型映射点），`Emit` 按 namespace 分三个文件：Enums/Structs/Classes。
**依赖**：`UsmapReader`、`JmapReader`；生成的代码依赖 `Polaris.Sdk` 运行时（UObject/UStruct/TArray/…/特性）。
**取舍**：M1 只生成**原生** SDK；蓝图类基座要等 bmap（[ADR 0003](../decisions/0003-four-inputs-bmap.md)）。包级 owner（`__DelegateSignature`，447 个）暂不生成。

### `Emit(usmap: UsmapFile, owners: IReadOnlyDictionary<string, JmapOwner>, options: SdkOptions?): SdkResult`

**状态：✅ 已实现**（真实数据：485 文件 / 4357 类 / 5349 结构 / 1875 枚举 / 12617 方法 / 19275 成员；两次调用输出逐字节一致）

- **签名与契约**：纯函数；返回 `SdkResult { Files, Classes, Structs, Enums, Methods, Members }`，不写盘。
- **输入输出**：`usmap` 来自 `UsmapReader.Read`；`owners` 来自 `JmapReader.Read`；`options.ModuleFilter` 只保留指定模块（按短名、忽略大小写）。
- **关键分支（伪代码）**：
  ```
  names = Names(usmap, owners, filter)         // 登记 schema/enum/owner，分配 ns + 唯一类型名
  foreach enum:   emit enum（值排序；underlying byte/long）
  foreach schema(StructKind==1): emit class X : <super 或 KStruct> { 成员 }
  classPaths = schema(kind==2) ∪ owner(有函数)
  foreach path: emit class X : KRef { 方法 }
  ```
- **边界与失败**：结构体自引用/类型缺失 → 成员类型退 `KStruct`/`KRef`（不猜）；结构/类/枚举同名冲突 → 同 namespace 内按排序加 `_2` 后缀；成员名与类型同名 → 加 `_`（CS0542）。
- **调用者与被调用者**：`Polaris.Cli sdk` 与 `SdkSourceGenerator` 调用；调用 `Names`（类型渲染）。

## 命名规则（实现事实）

| 项 | 规则 |
|---|---|
| namespace | `/Script/kards` → `Polaris.Sdk.Kards`（模块短名首字母大写，非字母数字 → `_`） |
| 类/结构/枚举名 | schema 短名；C# 关键字加 `@`；数字开头加 `_` |
| 方法 | UE 函数名原样（关键字加 `@`）；与类同名加 `_`；完全同签名重复加 `_N` |
| 静态/实例 | `FUNC_Static` → `static`，否则实例方法 |
| 参数方向 | 只有 `dir`（本 jmap 无 `ref/const`）：`in` → 值，`out` → `out T`，`return` → 返回类型 |
| 方法体 | `=> throw UhtStub.Call();`（声明面，不执行） |

## 验证

- `tests/Polaris.Tests`：解析计数、确定性、`PrintString` 签名（`TextColor` = `LinearColor`）、`BaseCardObject.hasBlitz`。
- `samples/SdkConsumer`：把全量 dump 编进一个 net10 项目，**零错误**（485 文件 / 7.2 MB）。
- `samples/SdkGenConsumer`：同一生成器在编译期产出同一份代码并编译，**零错误**；干净构建 ~31 s，增量 ~2 s。
