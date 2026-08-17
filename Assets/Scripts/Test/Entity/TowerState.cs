/// <summary>
/// 타워가 지금 무엇을 하고 있는가 — 전투 로직과 연출이 공유하는 단일 진실.
///
/// 값의 순서가 곧 우선순위다. <see cref="BattleTower"/>가 매 프레임 위에서부터 판정해
/// 처음 걸리는 것을 현재 상태로 삼는다. 새 상태를 넣을 때는 우선순위 자리에 맞춰 끼워야 한다.
///
/// 포탑이 없는 타워(감속 필드·울타리)는 turnSpeed가 0이라 <see cref="Aiming"/>을 그냥 지나친다.
/// 분기 코드가 아니라 데이터로 갈리므로, 포탑 없는 타워를 추가해도 여기는 손대지 않는다.
/// </summary>
public enum TowerState
{
    /// <summary>사망 — 뷰는 즉시 파괴된다. 폭발 연출은 분리 스폰이라야 보인다.</summary>
    Destroyed,

    /// <summary>배치 직후의 등장 연출 구간. 짧고, 한 번만 지나간다.</summary>
    Deploying,

    /// <summary>발사하지 않는 구조물(울타리) — 몸으로 막을 뿐이다. 영구 상태.</summary>
    Inert,

    /// <summary>
    /// 벨트 보급이 끊겨 탄약/연료가 없다.
    /// 목표 유무보다 위에 둔다 — 공장에서 탄이 오지 않는다는 사실이 더 급한 정보다.
    /// </summary>
    Starved,

    /// <summary>사거리 안에 목표가 없다. 포탑은 천천히 주변을 훑는다.</summary>
    Idle,

    /// <summary>목표를 잡았고 포탑이 아직 그쪽을 보지 않았다. 정렬될 때까지 쏘지 않는다.</summary>
    Aiming,

    /// <summary>정렬 완료 — 쿨다운이 도는 대로 쏜다.</summary>
    Firing,
}
