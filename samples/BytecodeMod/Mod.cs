// 一个「字节码 mod」示例（Polaris 当前形态）：
//   继承/使用 UHT 风格 SDK 的类型 → 编译器把方法体翻成 Kismet 字节码 → 写回资产 → 出 mod pak。
// 注意：现在只支持「静态/实例调用 + if/else + return + 字面量」，变量读写还在做。
using Polaris.Sdk;
using Polaris.Sdk.Kards;

public class AansMod
{
    // 卡牌事件 OnPlayedFromHand 的参数：targetCard（名字必须与 UFunction 的参数名一致）
    public void OnPlayed(BaseCardObject targetCard)
    {
        targetCard.CampaignAddAttack(1, false);
        return;
    }
}
