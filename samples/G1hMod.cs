// G1h：数组局部变量 + 真正的抽牌（DrawCardsFromDeckBySide 的 out TArray<int>）
using Polaris.Sdk;
using Polaris.Sdk.Kards;

[PolarisAsset("Blueprints/Cards/Soviet/Allegiance/units/card_unit_t_70eot.uasset")]
public class T70G1h
{
    [PolarisReplace("OnPlayedFromHand")]
    public void OnPlayed(BaseCardObject targetCard)
    {
        TArray<int> ids;
        targetCard.cardFunction.DrawCardsFromDeckBySide(0, ESideEnum.left, 2, true, false, out ids, 0.0f);
        return;
    }
}
