using UnityEngine;

public class PlayerInventoryHolder : MonoBehaviour
{
    public static PlayerInventoryHolder Instance { get; private set; }

    // ★ 인스펙터 창에서 슬롯 개수를 자유롭게 수정 가능!
    // 기본값은 SCR-04 — 핫바 7 · 가방 9×2. 칸 수가 곧 소지 한도로 보이는 화면이라
    // 여기 수치가 바뀌면 인벤토리 패널의 줄 수도 따라 바뀐다
    [Header("Slot Size Settings")]
    [SerializeField] private int hotbarSize = 7;
    [SerializeField] private int mainInventorySize = 18;
    [SerializeField] private int craftingInputSize = 4;
    [SerializeField] private int craftingOutputSize = 1;

    [Header("Starting Items (Inspector)")]
    [SerializeField] private ItemStack[] startingItems;
    // C# 데이터 메모리 공간
    public ItemContainer HotbarContainer { get; private set; }
    public ItemContainer MainContainer { get; private set; }
    public ItemContainer CraftingInputContainer { get; private set; }
    public ItemContainer CraftingOutputContainer { get; private set; }

    [Header("References")]
    public InventoryProcessor inventoryProcessor;
    public PlayerController playerController; // 플레이어 컨트롤러 참조

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        // 인스펙터에서 지정한 개수로 컨테이너 생성
        HotbarContainer = new ItemContainer(hotbarSize);
        MainContainer = new ItemContainer(mainInventorySize);
        CraftingInputContainer = new ItemContainer(craftingInputSize);
        CraftingOutputContainer = new ItemContainer(craftingOutputSize);

        // 2. 인스펙터에 등록된 시작 아이템 주입
        SeedStartingItems();
    }

    /// <summary>인스펙터 시작 아이템을 핫바/가방에 자동으로 적재</summary>
    private void SeedStartingItems()
    {
        if (startingItems == null) return;
        // 세이브를 불러오는 중이면 곧 저장된 소지품으로 덮어쓴다 — 시작 아이템까지 얹으면 중복 지급이 된다
        if (SaveLoadContext.IsRestoring) return;

        foreach (var stack in startingItems)
        {
            if (stack == null || stack.item == null || stack.amount <= 0) continue;
            AddItemToPlayer(stack.item, stack.amount);
        }
    }

    private void Start()
    {
        if (playerController == null) playerController = GetComponent<PlayerController>();

        if (playerController != null && playerController.inventoryUI != null)
        {
            playerController.inventoryUI.Bind(MainContainer);
        }

        // 핫바 UI 바인딩 (HotbarUI가 존재한다면)
        if (HotbarUI.Instance != null)
        {
            HotbarUI.Instance.Bind(HotbarContainer);
        }
    }

    /// <summary>아이템 획득 시: 핫바 -> 메인 가방 순으로 주입</summary>
    public bool AddItemToPlayer(ItemDataSO item, int amount)
    {
        if (item == null || amount <= 0) return false;

        // 1. 핫바 채우기 시도
        if (HotbarContainer.TryAdd(item, amount)) return true;

        // 2. 핫바 공간 부족 시 메인 가방 채우기 시도
        if (MainContainer.TryAdd(item, amount)) return true;

        return false;
    }

    /// <summary>제작 버튼 클릭 시 실행</summary>
    public void StartHandCrafting(RecipeDataSO recipe)
    {
        if (inventoryProcessor != null)
        {
            inventoryProcessor.SetRecipeAndStart(recipe);
        }
    }

    public void ReturnCraftingInputsToPlayer()
    {
        if (CraftingInputContainer == null) return;

        for (int i = 0; i < CraftingInputContainer.SlotCount; i++)
        {
            ItemStack stack = CraftingInputContainer.TakeAt(i);
            if (stack != null && stack.item != null && stack.amount > 0)
            {
                AddItemToPlayer(stack.item, stack.amount);
            }
        }
    }
}