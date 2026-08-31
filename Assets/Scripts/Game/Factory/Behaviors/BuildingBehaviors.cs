using CoreDawn.Sim;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 행동 등록부 — 정의의 모듈 조합 → 건물 행동. 구 BuildingDataSO.CreateBehavior(SO 종류별 가상 메서드)를 대체한다.
    ///
    /// 정체성 마커는 없다: 무엇이 <b>있느냐</b>로 고른다. 순서가 곧 우선순위다 — 포탑도 Inventory+Ports를 갖지만
    /// Turret이 먼저 잡는다. 아무 규칙에도 안 걸리면 행동 없음(null): 나무·둥지·울타리·지뢰는 칸을 차지할 뿐이다
    /// (둥지의 스폰은 아직 뷰 NestView가 맡는다).
    ///
    /// 5a-2d~2e에서 행동이 심 모듈(정의의 Create)로 바뀌면 이 표는 정의 쪽으로 흡수된다.
    /// </summary>
    public static class BuildingBehaviors
    {
        public static IBuildingBehavior Create(BuildingModule b)
        {
            var def = b.Def;
            if (def.Has<ConveyorModuleDef>()) return new BeltBehavior(b);

            var crafter = b.Owner.Get<CrafterModule>();   // 정의의 Crafter가 만든 모듈 — 행동은 공장과 모듈 사이의 어댑터
            if (crafter != null) return new AssemblerBehavior(b, crafter);

            var extractor = def.Get<ExtractorModuleDef>();
            if (extractor != null) return new MinerBehavior(b, extractor);

            var router = def.Get<RouterModuleDef>();
            if (router != null) return router.Mode == "merge" ? new MergerBehavior(b) : new SplitterBehavior(b);

            var core = def.Get<CoreModuleDef>();
            if (core != null) return new CoreBehavior(b, core);

            // 사격·펄스·기폭 건물 — 판단은 심 모듈(정의의 Create)이 하고, 행동은 공장 어댑터(칸 크기·깨우기·세이브·상류 깨우기)
            if (b.Owner.Get<TurretModule>() is { } turret) return new TurretBehavior(b, turret);
            if (b.Owner.Get<AuraEmitterModule>() is { } aura) return new AuraBehavior(b, aura);
            if (b.Owner.Get<TriggerModule>() is { } trigger) return new TriggerBehavior(b, trigger);

            // 버퍼와 포트만 있는 건물 = 통과 보관소(보관소·드론 포트)
            if (def.Has<InventoryModuleDef>() && def.Has<PortsModuleDef>()) return new StorageBehavior(b);

            return null;
        }
    }
}
