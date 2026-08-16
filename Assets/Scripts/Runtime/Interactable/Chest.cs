using UnityEngine;

[RequireComponent(typeof(Inventory))] // 상자 오브젝트에 Inventory 컴포넌트 자동 부착
public class Chest : Interactable
{
    private Inventory inventory;

    private void Awake()
    {
        inventory = GetComponent<Inventory>();
        promptMessage = "상자 열기";
    }

    public override void OnInteract(PlayerController player)
    {
        // Inventory 컴포넌트가 쥐고 있는 Container를 그대로 넘겨줌.
        // 어느 UI 체계로 여는지는 GameScreens의 정책 — 상자는 모른다.
        if (inventory != null)
            GameScreens.OpenContainer(inventory.Container);
    }
}