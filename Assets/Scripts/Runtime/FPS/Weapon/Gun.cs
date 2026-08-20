using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 플레이어 총기 — 게임플레이만 담당한다: 탄창·재장전·연사 간격·탄퍼짐·발사.
/// 수치는 전부 GunData(데이터), 여기는 상태(남은 탄·재장전 중·현재 탄종·탄퍼짐)뿐이다.
///
/// <b>탄약 실소비</b>: 재장전은 인벤토리(PlayerInventoryHolder)에서 현재 탄종 아이템을
/// 실제로 소모한다 — 포탑이 벨트 보급 탄을 소비하는 것과 같은 원칙. 탄종 전환
/// (TrySwitchAmmo)은 gunData.ammoFilter 안에서 돌며, 장전돼 있던 탄은 인벤토리로 반환한다.
/// 인벤토리가 없는 씬(테스트)에서는 추상 탄창으로 폴백한다 — 무한 보급.
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

    /// <summary>지금 장전된 탄종 — 발사 스펙(효과·탄도)의 출처. 기본은 gunData.ammo.</summary>
    public ItemDataSO CurrentAmmoItem { get; private set; }

    /// <summary>인벤토리에 남은 현재 탄종 수 — HUD 예비탄 표시용. 인벤토리 없는 씬은 -1(무한).</summary>
    public int ReserveAmmo => PlayerInventoryHolder.Instance != null ? CountInInventory(CurrentAmmoItem) : -1;

    /// <summary>재장전 중인가 — 읽기 전용. 진행은 StartReload로만 시작된다.</summary>
    public bool IsReloading { get; private set; }

    /// <summary>
    /// 세이브 복원 전용 — 장전된 탄수를 되돌린다. 재장전 중이었다면 그 상태는 취소한다.
    /// 예비탄은 인벤토리가 곧 복원되므로 여기서 건드리지 않는다.
    /// </summary>
    public void RestoreAmmo(int ammo)
    {
        CurrentAmmo = Mathf.Max(0, ammo);
        IsReloading = false;
    }

    private float lastFireTime;
    private float currentSpread;
    private Rigidbody playerRb; // 이동 속도에 따른 탄퍼짐 가중치용
    private Camera aimCamera;   // 조준점(화면 중앙)의 기준 — 총알은 총구에서 나와 조준점으로 수렴한다

    // 효과의 출처(Source)로 전달할 플레이어 엔티티.
    // Player는 BattleManager가 런타임에 부착하므로(Awake 시점엔 없을 수 있음) 찾을 때까지 재시도한다.
    private Entity ownerEntity;
    private Entity OwnerEntity =>
        ownerEntity != null ? ownerEntity : (ownerEntity = GetComponentInParent<Entity>());

    private void Awake()
    {
        playerRb = GetComponentInParent<Rigidbody>();
        aimCamera = transform.root.GetComponentInChildren<Camera>(); // 뷰모델 홀더는 카메라의 형제라 부모 탐색이 안 닿는다
    }

    private void Start()
    {
        CurrentAmmoItem = gunData != null ? gunData.DefaultAmmo : null;

        // 실소비 세계에서는 빈 탄창으로 시작한다 — 첫 발도 인벤토리의 탄에서 나와야 한다.
        // 인벤토리가 없는 씬(테스트)만 공짜 만장전.
        CurrentAmmo = PlayerInventoryHolder.Instance != null ? 0 : gunData.magSize;
    }

    private void Update()
    {
        // 플레이어가 죽었으면 무기 기능 전체 정지
        if (OwnerEntity != null && OwnerEntity.IsDead)
        {
            if (IsReloading)
            {
                StopAllCoroutines();
                IsReloading = false;
            }

            return;
        }

        // 안 쏠 때는 에임이 다시 모임 (이동 속도에 따라 기본 탄퍼짐 증가 — 달리면 2배)
        float speedFactor = (playerRb != null && playerRb.linearVelocity.magnitude > 1f) ? 2f : 1f;
        float targetSpread = gunData.baseSpread * speedFactor;

        currentSpread = Mathf.Lerp(currentSpread, targetSpread, Time.deltaTime * gunData.spreadRecoveryRate);

        // 빈 탄창은 알아서 채운다. 특정 시점(Start·장착)에 한 번 거는 대신 여기서 보는 이유는,
        // 초기화 중 무기가 껐다 켜지면 OnDisable의 StopAllCoroutines가 재장전을 끊어버리기
        // 때문이다 — 그 경합에서 살아남으려면 상태를 계속 확인하는 편이 맞다.
        // 인벤토리에 탄이 없으면 StartReload가 조용히 물러나므로 헛돌지 않는다.
        if (CurrentAmmo <= 0 && !IsReloading) StartReload();
    }

    private void OnEnable()
    {
        // 무기를 스왑해서 꺼낼 때마다 상태 초기화.
        // 집어넣을 때 OnDisable이 재장전 코루틴을 끊으므로 여기서 반드시 풀어줘야 한다
        // (안 풀면 재장전 중에 바꾼 총이 영영 '재장전 중'으로 굳어 쏘지 못한다).
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
        Debug.Log(
        $"[Gun] TryFire / gun={name} / owner={OwnerEntity} / ownerDead={OwnerEntity?.IsDead}"
    );
        // 사망 중에는 발사 금지
        if (OwnerEntity != null && OwnerEntity.IsDead)
            return false;

        Entity owner = OwnerEntity;

        // 플레이어가 죽어 있으면 발사 금지
        if (owner != null && owner.IsDead)
            return false;

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

        // 마지막 탄을 쐈으면 방아쇠를 다시 당길 것 없이 알아서 채운다.
        // 인벤토리에 탄이 없으면 StartReload가 조용히 물러나므로 헛돌지 않는다.
        if (CurrentAmmo <= 0) StartReload();
        return true;
    }

    // 발사 — 스펙(ProjectileShot)을 만들어 공용 시스템에 넘긴다. 타워도 같은 경로로 쏜다.
    private void Fire(int rounds)
    {
        Debug.Log(
    $"[Gun] 실제 Fire 실행 / gun={name} / owner={OwnerEntity} / ownerDead={OwnerEntity?.IsDead}"
);
        if (gunData != null && gunData.fireSound != null)
        {
            Vector3 soundPos = muzzlePoint != null ? muzzlePoint.position : transform.position;
            if (SoundManager.Instance != null)
                SoundManager.Instance.Play3DSFX(gunData.fireSound, soundPos, gunData.fireVolume);
        }

        // 탄도(속도·중력·폭발·수명·외형)는 장전된 탄종의 성질 — 총은 각도(조준·탄퍼짐)만 정한다
        var round = CurrentAmmoItem != null ? CurrentAmmoItem.GetModule<AmmoModuleSO>() : null;
        if (round == null)
        {
            Debug.LogWarning($"[Gun] '{gunData.Id}'에 탄약(AmmoModule)이 배선되지 않았습니다 — 발사 불가.");
            return;
        }

        // 판정 축 = 카메라(조준선). 총구 축으로 쏘면 ① 비행 중 충돌이 조준선 밖에서 판정되고
        // ② 근접 표적에서 탄이 옆으로 날며 ③ 중력탄(유탄)의 포물선이 크로스헤어와 어긋난다.
        // 탄의 몸은 총구에서 태어나 조준선 1m 지점에 합류 후 직진(ProjectileSystem) —
        // 카메라에서 태어나 화면을 가리는 일도 없다.
        Vector3 origin, forward;
        Vector3? muzzle = null;
        if (aimCamera != null)
        {
            Transform cam = aimCamera.transform;
            forward = cam.forward;
            origin = cam.position;
            if (muzzlePoint != null) muzzle = muzzlePoint.position;
        }
        else // 카메라 없는 구성(테스트 리그) — 총구 축 폴백
        {
            origin = muzzlePoint != null ? muzzlePoint.position : transform.position;
            forward = muzzlePoint != null ? muzzlePoint.forward : transform.forward;
        }

        // 명중 효과는 장전된 탄약이 정의하고(타워와 같은 원칙), 총은 배율만 곱는다(피해형 항목에만).
        // 공격 버프는 발사 시점에 항목별로 구워진다 — 탄이 날아가는 동안 버프가 끝나도 발사 때 배율 유지.
        var effects = ProjectileSystem.ScaleDamage(round.attackEffects, gunData.damageMultiplier);
        if (OwnerEntity != null) effects = OwnerEntity.Effects.BakeOutgoing(effects);

        var shot = new ProjectileShot(round.speed, round.lifetime, gunData.range,
                                      effects, gunData.enemyLayer, OwnerEntity,
                                      round.gravity, round.explosionRadius,
                                      gunData.fireMode, round.bulletPrefab, round.pierce, muzzle,
                                      round.hitEffectPrefab);

        // 총구 화염 — 방아쇠당 1회, 총구에 붙여서 총과 함께 움직이게 (탄약이 연출의 주인)
        if (muzzlePoint != null)
            ProjectileSystem.PlayEffect(round.muzzleFlashPrefab, muzzlePoint.position, muzzlePoint.rotation, muzzlePoint);

        // 빗맞아도 명백히 겨눈 사격이면 적의로 친다 — 조준선(카메라 축)이 정해진 지금이
        // 유일한 판정 시점이다. 탄이 실제로 맞으면 어차피 피해 경로가 각성을 처리한다.
        HostileIntentProbe.Report(origin, forward, OwnerEntity);

        // 펠릿마다 따로 탄퍼짐을 굴린다 — 샷건의 확산은 같은 방아쇠의 탄들이 서로 다른 곳에 맞는 것.
        // 전달 방식(투사체/히트스캔)은 스펙에 실려 있다 — 분기는 ProjectileSystem이 한다.
        for (int i = 0; i < rounds; i++)
        {
            Vector3 direction = forward + UnityEngine.Random.insideUnitSphere * (currentSpread / 100f);
            ProjectileSystem.Fire(origin, direction, shot);
        }
    }

    public void StartReload()
    {
        // 사망 중에는 장전 금지
        if (OwnerEntity != null && OwnerEntity.IsDead)
            return;

        if (IsReloading || CurrentAmmo == gunData.magSize || !gameObject.activeSelf) return;

        // 실소비 — 인벤토리에 현재 탄종이 하나도 없으면 재장전 자체가 시작되지 않는다
        if (PlayerInventoryHolder.Instance != null && CountInInventory(CurrentAmmoItem) <= 0) return;

        StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        IsReloading = true;

        if (gunData != null && gunData.reloadSound != null)
        {
            if (SoundManager.Instance != null)
                SoundManager.Instance.Play3DSFX(gunData.reloadSound, transform.position, gunData.reloadVolume);
        }
        yield return new WaitForSeconds(gunData.reloadTime);

        var holder = PlayerInventoryHolder.Instance;
        if (holder == null || CurrentAmmoItem == null)
            CurrentAmmo = gunData.magSize; // 추상 탄창 폴백 — 인벤토리 없는 씬(테스트)·미배선
        else
            CurrentAmmo += ConsumeFromInventory(CurrentAmmoItem, gunData.magSize - CurrentAmmo);

        IsReloading = false;
    }

    /// <summary>
    /// 탄종 전환 — gunData.ammoFilter 안에서 인벤토리에 있는 다음 탄종으로 돈다.
    /// 장전돼 있던 탄은 인벤토리로 반환하고 새 탄종으로 재장전을 시작한다. 성공 시 true.
    /// </summary>
    public bool TrySwitchAmmo()
    {
        var filter = gunData.ammoFilter;
        if (filter == null || filter.Length <= 1 || IsReloading) return false;

        var holder = PlayerInventoryHolder.Instance;
        int idx = System.Array.IndexOf(filter, CurrentAmmoItem);

        for (int step = 1; step <= filter.Length; step++)
        {
            var candidate = filter[(idx + step) % filter.Length];
            if (candidate == null || candidate == CurrentAmmoItem) continue;
            if (holder != null && CountInInventory(candidate) <= 0) continue; // 없는 탄으로는 못 바꾼다

            // 장전돼 있던 탄을 인벤토리로 반환 — 전환이 탄을 증발시키면 안 된다
            if (holder != null && CurrentAmmo > 0 && CurrentAmmoItem != null)
                holder.AddItemToPlayer(CurrentAmmoItem, CurrentAmmo);

            CurrentAmmo = 0;
            CurrentAmmoItem = candidate;
            StartReload();
            return true;
        }
        return false;
    }

    // ── 인벤토리 실소비 (보관 순서: 메인 가방 먼저, 핫바 나중) ──────

    private static int CountInInventory(ItemDataSO item)
    {
        var h = PlayerInventoryHolder.Instance;
        if (h == null || item == null) return 0;
        return h.MainContainer.CountOf(item) + h.HotbarContainer.CountOf(item);
    }

    private static int ConsumeFromInventory(ItemDataSO item, int need)
    {
        var h = PlayerInventoryHolder.Instance;
        if (h == null || item == null || need <= 0) return 0;

        int got = ConsumeFrom(h.MainContainer, item, need);
        got += ConsumeFrom(h.HotbarContainer, item, need - got);
        return got;
    }

    private static int ConsumeFrom(ItemContainer container, ItemDataSO item, int need)
    {
        if (container == null || need <= 0) return 0;
        int take = Mathf.Min(need, container.CountOf(item));
        return take > 0 && container.TryConsume(item, take) ? take : 0;
    }
}
