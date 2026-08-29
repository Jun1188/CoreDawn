using UnityEngine;
using CoreDawn.Tutorial;

namespace CoreDawn.Data
{
    [TutorialConditionMenu("자원/자원 캐기")]
    public class MineResourceCondition : CumulativeConditionSO
    {
        protected override int Counter(TutorialObserver w) => w.MinedTotal;

        protected override string Verb => "자원 캐기";
    }
}
