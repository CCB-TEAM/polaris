# `Polaris.Sdk/Runtime`

**职责**：生成的 SDK 代码引用的最小运行时类型（KRef / KStruct / KArray / FName / FText / 属性 / 占位异常）。
**对外入口**：类型本身 + `SdkStub.Call()`。
**内部结构**：全部是**标记/声明**，没有反射、序列化或 UE 语义；真正的语义在 `Polaris.Compiler` 里。
**依赖**：无。
**取舍**：故意不做成「能跑」的运行时——生成的属性 getter/方法都是 `throw SdkStub.Call()`，误执行会立刻暴露。

### `SdkStub.Call(): Exception`

**状态：✅ 已实现**（所有生成的方法体/属性 getter 都走它）

- **签名与契约**：返回一个 `InvalidOperationException`（供 `throw`），无副作用。
- **输入输出**：无参数；消息固定说明「声明面被执行」。
- **关键分支（伪代码）**：`return new InvalidOperationException("...")`。
- **边界与失败**：永远返回异常对象，不会返回 `null`；调用方必须 `throw`。
- **调用者与被调用者**：生成的 SDK 方法/属性调用；不调用别人。

## 类型清单

| 类型 | 对应 |
|---|---|
| `KRef` | UObject 引用（生成的 `<Class>Ref : KRef`） |
| `KStruct` | 结构体值（生成的 `<Struct> : KStruct`） |
| `KArray<T>` / `KSet<T>` / `KMap<K,V>` | TArray / TSet / TMap |
| `FName` / `FText` / `FFieldPath` / `KDelegate` | 对应 UE 类型 |
| `UClass` / `UStruct` / `UEnum` / `UFunction` 特性 | 生成代码上的 UE 身份/flags 标注 |
