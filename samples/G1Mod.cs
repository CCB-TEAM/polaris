// G1 运行期验证用 mod：
//   [PolarisPatch] 把费用改成 0；[PolarisReplace] 把 OnPlayedFromHand 换成打出时攻击 +5。
// 生成命令见 docs/verification/g1-card-unit-t70eot.md。
using Polaris.Sdk;
using Polaris.Sdk.Kards;

[PolarisAsset("Blueprints/Cards/Soviet/Allegiance/units/card_unit_t_70eot.uasset")]
[PolarisPatch("kredits", "0")]
public class T70G1
{
    [PolarisReplace("OnPlayedFromHand")]
    public void OnPlayed(BaseCardObject targetCard)
    {
        targetCard.CampaignAddAttack(5, false);
        return;
    }
}
