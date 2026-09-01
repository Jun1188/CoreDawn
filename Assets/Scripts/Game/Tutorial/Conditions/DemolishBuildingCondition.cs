using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("건설/건물 철거")]
    public sealed class DemolishBuildingCondition : CumulativeCondition
    {
        protected override int Counter(TutorialObserver w) => w.DemolishedCount;
        protected override string Verb => "건물 철거";
    }
}
