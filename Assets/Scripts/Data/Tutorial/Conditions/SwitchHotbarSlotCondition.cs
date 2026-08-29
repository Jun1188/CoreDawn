using UnityEngine;
using CoreDawn.Tutorial;

namespace CoreDawn.Data
{
    [TutorialConditionMenu("기본/핫바 칸 바꾸기")]
    public class SwitchHotbarSlotCondition : CumulativeConditionSO
    {
        protected override int Counter(TutorialObserver w) => w.HotbarSwitches;

        protected override string Verb => "핫바 칸 바꾸기";
    }
}
