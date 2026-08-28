using UnityEngine;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("건설/건설 모드 진입")]
    public class EnterBuildModeCondition : TutorialConditionSO
    {
        public override bool Evaluate(TutorialObserver w, int baseline) => w.BuildModeEntered;

        public override string Summary => "건설 모드 진입";
    }
}
