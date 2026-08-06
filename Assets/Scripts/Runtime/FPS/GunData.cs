using UnityEngine;

public enum WeaponType { Pistol, SubMachineGun, Sniper }

[CreateAssetMenu(fileName = "GunData", menuName = "ScriptableObjects/GunData", order = 1)]
public class GunData : ScriptableObject
{
    [Header("Weapon Identity")]
    public string gunName = "Pistol";
    public WeaponType weaponType;
    public bool isAutomatic;             // SMG는 true, 권총/저격은 false

    [Header("Gun General Settings")]
    public float damage = 10f;
    public float fireRate = 0.2f;        // 연사 속도 (낮을수록 빠름)
    public float bulletSpeed = 50f;
    public float maxRange = 100f;
    public GameObject bulletPrefab;
    public float range;
    public LayerMask enemyLayer;

    [Tooltip("명중 시 적용할 효과들. 비우면 damage만큼의 순수 피해. " +
             "피해 효과를 넣으면 damage가 Power로 전달돼 배율이 곱해진다.")]
    public EffectSO[] attackEffects;

    [Header("Ammo & Reload Settings")]
    public int magSize = 30;
    public float reloadTime = 1.5f;

    [Header("Recoil Settings (반동 제어)")]
    public float xRecoil = 3f;     
    public float yRecoil = 2f;
    public float zRecoil = 1f;
    public float visualKickbackZ = 1;
    public Vector3 visualKickbackRot = new Vector3(1, 1, 1);

    [Header("Spread (탄퍼짐)")]
    public float baseSpread = 0.5f;        // 기본 탄퍼짐 (가만히 있을 때)
    public float maxSpread = 5f;           // 최대 탄퍼짐
    public float spreadIncreasePerShot = 1f; // 쏠 때마다 늘어나는 수치
    public float spreadRecoveryRate = 5f;    // 다시 에임이 모이는 속도
}