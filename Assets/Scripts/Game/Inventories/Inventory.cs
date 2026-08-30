using System;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Save;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.Inventories
{
    /// <summary>
    /// 아이템 보관의 씬 접점 — 내부는 전부 ItemContainer(plain C#)에 위임한다.
    /// 플레이어 가방·상자는 자기 컨테이너를 만들고, 건물 보관함 화면은 Bind()로
    /// 심의 컨테이너를 그대로 꽂는다 (별도 동기화 없음 — 같은 객체를 본다).
    ///
    /// 슬롯 직접 접근(구 public slots 배열)은 제거 — 규칙(필터·스택 캡·종류당 1스택)이
    /// 항상 컨테이너에서 지켜지도록 위치 연산도 API 경유로만 한다.
    /// </summary>
    public class Inventory : MonoBehaviour
    {
        public int slotCount = 36; // 마크 인벤토리 기본 칸수

        [Tooltip("시작 아이템 — 씬/프리팹에서 저작 (상자 초기 내용물 등). 슬롯 위치 그대로 컨테이너에 주입된다.\n런타임 상태는 Container가 소유하며 이 배열로 되돌아오지 않는다.")]
        [SerializeField] private ItemStackAuthoring[] slots;   // SO 저작 — 심의 ItemStack(정의)은 직렬화되지 않는다   // 구 공개 배열과 같은 이름 — 기존 씬 직렬화 데이터 유지

        private ItemContainer container;

        /// <summary>위임 대상 컨테이너. 필요 시 지연 생성 — Awake 순서에 안전.</summary>
        public ItemContainer Container
        {
            get
            {
                if (container == null)
                {
                    container = new ItemContainer(slotCount);
                    SeedInitialItems();
                }
                return container;
            }
        }

        /// <summary>인스펙터에 저작된 시작 아이템을 슬롯 위치 그대로 1회 주입.</summary>
        private void SeedInitialItems()
        {
            if (slots == null) return;
            // 세이브 복원 중이면 저장된 내용물이 들어올 자리다 — 초기 내용물을 얹으면 상자가 두 배로 찬다
            if (SaveLoadContext.IsRestoring) return;
            for (int i = 0; i < slots.Length && i < container.SlotCount; i++)
            {
                var s = slots[i]?.ToStack();   // SO 저작 → 정의 스택 (팩에 없는 아이템은 건너뛴다)
                if (s == null) continue;
                // 상한 초과로 저작해 두면 TryPutAt이 통째로 거절한다 — 저작 실수로 상자가
                // 텅 비어 열리는 것보다 들어가는 만큼이라도 넣는 편이 낫다
                int fit = Mathf.Min(s.amount, container.CapFor(s.item));
                container.TryPutAt(i, new ItemStack(s.item, fit));
            }
        }

        public int SlotCount => Container.SlotCount;

        private void Awake()
        {
            _ = Container; // 조기 생성 (시작 아이템 주입 포함)
            // (구 Resources.Load("TestItemName") 테스트 코드 제거 — 해당 에셋이 존재한 적 없음.
            //  시작 아이템은 인스펙터의 slots 배열로 지정)
        }

        /// <summary>다른 컨테이너(건물 보관함 등)를 이 인벤토리의 뒷단으로 연결 — UI가 실시간으로 그 컨테이너를 본다.</summary>
        public void Bind(ItemContainer external)
        {
            container = external;
            if (external != null) slotCount = external.SlotCount;   // 인스펙터 표시·UI 계산 일관성
        }

        /// <summary>UI 전환 프레임의 스테일 인덱스(구 소켓 호버 등) 방어.</summary>
        private bool InRange(int i) => i >= 0 && i < Container.SlotCount;

        // ── 위치 무관 연산 (루팅·드롭 등) ──────────────────────────

        /// <summary>전량 수용 가능할 때만 추가 (all-or-nothing). 실패 시 false — 호출자가 아이템을 보존할 것.</summary>
        public bool AddItem(ItemDef newItem, int count) => Container.TryAdd(newItem, count);

        // ── 위치(슬롯 인덱스) 연산 — UI 드래그·분할·교환용 위임 ──────

        /// <summary>슬롯의 스택 (없거나 범위 밖이면 null). amount를 직접 수정했다면 Touch()를 호출할 것.</summary>
        public ItemStack GetAt(int i) => InRange(i) ? Container.PeekAt(i) : null;

        /// <summary>슬롯의 스택을 통째로 꺼낸다 (픽업).</summary>
        public ItemStack TakeAt(int i) => InRange(i) ? Container.TakeAt(i) : null;

        /// <summary>빈 슬롯에 놓기. 규칙 위반이면 false.</summary>
        public bool TryPutAt(int i, ItemStack stack) => InRange(i) && Container.TryPutAt(i, stack);

        /// <summary>슬롯 스택과 교환 (스왑). 규칙 위반이면 false.</summary>
        public bool TryExchangeAt(int i, ItemStack incoming, out ItemStack previous)
        {
            previous = null;
            return InRange(i) && Container.TryExchangeAt(i, incoming, out previous);
        }

        /// <summary>인플레이스 amount 수정 후 변경 통지 (UI 갱신 판단용).</summary>
        public void Touch() => Container.Touch();
    }
}
