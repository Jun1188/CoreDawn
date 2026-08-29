using UnityEngine;
using CoreDawn.Tutorial;

namespace CoreDawn.Data
{
    [TutorialConditionMenu("기본/점프")]
    public class JumpCondition : CumulativeConditionSO
    {
        protected override int Counter(TutorialObserver w) => w.JumpCount;

        protected override string Verb => "점프";
    }
}
