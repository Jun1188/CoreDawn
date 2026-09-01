using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.FPS;
using CoreDawn.Save;
using CoreDawn.Data;
using CoreDawn.Sound;
using CoreDawn.Sim;

namespace CoreDawn.Inventories
{
    /// <summary>
    /// 플레이어 소지품의 씬 접점. 소지품(main, 앞 HotbarSize칸이 핫바)은 플레이어 <b>엔티티</b>의 <see cref="InventoryModule"/>(팩 정의
    /// <c>coredawn:entity/player</c>가 칸 수를 정한다)의 것이고, 이 컴포넌트는 그 그릇을 UI·총기·비용 지불에 내주는 창구다.
    ///
    /// 엔티티는 여기서 먼저 만든다(PlayerSystem.Spawn — 이미 있으면 그것): 뷰(PlayerView)는 BattleManager가 Start에서
    /// 붙이는데 핫바·HUD는 Awake·Start부터 그릇을 읽기 때문이다. 팩이 없는 씬은 인스펙터 칸 수로 폴백한다.
    /// </summary>
    public class PlayerInventoryHolder : MonoBehaviour
    {
        public static PlayerInventoryHolder Instance { get; private set; }

        public const string PlayerDefId = "coredawn:entity/player";

        // 팩이 없는 씬(테스트)의 폴백 칸 수 — 정본은 팩 정의의 Inventory 모듈(main·hotbar)
        [Header("Slot Size Settings (팩 정의가 없을 때만)")]
        [SerializeField] private int hotbarSize = 7;
        [SerializeField] private int mainInventorySize = 18;

        [Header("Starting Items (Inspector)")]
        [SerializeField] private ItemStackAuthoring[] startingItems;

        /// <summary>플레이어 엔티티 — HP·효과·가방·제작이 여기 산다.</summary>
        public Entity Entity { get; private set; }
        public InventoryModule Inventory { get; private set; }
        /// <summary>소지품 전체 — 앞 <see cref="HotbarSize"/>칸이 핫바. 넣기는 앞(핫바)부터, 빼기는 뒤(가방)부터.</summary>
        public ItemContainer MainContainer { get; private set; }
        /// <summary>핫바로 보이는 앞 칸 수(장착 선택 범위).</summary>
        public int HotbarSize { get; private set; }

        [Header("References")]
        public PlayerController playerController; // 플레이어 컨트롤러 참조

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            Entity = SpawnEntity();
            Inventory = Entity.Get<InventoryModule>();
            MainContainer = Inventory?.Main;
            HotbarSize = Inventory?.HotbarSize ?? 0;
            if (MainContainer == null)
                Debug.LogError("[PlayerInventoryHolder] 플레이어 정의에 Inventory(main)가 없습니다 — 소지품이 비어 있습니다.", this);

            // 2. 인스펙터에 등록된 시작 아이템 주입
            SeedStartingItems();
        }

        /// <summary>정의(coredawn:entity/player)로 조립. 팩이 없으면 인스펙터 칸 수로 폴백 — 그래도 같은 모듈 구조다.</summary>
        Entity SpawnEntity()
        {
            var players = SimRunner.Players;
            var db = SimHost.Database;
            var def = db?.Entity(PlayerDefId);
            if (def != null) return players.Spawn(def, transform.position);

            Debug.LogWarning($"[PlayerInventoryHolder] 팩에 '{PlayerDefId}' 정의가 없어 인스펙터 칸 수로 폴백합니다.", this);
            var e = players.Spawn(100f, transform.position);
            if (e.Get<InventoryModule>() == null)
                e.Add(new InventoryModule(new InventoryModuleDef { Main = hotbarSize + mainInventorySize, Hotbar = hotbarSize }));
            if (e.Get<CrafterModule>() == null)
                e.Add(new CrafterModule(new CrafterModuleDef { Manual = true }));
            return e;
        }

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
                foreach (var authored in startingItems)
                {
                    var stack = authored != null ? authored.ToStack() : ItemStack.Empty;   // id → 정의 (팩에 없는 아이템은 경고 후 건너뛴다)
                    if (stack.IsEmpty) continue;
                    AddItemToPlayer(stack.item, stack.amount);
                }
            }
            finally { silentAdd = false; }
        }

        private bool silentAdd;

        private void Start()
        {
            // UITK 인벤토리(PlayerItemPanelView)가 드롭 위치·시선을 이 참조로 얻는다 — 인스펙터 배선이 없으면 같은 오브젝트에서 찾는다
            if (playerController == null) playerController = GetComponent<PlayerController>();
        }

        public bool AddItemToPlayer(ItemDef item, int amount, bool silent = false)
        {
            if (item == null || amount <= 0) return false;
            if (MainContainer == null) return false;
            // 앞 칸(핫바)부터 채워진다 — 그릇 하나의 규칙이라 경로마다 순서를 따로 정하지 않는다
            if (!MainContainer.TryAdd(item, amount)) return false;
            if (!silent && !silentAdd && !SaveLoadContext.IsRestoring)
                SoundManager.Instance?.PlayCommonSFX(CommonSFX.ItemPickup);
            return true;
        }
    }
}
