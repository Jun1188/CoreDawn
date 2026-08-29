using CoreDawn.Sim;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 행동 등록부 — 정의의 모듈 조합 → 건물 행동. 구 BuildingDataSO.CreateBehavior(SO 종류별 가상 메서드)를 대체한다.
    ///
    /// 정체성 마커는 없다: 무엇이 <b>있느냐</b>로 고른다. 순서가 곧 우선순위다 — 포탑도 Inventory+Ports를 갖지만
    /// TowerBrain이 먼저 잡는다. 아무 규칙에도 안 걸리면 행동 없음(null): 나무·둥지·울타리·지뢰는 칸을 차지할 뿐이다
    /// (둥지의 스폰은 아직 뷰 NestView가, 지뢰·감속장의 효과는 5a-2e의 심 모듈이 맡는다).
    ///
    /// 5a-2d~2e에서 행동이 심 모듈(정의의 Create)로 바뀌면 이 표는 정의 쪽으로 흡수된다.
    /// </summary>
    public static class BuildingBehaviors
    {
        public static IBuildingBehavior Create(BuildingModule b)
        {
            var def = b.Def;
            if (def.Has<ConveyorModuleDef>()) return new BeltBehavior(b);

            var crafter = def.Get<CrafterModuleDef>();
            if (crafter != null) return new AssemblerBehavior(b, crafter);

            var extractor = def.Get<ExtractorModuleDef>();
            if (extractor != null) return new MinerBehavior(b, extractor);

            var router = def.Get<RouterModuleDef>();
            if (router != null) return router.Mode == "merge" ? new MergerBehavior(b) : new SplitterBehavior(b);

            var core = def.Get<CoreModuleDef>();
            if (core != null) return new CoreBehavior(b, core);

            // 탄약함이 있는 사격·펄스 건물 — 발사 판정은 아직 뷰(TowerView)가 주도하고 심은 탄약 소비만 맡는다
            var ammo = def.Get<AmmoConsumerModuleDef>();
            if (ammo != null && (def.Has<TowerBrainModuleDef>() || def.Has<AuraEmitterModuleDef>()))
                return new TowerBehavior(b, ammo);

            // 버퍼와 포트만 있는 건물 = 통과 보관소(보관소·드론 포트)
            if (def.Has<InventoryModuleDef>() && def.Has<PortsModuleDef>()) return new StorageBehavior(b);

            return null;
        }
    }
}
