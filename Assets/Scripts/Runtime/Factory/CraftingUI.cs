using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CraftingUI : MonoBehaviour
{
    [Header("=== Crafting Panel Toggle ===")]
    [SerializeField] private GameObject craftingPanel;      
    [SerializeField] private Button toggleCraftingButton;   
    
    [Header("Recipe List UI")]
    [SerializeField] private Transform recipeListContent;
    [SerializeField] private RecipeSlotUI recipeSlotPrefab;

    [Header("Recipe Panel Toggle")]
    [SerializeField] private GameObject recipeSidePanel; 
    [SerializeField] private Button recipeListButton; 

    private bool isRecipePanelOpen = false;

    [Header("UI Inventory Grids")]
    public InventoryUI inputInventoryUI;   
    public InventoryUI outputInventoryUI;  

    [Header("Selected Recipe Info Display")]
    [SerializeField] private TextMeshProUGUI selectedRecipeNameText;
    [SerializeField] private TextMeshProUGUI selectedRecipeDescText;

    [Header("Crafting Controls")]
    public Button craftButton;
    public Slider progressBarSlider; 

    [Header("Crafting Display Policy")]
    [Tooltip("체크 시 0티어(손제작) 외 고티어 레시피는 아예 스크롤뷰 목록에서 숨깁니다.")]
    [SerializeField] private bool hideNonHandCraftableRecipes = false;

    private RecipeDataSO selectedRecipe;
    private readonly List<RecipeSlotUI> spawnedRecipeSlots = new();
    private InventoryProcessor processor;

    private void Start()
    {
        var holder = PlayerInventoryHolder.Instance;
        if (holder != null)
        {
            if (inputInventoryUI != null) inputInventoryUI.Bind(holder.CraftingInputContainer);
            if (outputInventoryUI != null) outputInventoryUI.Bind(holder.CraftingOutputContainer);
            processor = holder.inventoryProcessor;
        }

        if (craftButton != null)
            craftButton.onClick.AddListener(OnClickCraftButton);

        if (processor != null)
        {
            processor.OnProductionStarted += OnCraftStarted;
            processor.OnProductionCompleted += OnCraftCompleted;
            processor.OnProductionStopped += OnCraftStopped;
        }

        // RecipeManager 이벤트 구독
        if (RecipeManager.Instance != null)
        {
            RecipeManager.Instance.OnTierUnlocked += OnTierUnlocked;
        }

        GenerateRecipeList();
    }

    private void OnDestroy()
    {
        if (processor != null)
        {
            processor.OnProductionStarted -= OnCraftStarted;
            processor.OnProductionCompleted -= OnCraftCompleted;
            processor.OnProductionStopped -= OnCraftStopped;
        }

        if (RecipeManager.Instance != null)
        {
            RecipeManager.Instance.OnTierUnlocked -= OnTierUnlocked;
        }
    }

    private void OnTierUnlocked(int _) => GenerateRecipeList();

    private void Update()
    {
        if (processor != null && processor.IsProcessing())
        {
            if (progressBarSlider != null)
                progressBarSlider.value = processor.GetProgress();
        }
    }

    private void GenerateRecipeList()
    {
        if (recipeListContent == null || recipeSlotPrefab == null) return;

        foreach (Transform child in recipeListContent)
            Destroy(child.gameObject);
        spawnedRecipeSlots.Clear();

        if (RecipeManager.Instance == null) return;

        var allRecipes = RecipeManager.Instance.GetAllRecipes();

        foreach (var recipe in allRecipes)
        {
            if (recipe == null) continue;

            bool isHandCraftable = (recipe.tier == 0);
            bool isTierUnlocked = RecipeManager.Instance.IsTierUnlocked(recipe);

            if (hideNonHandCraftableRecipes && !isHandCraftable) continue;

            bool isSelectable = isHandCraftable && isTierUnlocked;

            RecipeSlotUI slot = Instantiate(recipeSlotPrefab, recipeListContent);
            slot.Init(recipe, isSelectable, OnSelectRecipe);
            spawnedRecipeSlots.Add(slot);
        }

        ClearSelection();
    }

    public void ClearSelection()
    {
        selectedRecipe = null;

        if (selectedRecipeNameText != null) selectedRecipeNameText.text = "";
        if (selectedRecipeDescText != null) selectedRecipeDescText.text = "";

        // 생성된 모든 레시피 슬롯의 하이라이트 끄기
        foreach (var slot in spawnedRecipeSlots)
        {
            if (slot != null)
                slot.SetHighlight(false);
        }
    }

    public void ToggleRecipePanel()
    {
        isRecipePanelOpen = !isRecipePanelOpen;
        if (recipeSidePanel != null) recipeSidePanel.SetActive(isRecipePanelOpen);
    }

    public void ToggleCraftingUI()
    {
        if (craftingPanel != null)
        {
            bool isCurrentActive = craftingPanel.activeInHierarchy;
            bool nextActive = !isCurrentActive;
            
            craftingPanel.SetActive(nextActive);

            // 창을 다시 열거나 닫을 때 선택 상태 초기화
            ClearSelection();
        }
    }

    public void OnSelectRecipe(RecipeDataSO recipe)
    {
        selectedRecipe = recipe;

        if (selectedRecipeNameText != null) selectedRecipeNameText.text = recipe != null ? recipe.displayName : "";
        if (selectedRecipeDescText != null) selectedRecipeDescText.text = recipe != null ? recipe.description : "";

        foreach (var slot in spawnedRecipeSlots)
        {
            slot.SetHighlight(slot.TargetRecipe == recipe);
        }
    }

    private void OnClickCraftButton()
    {
        if (selectedRecipe == null) return;

        if (PlayerInventoryHolder.Instance != null)
        {
            PlayerInventoryHolder.Instance.StartHandCrafting(selectedRecipe);
        }
    }

    private void OnCraftStarted()
    {
        if (craftButton != null) craftButton.interactable = false;
    }

    private void OnCraftCompleted(RecipeDataSO recipe)
    {
        if (craftButton != null) craftButton.interactable = true;
        if (progressBarSlider != null) progressBarSlider.value = 0f;
    }

    private void OnCraftStopped()
    {
        if (craftButton != null) craftButton.interactable = true;
        if (progressBarSlider != null) progressBarSlider.value = 0f;
    }

    private void OnEnable()
    {
        isRecipePanelOpen = false; 
        if (recipeSidePanel != null) recipeSidePanel.SetActive(false);
        if (craftingPanel != null) craftingPanel.SetActive(false);

        ClearSelection();
    }
}