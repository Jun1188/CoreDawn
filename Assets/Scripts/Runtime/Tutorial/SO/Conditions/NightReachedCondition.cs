using UnityEngine;

/// <summary>
/// 이 안내가 뜬 뒤로 밤을 count번 맞았다.
///
/// 누적형이지만 CumulativeConditionSO를 쓰지 않는 이유 — 기준점을 다르게 잡아야 한다.
/// 밤은 플레이어가 하는 일이 아니라 플레이어에게 <b>일어나는</b> 일이다. 기준점을 "지금까지 맞은 밤 수"로
/// 그대로 잡으면, 카드가 <b>밤중에</b> 떴을 때(앞 안내를 밤에 끝내면 그렇게 된다) 이 밤은 안 쳐주고
/// 다음 밤까지 기다리게 된다 — 카드가 밤 내내·다음 낮 내내 남고, 뒤의 "밤 넘기기"는 그 뒤에 줄을 서서
/// 둘 다 영영 안 풀리는 것처럼 보였다.
///
/// 그래서 밤중에 떴으면 기준점을 하나 낮춘다 = "이 밤은 아직 안 맞은 것"으로 시작한다. 그러면 읽을 시간
/// (판정 유예)이 지난 직후 완료된다 — 지금 겪고 있는 밤이 곧 맞은 밤이다. 낮에 떴으면 예전과 같다:
/// 밤이 시작되는 순간 완료.
/// </summary>
[TutorialConditionMenu("밤/밤 맞이하기")]
public class NightReachedCondition : TutorialConditionSO
{
    [Tooltip("이 안내가 뜬 뒤로 몇 번의 밤을 맞아야 하는가.")]
    [Min(1)] public int count = 1;

    public override int CounterOf(TutorialObserver w) => w.IsNight ? w.NightsStarted - 1 : w.NightsStarted;

    public override bool Evaluate(TutorialObserver w, int baseline) => w.NightsStarted - baseline >= Mathf.Max(1, count);

    public override string Summary => $"밤 맞이 ×{Mathf.Max(1, count)}";
}
