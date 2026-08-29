using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.Factory;
using CoreDawn.Managers;
using CoreDawn.Save;
using CoreDawn.UI;
using CoreDawn.Data;
using CoreDawn.Sound;

namespace CoreDawn.Inventories
{
    public class PlayerInventoryHolder : MonoBehaviour
    {
        public static PlayerInventoryHolder Instance { get; private set; }

        // ★ 인스펙터 창에서 슬롯 개수를 자유롭게 수정 가능!
        // 기본값은 SCR-04 — 핫바 7 · 가방 9×2. 칸 수가 곧 소지 한도로 보이는 화면이라
        // 여기 수치가 바뀌면 인벤토리 패널의 줄 수도 따라 바뀐다
        [Header("Slot Size Settings")]
        [SerializeField] private int hotbarSize = 7;
        [SerializeField] private int mainInventorySize = 18;

        [Header("Starting Items (Inspector)")]
        [SerializeField] private ItemStack[] startingItems;

        // C# 데이터 메모리 공간
        // (구 제작 4+1 컨테이너 제거 — uGUI 손제작 화면 전용이었다. UITK 인벤토리 패널은
        //  가방·핫바에서 직접 재료를 소모하고 결과를 돌려주므로 별도 그릇이 필요 없다)
        public ItemContainer HotbarContainer { get; private set; }
        public ItemContainer MainContainer { get; private set; }

        [Header("References")]
        public PlayerController playerController; // 플레이어 컨트롤러 참조

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            // 인스펙터에서 지정한 개수로 컨테이너 생성
            HotbarContainer = new ItemContainer(hotbarSize);
            MainContainer = new ItemContainer(mainInventorySize);

            // 2. 인스펙터에 등록된 시작 아이템 주입
            SeedStartingItems();
        }

        /// <summary>인스펙터 시작 아이템을 핫바/가방에 자동으로 적재</summary>
        private void SeedStartingItems()
        {
            if (startingItems == null) return;
            // 세이브를 불러오는 중이면 곧 저장된 소지품으로 덮어쓴다 — 시작 아이템까지 얹으면 중복 지급이 된다
            if (SaveLoadContext.IsRestoring) return;

            // 시작 소지품은 "주운 것"이 아니다 — 시작하자마자 획득음이 여러 번 겹쳐 울리면
            // 무엇을 주웠다는 신호가 아니라 그냥 잡음이 된다
            silentAdd = true;
            try
            {
                foreach (var stack in startingItems)
                {
                    if (stack == null || stack.item == null || stack.amount <= 0) continue;
                    AddItemToPlayer(stack.item, stack.amount);
                }
            }
            finally { silentAdd = false; }
        }

        /// <summary>true인 동안 <see cref="AddItemToPlayer"/>가 획득음을 내지 않는다 (시작 지급·세이브 복원).</summary>
        private bool silentAdd;

        private void Start()
        {
            // UITK 인벤토리(PlayerItemPanelView)가 드롭 위치·시선을 이 참조로 얻는다 — 인스펙터 배선이 없으면 같은 오브젝트에서 찾는다
            if (playerController == null) playerController = GetComponent<PlayerController>();
        }

        /// <summary>
        /// 아이템 획득 시: 핫바 -> 메인 가방 순으로 주입.
        ///
        /// 획득음은 여기 한 곳에서 낸다 — 줍기·손 채굴·제작 산출·환급·저장고 회수가 전부 이 문을 지나므로,
        /// 호출처마다 소리를 붙이면 어딘가는 빠지고 어딘가는 두 번 난다.
        /// 실패(가득 참)에는 소리가 없다 — 들어가지 않은 것을 들어간 것처럼 들려주면 안 된다.
        ///
        /// silent: 호출처가 같은 순간 자기 소리를 내는 경우(손 채굴의 Mine음 등) 획득음을 건너뛴다 —
        /// 같은 프레임에 같은 AudioSource로 두 소리를 겹치면 파형이 합산돼 찢어진 소리가 난다.
        /// </summary>
        public bool AddItemToPlayer(ItemDataSO item, int amount, bool silent = false)
        {
            if (item == null || amount <= 0) return false;

            // 1. 핫바 채우기 시도
            // 2. 핫바 공간 부족 시 메인 가방 채우기 시도
            if (!HotbarContainer.TryAdd(item, amount) && !MainContainer.TryAdd(item, amount))
                return false;

            if (!silent && !silentAdd && !SaveLoadContext.IsRestoring)
                SoundManager.Instance?.PlayCommonSFX(CommonSFX.ItemPickup);

            return true;
        }

        // (구 StartHandCrafting / ReturnCraftingInputsToPlayer 제거 — uGUI 손제작 화면 전용이었다.
        //  UITK 인벤토리 패널이 가방·핫바에서 직접 소모·지급하므로 재료를 맡아두는 그릇도,
        //  화면을 닫을 때 돌려주는 절차도 필요 없다)
    }
}
