using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("기본/이동과 시점 회전")]
    public sealed class MoveAndLookCondition : TutorialCondition
    {
        /// <summary>이동을 누적해야 하는 초. 시점 회전은 이 값의 1/4만 요구한다 — 계속 돌리고 있으라는 뜻이 아니라 둘러봤으면 됐다.</summary>
        public float seconds = 2f;
        public override void Configure(TutorialConditionDef def) => seconds = Mathf.Max(0.1f, def.Seconds);
        public override bool Evaluate(TutorialObserver w, int baseline) => w.MoveSeconds >= seconds && w.LookSeconds >= seconds * 0.25f;
        public override string Summary => $"이동 {seconds:0.#}초";
    }
}
