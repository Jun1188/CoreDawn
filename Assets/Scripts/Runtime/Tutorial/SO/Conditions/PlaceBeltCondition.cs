using UnityEngine;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("건설/컨베이어 설치")]
    public class PlaceBeltCondition : CumulativeConditionSO
    {
        protected override int Counter(TutorialObserver w) => w.PlacedBelts;

        protected override string Verb => "컨베이어 설치";
    }
}
