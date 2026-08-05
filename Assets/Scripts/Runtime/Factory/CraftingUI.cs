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
    [Tooltip("체크 시 아직 해금되지 않은 레시피를 목록에서 아예 숨깁니다 (끄면 잠긴 채로 표시).")]
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

        // 티어 해금 시 목록 갱신
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TierUnlocked += OnTierUnlocked;
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

        if (GameManager.Instance != null)
        {
            GameManager.Instance.TierUnlocked -= OnTierUnlocked;
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

        var db = RecipeDatabaseSO.LoadDefault();
        if (db == null || db.recipes == null) return;

        foreach (var recipe in db.recipes)
        {
            if (recipe == null) continue;

            // 해금되면 전부 수동 제작이 가능하다 (레퍼런스 SCR-04) —
            // 구 "tier==0만 손제작" 규칙은 폐기, tier는 해금 게이트 하나로 통합됐다
            bool isSelectable = RecipeDatabaseSO.IsUnlocked(recipe);

            if (hideNonHandCraftableRecipes && !isSelectable) continue;

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