using System;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.FPS;
using CoreDawn.Sim;
using CoreDawn.UI;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 건물 상호작용(E) 등록부 — 정의의 <b>모듈 조합</b>으로 "무슨 화면을 여는가"를 고른다. 심은 UI를 모르고(마커 모듈 없음),
    /// 행동(IBuildingBehavior)도 더 이상 UI를 열지 않는다. 심 쪽 <see cref="BuildingBehaviors"/>와 같은 꼴:
    /// 새 상호작용 건물 = 여기 한 줄. 조합에 안 맞으면 상호작용 없음(프롬프트 null).
    ///
    /// 과도기: 필터·코어 패널이 아직 행동 객체를 받는다 — Router·Core 모듈이 생기면 그쪽을 넘긴다.
    /// </summary>
    public static class BuildingInteractions
    {
        public static bool TryGet(BuildingModule b, out string prompt, out Action<PlayerController> open)
        {
            prompt = null; open = null;
            if (b == null || b.Def == null || b.Owner == null) return false;
            var def = b.Def; var owner = b.Owner;

            // 제작 설비 — 레시피·진행·버퍼 화면 (패널이 심 모듈 Crafter를 직접 읽는다)
            if (owner.Get<CrafterModule>() != null)
            {
                prompt = $"{b.DisplayName} 열기";
                open = _ => { if (!MachinePanelView.TryOpen(b)) Debug.LogWarning("[Interact] 설비 화면(UITK)을 열지 못했습니다 — GameUI 씬이 탑재되지 않았습니다."); };
                return true;
            }
            // 분배기 — 출구별 필터. 합류기는 설정할 것이 없다
            if (def.Get<RouterModuleDef>() is { } router && router.Mode != "merge" && b.Behavior is SplitterBehavior splitter)
            {
                prompt = "필터 설정";
                open = _ => { if (!SplitterPanelView.TryOpen(splitter)) Debug.LogWarning("[Interact] 필터 화면(UITK)을 열지 못했습니다 — GameUI 씬이 탑재되지 않았습니다."); };
                return true;
            }
            // 코어 — 납품·티어
            if (b.Behavior is CoreBehavior core)
            {
                prompt = core.HasNextTier ? "코어에 자원 납품" : "코어 (최고 티어 달성)";
                open = _ => GameScreens.OpenCore(core);
                return true;
            }
            // 탄·연료를 먹는 발사기 — 입력 그릇이 탄약함/연료함. 고정 탄(지뢰)은 그릇이 없어 여기 안 걸린다
            if (owner.Get<AmmoConsumerModule>() != null && b.Input != null && b.Input.SlotCount > 0)
            {
                prompt = owner.Get<AuraEmitterModule>() != null ? "연료함 열기" : "탄약함 열기";
                open = _ => GameScreens.OpenContainer(b.Input);
                return true;
            }
            // 그릇 + 포트 = 보관소. 보관함 = 입력 버퍼(벨트가 넣는 곳과 같아서 보이는 것이 곧 전부)
            if (def.Has<InventoryModuleDef>() && def.Has<PortsModuleDef>() && b.Input != null && b.Input.SlotCount > 0)
            {
                prompt = "보관함 열기";
                open = _ => GameScreens.OpenContainer(b.Input);
                return true;
            }
            return false;
        }
    }
}
