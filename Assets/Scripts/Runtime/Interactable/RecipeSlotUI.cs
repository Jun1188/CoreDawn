using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class RecipeSlotUI : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Button button;
    [SerializeField] private GameObject highlightOverlay;
    [SerializeField] private GameObject lockOverlay; // 잠금/손제작불가 아이콘 또는 레이어
    [SerializeField] private CanvasGroup canvasGroup; // 옅은 회색(투명도) 조절용

    public RecipeDataSO TargetRecipe { get; private set; }

    public void Init(RecipeDataSO recipe, bool isSelectable, Action<RecipeDataSO> onSelect)
    {
        TargetRecipe = recipe;

        if (nameText != null) 
            nameText.text = recipe != null ? recipe.displayName : "";

        // ★ 아이콘 적용 로직 강화
        if (iconImage != null && recipe != null)
        {
            // 1. 레시피 자체 아이콘 확인
            Sprite displaySprite = recipe.icon;

            // 2. 레시피 아이콘이 없다면 첫 번째 결과물(Output) 아이템의 아이콘을 가져옴
            if (displaySprite == null && recipe.outputs != null && recipe.outputs.Length > 0)
            {
                if (recipe.outputs[0].item != null)
                {
                    displaySprite = recipe.outputs[0].item.icon;
                }
            }

            // 3. 스프라이트 할당 및 이미지 활성화
            if (displaySprite != null)
            {
                iconImage.sprite = displaySprite;
                iconImage.enabled = true;
            }
            else
            {
                // 아이콘이 전혀 없을 때는 투명하게 숨김 처리
                iconImage.enabled = false; 
            }
        }
        // 버튼 클릭 이벤트 세팅
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onSelect?.Invoke(recipe));
            button.interactable = isSelectable; // 제작 불가능하면 클릭 불가!
        }

        // 시각적 비활성화 (옅은 회색 / 잠금 레이어)
        if (canvasGroup != null)
        {
            canvasGroup.alpha = isSelectable ? 1.0f : 0.4f; // 손제작 불가는 옅게 처리
        }

        if (lockOverlay != null)
        {
            lockOverlay.SetActive(!isSelectable);
        }
    }

    public void SetHighlight(bool isSelected)
    {
        if (highlightOverlay != null)
            highlightOverlay.SetActive(isSelected);
    }
}