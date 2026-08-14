using UnityEngine;

/// <summary>
/// 총 하나의 데이터 전부 — 전투 정합(발사 방식·연사·탄창)과 감각 튜닝(반동·탄퍼짐).
/// 컴포넌트(Gun)는 상태만 갖고, 수치는 전부 여기다.
///
/// <b>명중 효과와 탄도(속도·중력·폭발·외형)는 총이 아니라 탄약이 정의한다</b> — 타워와 같은 원칙:
/// 탄약 = 효과 + 탄도(AmmoModuleSO), 총 = 각도(조준·탄퍼짐) + 연사 + 배율 + 소비.
/// 탄약이 강해지면 그 탄을 쓰는 총·포탑이 함께 강해진다. (지금은 ammo 참조로 탄종이
/// 고정되고 실소비는 추상 탄창 — 인벤토리 소비·탄종 전환은 후속 작업)
///
/// GameDataSO 상속인 이유: 총 수치는 json(GameData)이 소유한다 — 임포터가 id("Gun:이름")로
/// 찾아 갱신하고, 무기 아이템의 WeaponModuleSO가 json의 gun 필드로 참조를 배선한다.
/// 단 enemyLayer는 씬 참조라 json 밖 — 인스펙터에서 배선한다.
/// </summary>
[CreateAssetMenu(fileName = "GunData", menuName = "ScriptableObjects/GunData", order = 1)]
public class GunData : GameDataSO
{
    [Header("동작")]
    [Tooltip("눌림 유지로 연사되는가. 권총/저격은 false — 클릭마다 한 발.")]
    public bool isAutomatic;

    [Header("발사")]
    [Tooltip("전달 방식 — Projectile은 탄약의 bulletPrefab을 날리고, Hitscan은 즉시 판정.")]
    public FireMode fireMode = FireMode.Projectile;
    [Tooltip("발사 간격(초). 낮을수록 빠른 연사.")]
    public float fireRate = 0.2f;
    [Tooltip("사거리(m) — 투사체 소멸·히트스캔 판정 한계.")]
    public float range;
    [Tooltip("한 번의 방아쇠로 나가는 탄 수 — 샷건 8. 펠릿마다 따로 탄퍼짐을 받고, 탄창도 그만큼 소비한다.")]
    [Min(1)] public int pellets = 1;
    public LayerMask enemyLayer;

    [Header("탄약 (효과·탄도의 주인)")]
    [Tooltip("장전 가능한 탄종들(AmmoModuleSO 필수) — 첫 항목이 기본 탄종이고, 탄종 전환(V)은 이 목록 안에서 돈다. " +
             "포탑 ammoFilter와 같은 개념. json의 ammoFilter 필드로 임포터가 배선한다.")]
    public ItemDataSO[] ammoFilter;

    [Tooltip("탄약 효과 중 피해형(Damage·DoT) 항목에 곱하는 배율 — 포탑의 damageMultiplier와 같은 개념.")]
    public float damageMultiplier = 1f;

    /// <summary>기본 탄종 = ammoFilter의 첫 항목 — 없으면 null (발사 불가).</summary>
    public ItemDataSO DefaultAmmo => ammoFilter != null && ammoFilter.Length > 0 ? ammoFilter[0] : null;

    /// <summary>기본 탄종의 모듈(효과+탄도) — 없으면 null.</summary>
    public AmmoModuleSO AmmoModule => DefaultAmmo != null ? DefaultAmmo.GetModule<AmmoModuleSO>() : null;

    /// <summary>기본 탄종의 명중 효과 — 없으면 null (발사해도 아무 일도 없음).</summary>
    public EffectEntry[] AmmoEffects => AmmoModule?.attackEffects;

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

    /// <summary>방아쇠 한 번의 피해 총량(피해 항목 합 × 배율 × 펠릿 수) — 반동 연출 크기·툴팁 표기용 (전투 계산엔 쓰지 않는다).</summary>
    public float BaseDamage
    {
        get
        {
            float sum = 0f;
            var effects = AmmoEffects;
            if (effects != null)
                foreach (var e in effects)
                    if (e.effect is DamageEffectSO) sum += e.value;
            return sum * damageMultiplier * Mathf.Max(1, pellets);
        }
    }
}
