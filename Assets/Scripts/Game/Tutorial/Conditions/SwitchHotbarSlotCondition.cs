using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("기본/핫바 칸 바꾸기")]
    public sealed class SwitchHotbarSlotCondition : CumulativeCondition
    {
        protected override int Counter(TutorialObserver w) => w.HotbarSwitches;
        protected override string Verb => "핫바 칸 바꾸기";
    }
}
