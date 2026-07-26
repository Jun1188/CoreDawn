using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// 전투 테스트 씬(TestCombat1.0) 전용 부트스트랩.
//   - 플레이어 인벤토리에 시작 무기 지급 + 즉시 장착 (총 사격 테스트용)
//   - H키로 낮/밤 강제 전환 (TimeManager 필요) — 낮=건설, 밤=웨이브 디펜스 사이클을 즉시 확인
public class TestCombatBootstrap : MonoBehaviour
{
    [Tooltip("시작 시 플레이어 인벤토리에 넣어줄 무기 아이템(WeaponItemSO). 비우면 지급하지 않는다.")]
    [SerializeField] private ItemDataSO startingWeapon;

    // H키 — 낮/밤 강제 전환 (디버그/테스트 전용이라 입력 파이프라인을 거치지 않는다)
    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.hKey.wasPressedThisFrame) return;

        var tm = TimeManager.Instance;
        if (tm == null)
        {
            Debug.LogWarning("[TestCombatBootstrap] TimeManager가 없어 낮/밤 전환을 할 수 없습니다.");
            return;
        }

        if (tm.Phase == DayPhase.Night)
        {
            tm.EndNightEarly(); // 공개 API — 즉시 아침
        }
        else
        {
            // 낮 → 밤 강제: 남은 낮 시간을 소진시켜 페이즈 경계를 넘긴다 (공개 API만 사용)
            tm.Cycle.Advance(tm.Cycle.PhaseRemaining + 0.001f);
        }
        Debug.Log($"[TestCombatBootstrap] H — 강제 전환 → {tm.Phase} (Day {tm.DayNumber})");
    }

    private IEnumerator Start()
    {
        // 인벤토리/핫바/무기 매니저의 Awake·Start가 모두 끝난 뒤 지급하도록 한 프레임 대기
        yield return null;

        if (startingWeapon == null) yield break;

        var controller = FindFirstObjectByType<PlayerController>();
        if (controller == null || controller.playerInventory == null)
        {
            Debug.LogWarning("[TestCombatBootstrap] PlayerController/인벤토리를 찾지 못해 시작 무기를 지급하지 못했습니다.");
            yield break;
        }

        controller.playerInventory.AddItem(startingWeapon, 1);

        // 핫바 0번(기본 선택 슬롯)에 들어간 무기를 즉시 장착
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.CheckWeaponEquip(PlayerInventoryHolder.Instance.HotbarContainer);

        Debug.Log($"[TestCombatBootstrap] 시작 무기 지급: {startingWeapon.name}");
    }
}
