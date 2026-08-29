using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.Visuals;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 몬스터의 <b>연출</b> 담당 — 이동/공격/피격/사망 애니메이션과 사망 후처리.
    /// 전투 판정은 일절 하지 않는다. 무엇이 언제 일어날지는 <see cref="Monster"/>와 상태머신이 정하고,
    /// 여기는 "그렇게 보이도록" 만들 뿐이다. <see cref="TowerVisualController"/>와 같은 역할·같은 자리다.
    ///
    /// 리그 참조는 전부 선택이다 — 모델이 없는 몬스터(자리표시 캡슐)는 animator를 비워두면
    /// 모든 호출이 무해하게 통과한다. 그래서 적 종류가 늘어도 여기에 분기가 생기지 않는다.
    ///
    /// 트랜스폼 하나당 주인은 하나다:
    ///   루트          → MovementComponent (위치·회전)
    ///   View          → 이 스크립트 (사망 시 가라앉기)
    ///   Anim 이하 본  → Animator (클립)
    /// 셋이 겹치면 매 프레임 싸우므로, MonsterRigBuilder가 계층을 그렇게 나눠 두었다.
    ///
    /// <b>Update()가 없다.</b> 매 프레임 구동은 <see cref="MonsterAnimationSystem"/>이
    /// <see cref="VisualTick"/>으로 대신 돌린다 — 개체 수만큼의 Update() 호출을 없애는 것이
    /// 물량 대비의 절반이다(CrowdSystem이 겹침 해소에 쓴 것과 같은 논리).
    /// </summary>
    [DisallowMultipleComponent]
    public class MonsterVisualController : MonoBehaviour
    {
        /// <summary>사망 연출 방식 — 종에 사망 클립이 있는지에 따라 갈린다.</summary>
        public enum DeathStyle
        {
            /// <summary>전용 사망 클립이 있다 (Grenadier).</summary>
            AnimationClip,

            /// <summary>사망 클립이 없다 (Chomper·Spitter). 피격 모션 뒤 지면으로 가라앉힌다.</summary>
            SinkAway,
        }

        [Header("리그 — 비우면 해당 연출을 건너뛴다")]
        [Tooltip("빌더가 축척·접지를 실어 둔 노드. 사망 시 이 노드를 가라앉힌다. Animator는 건드리지 않는다.")]
        [SerializeField] private Transform view;

        [Tooltip("종별 AnimatorOverrideController가 붙은 Animator. 비우면 애니메이션 연출 전체를 건너뛴다.")]
        [SerializeField] private Animator animator;

        [Header("이동 → Speed 파라미터")]
        [Tooltip("이 속도(월드 단위/초)에서 Speed=1(달리기)이 된다. 0 이하면 심 MovementModule.MoveSpeed를 쓴다.")]
        [SerializeField] private float runSpeed = 0f;

        [Tooltip("Speed 파라미터 감쇠 시간(초). 클수록 걷기↔달리기 전환이 느긋해진다.")]
        [SerializeField] private float speedDamp = 0.12f;

        [Header("변종 — 오버라이드 컨트롤러가 실제로 채운 슬롯 수")]
        [Tooltip("공격 모션 개수. 1이면 항상 Attack_0.")]
        [SerializeField, Min(1)] private int attackVariants = 1;

        [Tooltip("피격 모션 개수. 1이면 항상 Hit_0.")]
        [SerializeField, Min(1)] private int hitVariants = 1;

        [Tooltip("피격 반응 최소 간격(초). 없으면 연사에 맞을 때 몸이 계속 튕겨 이동이 뭉개져 보인다.")]
        [SerializeField] private float hitReactionCooldown = 0.6f;

        [Header("사망")]
        [SerializeField] private DeathStyle deathStyle = DeathStyle.AnimationClip;

        [Tooltip("가라앉기 시작까지의 뜸(초). 피격 모션이 한 번 보일 시간을 준다.")]
        [SerializeField] private float sinkDelay = 0.4f;

        [Tooltip("가라앉는 데 걸리는 시간(초). Entity.deathDelay보다 짧아야 소멸 전에 다 묻힌다.")]
        [SerializeField] private float sinkDuration = 1.2f;

        [Tooltip("가라앉는 깊이(월드 단위).")]
        [SerializeField] private float sinkDepth = 1.5f;

        // 파라미터는 MonsterAnimationBuilder가 만드는 MonsterCommon.controller와 이름이 맞아야 한다.
        // 없는 파라미터에 Set을 하면 매 호출 에러가 찍히므로 양쪽을 함께 고칠 것.
        private static readonly int HashSpeed = Animator.StringToHash("Speed");
        private static readonly int HashAttack = Animator.StringToHash("Attack");
        private static readonly int HashAttackIndex = Animator.StringToHash("AttackIndex");
        private static readonly int HashHit = Animator.StringToHash("Hit");
        private static readonly int HashHitIndex = Animator.StringToHash("HitIndex");
        private static readonly int HashAlert = Animator.StringToHash("Alert");
        private static readonly int HashDead = Animator.StringToHash("Dead");

        private EntityView entity;
        private Renderer[] renderers;

        private Vector3 lastPosition;
        private float lastHealth = -1f;
        private float lastHitTime = float.MinValue;

        private bool dead;
        private float deadElapsed;
        private Vector3 viewHome;

        /// <summary>LOD 시스템이 갱신 주기를 조절하려고 읽는다. 없을 수 있다.</summary>
        public Animator Animator => animator;

        /// <summary>LOD 시스템이 그림자·스키닝 품질을 조절하려고 읽는다.</summary>
        public Renderer[] Renderers => renderers;

        private void Awake()
        {
            entity = GetComponent<EntityView>();
            if (entity == null) entity = GetComponentInParent<EntityView>();

            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (view == null && animator != null) view = animator.transform.parent;

            renderers = GetComponentsInChildren<Renderer>(true);

            // 화면 밖에서 본 경계를 매 프레임 다시 재는 비용은 몬스터 물량에서 그대로 부담이 된다.
            // 우리 몬스터는 스킨 경계를 크게 벗어나는 클립이 없어 꺼도 잘린 그림이 나오지 않는다.
            foreach (var r in renderers)
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;

            if (view != null) viewHome = view.localPosition;
            lastPosition = transform.position;
        }

        private void OnEnable()
        {
            // 재활성(풀링이 생길 경우)에도 깨끗한 상태에서 시작해야 한다
            dead = false;
            deadElapsed = 0f;
            lastHitTime = float.MinValue;
            lastPosition = transform.position;
            if (view != null) view.localPosition = viewHome;

            if (entity != null)
            {
                // 심이 먼저 만드는 몬스터는 Instantiate 시점(OnEnable)에 아직 엔티티가 없다 — 붙을 때 AttachEntity가 OnHealthChanged를 한 번 쏴 준다
                lastHealth = entity.Health != null ? entity.Health.CurrentHealth : 0f;
                entity.OnAttackAction += PlayAttack;
                entity.OnHealthChanged += HandleHealthChanged;
                entity.OnDeath += PlayDeath;
            }

            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.SetBool(HashDead, false);
            }

            MonsterAnimationSystem.Register(this);
        }

        private void OnDisable()
        {
            MonsterAnimationSystem.Unregister(this);

            if (entity != null)
            {
                entity.OnAttackAction -= PlayAttack;
                entity.OnHealthChanged -= HandleHealthChanged;
                entity.OnDeath -= PlayDeath;
            }
        }

        /// <summary>
        /// <see cref="MonsterAnimationSystem"/>이 티어에 맞는 주기로 호출한다.
        /// <paramref name="deltaTime"/>은 <b>지난 호출 이후 누적된 시간</b>이지 Time.deltaTime이 아니다 —
        /// 저빈도 티어에서는 한 번에 여러 프레임치가 들어온다.
        /// </summary>
        public void VisualTick(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            if (dead)
            {
                TickDeath(deltaTime);
                return;
            }

            TickSpeed(deltaTime);
        }

        /// <summary>
        /// 컬링 티어에서 돌아왔다 — 위치 기준점을 다시 잡는다.
        /// 이걸 안 하면 그동안 이동한 거리가 한 번에 속도로 환산돼 정지한 몬스터가 달리는 모션을 낸다.
        /// </summary>
        public void ResumeFromCulled()
        {
            lastPosition = transform.position;
        }

        // ── 이동 ────────────────────────────────────────────────────

        /// <summary>
        /// Speed는 <see cref="MovementComponent.IsMoving"/>이 아니라 <b>실제 변위</b>로 낸다.
        /// 플로우필드 모드에서는 벽에 막혀도 IsMoving이 true라(MovementComponent.cs:26)
        /// 그 값을 그대로 쓰면 제자리걸음이 생긴다. 넉백·군중 밀림까지 자연히 반영되는 것은 덤이다.
        /// </summary>
        private void TickSpeed(float deltaTime)
        {
            if (animator == null) return;

            Vector3 position = transform.position;
            Vector3 delta = position - lastPosition;
            delta.y = 0f;
            lastPosition = position;

            float reference = runSpeed > 0.01f
                ? runSpeed
                : (entity is MonsterView mv && mv.SimMovement != null ? mv.SimMovement.MoveSpeed : 5f);
            if (reference < 0.01f) reference = 5f;

            float normalized = Mathf.Clamp01(delta.magnitude / deltaTime / reference);
            animator.SetFloat(HashSpeed, normalized, speedDamp, deltaTime);
        }

        // ── 이벤트 반응 ─────────────────────────────────────────────

        /// <summary>플레이어를 발견했다 — 짧은 경계 모션. Monster가 어그로를 잡을 때 부른다.</summary>
        public void PlayAlert()
        {
            if (dead || animator == null) return;
            animator.SetTrigger(HashAlert);
        }

        private void PlayAttack()
        {
            if (dead || animator == null) return;
            animator.SetInteger(HashAttackIndex, attackVariants > 1 ? Random.Range(0, attackVariants) : 0);
            animator.SetTrigger(HashAttack);
        }

        /// <summary>
        /// 체력 변화 이벤트는 회복에도 온다(보스의 교전 포기 시 Health.Initialize 등).
        /// 줄었을 때만 피격 반응을 낸다.
        /// </summary>
        private void HandleHealthChanged(float current, float max)
        {
            bool damaged = lastHealth >= 0f && current < lastHealth;
            lastHealth = current;

            if (!damaged || dead || animator == null) return;
            if (Time.time < lastHitTime + hitReactionCooldown) return;
            lastHitTime = Time.time;

            animator.SetInteger(HashHitIndex, hitVariants > 1 ? Random.Range(0, hitVariants) : 0);
            animator.SetTrigger(HashHit);
        }

        // ── 사망 ────────────────────────────────────────────────────

        /// <summary>
        /// Dead는 트리거가 아니라 <b>bool</b>이다. LOD가 Animator를 껐다 켜는 사이에
        /// 트리거가 소비되지 않고 남거나 반대로 흘러가 버리는 사고를 막는다.
        /// </summary>
        private void PlayDeath()
        {
            if (dead) return;
            dead = true;
            deadElapsed = 0f;

            if (animator == null) return;

            animator.SetBool(HashDead, true);
            animator.SetFloat(HashSpeed, 0f);

            // 사망 클립이 없는 종은 마지막으로 피격 모션을 한 번 보여주고 가라앉기로 넘긴다.
            if (deathStyle == DeathStyle.SinkAway)
            {
                animator.SetInteger(HashHitIndex, hitVariants > 1 ? Random.Range(0, hitVariants) : 0);
                animator.SetTrigger(HashHit);
            }
        }

        private void TickDeath(float deltaTime)
        {
            if (deathStyle != DeathStyle.SinkAway || view == null) return;

            deadElapsed += deltaTime;
            float t = Mathf.Clamp01((deadElapsed - sinkDelay) / Mathf.Max(0.01f, sinkDuration));
            if (t <= 0f) return;

            // 부드럽게 시작해 일정하게 묻힌다 — 툭 떨어지면 죽은 게 아니라 사라진 것처럼 보인다
            view.localPosition = viewHome + Vector3.down * (sinkDepth * Mathf.SmoothStep(0f, 1f, t));
        }
    }
}
