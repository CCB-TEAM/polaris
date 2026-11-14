// G1g：通过 BaseCardObject.cardFunction（CardFunctionsStub 实例）调用「难调用」的卡函数。
// 参考真实卡：cardFunction 是成员变量，指向本卡的函数桩实例。
using Polaris.Sdk;
using Polaris.Sdk.Kards;

[PolarisAsset("Blueprints/Cards/Soviet/Allegiance/units/card_unit_t_70eot.uasset")]
public class T70G1g
{
    [PolarisReplace("OnPlayedFromHand")]
    public void OnPlayed(BaseCardObject targetCard)
    {
        // 1) 把刚打出的单位收回手牌（可见、无需 out 数组）
        targetCard.cardFunction.MoveUnitFromBoardToOwnersHand(targetCard, 0);

        // 2) 抽牌 API 调用形状（按 ID 抽指定牌；ID 不存在时是 no-op）
        targetCard.cardFunction.DrawSpecificCardFromDeckBySide(0, targetCard.CardID, ESideEnum.left, true);

        return;
    }
}
