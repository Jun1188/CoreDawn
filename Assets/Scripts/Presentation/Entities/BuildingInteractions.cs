using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Data;
using CoreDawn.FPS;
using CoreDawn.Sim;
using CoreDawn.UI;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 건물 상호작용(E) — 팩 <c>view.interact</c>(<see cref="InteractKinds"/>)가 고른 화면을 연다. 추론하지 않는다:
    /// 모듈 조합으로 "무슨 화면인지" 짐작하던 등록부(구)를 데이터 지정으로 바꿨다(2026-09-04). 종류가 요구하는 모듈은
    /// 로드·편집기 저장에서 검증되므로 여기서는 이름을 화면에 잇기만 한다. 값이 없으면 상호작용 없음(프롬프트 null).
    /// 새 상호작용 = InteractKinds 에 이름·요구 조건 한 줄 + 여기 case 한 줄.
    /// </summary>
    public static class BuildingInteractions
    {
        static readonly HashSet<string> reported = new();

        public static bool TryGet(BuildingModule b, out string prompt, out Action<PlayerController> open)
        {
            prompt = null; open = null;
            if (b == null || b.Def == null || b.Owner == null) return false;
            var def = b.Def; var owner = b.Owner;
            string kind = ViewSchema.Entity(def)?.Interact;
            if (string.IsNullOrEmpty(kind)) return false;

            switch (kind)
            {
                case InteractKinds.Machine:   // 레시피·진행·버퍼 화면 (패널이 심 모듈 Crafter를 직접 읽는다)
                    prompt = $"{b.DisplayName} 열기";
                    open = _ => { if (!MachinePanelView.TryOpen(b)) Debug.LogWarning("[Interact] 설비 화면(UITK)을 열지 못했습니다 — GameUI 씬이 탑재되지 않았습니다."); };
                    return true;
                case InteractKinds.Filters:   // 출구별 필터 (패널이 심 모듈 Router를 직접 읽는다)
                    prompt = "필터 설정";
                    open = _ => { if (!SplitterPanelView.TryOpen(b)) Debug.LogWarning("[Interact] 필터 화면(UITK)을 열지 못했습니다 — GameUI 씬이 탑재되지 않았습니다."); };
                    return true;
                case InteractKinds.Core:      // 납품·티어 (패널이 심 모듈 Core를 직접 읽는다)
                {
                    var core = owner.Get<CoreModule>();
                    if (core == null) return Missing(def, kind);
                    prompt = core.HasNextTier ? "코어에 자원 납품" : "코어 (최고 티어 달성)";
                    open = _ => GameScreens.OpenCore(core);
                    return true;
                }
                case InteractKinds.Ammo:      // 입력 버퍼 = 탄약함
                case InteractKinds.Fuel:      // 입력 버퍼 = 연료함
                case InteractKinds.Storage:   // 입력 버퍼 = 보관함(통과 보관소: 벨트가 넣는 곳과 같아서 보이는 것이 곧 전부)
                    if (b.Input == null || b.Input.SlotCount == 0) return Missing(def, kind);
                    prompt = kind == InteractKinds.Ammo ? "탄약함 열기" : kind == InteractKinds.Fuel ? "연료함 열기" : "보관함 열기";
                    open = _ => GameScreens.OpenContainer(b.Input);
                    return true;
                default:
                    return Missing(def, kind);
            }
        }

        // 로드 검증(ViewSchema)을 통과했다면 오지 않는 자리 — 왔다면 정의당 한 번만 소리 낸다(매 프레임 묻는 호출부라 도배 방지)
        static bool Missing(EntityDef def, string kind)
        {
            if (reported.Add(def.Id + "|" + kind))
                Debug.LogError($"[Interact] {def.Id}: view.interact '{kind}'를 열 수 없습니다 — {InteractKinds.Validate(def, kind) ?? "런타임 모듈이 없습니다"}");
            return false;
        }
    }
}
