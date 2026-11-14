// 一个「字节码 mod」示例（Polaris 当前形态）：
//   [PolarisAsset] 标目标资产、[PolarisReplace] 标要替换的 UFunction，
//   编译器把方法体翻成 Kismet 字节码 → 写回资产 → 出 mod pak。
// 注意：现在只支持「静态/实例调用 + if/else + return + 字面量」，变量读写还在做。
using Polaris.Sdk;
using Polaris.Sdk.Kards;

[PolarisAsset("Blueprints/Cards/Soviet/Allegiance/units/card_unit_t_70eot.uasset")]
public class T70Mod
{
    // 参数名必须与 UFunction 的参数名一致（OnPlayedFromHand 的 targetCard）
    [PolarisReplace("OnPlayedFromHand")]
    public void OnPlayed(BaseCardObject targetCard)
    {
        targetCard.CampaignAddAttack(1, false);
        return;
    }
}
