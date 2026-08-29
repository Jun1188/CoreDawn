using UnityEngine;
using CoreDawn.Tutorial;

namespace CoreDawn.Data
{
    [TutorialConditionMenu("건설/건물 철거")]
    public class DemolishBuildingCondition : CumulativeConditionSO
    {
        protected override int Counter(TutorialObserver w) => w.DemolishedCount;

        protected override string Verb => "건물 철거";
    }
}
