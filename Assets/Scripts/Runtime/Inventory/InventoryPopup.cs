using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 인벤토리 패널(inventoryUIPanel)에 부착하는 파이프라인 어댑터.
/// 열기/닫기 로직 자체는 PlayerController가 계속 소유하고, 이 컴포넌트는:
///  - 패널 활성화 시 UI 맵 Push → 사격·건설 등 Gameplay 입력 신호 차단
///  - 크로스헤어 표시를 Enter/Exit에 집중 (커서 해제/잠금은 UIPopup 공통)
///  - Cancel(ESC) / ToggleInventory(I) / Interact(E) → CloseInventory() 위임
///    (마우스 캐리지 아이템 드롭, 상자 패널 정리 포함)
/// </summary>
public class InventoryPopup : UIPopup
{
    [SerializeField] private PlayerController player;

    protected override void OnEnable()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        base.OnEnable();

        if (player != null && player.crosshairUI != null) player.crosshairUI.SetActive(false);
    }

    protected override void OnDisable()
    {
        if (player != null && player.crosshairUI != null) player.crosshairUI.SetActive(true);
        base.OnDisable();
    }

    public override bool OnInput(in InputEvent e)
    {
        // I(Global 맵)와 E(UI 맵)도 닫기로 — 연 키로 다시 닫는 대칭 조작
        if (e.Phase == InputActionPhase.Performed &&
            (e.Id == InputActionId.ToggleInventory || e.Id == InputActionId.Interact))
        {
            Close();
            return true;
        }
        return base.OnInput(e);   // Cancel(ESC) 닫기 + 모달 삼킴
    }

    public override void Close()
    {
        // 캐리지 드롭·상자 패널 정리·패널 SetActive(false)까지 수행
        if (InventoryManager.Instance != null) InventoryManager.Instance.CloseScreen();
        else base.Close();
    }
}