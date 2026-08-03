using UnityEngine;

/// <summary>
/// 탄약 아이템 — 무기·포탑이 소모하는 것. <see cref="WeaponItemSO"/>와 같은 패턴.
///
/// 피해량을 탄약이 갖고 포탑이 배율을 갖는 이유:
/// 탄약이 강해지면 그 탄을 쓰는 모든 포탑이 함께 강해져야 한다. 포탑마다 고정
/// 피해를 박아두면 새 탄약을 추가할 때마다 모든 포탑 수치를 다시 만져야 한다.
/// </summary>
[CreateAssetMenu(fileName = "NewAmmo", menuName = "Factory/AmmoItem")]
public class AmmoItemSO : ItemDataSO
{
    [Header("Ammo Specific")]
    [Tooltip("이 탄약 1발의 기본 피해. 포탑·총기가 각자의 배율을 곱해 쓴다.")]
    public float damage = 10f;
}
