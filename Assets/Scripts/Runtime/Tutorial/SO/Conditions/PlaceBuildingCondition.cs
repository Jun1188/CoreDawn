using UnityEngine;

[TutorialConditionMenu("건설/건물 설치")]
public class PlaceBuildingCondition : CumulativeConditionSO
{
    protected override int Counter(TutorialObserver w) => w.PlacedCount;

    protected override string Verb => "건물 설치";
}
