// G1f：指向性卡牌。spirit_of_rome 这类指向卡在 CDO 里把
//   selectTargetOnPlayedFromHand = true
// 我们给 t_70eot 补上这个 CDO 字段，并在打出时对选中的目标生效。
using Polaris.Sdk;
using Polaris.Sdk.Kards;

[PolarisAsset("Blueprints/Cards/Soviet/Allegiance/units/card_unit_t_70eot.uasset")]
[PolarisPatch("selectTargetOnPlayedFromHand", "true")]
public class T70G1f
{
    [PolarisReplace("OnPlayedFromHand")]
    public void OnPlayed(BaseCardObject targetCard)
    {
        bool canIt;
        string reason;
        string reasonParam1;
        string reasonParam2;
        targetCard.CanBeTargetted(out canIt, out reason, out reasonParam1, out reasonParam2, targetCard, true);
        if (canIt)
        {
            targetCard.CampaignAddAttack(2, false);
        }

        return;
    }
}
