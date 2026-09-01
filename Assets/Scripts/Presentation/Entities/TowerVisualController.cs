using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.Factory;
using CoreDawn.Managers;
using CoreDawn.Sound;
using CoreDawn.Data;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 타워의 <b>연출</b> 담당 — 조준 회전·반동·배럴 순환·사운드·상태별 애니메이션.
    /// 전투 판정은 일절 하지 않는다. 무엇을 언제 쏠지는 <see cref="TowerView"/>가 정하고,
    /// 여기는 "그렇게 보이도록" 만들 뿐이다.
    ///
    /// 리그 참조는 전부 선택이다 — 포탑이 없는 타워(감속 필드·울타리)는 yawPivot을 비워두면
    /// 조준 관련 호출이 전부 무해하게 통과한다. 그래서 타워 종류가 늘어도 여기에 분기가 생기지 않는다.
    ///
    /// 트랜스폼 하나당 주인은 하나다:
    ///   yawPivot·pitchPivot·recoil → 이 스크립트
    ///   droop                      → 코드(굶으면 처짐)
    /// 둘이 같은 트랜스폼을 건드리면 매 프레임 싸우므로, 프리팹 계층을 그렇게 나눠 두었다.
    /// </summary>
    [DisallowMultipleComponent]
    public class TowerVisualController : MonoBehaviour
    {
        // View 노드는 Animator(등장 연출)의 것이라 이 스크립트가 참조할 일이 없다.
        // 예전엔 쓰지도 않는 view 필드를 빌더가 배선해 두었는데, "이 스크립트도 View를 만진다"는
        // 오해만 부르므로 걷어냈다.

        [Header("리그 — 조립기가 모델의 노드 이름으로 채운다(WireRig). 비면 해당 연출을 건너뛴다")]
        [Tooltip("좌우 선회 축. 비우면 조준 회전이 없는 타워로 취급하고 항상 '조준 완료'로 답한다.")]
        [SerializeField] private Transform yawPivot;

        [Tooltip("상하 부앙 축. 비우거나 yawPivot과 같으면 한 트랜스폼에 합쳐서 적용한다.")]
        [SerializeField] private Transform pitchPivot;

        [Tooltip("반동으로 뒤로 밀리는 마디. 로컬 -Z로 밀었다가 되돌아온다.")]
        [SerializeField] private Transform recoil;

        [Tooltip("총구들. 여러 개면 발사마다 번갈아 쓴다(다총신). 비우면 BattleTower가 muzzleHeight로 대신한다.")]
        [SerializeField] private Transform[] muzzles;
        [Tooltip("탄약이 끊기면 처지는 마디(Droop). 비면 처짐 연출 없음.")]
        [SerializeField] private Transform droop;
        [Header("처짐 — 탄약이 끊긴 타워")]
        [SerializeField] private float droopAngle = 18f;
        [Tooltip("클수록 빨리 처지고 빨리 든다.")]
        [SerializeField] private float droopSpeed = 4f;

        [Header("조준")]
        [Tooltip("부앙 가동범위(도). x=아래 한계, y=위 한계. 모델 사정이라 밸런스가 아닌 여기에 둔다.")]
        [SerializeField] private Vector2 pitchRange = new Vector2(-10f, 25f);

        [Tooltip("목표가 없을 때 훑는 속도(도/초) — 스윕이 가장 빠른 순간(중앙 통과)의 각속도다.")]
        [SerializeField] private float idleScanSpeed = 20f;
        [Tooltip("훑는 좌우 폭(도). 중앙 기준으로 ±절반씩 오간다.")]
        [SerializeField] private float idleScanArc = 80f;

        [Header("반동")]
        [SerializeField] private float recoilDistance = 0.15f;
        [Tooltip("클수록 빨리 제자리로 돌아온다.")]
        [SerializeField] private float recoilRecover = 7f;

        [Header("연출 프리팹")]
        [SerializeField] private GameObject destroyVfx;

        // ── 내부 상태 ───────────────────────────────────────────────
        private TowerView tower;

        private float yaw;
        private float pitch;
        private float recoilOffset;   // 0 = 제자리, 음수 = 뒤로 밀린 상태
        private float idlePhase;
        private int muzzleIndex;
        private Transform lastMuzzle;
        private bool deathPlayed;
        private float droopTilt;

        // 애니메이터 파라미터는 이 하나뿐이다. 등장(Deploy)→기본(Active) 전이는 클립 길이로
        // 끝내므로 파라미터가 필요 없다 — 없는 파라미터에 SetBool을 하면 매번 에러가 찍힌다.

        /// <summary>총구가 하나라도 있는가 — BattleTower가 muzzleHeight 폴백을 쓸지 판단한다.</summary>
        public bool HasMuzzle => muzzles != null && muzzles.Length > 0;

        private void Awake()
        {
            tower = GetComponent<TowerView>();
        }

        /// <summary>
        /// 리그 배선 — 모델 안에서 이름으로 찾는다(블렌더 규약: YawPivot → PitchPivot → Droop → Recoil, 총구 Muzzle_*).
        /// 이름은 정의의 view.rig{yaw, pitch, droop, recoil, muzzle}로 바꿀 수 있다(모델을 못 고치는 서드파티 리그).
        /// </summary>
        public void WireRig(Transform model, ViewSpec view)
        {
            var rig = view?.Object("rig");
            string N(string key, string fallback) => (string)rig?[key] ?? fallback;
            yawPivot   = Find(model, N("yaw", "YawPivot"));
            pitchPivot = Find(model, N("pitch", "PitchPivot"));
            droop      = Find(model, N("droop", "Droop"));
            recoil     = Find(model, N("recoil", "Recoil"));
            string muzzlePrefix = N("muzzle", "Muzzle_");
            var list = new System.Collections.Generic.List<Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(muzzlePrefix, System.StringComparison.Ordinal)) list.Add(t);
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            muzzles = list.ToArray();
        }

        static Transform Find(Transform root, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private void OnEnable()
        {
            if (tower != null) tower.OnDeath += PlayDestroyed;
        }

        private void OnDisable()
        {
            if (tower != null) tower.OnDeath -= PlayDestroyed;
        }

        private void Update()
        {
            // 처짐 — 굶으면 포신이 아래로 처지고, 보급되면 다시 든다(구 Animator 클립을 코드로)
            if (droop != null)
            {
                float target = current == TowerState.Starved ? droopAngle : 0f;
                droopTilt = Mathf.Lerp(droopTilt, target, 1f - Mathf.Exp(-droopSpeed * Time.deltaTime));
                droop.localRotation = Quaternion.Euler(droopTilt, 0f, 0f);
            }

            // 반동 복귀 — 발사와 무관하게 항상 0을 향해 되돌아온다
            if (recoil != null)
            {
                if (!Mathf.Approximately(recoilOffset, 0f))
                {
                    recoilOffset = Mathf.Lerp(recoilOffset, 0f, 1f - Mathf.Exp(-recoilRecover * Time.deltaTime));
                    if (Mathf.Abs(recoilOffset) < 0.0005f) recoilOffset = 0f;
                    var lp = recoil.localPosition;
                    recoil.localPosition = new Vector3(lp.x, lp.y, recoilOffset);
                }
            }
        }

        // ── 조준 (BattleTower가 매 프레임 호출) ─────────────────────

        /// <summary>
        /// 주어진 <b>월드 방향</b>으로 포탑을 돌린다. 조준이 끝났으면(좌우 오차 &lt;= tolerance) true.
        ///
        /// 방향을 인자로 받는 이유: 곡사탄은 목표 지점이 아니라 탄도해가 준 <i>발사각</i>을 향해야 하는데,
        /// 그 계산에 필요한 탄속·중력은 탄약(AmmoModuleDef)의 것이라 BattleTower 쪽에 있다.
        /// 여기까지 탄약을 끌고 오면 연출 코드가 전투 데이터를 알게 된다.
        ///
        /// 정렬 판정은 <b>좌우(yaw)만</b> 본다. 부앙은 탄종의 중력에 따라 매 발 달라지는 데다,
        /// 실제 발사각은 발사 순간 다시 계산되므로 게이트로 쓰면 서로를 기다리는 꼴이 된다.
        /// </summary>
        public bool AimTowards(Vector3 worldDirection, float turnSpeed, float toleranceDeg)
        {
            if (yawPivot == null) return true;                 // 포탑이 없는 타워 — 늘 조준 완료
            if (worldDirection.sqrMagnitude < 0.000001f) return true;

            Transform space = yawPivot.parent != null ? yawPivot.parent : yawPivot;
            Vector3 local = space.InverseTransformDirection(worldDirection.normalized);

            float desiredYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float horizontal = new Vector2(local.x, local.z).magnitude;
            // Unity의 +X 회전은 아래를 향하므로 부호를 뒤집는다
            float desiredPitch = Mathf.Clamp(-Mathf.Atan2(local.y, horizontal) * Mathf.Rad2Deg,
                                             pitchRange.x, pitchRange.y);

            if (turnSpeed <= 0f)
            {
                yaw = desiredYaw;
                pitch = desiredPitch;
                ApplyAim();
                return true;
            }

            float step = turnSpeed * Time.deltaTime;
            float yawError = Mathf.DeltaAngle(yaw, desiredYaw);
            yaw = Mathf.MoveTowardsAngle(yaw, desiredYaw, step);
            pitch = Mathf.MoveTowards(pitch, desiredPitch, step);
            ApplyAim();

            return Mathf.Abs(yawError) <= toleranceDeg;
        }

        /// <summary>목표가 없을 때 — 좌우로 느리게 훑고 부앙은 수평으로 되돌린다.</summary>
        public void AimIdle(float turnSpeed)
        {
            if (yawPivot == null) return;

            // idleScanSpeed는 "포신이 도는 속도(도/초)"다. 사인 스윕이 가장 빠른 순간은 중앙을
            // 지날 때이고 그 각속도가 (arc/2) × 위상속도이므로, 위상속도를 여기서 역산해야
            // 필드 이름과 실제 동작이 일치한다. 예전에는 Deg2Rad를 곱해 위상속도로 곧장 썼는데,
            // 그러면 arc를 건드릴 때마다 실제 속도가 딸려 변해서 "속도"라는 이름이 거짓이 됐다.
            float halfArc = Mathf.Max(1f, idleScanArc * 0.5f);
            idlePhase += (idleScanSpeed / halfArc) * Time.deltaTime;
            float sweep = Mathf.Sin(idlePhase) * halfArc;

            float step = (turnSpeed > 0f ? turnSpeed : 180f) * Time.deltaTime;
            yaw = Mathf.MoveTowardsAngle(yaw, sweep, step);
            pitch = Mathf.MoveTowards(pitch, 0f, step);
            ApplyAim();
        }

        private void ApplyAim()
        {
            if (yawPivot == null) return;

            if (pitchPivot == null || pitchPivot == yawPivot)
            {
                // 축이 하나뿐인 리그 — 한 회전에 합쳐서 넣는다
                yawPivot.localRotation = Quaternion.Euler(pitch, yaw, 0f);
                return;
            }

            yawPivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            pitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        // ── 발사 ────────────────────────────────────────────────────

        /// <summary>
        /// 이번 발사에 쓸 총구를 하나 꺼내고 다음 총구로 넘어간다(다총신 교대 사격).
        /// 총구가 없으면 false — 호출자는 muzzleHeight 폴백을 쓴다.
        /// </summary>
        public bool TryTakeMuzzle(out Transform muzzle)
        {
            muzzle = null;
            if (muzzles == null || muzzles.Length == 0) return false;

            // 인스펙터에 빈 칸이 섞여 있어도 살아 있는 총구를 찾을 때까지 한 바퀴 돈다
            for (int i = 0; i < muzzles.Length; i++)
            {
                Transform candidate = muzzles[muzzleIndex];
                muzzleIndex = (muzzleIndex + 1) % muzzles.Length;
                if (candidate != null)
                {
                    muzzle = candidate;
                    lastMuzzle = candidate;
                    return true;
                }
            }
            return false;
        }

        /// <summary>한 발 나갔다 — 반동을 먹이고 발사음을 낸다. BattleTower가 발사 직후 호출한다.</summary>
        public void OnShotFired()
        {
            recoilOffset = -recoilDistance;

            Play(View?.SfxOf("fire"), lastMuzzle != null ? lastMuzzle.position : transform.position);
        }

        // ── 상태 반영 ───────────────────────────────────────────────

        private TowerState current = (TowerState)(-1);

        /// <summary>BattleTower가 상태가 바뀔 때만 호출한다.</summary>
        public void OnStateChanged(TowerState state)
        {
            if (state == current) return;
            bool wasStarved = current == TowerState.Starved;
            current = state;


            if (state == TowerState.Starved && !wasStarved)
                Play(View?.SfxOf("starved"), transform.position);

            if (state == TowerState.Destroyed) PlayDestroyed();
        }

        /// <summary>
        /// 파괴 연출. 뷰 GameObject는 이 직후 파괴되므로 <b>부모 없이</b> 띄워야 살아남는다
        /// (ProjectileSystem의 풀은 전용 루트 아래에 두므로 그대로 안전하다).
        /// OnDeath와 상태 전이 양쪽에서 불릴 수 있어 1회만 나가도록 잠근다.
        /// </summary>
        private void PlayDestroyed()
        {
            if (deathPlayed) return;
            deathPlayed = true;

            Vector3 at = transform.position;
            ProjectileSystem.PlayEffect(destroyVfx, at, Quaternion.identity);

            Play(View?.SfxOf("destroy"), at);
        }

        /// <summary>정의의 표현 사양 — 소리 자리(fire·destroy·starved)는 팩 view.sfx에 있다. 심에 붙기 전엔 null.</summary>
        private ViewSpec View => ViewSchema.Of(GetComponent<BuildingView>()?.Def);

        /// <summary>SoundManager가 없는 씬(테스트 씬 등)에서도 게임은 돌아가야 한다.</summary>
        private static void Play(SoundUse use, Vector3 position) => SoundManager.Instance?.Play(use, position);
    }
}
