using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("건설/건물 설치")]
    public sealed class PlaceBuildingCondition : CumulativeCondition
    {
        protected override int Counter(TutorialObserver w) => w.PlacedCount;
        protected override string Verb => "건물 설치";
    }
}
