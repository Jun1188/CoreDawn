using UnityEngine;
using CoreDawn.Tutorial;

namespace CoreDawn.Data
{
    [TutorialConditionMenu("밤/밤 넘기기")]
    public class SurviveNightCondition : CumulativeConditionSO
    {
        protected override int Counter(TutorialObserver w) => w.NightsSurvived;

        protected override string Verb => "밤 넘기기";
    }
}
