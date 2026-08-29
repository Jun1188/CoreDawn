using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using CoreDawn.DayTime;
using CoreDawn.Factory;
using CoreDawn.Inventories;
using CoreDawn.Data;

namespace CoreDawn.Tests
{
    // 전투 테스트 씬(TestCombat1.0) 전용 부트스트랩.
    //   - 플레이어 인벤토리에 시작 무기 지급 + 즉시 장착 (총 사격 테스트용)
    //   - H키로 낮/밤 강제 전환 (TimeManager 필요) — 낮=건설, 밤=웨이브 디펜스 사이클을 즉시 확인
    public class TestCombatBootstrap : MonoBehaviour
    {
        [Tooltip("시작 시 플레이어 인벤토리에 넣어줄 무기 아이템(WeaponModuleSO를 단 아이템). 비우면 지급하지 않는다.")]
        [SerializeField] private ItemDataSO startingWeapon;

        [Tooltip("시작 시 함께 지급할 아이템들(탄약 등) — 실소비 세계에서는 탄이 있어야 재장전이 된다.")]
        [SerializeField] private StartingStack[] startingItems;

        [System.Serializable]
        private struct StartingStack
        {
            public ItemDataSO item;
            public int amount;
        }

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

            // 지급은 홀더(핫바→가방 순 배치)가 정본 — controller.playerInventory(Inventory 컴포넌트)는
            // 별개 컨테이너라 거기 넣으면 핫바 UI·무기 장착·탄약 실소비가 전부 못 본다.
            var holder = PlayerInventoryHolder.Instance;
            if (holder == null)
            {
                Debug.LogWarning("[TestCombatBootstrap] PlayerInventoryHolder가 없어 시작 지급을 하지 못했습니다.");
                yield break;
            }

            if (startingWeapon != null)
                holder.AddItemToPlayer(startingWeapon, 1);

            if (startingItems != null)
                foreach (var s in startingItems)
                    if (s.item != null && s.amount > 0)
                        holder.AddItemToPlayer(s.item, s.amount);

            // 핫바에 들어간 무기를 즉시 장착 — 장착 브리지는 핫바 컨트롤러가 유일 소유
            if (HotbarController.Instance != null)
                HotbarController.Instance.EquipFromActiveSlot();

            Debug.Log($"[TestCombatBootstrap] 시작 지급 완료: 무기={(startingWeapon != null ? startingWeapon.name : "없음")}, 아이템 {startingItems?.Length ?? 0}종");
        }
    }
}
