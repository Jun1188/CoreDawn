using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.Placement;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 나무의 건물 데이터 — <see cref="NestDataSO"/>와 같은 이유로 건물이다.
    ///
    /// 나무는 플레이어가 짓는 것이 아니라 <see cref="WorldTreePlanter"/>가 심는 것인데도 건물인
    /// 이유는 <b>칸을 차지해야</b> 하기 때문이다. 건설 판정(PlacementSystem.CanPlace)은 팩토리
    /// 그리드의 점유만 보므로, 그리드에 들어가지 않은 나무 위에는 벨트도 포탑도 그대로 올라간다.
    ///
    /// 뷰(BuildingEntity)는 만들지 않는다 — 나무는 수백 그루가 깔리는데, 그루마다 BuildingEntity 를
    /// 붙이면 전부 BuildingEntity.All 에 들어가 플로우필드 시드 수집과 몬스터의 사거리 검색이
    /// 매번 그 목록을 훑게 된다. 지금 나무에 필요한 것은 "칸을 막는다"뿐이라 심에만 넣는다.
    /// 나중에 베어낼 수 있게 만들 때 뷰가 필요해진다 — 그때 isAttackable 이 살아난다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewTree", menuName = "Factory/Buildings/Tree")]
    public class TreeDataSO : BuildingDataSO
    {
        public override IBuildingBehavior CreateBehavior(Building building) => new TreeBehavior();
    }

    // ─── 행동 ──────────────────────────────────────────────────────

    /// <summary>
    /// 아무 일도 하지 않는 행동. 나무는 아이템을 주고받지 않으므로 심의 틱에 걸릴 일이 없다
    /// (Dirty 큐에 들어가지 않으면 Tick 자체가 호출되지 않는다).
    /// </summary>
    public class TreeBehavior : IBuildingBehavior
    {
        public void Tick(float dt) { }
        public void OnAfterPlaced() { }
    }
}
