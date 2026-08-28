using UnityEngine;

/// <summary>
/// "바라본 대상"이 무엇인가 — 조준선에서 핑 대상(<see cref="IPingable"/>)을 고르는 규칙.
/// 입력·튜토리얼·조준 UI가 같이 쓴다.
///
/// 규칙은 둘이다:
///   ① <b>자기 계층은 없는 것으로 친다.</b> 카메라가 플레이어 캡슐 꼭대기(y=1)에 정확히 놓여 있어
///      아래를 볼수록 레이가 자기 콜라이더 안쪽으로 들어가 맞는다 — 그래서 "어떨 때만" 자기 몸(총 뷰모델 포함)이
///      찍혔다. 레이어로 빼지 않는 이유는 멀티플레이의 다른 플레이어는 찍혀야 하기 때문이다.
///   ② 그 다음 <b>가장 가까운 히트 하나</b>만 본다. 그것이 IPingable이면 대상, 벽·지형이면 대상 없음 —
///      벽 너머의 것은 찍히지 않는다(가림). 맞은 자리는 위치 핑용으로 함께 돌려준다.
/// </summary>
public static class PingTargeting
{
    static readonly RaycastHit[] hits = new RaycastHit[24];

    /// <summary>조준선의 대상. 없으면 false — 그래도 <paramref name="hitPoint"/>는 처음 맞은 자리를 준다(없으면 사거리 끝).</summary>
    /// <param name="selfRoot">찍는 쪽의 루트 — 이 아래 콜라이더는 전부 무시한다. null이면 제외 없음.</param>
    public static bool TryFindAimed(Camera camera, Transform selfRoot, float range,
                                    out IPingable target, out Vector3 hitPoint)
    {
        target = null;
        hitPoint = default;
        if (camera == null) return false;

        var origin = camera.transform.position;
        var dir = camera.transform.forward;
        hitPoint = origin + dir * range;

        int n = Physics.RaycastNonAlloc(origin, dir, hits, range, ~0, QueryTriggerInteraction.Ignore);
        if (n <= 0) return false;

        // 자기 계층을 뺀 것 중 가장 가까운 히트 — RaycastNonAlloc은 거리순을 보장하지 않는다
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            if (selfRoot != null && hits[i].transform.IsChildOf(selfRoot)) continue;
            if (best < 0 || hits[i].distance < hits[best].distance) best = i;
        }
        if (best < 0) return false;

        hitPoint = hits[best].point;
        target = PingableOf(hits[best].collider);
        return target != null;
    }

    /// <summary>콜라이더 → 찍을 수 있는 대상. 부모 어딘가의 IPingable이고 지금 찍을 수 있어야 한다.</summary>
    public static IPingable PingableOf(Collider collider)
    {
        if (collider == null) return null;
        var p = collider.GetComponentInParent<IPingable>();
        return p != null && p.CanBePinged ? p : null;
    }
}
