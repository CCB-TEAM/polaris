// G1b 运行期验证：局部变量 + 算术 + 分支，最终把 x 作为攻击加成。
//   x = 1 -> +2 = 3 -> if (3 > 2) x = 4 else x = 5 -> CampaignAddAttack(4)
// 预期：打出该卡时攻击 +4（与 G1 的 +5 不同，可区分）。
using Polaris.Sdk;
using Polaris.Sdk.Kards;

[PolarisAsset("Blueprints/Cards/Soviet/Allegiance/units/card_unit_t_70eot.uasset")]
public class T70G1b
{
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
