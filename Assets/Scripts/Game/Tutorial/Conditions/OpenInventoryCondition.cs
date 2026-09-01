using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("기본/인벤토리 열기")]
    public sealed class OpenInventoryCondition : TutorialCondition
    {
        public override bool Evaluate(TutorialObserver w, int baseline) => w.InventoryOpened;
        public override string Summary => "인벤토리 열기";
    }
}
