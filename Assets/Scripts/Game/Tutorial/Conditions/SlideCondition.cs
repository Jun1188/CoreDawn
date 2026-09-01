using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("기본/슬라이딩")]
    public sealed class SlideCondition : CumulativeCondition
    {
        protected override int Counter(TutorialObserver w) => w.SlideCount;
        protected override string Verb => "슬라이딩";
    }
}
