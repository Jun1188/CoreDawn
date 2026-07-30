using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 코어 진행률 UI 한 줄 — 아이템 아이콘 + 이름 + "현재/필요" 수량.
/// InventoryManager.OpenCoreScreen이 CoreBehavior.GetProgress()로 채운다.
/// </summary>
public class CoreRequirementRowUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI amountText;
    [Tooltip("선택 — Image.Type=Filled로 설정하면 진행률 바로 사용된다.")]
    [SerializeField] private Image fillBar;

    public void Set(ItemDataSO item, int current, int required)
    {
        if (icon != null)
        {
            icon.sprite = item != null ? item.icon : null;
            icon.enabled = item != null && item.icon != null;
        }

        if (nameText != null) nameText.text = item != null ? item.displayName : "";
        if (amountText != null) amountText.text = $"{current} / {required}";
        if (fillBar != null) fillBar.fillAmount = required > 0 ? Mathf.Clamp01((float)current / required) : 1f;
    }
}
