using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("진행/코어 티어 도달")]
    public sealed class CoreTierCondition : TutorialCondition
    {
        /// <summary>코어 티어가 이 값 이상이면 완료 (= 수리를 이만큼 끝냈다). 횟수가 아니라 도달 지점이다.</summary>
        public int tier = 1;
        public override void Configure(TutorialConditionDef def) => tier = Mathf.Max(1, def.Tier);
        public override bool Evaluate(TutorialObserver w, int baseline) => w.CoreTier >= tier;
        public override string Summary => $"코어 티어 {tier}";
    }
}
