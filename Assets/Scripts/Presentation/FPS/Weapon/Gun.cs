using System;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.Entities;
using CoreDawn.Inventories;
using CoreDawn.Data;
using CoreDawn.Sound;
using CoreDawn.Sim;
using CoreDawn.Save;

namespace CoreDawn.FPS
{
    /// <summary>
    /// 플레이어 총기의 뷰 — 판단은 심(<see cref="WeaponModule"/>)이 한다: 탄창·재장전·연사 간격·탄 소비·효과 배율은 전부 심의 것이고
    /// 여기는 입력을 심에 넘기고(<c>TryFire → Weapon.TryFire</c>), 심이 승인한 방아쇠(<see cref="WeaponShot"/>)를 조준축·탄퍼짐·펠릿으로
    /// 풀어 <see cref="ProjectileSystem"/>에 넘기고, 소리·총구 화염을 낸다 — 포탑(TowerView)과 같은 "심 승인 → 뷰 발사" 틀.
    /// 상태 프로퍼티(CurrentAmmo·IsReloading·ReloadProgress…)는 HUD·입력이 읽는 심 상태의 창구다.
    ///
    /// 수치의 정본은 팩 <see cref="GunDef"/>(<see cref="gunId"/>로 찾는다). 소리·피격 레이어 같은 뷰 값은 이 컴포넌트(총 프리팹)가 든다.
    /// 연출(반동·킥백·셰이크)은 모른다 — 발사하면 <see cref="Fired"/>만 알리고, 반응은 WeaponManager가 팬아웃한다.
    /// </summary>
    public class Gun : MonoBehaviour
    {
        [Header("Core References")]
        [Tooltip("팩 총 정의 id(coredawn:gun/pistol) — 수치의 정본.")]
        public string gunId;
        public Transform muzzlePoint;

        [Header("뷰 값 — 소리·피격 레이어")]
        public AudioClip fireSound;
        public AudioClip reloadSound;
        [Range(0f, 1f)] public float fireVolume = 0.8f;
        [Range(0f, 1f)] public float reloadVolume = 0.7f;
        [Tooltip("탄이 맞힐 레이어.")]
        public LayerMask enemyLayer;

        [Tooltip("이 총의 가늠자(눈 위치) 앵커 — ADS가 카메라 정렬에 쓴다.")]
        public Transform sightPoint;

        [Header("피해 비례 넉백")]
        [Tooltip("탄에 얹을 넉백 효과의 팩 id(coredawn:effect/knockback) — 탄약이 넉백을 직접 명시하지 않았을 때만, 피해 합 × 계수만큼 밀어낸다. 비우면 꺼짐.")]
        public string knockbackEffectId;
        [Tooltip("피해 1당 밀어내는 거리(m). 유탄의 수동 튜닝(피해 70 · 넉백 2)과 같은 비율이 약 0.03.")]
        public float knockbackPerDamage = 0.03f;

        /// <summary>발사 성공 순간 발화 — WeaponManager가 구독해 연출(반동·킥백·셰이크)을 반응시킨다.</summary>
        public event Action<Gun> Fired;

        // ── 심 창구 ────────────────────────────────────────────

        private GunDef def;
        /// <summary>이 총의 정의(팩). 없으면 오류 — 정의 없는 총은 쏠 수 없다.</summary>
        public GunDef Def
        {
            get
            {
                if (def != null) return def;
                if (string.IsNullOrEmpty(gunId)) throw new InvalidOperationException($"[Gun] {name}: gunId가 없습니다");
                def = SimHost.Database?.Gun(gunId);
                if (def == null) throw new InvalidOperationException($"[Gun] {name}: 팩에 총 정의 '{gunId}'가 없습니다 — guns 섹션을 확인하세요");
                return def;
            }
        }

        private WeaponModule weapon;
        /// <summary>소지자(플레이어 엔티티)의 무기 모듈. 플레이어 엔티티가 아직 없으면 null.</summary>
        public WeaponModule Weapon
        {
            get
            {
                if (weapon != null) return weapon;
                var holder = PlayerInventoryHolder.Instance;
                var entity = holder != null ? holder.Entity : OwnerEntity != null ? OwnerEntity.Entity : null;
                weapon = entity?.Get<WeaponModule>();
                return weapon;
            }
        }

        private static float Now => SimRunner.Players.Now;
        private Magazine Mag => Weapon != null ? Weapon.MagazineOf(Def) : null;
        private bool IsCurrent => Weapon != null && ReferenceEquals(Weapon.Equipped, Def);

        /// <summary>현재 장전 수 — HUD 표시용.</summary>
        public int CurrentAmmo => Mag?.Loaded ?? 0;

        /// <summary>지금 장전된 탄종의 표현 에셋 — HUD 이름·아이콘용. 정본은 심(Magazine.Round).</summary>
        public ItemDef CurrentAmmoItem => Mag?.Round;

