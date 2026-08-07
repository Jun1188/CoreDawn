using UnityEngine;

/// <summary>발사 방식 — 클래스 상속이 아니라 데이터가 정한다. Gun과 (향후) 타워가 공용.</summary>
public enum FireMode
{
    Projectile, // bulletPrefab을 날린다 — 탄속·탄도 있음
    Hitscan,    // 즉시 판정 — 속도 무한의 발사 (레이저·저격)
}

/// <summary>
/// 총 하나의 데이터 전부 — 전투 정합(발사 방식·효과·연사·탄창)과 감각 튜닝(반동·탄퍼짐).
/// 컴포넌트(Gun)는 상태만 갖고, 수치는 전부 여기다.
/// </summary>
[CreateAssetMenu(fileName = "GunData", menuName = "ScriptableObjects/GunData", order = 1)]
public class GunData : ScriptableObject
{
    [Header("Identity")]
    public string gunName = "Pistol";
    [Tooltip("눌림 유지로 연사되는가. 권총/저격은 false — 클릭마다 한 발.")]
    public bool isAutomatic;

    [Header("발사")]
    [Tooltip("발사 방식 — Projectile은 bulletPrefab을 날리고, Hitscan은 즉시 판정.")]
    public FireMode fireMode = FireMode.Projectile;
    [Tooltip("발사 간격(초). 낮을수록 빠른 연사.")]
    public float fireRate = 0.2f;
    public float bulletSpeed = 50f;
    [Tooltip("사거리(m) — 투사체 소멸·히트스캔 판정 한계.")]
    public float range;
    public GameObject bulletPrefab;
    public LayerMask enemyLayer;

    [Tooltip("명중 시 무슨 일이 일어나는가 — 이 총의 공격 정의 전부. " +
             "피해도 항목의 하나다: {Damage, 20} = 피해 20. bare 피해 필드는 없다.")]
    public EffectEntry[] attackEffects;

    [Header("탄창")]
    public int magSize = 30;
    public float reloadTime = 1.5f;

    [Header("조준 (ADS)")]
    [Tooltip("정조준 시 시야각.")]
    public float zoomFOV = 50f;

    [Header("반동 (Recoil — 카메라 회전)")]
    public float xRecoil = 3f;
    public float yRecoil = 2f;
    public float zRecoil = 1f;

    [Header("킥백 (시각 반동 — 무기 모델)")]
    public float visualKickbackZ = 1;
    public Vector3 visualKickbackRot = new Vector3(1, 1, 1);

    [Header("탄퍼짐 (Spread)")]
    public float baseSpread = 0.5f;          // 기본 탄퍼짐 (가만히 있을 때)
    public float maxSpread = 5f;             // 최대 탄퍼짐
    public float spreadIncreasePerShot = 1f; // 쏠 때마다 늘어나는 수치
    public float spreadRecoveryRate = 5f;    // 다시 에임이 모이는 속도

    /// <summary>피해 항목들의 value 합 — 반동 연출 크기·툴팁 표기용 (전투 계산엔 쓰지 않는다).</summary>
    public float BaseDamage
    {
        get
        {
            float sum = 0f;
            if (attackEffects != null)
                foreach (var e in attackEffects)
                    if (e.effect is DamageEffectSO) sum += e.value;
            return sum;
        }
    }
}
