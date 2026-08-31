using CoreDawn.Sim;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 행동 등록부 — 정의의 모듈 조합 → 건물 행동. 구 BuildingDataSO.CreateBehavior(SO 종류별 가상 메서드)를 대체한다.
    ///
    /// 정체성 마커는 없다: 무엇이 <b>있느냐</b>로 고른다. 순서가 곧 우선순위다 — 포탑도 Inventory+Ports를 갖지만
    /// Turret이 먼저 잡는다. 아무 규칙에도 안 걸리면 행동 없음(null): 나무·둥지·울타리·지뢰는 칸을 차지할 뿐이다
    /// (둥지의 상태는 심 Nest 모듈이, 낮 방어 스폰의 시점·자리는 아직 뷰 NestView가 맡는다).
    ///
    /// 5a-2d~2e에서 행동이 심 모듈(정의의 Create)로 바뀌면 이 표는 정의 쪽으로 흡수된다.
    /// </summary>
    public static class BuildingBehaviors
    {
        public static IBuildingBehavior Create(BuildingModule b)
        {
            var def = b.Def;
            if (def.Has<ConveyorModuleDef>()) return new BeltBehavior(b);

            var core = def.Get<CoreModuleDef>();
            if (core != null) return new CoreBehavior(b, core);

            // 그 밖은 행동 없음 — 걷는 모듈(ISteppable: 제작기·포탑·오라·지뢰…)과 통과 보관소(그릇+포트)는
            // BuildingModule의 공통 틱이 돌리고, 나무·둥지·울타리는 칸을 차지할 뿐이다.
            return null;
        }
    }
}
