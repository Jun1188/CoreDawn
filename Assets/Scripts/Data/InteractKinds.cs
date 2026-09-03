using System;
using System.Collections.Generic;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    /// <summary>
    /// 건물 상호작용(E) 종류 표 — 팩 <c>view.interact</c>가 고른다(없으면 상호작용 없음). 추론하지 않는다:
    /// 어떤 화면을 여는지는 데이터가 말하고, 그 화면이 요구하는 모듈이 정의에 없으면 로드(ViewSchema)·편집기 저장(GdPack.Validate)에서 오류다.
    /// 화면 자체는 Presentation(BuildingInteractions)이 이름으로 잇는다 — 여기는 이름과 요구 조건만.
    /// </summary>
    public static class InteractKinds
    {
        /// <summary>제작 설비 화면(MachinePanelView) — Crafter 모듈.</summary>
        public const string Machine = "machine";
        /// <summary>출구별 필터 설정(SplitterPanelView) — Router 모듈, mode ≠ merge.</summary>
        public const string Filters = "filters";
        /// <summary>코어 납품·티어 — Core 모듈.</summary>
        public const string Core = "core";
        /// <summary>탄약함(입력 버퍼) 열기 — AmmoConsumer + Inventory.input &gt; 0.</summary>
        public const string Ammo = "ammo";
        /// <summary>연료함(입력 버퍼) 열기 — AmmoConsumer + AuraEmitter + Inventory.input &gt; 0.</summary>
        public const string Fuel = "fuel";
        /// <summary>보관함(입력 버퍼) 열기 — Inventory.input &gt; 0 + Ports. 통과 보관소(보관소·드론 포트)용.</summary>
        public const string Storage = "storage";

        public static readonly IReadOnlyList<string> Names = new[] { Machine, Filters, Core, Ammo, Fuel, Storage };

        /// <summary>정의가 종류의 요구 모듈을 갖췄는가. 문제가 있으면 오류 문장(정의 id 포함), 없으면 null. kind 가 비었으면 항상 null.</summary>
        public static string Validate(EntityDef def, string kind)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            if (def == null) return $"view.interact '{kind}': 정의가 없습니다";
            string id = def.Id;
            int input = def.Get<InventoryModuleDef>()?.Input ?? 0;
            switch (kind)
            {
                case Machine:
                    return def.Has<CrafterModuleDef>() ? null : $"{id}: view.interact 'machine'은 Crafter 모듈이 필요합니다";
                case Filters:
                    var router = def.Get<RouterModuleDef>();
                    if (router == null) return $"{id}: view.interact 'filters'는 Router 모듈이 필요합니다";
                    return router.Mode != "merge" ? null : $"{id}: view.interact 'filters'는 합류기(mode merge)에는 설정할 것이 없습니다";
                case Core:
                    return def.Has<CoreModuleDef>() ? null : $"{id}: view.interact 'core'는 Core 모듈이 필요합니다";
                case Ammo:
                    if (!def.Has<AmmoConsumerModuleDef>()) return $"{id}: view.interact 'ammo'는 AmmoConsumer 모듈이 필요합니다";
                    return input > 0 ? null : $"{id}: view.interact 'ammo'는 Inventory.input > 0(탄약함)이 필요합니다";
                case Fuel:
                    if (!def.Has<AmmoConsumerModuleDef>() || !def.Has<AuraEmitterModuleDef>()) return $"{id}: view.interact 'fuel'은 AmmoConsumer + AuraEmitter 모듈이 필요합니다";
                    return input > 0 ? null : $"{id}: view.interact 'fuel'은 Inventory.input > 0(연료함)이 필요합니다";
                case Storage:
                    if (!def.Has<PortsModuleDef>()) return $"{id}: view.interact 'storage'는 Ports 모듈이 필요합니다";
                    return input > 0 ? null : $"{id}: view.interact 'storage'는 Inventory.input > 0(보관함)이 필요합니다";
                default:
                    return $"{id}: 모르는 view.interact '{kind}' (허용: {string.Join(", ", Names)})";
            }
        }
    }
}
