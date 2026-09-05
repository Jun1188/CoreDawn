using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("아이템/분류별 소지")]
    public sealed class AcquireItemTypeCondition : TutorialCondition
    {
        /// <summary>이 분류의 아이템을 갖고 있으면 완료 (핫바 + 가방 합산).</summary>
        public ItemType itemType = ItemType.Salvage;
        public int count = 1;
        public override void Configure(TutorialConditionDef def)
        {
            if (!string.IsNullOrEmpty(def.ItemType) && System.Enum.TryParse(def.ItemType, true, out ItemType t)) itemType = t;
            count = Mathf.Max(1, def.Count);
        }
        public override bool Evaluate(TutorialObserver w, int baseline) => TutorialObserver.CountOfType(itemType) >= Mathf.Max(1, count);
        public override string Summary => $"{itemType} {count}개 소지";
    }
}
