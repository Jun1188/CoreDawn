using UnityEngine;
namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("기본/점프")]
    public sealed class JumpCondition : CumulativeCondition
    {
        protected override int Counter(TutorialObserver w) => w.JumpCount;
        protected override string Verb => "점프";
    }
}
