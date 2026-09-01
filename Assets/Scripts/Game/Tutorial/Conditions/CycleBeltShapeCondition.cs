using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("건설/벨트 모양 바꾸기 (T)")]
    public sealed class CycleBeltShapeCondition : CumulativeCondition
    {
        protected override int Counter(TutorialObserver w) => w.BeltShapeCycles;
        protected override string Verb => "벨트 모양 바꾸기";
    }
}
