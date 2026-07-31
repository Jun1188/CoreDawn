using UnityEngine;

/// <summary>
/// 단독 상호작용 오브젝트(상자·드롭 아이템)용 IInteractable 편의 베이스.
/// 상속이 이미 차 있는 클래스(BuildingEntity 등)는 IInteractable을 직접 구현할 것.
/// </summary>
public abstract class Interactable : MonoBehaviour, IInteractable
{
    [Header("Interaction Info")]
    public string promptMessage = "열기"; // 화면 중앙 조준점 근처에 띄울 글자 (예: "상자 열기")

    public virtual string Prompt => promptMessage;

    public void Interact(PlayerController player) => OnInteract(player);

    // 플레이어가 바라보고 E키를 눌렀을 때 실행될 함수 (자식들이 직접 구현)
    public abstract void OnInteract(PlayerController player);
}
