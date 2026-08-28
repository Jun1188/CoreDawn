using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.Interaction;
using CoreDawn.ResourceNodes;

namespace CoreDawn.Pings
{
    /// <summary>
    /// 핑으로 가리킬 수 있는 것 — "저거"라고 찍을 만한 대상의 계약. IInteractable과 같은 발견 방식이다:
    /// 콜라이더가 있는 GO의 부모 어딘가에 이 인터페이스가 있으면 대상.
    ///
    /// 레이어가 아니라 인터페이스인 이유: 레이어는 물리·렌더링 축이고 이미 의미 축까지 겹쳐 쓰고 있어
    /// (Interactable·Monster 레이어로 적대 판정 등) 거기에 "핑 가능"을 얹으면 정리할 것이 하나 더 는다.
    /// 인터페이스는 타입 옆에 살고, 예/아니오 너머의 것(표시 이름·기준 루트·지금 가능한가)을 함께 준다 —
    /// HUD 마커와 알림이 바로 쓸 값들이다.
    ///
    /// 구현 지점: Entity(몬스터·건물·둥지·플레이어), Interactable(드롭 아이템·상자), ResourceNode(광맥).
    /// 자기 자신은 대상에서 빠진다 — 그건 구현이 아니라 조준(PingTargeting)이 거른다.
    /// </summary>
    public interface IPingable
    {
        /// <summary>알림·마커에 뜰 이름 ("기본 포탑", "철광석 ×3").</summary>
        string PingLabel { get; }

        /// <summary>아웃라인·마커의 기준 오브젝트 — 보통 자기 자신. 핑은 이 참조를 든다.</summary>
        GameObject PingRoot { get; }

        /// <summary>지금 찍을 수 있는가 — 죽은 몬스터·비활성 오브젝트는 false.</summary>
        bool CanBePinged { get; }
    }
}
