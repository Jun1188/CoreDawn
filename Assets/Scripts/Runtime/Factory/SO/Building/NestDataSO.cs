using UnityEngine;

/// <summary>
/// 몬스터 둥지의 건물 데이터.
///
/// 둥지는 플레이어가 짓는 것이 아니라 맵이 세우는 것인데도 건물인 이유는 <b>칸을 차지해야</b>
/// 하기 때문이다. 건설 판정(PlacementSystem.CanPlace)은 팩토리 그리드의 점유만 보므로,
/// 그리드에 들어가지 않은 둥지 위에는 벨트도 포탑도 그대로 올라갔다.
///
/// 다만 <b>뷰는 만들지 않는다</b> — 씬 위의 둥지는 MonsterNest(Entity)가 이미 담당하고 있다.
/// <see cref="WorldPopulator"/>가 FactorySim.Place만 호출해 칸을 잡고, 전투·스폰·복구는
/// 전부 MonsterNest에 남는다. BuildingEntity를 붙이면 한 오브젝트에 Entity가 둘이 되어
/// 총알이 어느 쪽을 맞혔는지가 불확실해지고, 몬스터가 자기 둥지를 목표로 삼는다.
///
/// 그래서 이 SO가 실제로 들고 있는 값은 크기와 파괴 규칙(isDemolishable/isAttackable)뿐이다.
/// </summary>
[CreateAssetMenu(fileName = "NewNest", menuName = "Factory/Buildings/Nest")]
public class NestDataSO : BuildingDataSO
{
    public override IBuildingBehavior CreateBehavior(Building building) => new NestBehavior();
}

// ─── 행동 ──────────────────────────────────────────────────────

/// <summary>
/// 아무 일도 하지 않는 행동. 둥지는 아이템을 주고받지 않으므로 심의 틱에 걸릴 일이 없다
/// (Dirty 큐에 들어가지 않으면 Tick 자체가 호출되지 않는다).
/// </summary>
public class NestBehavior : IBuildingBehavior
{
    public void Tick(float dt) { }
    public void OnAfterPlaced() { }
}
