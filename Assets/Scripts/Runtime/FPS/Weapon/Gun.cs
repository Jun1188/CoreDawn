using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 플레이어 총기 — 게임플레이만 담당한다: 탄창·재장전·연사 간격·탄퍼짐·발사.
/// 수치는 전부 GunData(데이터), 여기는 상태(남은 탄·재장전 중·현재 탄퍼짐)뿐이다.
///
/// 연출(반동·킥백·셰이크)은 모른다 — 발사하면 <see cref="Fired"/>만 알리고,
/// 반응은 WeaponManager가 연출 모듈들로 팬아웃한다. 실제 발사(투사체·히트스캔·명중 효과)는
/// ProjectileSystem이 타워와 공용으로 처리한다.
/// </summary>
public class Gun : MonoBehaviour
{
    [Header("Core References")]
    public GunData gunData;
    public Transform muzzlePoint;

    [Tooltip("이 총의 가늠자(눈 위치) 앵커 — ADS가 카메라 정렬에 쓴다.")]
    public Transform sightPoint;

    /// <summary>발사 성공 순간 발화 — WeaponManager가 구독해 연출(반동·킥백·셰이크)을 반응시킨다.</summary>
    public event Action<Gun> Fired;

    /// <summary>현재 장전 수 — HUD 표시용 읽기 전용 (SCR-02).</summary>
    public int CurrentAmmo { get; private set; }

    /// <summary>재장전 중인가 — 읽기 전용. 진행은 StartReload로만 시작된다.</summary>
    public bool IsReloading { get; private set; }

    private float lastFireTime;
    private float currentSpread;
    private Rigidbody playerRb; // 이동 속도에 따른 탄퍼짐 가중치용

    // 효과의 출처(Source)로 전달할 플레이어 엔티티.
    // Player는 BattleManager가 런타임에 부착하므로(Awake 시점엔 없을 수 있음) 찾을 때까지 재시도한다.
    private Entity ownerEntity;
    private Entity OwnerEntity =>
        ownerEntity != null ? ownerEntity : (ownerEntity = GetComponentInParent<Entity>());

    private void Awake()
    {
        playerRb = GetComponentInParent<Rigidbody>();
    }

    private void Start()
    {
        CurrentAmmo = gunData.magSize;
    }

    private void Update()
    {
        // 안 쏠 때는 에임이 다시 모임 (이동 속도에 따라 기본 탄퍼짐 증가 — 달리면 2배)
        float speedFactor = (playerRb != null && playerRb.linearVelocity.magnitude > 1f) ? 2f : 1f;
        float targetSpread = gunData.baseSpread * speedFactor;

        currentSpread = Mathf.Lerp(currentSpread, targetSpread, Time.deltaTime * gunData.spreadRecoveryRate);
    }

    private void OnEnable()
    {
        // 무기를 스왑해서 꺼낼 때마다 상태 초기화
        IsReloading = false;
    }

    private void OnDisable()
    {
        // 무기를 집어넣을 때 재장전 코루틴 안전하게 정지
        StopAllCoroutines();
    }

    /// <summary>사격 시도 — 재장전·탄약·연사 간격을 통과하면 발사하고 true.</summary>
    public bool TryFire()
    {
        if (IsReloading) return false;

        if (CurrentAmmo <= 0)
        {
            StartReload();
            return false;
        }

        if (Time.time < lastFireTime + gunData.fireRate) return false;

        // 샷건은 방아쇠 한 번에 펠릿 수만큼 탄을 소비한다 — 탄창이 모자라면 남은 만큼만 나간다
        int rounds = Mathf.Min(CurrentAmmo, Mathf.Max(1, gunData.pellets));
        CurrentAmmo -= rounds;
        lastFireTime = Time.time;
        currentSpread = Mathf.Min(currentSpread + gunData.spreadIncreasePerShot, gunData.maxSpread);

        Fire(rounds);
        Fired?.Invoke(this);
        return true;
    }

    // 발사 — 스펙(ProjectileShot)을 만들어 공용 시스템에 넘긴다. 타워도 같은 경로로 쏜다.
    private void Fire(int rounds)
    {
        // 탄도(속도·중력·폭발·수명·외형)는 탄약의 성질 — 총은 각도(조준·탄퍼짐)만 정한다
        var round = gunData.AmmoModule;
        if (round == null)
        {
            Debug.LogWarning($"[Gun] '{gunData.Id}'에 탄약(AmmoModule)이 배선되지 않았습니다 — 발사 불가.");
            return;
        }

        Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position;
        Vector3 forward = muzzlePoint != null ? muzzlePoint.forward : transform.forward;

        // 명중 효과는 장전된 탄약이 정의하고(타워와 같은 원칙), 총은 배율만 곱는다(피해형 항목에만).
        // 공격 버프는 발사 시점에 항목별로 구워진다 — 탄이 날아가는 동안 버프가 끝나도 발사 때 배율 유지.
        var effects = ProjectileSystem.ScaleDamage(round.attackEffects, gunData.damageMultiplier);
        if (OwnerEntity != null) effects = OwnerEntity.Effects.BakeOutgoing(effects);

        var shot = new ProjectileShot(round.speed, round.lifetime, gunData.range,
                                      effects, gunData.enemyLayer, OwnerEntity,
                                      round.gravity, round.explosionRadius);

        // 펠릿마다 따로 탄퍼짐을 굴린다 — 샷건의 확산은 같은 방아쇠의 탄들이 서로 다른 곳에 맞는 것
        for (int i = 0; i < rounds; i++)
        {
            Vector3 direction = forward + UnityEngine.Random.insideUnitSphere * (currentSpread / 100f);

            if (gunData.fireMode == FireMode.Hitscan)
                ProjectileSystem.Hitscan(origin, direction, shot);
            else
                ProjectileSystem.Fire(round.bulletPrefab, origin, direction, shot);
        }
    }

    public void StartReload()
    {
        if (IsReloading || CurrentAmmo == gunData.magSize || !gameObject.activeSelf) return;
        StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        IsReloading = true;
        yield return new WaitForSeconds(gunData.reloadTime);

        CurrentAmmo = gunData.magSize;
        IsReloading = false;
    }
}
