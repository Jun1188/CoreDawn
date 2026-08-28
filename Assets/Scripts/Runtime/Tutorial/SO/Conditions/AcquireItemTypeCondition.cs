using UnityEngine;
using CoreDawn.Factory;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("아이템/분류별 소지")]
    public class AcquireItemTypeCondition : TutorialConditionSO
    {
        [Tooltip("이 분류의 아이템을 갖고 있으면 완료 (핫바 + 가방 합산).")]
        public ItemType itemType = ItemType.Salvage;
        [Min(1)] public int count = 1;

        public override bool Evaluate(TutorialObserver w, int baseline) => TutorialObserver.CountOfType(itemType) >= Mathf.Max(1, count);

        public override string Summary => $"{itemType} {count}개 소지";
    }
}
