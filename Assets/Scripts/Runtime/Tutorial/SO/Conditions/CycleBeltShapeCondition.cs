using UnityEngine;

[TutorialConditionMenu("건설/벨트 모양 바꾸기 (T)")]
public class CycleBeltShapeCondition : CumulativeConditionSO
{
    protected override int Counter(TutorialObserver w) => w.BeltShapeCycles;

    protected override string Verb => "벨트 모양 바꾸기";
}
