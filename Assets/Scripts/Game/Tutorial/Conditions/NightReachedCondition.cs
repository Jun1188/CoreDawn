using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("밤/밤 맞이하기")]
    public sealed class NightReachedCondition : TutorialCondition
    {
        /// <summary>이 안내가 뜬 뒤로 몇 번의 밤을 맞아야 하는가.</summary>
        public int count = 1;
        public override void Configure(TutorialConditionDef def) => count = Mathf.Max(1, def.Count);
        public override int CounterOf(TutorialObserver w) => w.IsNight ? w.NightsStarted - 1 : w.NightsStarted;
        public override bool Evaluate(TutorialObserver w, int baseline) => w.NightsStarted - baseline >= Mathf.Max(1, count);
        public override string Summary => $"밤 맞이 ×{Mathf.Max(1, count)}";
    }
}
