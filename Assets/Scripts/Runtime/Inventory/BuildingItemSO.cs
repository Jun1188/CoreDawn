using UnityEngine;

/// <summary>
/// 건물을 인벤토리에 담기 위한 아이템. WeaponItemSO가 총기 데이터를 물고 다니는 것과 같은 패턴이다
/// (아이템 = 인벤토리에 들어가는 것, 여기 붙은 SO = 그 아이템이 무엇이 되는가).
///
/// 설치하면 인벤토리에서 1개 빠지고 월드에 건물이 서고, 회수하면 다시 1개가 들어온다.
/// </summary>
[CreateAssetMenu(fileName = "NewBuildingItem", menuName = "Factory/BuildingItem")]
public class BuildingItemSO : ItemDataSO
{
    [Header("Building Specific")]
    [Tooltip("이 아이템을 설치했을 때 세워지는 건물.")]
    public BuildingDataSO building;
}
