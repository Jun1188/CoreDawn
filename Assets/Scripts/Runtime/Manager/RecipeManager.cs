using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RecipeManager : MonoBehaviour
{
    public static RecipeManager Instance { get; private set; }

    [Header("Loaded Recipes")]
    [SerializeField] private List<RecipeDataSO> allRecipes = new List<RecipeDataSO>();

    /// <summary> 티어가 해금될 때 UI나 타 시스템에 알리는 이벤트 </summary>
    public event Action<int> OnTierUnlocked;

    /// <summary> 현재 게임의 코어 티어 (GameManager 연동) </summary>
    public int CurrentCoreTier => GameManager.Instance != null ? GameManager.Instance.UnlockedTier : 0;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            LoadAllRecipes();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TierUnlocked += HandleTierUnlocked;
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TierUnlocked -= HandleTierUnlocked;
        }
    }

    private void HandleTierUnlocked(int newTier)
    {
        Debug.Log($"<color=cyan>[RecipeManager] 코어 티어 상승: {newTier}티어</color>");
        OnTierUnlocked?.Invoke(newTier);
    }

    private void LoadAllRecipes()
    {
        RecipeDataSO[] loaded = Resources.LoadAll<RecipeDataSO>("");
        if (loaded != null && loaded.Length > 0)
        {
            allRecipes = loaded.ToList();
        }

#if UNITY_EDITOR
        if (allRecipes.Count == 0)
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:RecipeDataSO");
            allRecipes = guids
                .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<RecipeDataSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)))
                .Where(r => r != null)
                .ToList();
        }
#endif
    }


    /// <summary> 단일 레시피의 코어 티어 해금 여부 확인 </summary>
    public bool IsRecipeUnlocked(RecipeDataSO recipe)
    {
        if (recipe == null) return false;
        
        // requiredCoreTier 대신 recipe.tier 사용!
        return recipe.tier <= CurrentCoreTier;
    }

    /// <summary> 현재 티어에서 사용 가능한(해금된) 레시피 목록만 반환 </summary>
    public List<RecipeDataSO> GetUnlockedRecipes()
    {
        return allRecipes.Where(r => r != null && IsRecipeUnlocked(r)).ToList();
    }

    /// <summary> 아직 잠겨있는(미해금) 레시피 목록만 반환 </summary>
    public List<RecipeDataSO> GetLockedRecipes()
    {
        return allRecipes.Where(r => r != null && !IsRecipeUnlocked(r)).ToList();
    }

    /// <summary> 전체 레시피 목록 반환 </summary>
    public IReadOnlyList<RecipeDataSO> GetAllRecipes() => allRecipes;
}