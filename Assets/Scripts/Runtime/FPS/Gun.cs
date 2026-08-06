using System.Collections;
using UnityEngine;

/// <summary>
/// 플레이어 총기 — 단일 클래스, 상속 없음(구 WeaponBase/ProjectileGun/RaycastGun 통합).
/// 투사체냐 히트스캔이냐는 코드가 아니라 데이터(GunData.fireMode)가 정한다.
///
/// 이 클래스는 FPS 총의 역할만 담당한다: 탄창·재장전·연사 간격·탄퍼짐·반동.
/// 실제 발사(투사체 생성·히트스캔 판정·명중 효과)는 ProjectileSystem이 타워와 공용으로 처리한다.
/// </summary>
public class Gun : MonoBehaviour
{
    [Header("Core References")]
    public GunData gunData;
    public Transform muzzlePoint;
    [HideInInspector]
    public WeaponManager weaponManager;

    [Header("ADS Settings")]
    public Transform sightPoint; // 이 총의 가늠자(눈 위치) 앵커
    public float zoomFOV = 50f;  // 정조준 시 시야각

    [Header("Current States")]
    private int currentAmmo;

    /// <summary>현재 장전 수 — HUD 표시용 읽기 전용 (SCR-02).</summary>
    public int CurrentAmmo => currentAmmo;

    public bool isReloading = false;
    private float lastFireTime = 0f;
    private float currentSpread;
    private Rigidbody playerRb; // 플레이어의 속도 측정을 위함

    // 효과의 출처(Source)로 전달할 플레이어 엔티티.
    // Player는 BattleManager가 런타임에 부착하므로(Awake 시점엔 없을 수 있음) 찾을 때까지 재시도한다.
    private Entity ownerEntity;
    private Entity OwnerEntity =>
        ownerEntity != null ? ownerEntity : (ownerEntity = GetComponentInParent<Entity>());

    // 발사 시점의 실제 위력 — 기본 피해 × 플레이어 공격 버프. 투사체·히트스캔 공용.
    private float FirePower =>
        gunData.damage * (OwnerEntity != null ? OwnerEntity.Effects.AttackMultiplier : 1f);

    private void Awake()
    {
        playerRb = GetComponentInParent<Rigidbody>();
    }

    private void Start()
    {
        currentAmmo = gunData.magSize;
    }

    private void Update()
    {
        // 안 쏠 때는 에임이 다시 모임 (이동 속도에 따라 기본 탄퍼짐 증가)
        float speedFactor = (playerRb != null && playerRb.linearVelocity.magnitude > 1f) ? 2f : 1f; // 달리면 2배
        float targetSpread = gunData.baseSpread * speedFactor;

        currentSpread = Mathf.Lerp(currentSpread, targetSpread, Time.deltaTime * gunData.spreadRecoveryRate);
    }

    private void OnEnable()
    {
        // 무기를 스왑해서 꺼낼 때마다 상태 초기화
        isReloading = false;
    }

    private void OnDisable()
    {
        // 무기를 집어넣을 때 코루틴 안전하게 정지
        StopAllCoroutines();
    }

    // 매니저가 호출하는 사격 시도 함수
    public bool TryFire()
    {
        if (isReloading) return false;

        // 탄약 부족 처리
        if (currentAmmo <= 0)
        {
            StartReload();
            return false;
        }

        // 연사속도 제어
        if (Time.time < lastFireTime + gunData.fireRate) return false;

        // 실제 사격 로직 실행 (탄약 차감, 쿨타임 갱신)
        currentAmmo--;
        lastFireTime = Time.time;

        currentSpread = Mathf.Min(currentSpread + gunData.spreadIncreasePerShot, gunData.maxSpread);

        Fire();
        ApplyRecoil();

        return true;
    }

    // 발사 — 스펙(ProjectileShot)을 만들어 공용 시스템에 넘긴다. 타워도 같은 경로로 쏜다.
    private void Fire()
    {
        Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position;

        // 탄퍼짐 적용 (정면 방향에 랜덤한 구형 오차를 더함)
        Vector3 direction = (muzzlePoint != null ? muzzlePoint.forward : transform.forward);
        direction += Random.insideUnitSphere * (currentSpread / 100f);

        // 효과는 명중 시 전달 — 효과 미지정이면 위력만큼의 순수 피해.
        // 위력은 발사 시점 기준 (탄이 날아가는 동안 버프가 끝나도 발사 때 배율 유지)
        var shot = new ProjectileShot(gunData.bulletSpeed, 3f, gunData.range, FirePower,
                                      gunData.enemyLayer, gunData.attackEffects, OwnerEntity);

        if (gunData.fireMode == FireMode.Hitscan)
            ProjectileSystem.Hitscan(origin, direction, shot);
        else
            ProjectileSystem.Fire(gunData.bulletPrefab, origin, direction, shot);
    }

    private void ApplyRecoil()
    {
        CameraShakeManager.Instance.ShakeOnPlayerShoot(gunData.damage);
        weaponManager.recoilManager.FireRecoil(gunData.xRecoil, gunData.yRecoil, gunData.zRecoil);
        bool currentAimState = weaponManager.adsModule.isAiming; // ADS 모듈에서 현재 조준 상태 가져오기
        weaponManager.kickbackModule.Fire(gunData.visualKickbackZ, gunData.visualKickbackRot, currentAimState);
    }

    public void StartReload()
    {
        if (isReloading || currentAmmo == gunData.magSize || !gameObject.activeSelf) return;
        StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        isReloading = true;
        // 필요 시 애니메이션 트리거 호출
        // anim.SetTrigger("Reload");

        yield return new WaitForSeconds(gunData.reloadTime);

        currentAmmo = gunData.magSize;
        isReloading = false;
        Debug.Log($"{gunData.gunName} 재장전 완료!");
    }
}
