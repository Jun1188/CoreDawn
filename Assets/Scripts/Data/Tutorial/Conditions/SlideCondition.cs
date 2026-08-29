using UnityEngine;
using CoreDawn.Tutorial;

namespace CoreDawn.Data
{
    [TutorialConditionMenu("기본/슬라이딩")]
    public class SlideCondition : CumulativeConditionSO
    {
        protected override int Counter(TutorialObserver w) => w.SlideCount;

        protected override string Verb => "슬라이딩";
    }
}
