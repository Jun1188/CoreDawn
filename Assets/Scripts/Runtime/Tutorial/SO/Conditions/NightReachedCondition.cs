using UnityEngine;

[TutorialConditionMenu("밤/밤 맞이하기")]
public class NightReachedCondition : CumulativeConditionSO
{
    protected override int Counter(TutorialObserver w) => w.NightsStarted;

    protected override string Verb => "밤 맞이";
}
