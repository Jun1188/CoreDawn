using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RecipeManager : MonoBehaviour
{
    public static RecipeManager Instance { get; private set; }

    [Header("Auto Loaded Recipes")]
    [SerializeField] private List<RecipeDataSO> allRecipes = new List<RecipeDataSO>();

    public event Action<int> OnTierUnlocked;

    public int CurrentCoreTier => GameManager.Instance != null ? GameManager.Instance.UnlockedTier : 0;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            LoadAllRecipesAutomatically();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // GameManager의 티어 상승 이벤트를 중계
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TierUnlocked += (newTier) => OnTierUnlocked?.Invoke(newTier);
        }
    }

    private void LoadAllRecipesAutomatically()
    {
        // 1. 실제 빌드/런타임: Resources 폴더 내부 탐색
        RecipeDataSO[] loadedRecipes = Resources.LoadAll<RecipeDataSO>("");
        if (loadedRecipes != null && loadedRecipes.Length > 0)
        {
            allRecipes = loadedRecipes.ToList();
            Debug.Log($"<color=green>[RecipeManager] Resources 스캔 완료: 총 {allRecipes.Count}개 로드</color>");
        }

    #if UNITY_EDITOR
        // 2. 에디터 플레이 모드: Resources 폴더 밖에 있어도 프로젝트 전체에서 자동 수집
        if (allRecipes.Count == 0)
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:RecipeDataSO");
            allRecipes = guids
                .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<RecipeDataSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)))
                .Where(r => r != null)
                .ToList();

            Debug.Log($"<color=yellow>[RecipeManager] (Editor Fallback) 프로젝트 전체에서 {allRecipes.Count}개의 레시피를 찾았습니다!</color>");
        }
    #endif

        if (allRecipes.Count == 0)
        {
            Debug.LogWarning("[RecipeManager] 레시피 에셋을 하나도 찾지 못했습니다. 에셋 경로 및 Resources 폴더를 확인해주세요!");
        }
    }

    /// <summary> 특정 레시피가 코어 티어상 해금되었는지 검사 </summary>
    public bool IsTierUnlocked(RecipeDataSO recipe)
    {
        if (recipe == null) return false;
        return recipe.requiredCoreTier <= CurrentCoreTier;
    }

    /// <summary> 모든 레시피 목록 반환 </summary>
    public IReadOnlyList<RecipeDataSO> GetAllRecipes() => allRecipes;

    /// <summary> 
    /// [범용 함수] 조건에 맞는 레시피 목록 반환
    /// ex) 손제작 UI: GetRecipes(r => r.tier == 0)
    /// ex) 용광로 UI: GetRecipes(r => r.category == RecipeCategory.Smelting)
    /// </summary>
    public List<RecipeDataSO> GetRecipes(Predicate<RecipeDataSO> filter)
    {
        return allRecipes.Where(r => r != null && filter(r)).ToList();
    }
}