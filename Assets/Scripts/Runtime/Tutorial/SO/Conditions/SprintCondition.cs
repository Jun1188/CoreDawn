using UnityEngine;

[TutorialConditionMenu("기본/달리기 (누적 초)")]
public class SprintCondition : TutorialConditionSO
{
    [Tooltip("Shift를 누른 채 실제로 달린 시간의 누적(초). 키만 누르고 서 있으면 세지 않는다 — IsSprinting은 전진 입력이 있을 때만 켜진다.")]
    [Min(0.1f)] public float seconds = 1f;

    public override bool Evaluate(TutorialObserver w, int baseline) => w.SprintSeconds >= seconds;

    public override string Summary => $"달리기 {seconds:0.#}초";
}
