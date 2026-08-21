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
/// <b>근접무기</b>(GunData.unlimitedAmmo)는 이 소비 경로를 통째로 건너뛴다: 탄창은 늘 가득이고
/// 재장전도 없다. 근접무기의 '탄'은 보이지 않는 짧은 사거리의 광역탄 — 휘두름 그 자체다.
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

    [Header("피해 비례 넉백")]
    [Tooltip("탄에 얹을 넉백 효과 — 탄약이 넉백을 직접 명시하지 않았을 때만, 피해 합 × 계수만큼 밀어낸다. 비우면 꺼짐.")]
    public KnockbackEffectSO knockbackEffect;
    [Tooltip("피해 1당 밀어내는 거리(m). 유탄의 수동 튜닝(피해 70 · 넉백 2)과 같은 비율이 약 0.03.")]
    public float knockbackPerDamage = 0.03f;

    /// <summary>발사 성공 순간 발화 — WeaponManager가 구독해 연출(반동·킥백·셰이크)을 반응시킨다.</summary>
    public event Action<Gun> Fired;

    /// <summary>현재 장전 수 — HUD 표시용 읽기 전용 (SCR-02).</summary>
    public int CurrentAmmo { get; private set; }

    /// <summary>지금 장전된 탄종 — 발사 스펙(효과·탄도)의 출처. 기본은 gunData.ammo.</summary>
    public ItemDataSO CurrentAmmoItem { get; private set; }

    /// <summary>인벤토리에 남은 현재 탄종 수 — HUD 예비탄 표시용. 무한 탄약(근접)·인벤토리 없는 씬은 -1(무한).</summary>
    public int ReserveAmmo => Unlimited || PlayerInventoryHolder.Instance == null ? -1 : CountInInventory(CurrentAmmoItem);

    /// <summary>탄을 소비하지 않는 무기인가 — 근접무기. 재장전·인벤토리 소비 경로를 통째로 건너뛴다.</summary>
    private bool Unlimited => gunData != null && gunData.unlimitedAmmo;

    /// <summary>재장전 중인가 — 읽기 전용. 진행은 StartReload로만 시작된다.</summary>
    public bool IsReloading { get; private set; }

    // 재장전 진행 표시용 — 코루틴의 WaitForSeconds는 남은 시간을 알려주지 않으므로
    // 시작 시각과 길이를 따로 적어 둔다. 값의 주인은 여전히 코루틴 하나뿐이다.
    private float reloadStartedAt;
    private float reloadDuration;

    /// <summary>
    /// 재장전 진행도 0~1. 재장전 중이 아니면 0.
    /// HUD가 크로스헤어 링으로 그린다 — "장전 중"이라는 글자만으로는 언제 끝나는지 알 수 없고,
    /// 그 사이 쏠 수 없으니 남은 시간이 곧 생존에 필요한 정보다.
    /// </summary>
    public float ReloadProgress => IsReloading && reloadDuration > 0f
        ? Mathf.Clamp01((Time.time - reloadStartedAt) / reloadDuration)
        : 0f;

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
    private IPlayerMotionProvider motionProvider; // 조준 가중치(AimWeight) — 조준하면 탄퍼짐 절반
    private Camera aimCamera;   // 조준점(화면 중앙)의 기준 — 총알은 총구에서 나와 조준점으로 수렴한다

    // 효과의 출처(Source)로 전달할 플레이어 엔티티.
    // Player는 BattleManager가 런타임에 부착하므로(Awake 시점엔 없을 수 있음) 찾을 때까지 재시도한다.
    private Entity ownerEntity;
    private Entity OwnerEntity =>
        ownerEntity != null ? ownerEntity : (ownerEntity = GetComponentInParent<Entity>());

    private void Awake()
    {
        playerRb = GetComponentInParent<Rigidbody>();
        motionProvider = GetComponentInParent<IPlayerMotionProvider>();
        aimCamera = transform.root.GetComponentInChildren<Camera>(); // 뷰모델 홀더는 카메라의 형제라 부모 탐색이 안 닿는다
    }

    private void Start()
    {
        CurrentAmmoItem = gunData != null ? gunData.DefaultAmmo : null;

        // 실소비 세계에서는 빈 탄창으로 시작한다 — 첫 발도 인벤토리의 탄에서 나와야 한다.
        // 무한 탄약(근접무기)과 인벤토리가 없는 씬(테스트)만 공짜 만장전.
        CurrentAmmo = Unlimited || PlayerInventoryHolder.Instance == null ? gunData.magSize : 0;
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
                if (reloadSource != null && reloadSource.isPlaying) reloadSource.Stop();
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
        // 무기를 스왑해서 꺼낼 때마다 상태 초기화 — OnDisable이 이미 풀어두지만,
        // 비활성화를 거치지 않고 켜지는 경로(첫 생성 등)까지 덮는 이중 안전장치다.
        // 여기서 탄을 채워주지는 않는다: 손에서 내린 순간 장전은 취소된 것이고,
        // 부분 장전 상태였다면 다시 들고 R을 눌러야 한다 (빈 탄창만 Update가 알아서 채운다).
        IsReloading = false;
    }

    private void OnDisable()
    {
        // 무기를 집어넣을 때 재장전 코루틴 안전하게 정지 — 소리도 함께 끊는다.
        // 플래그도 여기서 내린다: 코루틴이 죽은 뒤에도 IsReloading이 켜져 있으면
        // 집어넣은 총이 "재장전 중"이라고 거짓말을 하고, 그 사이 상태를 읽는 쪽(HUD·세이브)이
        // 영원히 끝나지 않는 장전을 보게 된다. 취소는 취소라고 말해야 한다.
        IsReloading = false;
        StopAllCoroutines();
        if (reloadSource != null && reloadSource.isPlaying) reloadSource.Stop();
    }

    /// <summary>사격 시도 — 재장전·탄약·연사 간격을 통과하면 발사하고 true.</summary>
    public bool TryFire()
    {
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

        // 샷건은 방아쇠 한 번에 펠릿 수만큼 탄을 소비한다 — 탄창이 모자라면 남은 만큼만 나간다.
        // 근접무기(무한 탄약)는 휘두를 뿐이라 탄창이 줄지 않는다 — 재장전도 영영 오지 않는다.
        int rounds = Unlimited ? Mathf.Max(1, gunData.pellets)
                               : Mathf.Min(CurrentAmmo, Mathf.Max(1, gunData.pellets));
        if (!Unlimited) CurrentAmmo -= rounds;
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
        // 피해 비례 넉백 — 배율이 구워진 최종 피해 기준. 펠릿마다 얹히므로 샷건은 맞은 수만큼 세게 민다.
        effects = ProjectileSystem.AppendDamageKnockback(effects, knockbackEffect, knockbackPerDamage);

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
        // 조준(ADS)하면 탄퍼짐 절반 — 누적된 currentSpread에 사용 시점에서 곱한다.
        // AimWeight가 연속값이라 조준을 올리는 중에도 부드럽게 조여진다.
        float aim = motionProvider?.Motion != null ? Mathf.Clamp01(motionProvider.Motion.AimWeight) : 0f;
        float spread = currentSpread * Mathf.Lerp(1f, 0.5f, aim);

        for (int i = 0; i < rounds; i++)
        {
            Vector3 direction = forward + UnityEngine.Random.insideUnitSphere * (spread / 100f);
            ProjectileSystem.Fire(origin, direction, shot);
        }
    }

    public void StartReload()
    {
	// 사망 중에는 장전 금지
    	if (OwnerEntity != null && OwnerEntity.IsDead)
        	return;

    	if (Unlimited) return;   // 근접무기 — 채울 탄창이 없다 
	       
	if (IsReloading || CurrentAmmo == gunData.magSize || !gameObject.activeSelf) return;

        // 실소비 — 인벤토리에 현재 탄종이 하나도 없으면 재장전 자체가 시작되지 않는다
        if (PlayerInventoryHolder.Instance != null && CountInInventory(CurrentAmmoItem) <= 0) return;

        StartCoroutine(ReloadRoutine());
    }

    // 재장전 소리는 전용 소스로 — 공용 풀의 PlayOneShot 은 개별 정지가 안 돼서,
    // 총을 내렸다 들었다 반복하면 끊긴 재장전마다 소리가 쌓여 중첩된다.
    private AudioSource reloadSource;

    private IEnumerator ReloadRoutine()
    {
        IsReloading = true;
        reloadStartedAt = Time.time;
        reloadDuration = gunData.reloadTime;

        if (gunData != null && gunData.reloadSound != null)
        {
            if (reloadSource == null)
            {
                reloadSource = gameObject.AddComponent<AudioSource>();
                reloadSource.playOnAwake = false;
                reloadSource.spatialBlend = 1f; // SoundManager 없는 씬(테스트) 폴백
                // 공용 3D 세팅 — 안 거치면 SFX 믹서 그룹 밖이라 볼륨 슬라이더가 이 소리만 못 잡는다
                if (SoundManager.Instance != null) SoundManager.Instance.Setup3DSource(reloadSource);
            }
            reloadSource.clip = gunData.reloadSound;
            reloadSource.volume = gunData.reloadVolume;
            reloadSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
            reloadSource.Play();
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

        // 무한 탄약(근접)은 인벤토리를 아예 보지 않는다 — 없는 탄을 반환하거나 요구하면 안 된다
        var holder = Unlimited ? null : PlayerInventoryHolder.Instance;
        int idx = System.Array.IndexOf(filter, CurrentAmmoItem);

        for (int step = 1; step <= filter.Length; step++)
        {
            var candidate = filter[(idx + step) % filter.Length];
            if (candidate == null || candidate == CurrentAmmoItem) continue;
            if (holder != null && CountInInventory(candidate) <= 0) continue; // 없는 탄으로는 못 바꾼다

            // 장전돼 있던 탄을 인벤토리로 반환 — 전환이 탄을 증발시키면 안 된다
            if (holder != null && CurrentAmmo > 0 && CurrentAmmoItem != null)
                holder.AddItemToPlayer(CurrentAmmoItem, CurrentAmmo);

            CurrentAmmo = Unlimited ? gunData.magSize : 0;
            CurrentAmmoItem = candidate;
            StartReload();   // 무한 탄약이면 조용히 물러난다
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
