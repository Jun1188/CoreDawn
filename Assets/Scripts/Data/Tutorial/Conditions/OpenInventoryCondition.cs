using UnityEngine;
using CoreDawn.Tutorial;

namespace CoreDawn.Data
{
    [TutorialConditionMenu("기본/인벤토리 열기")]
    public class OpenInventoryCondition : TutorialConditionSO
    {
        public override bool Evaluate(TutorialObserver w, int baseline) => w.InventoryOpened;

        public override string Summary => "인벤토리 열기";
    }
}
