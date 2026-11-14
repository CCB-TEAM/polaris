// G1d：把 card_event_spirit_of_rome.CanPlayFromHand 的「先判断是否步兵」逻辑搬过来：
//   bool infantry; IsInfantry(out infantry);
//   if (infantry) 攻击 +3 else +1
using Polaris.Sdk;
using Polaris.Sdk.Kards;

[PolarisAsset("Blueprints/Cards/Soviet/Allegiance/units/card_unit_t_70eot.uasset")]
public class T70G1d
{
    [PolarisReplace("OnPlayedFromHand")]
    public void OnPlayed(BaseCardObject targetCard)
    {
        bool infantry;
        targetCard.IsInfantry(out infantry);
        if (infantry)
        {
            targetCard.CampaignAddAttack(3, false);
        }
        else
        {
            targetCard.CampaignAddAttack(1, false);
        }

        return;
    }
}
