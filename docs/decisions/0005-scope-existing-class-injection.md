# ADR 0005：写入范围止于「既有类」，首期注入形式是既有卡类新增函数

- 状态：**已接受**（用户拍板，2026-12）

## 决定

Polaris 的写入范围（按用户选择）：

| 层 | 是否做 | 说明 |
|---|---|---|
| 既有函数的字节码 | ✅ | 平坦函数整函数重生成；事件方法进 ubergraph（规范化重烘 + 回填存根） |
| CDO 默认值（既有字段） | ✅ | `int kredits = 3;` → `Default__<Class>_C` 的 `PropertyData` |
| 新成员 | ✅ | 写类 `LoadedProperties` + `Children`；默认值写 CDO |
| 新函数（注入到**既有卡类**） | ✅ | 追加 `FunctionExport` + 挂 `Children`，事件则同时加存根与 ubergraph 段 |
| 新类 / 新资产（新卡） | ⛔ | 不做。新卡需要贴图/本地化/卡池注册，是另一个项目 |

「注入 Polaris 的类」在首期落地为：**在既有卡类的 `BlueprintGeneratedClass` 上新增 UFunction**，
不新建 UClass、不新建包。

## 为什么不是「新类」

- 新类 = 新包 + 新 `BlueprintGeneratedClass` + CDO + 全部导入引用，等于「资产制造」。
- 评估把它列为风险最高的一层（G2 闸才验证 `FindFunctionByName` 能否认出新增函数），
  且 KARDS 的卡池注册在外部数据里，光有资产不能出卡。

## 代价

- 不能「凭空造一张新卡」——需要时用「复制一张现有卡改 CDO + 字节码」的外部流程，
  Polaris 只提供改的部分（未来再谈）。
- 新增函数后运行时能否被 `FindFunctionByName`/BP VM 找到，必须在 M6 用 G4 闸实测；
  不通过则回退为「只改既有函数 + CDO」。
