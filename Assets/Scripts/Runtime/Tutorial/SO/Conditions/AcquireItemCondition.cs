using UnityEngine;
using CoreDawn.Factory;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("아이템/특정 아이템 소지")]
    public class AcquireItemCondition : TutorialConditionSO
    {
        [Tooltip("이 아이템을 갖고 있으면 완료 (핫바 + 가방 합산).")]
        public ItemDataSO item;
        [Min(1)] public int count = 1;

        public override bool Evaluate(TutorialObserver w, int baseline) => item != null && TutorialObserver.CountOfItem(item) >= Mathf.Max(1, count);

        public override string Summary => $"{(item != null ? item.displayName : "(아이템 미지정)")} {count}개 소지";
    }
}
