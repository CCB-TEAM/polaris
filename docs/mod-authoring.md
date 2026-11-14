# 写一个 Polaris mod（从零到进游戏）

本文只讲**当前可用**的流程。命令里的路径按本机示例写，换成你的即可。

## 0. 一次性准备

```powershell
.\run.ps1 build          # 构建工具链
.\run.ps1 sdk-jmap       # 纯 jmap -> ..\.tmp\polaris\sdk-jmap\Polaris.Sdk（485 个 .cs）
```

需要的数据：

| 输入 | 例子 |
|---|---|
| 纯 jmap | `D:\files\projects\kards\Content\Paks\kards1.60_No_UE4SS.jmap` |
| usmap（卡资产读写） | `D:\files\projects\.tmp\polaris\kards.usmap` |
| Content 根 | `D:\devenv\fmodel\Output\Exports\kards\Content` |

## 1. 写 Mod.cs

```csharp
using Polaris.Sdk;
using Polaris.Sdk.Kards;

[PolarisAsset("Blueprints/Cards/Soviet/Allegiance/units/card_unit_t_70eot.uasset")]
[PolarisPatch("kredits", "0")]                        // CDO：费用 2 -> 0
public class T70Mod
{
    // 方法参数名必须与目标 UFunction 的参数名一致
    [PolarisReplace("OnPlayedFromHand")]
    public void OnPlayed(BaseCardObject targetCard)
    {
        int x = 1;
        x = x + 2;
        if (x > 2) { x = x + 1; } else { x = x + 2; }
        targetCard.CampaignAddAttack(x, false);
        return;
    }
}
```

规则：

- `[PolarisAsset]` 标在类上，路径相对 `Content`、`/Game/...` 或绝对路径都行
- `[PolarisReplace("函数名")]` 标方法：整个函数体被替换
- `[PolarisPatch("字段", "值")]` 可重复：CDO 改值；空字符串表示 Reset
- 方法参数名必须和目标函数参数名**完全一致**（apply 会校验并报错）
- 当前 C# 子集：静态/实例调用、赋值、变量实参、`out/ref`、局部变量（int/bool/float/double/long/string）、`+ - * / %`、比较、`& | ^ << >>`、字符串 `+ == !=`、`&& ||`、`-x`、`!x`、`if/else`、`return 表达式`

## 2. 先 dry-run

```powershell
dotnet run --project src\Polaris.Cli -- apply `
  --mod Mod.cs --content <Content> --usmap <kards.usmap> `
  --sdk-source ..\.tmp\polaris\sdk-jmap\Polaris.Sdk `
  --out out --dry-run
```

会编译 + 自校验 + 校验参数名与 CDO 字段，但**不写盘**。

## 3. 正式写回 + 出 pak

```powershell
dotnet run --project src\Polaris.Cli -- apply `
  --mod Mod.cs --content <Content> --usmap <kards.usmap> `
  --sdk-source ..\.tmp\polaris\sdk-jmap\Polaris.Sdk `
  --out out --pak out\mod.pak
```

输出：

```
mod      card_unit_t_70eot.uasset :: OnPlayedFromHand（9 条）
patch    card_unit_t_70eot.uasset :: kredits = 0
pak      out\mod.pak（8088 字节 / 2 条目）
manifest out\mod.json
ok       1 个函数写回，1 个 CDO 字段
```

- 多资产 mod：`--pak` 传目录，每张卡一个 `<资产名>.pak`
- 写回后自动校验：其余函数字节不变、被替换函数往返一致、CDO 字段生效

## 4. 验收（进游戏前）

```powershell
# 改了什么
polaris diff --before <原卡> --after out\card_unit_t_70eot.uasset --usmap <kards.usmap>

# 写回后的资产仍能无损往返
polaris check --asset out\card_unit_t_70eot.uasset --usmap <kards.usmap>
```

## 5. 进游戏

1. 关闭游戏
2. 把 `mod.pak` 放进 `<游戏目录>\KARDS\Content\Paks\`
3. 启动游戏，验证行为
4. 回滚：删掉 pak，重启

注意：**同一张卡的多个 mod pak 会冲突**，一次只放一个。

## 6. 常见错误

| 现象 | 原因 / 处理 |
|---|---|
| `mod 参数 X 在目标函数里不存在` | 参数名必须与 UFunction 参数名一致（错误信息会列出现有参数） |
| `找不到函数 Y` | `[PolarisReplace]` 名字必须是资产里真实存在的 UFunction |
| `CDO 字段校验失败` | `[PolarisPatch]` 字段名/类型不匹配；先用 `cards list` 看字段 |
| `其他函数被改动` | 不应发生；说明写回路径有 bug，保留输出目录以便排查 |
| 游戏内无变化 | 先确认 pak 放对目录/没被别的 pak 覆盖；再用 `diff` 确认产物 |

## 7. 当前边界

- 只替换**平坦函数**；ubergraph 段重排（改事件体但保留原逻辑）还在 M4，被偏移语义阻塞
- 不新增资产、不新增蓝图类
- 运行期已验证：CDO 改值、平坦函数替换（G1）；局部变量/分支/算术的运行期验证见 G1b
