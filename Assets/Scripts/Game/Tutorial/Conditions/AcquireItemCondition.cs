using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("아이템/특정 아이템 소지")]
    public sealed class AcquireItemCondition : TutorialCondition
    {
        /// <summary>이 아이템을 갖고 있으면 완료 (핫바 + 가방 합산).</summary>
        public ItemDef item;
        public int count = 1;
        public override void Configure(TutorialConditionDef def) { item = def.Item; count = Mathf.Max(1, def.Count); }
        public override bool Evaluate(TutorialObserver w, int baseline) => item != null && TutorialObserver.CountOfItem(item) >= Mathf.Max(1, count);
        public override string Summary => $"{(item != null ? item.DisplayName : "(아이템 미지정)")} {count}개 소지";
    }
}
