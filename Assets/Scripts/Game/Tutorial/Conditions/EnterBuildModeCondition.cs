using UnityEngine;
namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("건설/건설 모드 진입")]
    public sealed class EnterBuildModeCondition : TutorialCondition
    {
        public override bool Evaluate(TutorialObserver w, int baseline) => w.BuildModeEntered;
        public override string Summary => "건설 모드 진입";
    }
}
