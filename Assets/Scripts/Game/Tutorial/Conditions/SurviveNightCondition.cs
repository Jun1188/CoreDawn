using UnityEngine;
namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("밤/밤 넘기기")]
    public sealed class SurviveNightCondition : CumulativeCondition
    {
        protected override int Counter(TutorialObserver w) => w.NightsSurvived;
        protected override string Verb => "밤 넘기기";
    }
}