        /// <summary>소지품에 남은 현재 탄종 수 — HUD 예비탄 표시용. 무한 탄약(근접)·소지품 없는 씬은 -1(무한).</summary>
        public int ReserveAmmo => Def.UnlimitedAmmo || Weapon == null ? -1 : Weapon.ReserveOf(Mag?.Round);

        /// <summary>재장전 중인가 — 이 총을 들고 있을 때만 참.</summary>
        public bool IsReloading => IsCurrent && Weapon.Reloading;

        /// <summary>재장전 진행도 0~1. HUD가 크로스헤어 링으로 그린다.</summary>
        public float ReloadProgress => IsReloading ? Weapon.ReloadProgress(Now) : 0f;

        // ── 뷰 상태 ────────────────────────────────────────────

        private float currentSpread;
        private Rigidbody playerRb; // 이동 속도에 따른 탄퍼짐 가중치용
        private IPlayerMotionProvider motionProvider; // 조준 가중치(AimWeight) — 조준하면 탄퍼짐 절반
        private Camera aimCamera;   // 조준점(화면 중앙)의 기준 — 총알은 총구에서 나와 조준점으로 수렴한다

        // 효과의 출처(Source)로 전달할 플레이어 엔티티. 뷰는 BattleManager가 런타임에 부착하므로 찾을 때까지 재시도한다.
        private EntityView ownerEntity;
        private EntityView OwnerEntity =>
            ownerEntity != null ? ownerEntity : (ownerEntity = GetComponentInParent<EntityView>());

        // 재장전 소리는 전용 소스로 — 공용 풀의 PlayOneShot은 개별 정지가 안 돼서, 총을 내렸다 들었다 반복하면 소리가 쌓인다.
        private AudioSource reloadSource;
        private bool listening;

        private void Awake()
        {
            playerRb = GetComponentInParent<Rigidbody>();
            motionProvider = GetComponentInParent<IPlayerMotionProvider>();
            aimCamera = transform.root.GetComponentInChildren<Camera>(); // 뷰모델 홀더는 카메라의 형제라 부모 탐색이 안 닿는다
        }

        private void OnEnable() => Listen(true);

        private void OnDisable()
        {
            // 총을 내리면 심(WeaponManager → Weapon.Equip)이 재장전을 취소한다 — 여기선 소리만 끊는다
            Listen(false);
            if (reloadSource != null && reloadSource.isPlaying) reloadSource.Stop();
        }

        private void Listen(bool on)
        {
            var w = Weapon;
            if (w == null || listening == on) return;
            if (on) { w.ReloadStarted += OnReloadStarted; w.ReloadEnded += OnReloadEnded; }
            else { w.ReloadStarted -= OnReloadStarted; w.ReloadEnded -= OnReloadEnded; }
            listening = on;
        }

        private void Update()
        {
            if (!listening) Listen(true);   // 플레이어 엔티티가 늦게 생긴 경우
            if (OwnerEntity != null && OwnerEntity.IsDead) return;

            // 안 쏠 때는 에임이 다시 모임 (이동 속도에 따라 기본 탄퍼짐 증가 — 달리면 2배)
            float speedFactor = (playerRb != null && playerRb.linearVelocity.magnitude > 1f) ? 2f : 1f;
            float targetSpread = Def.BaseSpread * speedFactor;
            currentSpread = Mathf.Lerp(currentSpread, targetSpread, Time.deltaTime * Def.SpreadRecoveryRate);
        }

        // ── 입력 → 심 ──────────────────────────────────────────

        /// <summary>사격 시도 — 심이 재장전·탄약·연사 간격을 판정한다. 승인되면 탄을 만들고 true.</summary>
        public bool TryFire()
        {
            if (OwnerEntity != null && OwnerEntity.IsDead) return false;
            var w = Weapon;
            if (w == null || !IsCurrent) return false;
            if (!w.TryFire(Now, out var shot)) return false;

            currentSpread = Mathf.Min(currentSpread + Def.SpreadIncreasePerShot, Def.MaxSpread);
            Fire(shot);
            Fired?.Invoke(this);
            return true;
        }

        public void StartReload()
        {
            if (OwnerEntity != null && OwnerEntity.IsDead) return;
            var w = Weapon;
            if (w == null || !IsCurrent || !gameObject.activeSelf) return;
            w.TryStartReload(Now);
        }

        /// <summary>탄종 전환(V) — 소지품에 다른 탄종이 없으면 조용히 실패. 판정은 심.</summary>
        public bool TrySwitchAmmo()
        {
            var w = Weapon;
            return w != null && IsCurrent && w.TrySwitchAmmo(Now);
        }

        // ── 심 → 뷰 ────────────────────────────────────────────

