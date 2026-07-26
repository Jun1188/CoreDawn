using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CraftingUI : MonoBehaviour
{
    [Header("Recipe Database")]
    [SerializeField] private List<RecipeDataSO> availableRecipes = new List<RecipeDataSO>();

    [Header("Recipe List UI")]
    [SerializeField] private Transform recipeListContent;
    [SerializeField] private RecipeSlotUI recipeSlotPrefab;

    [Header("UI Inventory Grids")]
    public InventoryUI inputInventoryUI;   // 재료 4칸 Grid
    public InventoryUI outputInventoryUI;  // 결과 1칸 Grid

    [Header("Selected Recipe Info Display")]
    [SerializeField] private TextMeshProUGUI selectedRecipeNameText;
    [SerializeField] private TextMeshProUGUI selectedRecipeDescText;

    [Header("Crafting Controls")]
    public Button craftButton;
    public Slider progressBarSlider; // ★ Image 대신 Slider 컴포넌트를 연결!

    private RecipeDataSO selectedRecipe;
    private List<RecipeSlotUI> spawnedRecipeSlots = new List<RecipeSlotUI>();
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
    }

    private void Update()
    {
        // Slider value (0.0 ~ 1.0) 갱신
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

        foreach (var recipe in availableRecipes)
        {
            if (recipe == null || recipe.tier > 0) continue;

            RecipeSlotUI slot = Instantiate(recipeSlotPrefab, recipeListContent);
            slot.Init(recipe, OnSelectRecipe);
            spawnedRecipeSlots.Add(slot);
        }

        if (availableRecipes.Count > 0 && availableRecipes[0].tier == 0)
        {
            OnSelectRecipe(availableRecipes[0]);
        }
    }

    public void OnSelectRecipe(RecipeDataSO recipe)
    {
        selectedRecipe = recipe;

        if (selectedRecipeNameText != null) selectedRecipeNameText.text = recipe != null ? recipe.displayName : "";
        if (selectedRecipeDescText != null) selectedRecipeDescText.text = recipe != null ? recipe.description : "";

        // ★ 내가 클릭한 레시피만 하이라이트 켜기
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
}