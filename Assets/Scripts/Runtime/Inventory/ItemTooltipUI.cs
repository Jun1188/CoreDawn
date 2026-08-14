using UnityEngine;
using TMPro;

public class ItemTooltipUI : MonoBehaviour
{
    public static ItemTooltipUI Instance { get; private set; }

    [Header("UI References")]
    public GameObject tooltipPanel;    // 툴팁 전체를 감싸는 부모 오브젝트
    public TextMeshProUGUI nameText;   // 아이템 이름 텍스트
    public TextMeshProUGUI typeText;   // 아이템 타입 텍스트

    [Header("Position Offset")]
    public Vector2 mouseOffset = new Vector2(15f, -15f); // 마우스 커서에서 약간 빗겨나게 표시

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        HideTooltip(); // 시작할 때는 숨김
    }

    private void Update()
    {
        // 툴팁이 켜져있을 때 실시간으로 마우스 위치를 따라다님
        if (tooltipPanel.activeSelf)
        {
            transform.position = (Vector2)Input.mousePosition + mouseOffset;
        }
    }

    // 툴팁 정보 세팅 및 활성화
    public void ShowTooltip(ItemDataSO item)
    {
        if (item == null) return;

        nameText.text = item.displayName;
        typeText.text = $"유형: {item.type}"; // 예: 유형: Weapon, 유형: Ore

        // 역할 모듈별 추가 정보 — 타입 검사 대신 모듈 존재로 판정한다
        if (item.TryGetModule<WeaponModuleSO>(out var weapon) && weapon.gun != null)
            typeText.text += $" (공격력: {weapon.gun.BaseDamage})";
        else if (item.TryGetModule<AmmoModuleSO>(out var ammo))
            typeText.text += $" (피해: {ammo.BaseDamage})";

        tooltipPanel.SetActive(true);
    }

    public void HideTooltip()
    {
        if (tooltipPanel != null) tooltipPanel.SetActive(false);
    }
}