        // 발사 — 심이 승인한 방아쇠를 조준축·탄퍼짐·펠릿으로 풀어 공용 전달 시스템에 넘긴다. 타워도 같은 경로로 쏜다.
        private void Fire(in WeaponShot shot)
        {
            if (fireSound != null)
            {
                Vector3 soundPos = muzzlePoint != null ? muzzlePoint.position : transform.position;
                if (SoundManager.Instance != null)
                    SoundManager.Instance.Play3DSFX(fireSound, soundPos, fireVolume);
            }

            // 탄도(속도·중력·폭발·수명·외형)는 장전된 탄종의 성질 — 총은 각도(조준·탄퍼짐)만 정한다.
            // 프리팹·연출은 뷰 카탈로그(탄약 항목)가 든다.
            var round = ViewCatalogSO.Of(shot.Round);
            if (round == null || round.bulletPrefab == null)
                Debug.LogError($"[Gun] '{Def.Id}': 탄 '{shot.Round?.Id}'의 표현 에셋(뷰 카탈로그 bullet)이 없습니다 — 프리팹·연출 없이 발사됩니다.");

            // 판정 축 = 카메라(조준선). 총구 축으로 쏘면 ① 비행 중 충돌이 조준선 밖에서 판정되고
            // ② 근접 표적에서 탄이 옆으로 날며 ③ 중력탄(유탄)의 포물선이 크로스헤어와 어긋난다.
            // 탄의 몸은 총구에서 태어나 조준선 1m 지점에 합류 후 직진(ProjectileSystem) — 카메라에서 태어나 화면을 가리는 일도 없다.
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

            // 명중 효과는 심이 이미 구웠다(탄약 효과 × 총 배율 × 소유자 버프). 피해 비례 넉백은 뷰의 손맛 값이라 여기서 얹는다 —
            // 펠릿마다 얹히므로 샷건은 맞은 수만큼 세게 민다.
            var effects = ProjectileSystem.AppendDamageKnockback(shot.Effects, SaveRefs.Effect(knockbackEffectId), knockbackPerDamage);

            var spec = new ProjectileShot(shot.Ammo.Speed, shot.Ammo.Lifetime, shot.Range,
                                          effects, enemyLayer.value, OwnerEntity,
                                          shot.Ammo.Gravity, shot.Ammo.ExplosionRadius,
                                          Def.IsAura ? FireMode.Aura : shot.Hitscan ? FireMode.Hitscan : FireMode.Projectile,
                                          round != null ? round.bulletPrefab : null, shot.Ammo.Pierce, muzzle,
                                          round != null ? round.hitEffectPrefab : null);

            // 총구 화염 — 방아쇠당 1회, 총구에 붙여서 총과 함께 움직이게 (탄약이 연출의 주인)
            if (muzzlePoint != null && round != null)
                ProjectileSystem.PlayEffect(round.muzzleFlashPrefab, muzzlePoint.position, muzzlePoint.rotation, muzzlePoint);

            // 빗맞아도 명백히 겨눈 사격이면 적의로 친다 — 조준선(카메라 축)이 정해진 지금이 유일한 판정 시점이다.
            HostileIntentProbe.Report(origin, forward, OwnerEntity);

            // 펠릿마다 따로 탄퍼짐을 굴린다 — 샷건의 확산은 같은 방아쇠의 탄들이 서로 다른 곳에 맞는 것.
            // 조준(ADS)하면 탄퍼짐 절반 — 누적된 currentSpread에 사용 시점에서 곱한다.
            float aim = motionProvider?.Motion != null ? Mathf.Clamp01(motionProvider.Motion.AimWeight) : 0f;
            float spread = currentSpread * Mathf.Lerp(1f, 0.5f, aim);

            for (int i = 0; i < shot.Pellets; i++)
            {
                Vector3 direction = forward + UnityEngine.Random.insideUnitSphere * (spread / 100f);
                ProjectileSystem.Fire(origin, direction, spec);
            }
        }

        private void OnReloadStarted(GunDef gun)
        {
            if (!ReferenceEquals(gun, Def) || !isActiveAndEnabled) return;
            if (reloadSound == null) return;
            if (reloadSource == null)
            {
                reloadSource = gameObject.AddComponent<AudioSource>();
                reloadSource.playOnAwake = false;
                reloadSource.spatialBlend = 1f; // SoundManager 없는 씬(테스트) 폴백
                // 공용 3D 세팅 — 안 거치면 SFX 믹서 그룹 밖이라 볼륨 슬라이더가 이 소리만 못 잡는다
                if (SoundManager.Instance != null) SoundManager.Instance.Setup3DSource(reloadSource);
            }
            reloadSource.clip = reloadSound;
            reloadSource.volume = reloadVolume;
            reloadSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
            reloadSource.Play();
        }

        private void OnReloadEnded(GunDef gun, bool completed)
        {
            if (!ReferenceEquals(gun, Def)) return;
            if (!completed && reloadSource != null && reloadSource.isPlaying) reloadSource.Stop();   // 취소는 취소라고 말해야 한다
        }
    }
}
