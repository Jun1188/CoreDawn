using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RecipeSlotUI : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private Image recipeIcon;
    [SerializeField] private TextMeshProUGUI recipeNameText;
    [SerializeField] private Button button;
    [SerializeField] private GameObject selectHighlight; // ★ 프리팹 전체가 아닌 '자식 하이라이트 이미지'를 할당해야 함!

    public RecipeDataSO TargetRecipe { get; private set; } // 외부에서 어떤 레시피인지 확인용
    private System.Action<RecipeDataSO> onSelectCallback;

    public void Init(RecipeDataSO recipe, System.Action<RecipeDataSO> callback)
    {
        TargetRecipe = recipe;
        onSelectCallback = callback;

        if (recipeIcon != null && recipe.icon != null) recipeIcon.sprite = recipe.icon;
        if (recipeNameText != null) recipeNameText.text = recipe.displayName;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClicked);
        }

        SetHighlight(false);
    }

    private void OnClicked()
    {
        onSelectCallback?.Invoke(TargetRecipe);
    }

    public void SetHighlight(bool isSelected)
    {
        // ★ 실수로 자기 자신(this.gameObject)을 넣었을 때 꺼지는 현상 방지 안전장치
        if (selectHighlight != null && selectHighlight != this.gameObject)
        {
            selectHighlight.SetActive(isSelected);
        }
    }
}