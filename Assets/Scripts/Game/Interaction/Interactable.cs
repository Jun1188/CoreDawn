using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.FPS;
using CoreDawn.Pings;

namespace CoreDawn.Interaction
{
    /// <summary>
    /// 단독 상호작용 오브젝트(상자·드롭 아이템)용 IInteractable 편의 베이스.
    /// 상속이 이미 차 있는 클래스(BuildingEntity 등)는 IInteractable을 직접 구현할 것.
    /// </summary>
    public abstract class Interactable : MonoBehaviour, IInteractable, IPingable
    {
        [Header("Interaction Info")]
        public string promptMessage = "열기"; // 화면 중앙 조준점 근처에 띄울 글자 (예: "상자 열기")

        public virtual string Prompt => promptMessage;

        // ── 핑 대상 (IPingable) — 상호작용할 수 있는 것은 가리킬 수도 있다. 이름은 하위가 덮어쓴다.
        public virtual string PingLabel => promptMessage;
        public GameObject PingRoot => gameObject;
        public virtual bool CanBePinged => isActiveAndEnabled;

        public void Interact(PlayerController player) => OnInteract(player);

        // 플레이어가 바라보고 E키를 눌렀을 때 실행될 함수 (자식들이 직접 구현)
        public abstract void OnInteract(PlayerController player);
    }
}
