using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.Interaction;
using CoreDawn.Inventories;
using CoreDawn.Managers;
using CoreDawn.Sim;
using CoreDawn.Factory;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 플레이어 상호작용(E)이 있는 행동만 추가로 구현하는 opt-in 인터페이스.
    /// 심 계약(IBuildingBehavior·Tick)과 분리된 뷰 이벤트 — 시나리오 테스트는 이것을 모른다.
    /// 조준 시 BuildingEntity(IInteractable)이 여기로 위임한다.
    /// 새 상호작용 건물 추가 = 행동 클래스에 이 인터페이스 구현 (기존 코드 무수정).
    /// </summary>
    public interface IInteractiveBehavior
    {
        /// <summary>조준 프롬프트. null/빈 문자열 = 지금은 상호작용 불가.</summary>
        string InteractPrompt { get; }

        void Interact(PlayerController player);
    }
}
