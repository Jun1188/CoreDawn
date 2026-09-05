using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Visuals;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 몬스터 연출 — 심 이벤트(공격·피격·사망)와 이동 속도를 팩 모델(glb)의 클립으로 옮긴다(5a-4c: Animator·오버라이드 컨트롤러 퇴역).
    /// 상태기는 심에 있고 여기는 얇은 재생기다: <c>view.anim{idle, walk, run, alert, attack[], hit[], death}</c>의 클립 이름을
    /// glTFast가 만든 legacy <see cref="Animation"/>에서 찾아 CrossFade 한다. 이동은 속도(0..1)로 idle/walk/run 중 하나를 고르고,
    /// 한 번 재생(alert·attack·hit·death)은 끝날 때까지 이동 클립을 덮는다.
    /// 클립 이름이 모델에 없으면 오류 한 번 + 그 연출은 건너뛴다(폴백 없음).
    /// </summary>
    [DisallowMultipleComponent]
    public class MonsterVisualController : MonoBehaviour
    {
        public enum DeathStyle
        {
            AnimationClip,   // death 클립을 끝까지 틀고 멈춘다
            SinkAway,        // 피격 모션 한 번 뒤 가라앉는다(사망 클립이 없는 종)
        }

        /// <summary>view.anim — 상태 → 클립 이름. attack/hit는 변형 배열(재생 때 무작위).</summary>
        public sealed class ClipMap
        {
            public string Idle, Walk, Run, Alert, Death;
            public string[] Attack = System.Array.Empty<string>(), Hit = System.Array.Empty<string>();

            public static ClipMap From(CoreDawn.Data.AnimDef anim)
            {
                var m = new ClipMap();
                if (anim == null) return m;
                m.Idle = anim.Idle; m.Walk = anim.Walk; m.Run = anim.Run;
                m.Alert = anim.Alert; m.Death = anim.Death;
                m.Attack = anim.Attack?.ToArray() ?? System.Array.Empty<string>();
                m.Hit = anim.Hit?.ToArray() ?? System.Array.Empty<string>();
                return m;
            }
        }

        [Header("리그 — 비우면 해당 연출을 건너뛴다")]
        [Tooltip("모델 루트(pose가 실린 노드). 사망 시 이 노드를 가라앉힌다.")]
        [SerializeField] private Transform view;
        [Tooltip("glb 클립을 든 legacy Animation. 비우면 애니메이션 연출 전체를 건너뛴다.")]
        [SerializeField] private Animation anim;

        [Header("이동")]
        [Tooltip("이 속도(월드 단위/초)에서 달리기가 된다. 0 이하면 심 MovementModule.MoveSpeed를 쓴다.")]
        [SerializeField] private float runSpeed = 0f;
        [Tooltip("속도 감쇠 시간(초). 클수록 걷기↔달리기 전환이 느긋해진다.")]
        [SerializeField] private float speedDamp = 0.12f;
        [Tooltip("걷기로 넘어가는 정규화 속도")]
        [SerializeField] private float walkThreshold = 0.15f;
        [Tooltip("달리기로 넘어가는 정규화 속도")]
        [SerializeField] private float runThreshold = 0.6f;
        [Tooltip("클립 전환 페이드(초)")]
        [SerializeField] private float fade = 0.15f;

        [Tooltip("피격 반응 최소 간격(초). 없으면 연사에 맞을 때 몸이 계속 튕겨 이동이 뭉개져 보인다.")]
        [SerializeField] private float hitReactionCooldown = 0.6f;

        [Header("사망")]
        [SerializeField] private DeathStyle deathStyle = DeathStyle.AnimationClip;
        [Tooltip("가라앉기 시작까지의 뜸(초). 피격 모션이 한 번 보일 시간을 준다.")]
        [SerializeField] private float sinkDelay = 0.4f;
        [Tooltip("가라앉는 데 걸리는 시간(초). MonsterBrain.corpseSeconds(심의 제거 시점)보다 짧아야 소멸 전에 다 묻힌다.")]
        [SerializeField] private float sinkDuration = 1.2f;
        [Tooltip("가라앉는 깊이(월드 단위).")]
        [SerializeField] private float sinkDepth = 1.5f;

        ClipMap clips = new ClipMap();
        readonly HashSet<string> warned = new HashSet<string>();

        public void Wire(Transform viewRoot, Animation animation, ClipMap clipMap, DeathStyle style, float sink)
        {
            view = viewRoot;
            anim = animation;
            clips = clipMap ?? new ClipMap();
            deathStyle = style;
            sinkDepth = sink;
            if (anim != null)
            {
                anim.playAutomatically = false;
                foreach (var name in new[] { clips.Idle, clips.Walk, clips.Run }) SetWrap(name, WrapMode.Loop);
                foreach (var name in clips.Attack) SetWrap(name, WrapMode.Once);
                foreach (var name in clips.Hit) SetWrap(name, WrapMode.Once);
                SetWrap(clips.Alert, WrapMode.Once);
                SetWrap(clips.Death, WrapMode.ClampForever);
            }
        }

        void SetWrap(string name, WrapMode mode)
        {
            if (string.IsNullOrEmpty(name)) return;
            var st = anim[name];
            if (st != null) st.wrapMode = mode;
        }

        private EntityView entity;
        private Renderer[] renderers;
        private Vector3 lastPosition;
        private float lastHealth = -1f;
        private float lastHitTime = float.MinValue;
        private float speed;               // 정규화 속도(감쇠)
        private string locomotion;         // 지금 도는 이동 클립
        private float busyUntil = float.MinValue;   // 한 번 재생이 끝나는 시각 — 그때까지 이동 클립을 덮는다
        private bool dead;
        private float deadElapsed;
        private Vector3 viewHome;

        /// <summary>legacy Animation — MonsterAnimationSystem이 LOD로 켜고 끈다.</summary>
        public Animation Anim => anim;
        public Renderer[] Renderers => renderers;

        private void Awake()
        {
            entity = GetComponent<EntityView>();
            if (entity == null) entity = GetComponentInParent<EntityView>();
            if (anim == null) anim = GetComponentInChildren<Animation>(true);
            if (view == null && anim != null) view = anim.transform;
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
            busyUntil = float.MinValue;
            speed = 0f;
            locomotion = null;
            lastPosition = transform.position;
            if (view != null) view.localPosition = viewHome;
            if (entity != null)
            {
                // 심이 먼저 만드는 몬스터는 Instantiate 시점(OnEnable)에 아직 엔티티가 없다 — 붙을 때 AttachEntity가 OnHealthChanged를 한 번 쏜다
                lastHealth = entity.Health != null ? entity.Health.CurrentHealth : 0f;
                entity.OnAttackAction += PlayAttack;
                entity.OnHealthChanged += HandleHealthChanged;
                entity.OnDeath += PlayDeath;
            }
            PlayLocomotion(clips.Idle, true);
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

        /// <summary>LOD Reduced — Animation이 꺼진 채로 우리가 시간을 밀어 샘플한다(Animator.Update의 자리).</summary>
        public void Advance(float step)
        {
            if (anim == null || step <= 0f) return;
            foreach (AnimationState st in anim)
                if (st.enabled) st.time += step;
            anim.Sample();
        }

        public void ResumeFromCulled()
        {
            lastPosition = transform.position;
        }

        // ── 이동 ────────────────────────────────────────────────

        private void TickSpeed(float deltaTime)
        {
            if (anim == null) return;
            Vector3 position = transform.position;
            Vector3 delta = position - lastPosition;
            delta.y = 0f;
            lastPosition = position;

            float reference = runSpeed > 0.01f
                ? runSpeed
                : (entity is MonsterView mv && mv.SimMovement != null ? mv.SimMovement.MoveSpeed : 5f);
            if (reference < 0.01f) reference = 5f;

            float target = Mathf.Clamp01(delta.magnitude / deltaTime / reference);
            speed = speedDamp > 0f ? Mathf.Lerp(speed, target, 1f - Mathf.Exp(-deltaTime / speedDamp)) : target;

            if (Time.time < busyUntil) return;   // 한 번 재생 중 — 끝나면 이동 클립으로 돌아간다
            string want = speed >= runThreshold && !string.IsNullOrEmpty(clips.Run) ? clips.Run
                        : speed >= walkThreshold && !string.IsNullOrEmpty(clips.Walk) ? clips.Walk
                        : clips.Idle;
            PlayLocomotion(want, false);
        }

        void PlayLocomotion(string name, bool immediate)
        {
            if (anim == null || string.IsNullOrEmpty(name)) return;
            if (locomotion == name && anim.IsPlaying(name)) return;
            if (!Has(name)) return;
            locomotion = name;
            if (immediate) anim.Play(name); else anim.CrossFade(name, fade);
        }

        /// <summary>한 번 재생 — 끝날 때까지 이동 클립을 덮는다.</summary>
        void PlayOnce(string name)
        {
            if (anim == null || string.IsNullOrEmpty(name) || !Has(name)) return;
            var st = anim[name];
            st.time = 0f;
            anim.CrossFade(name, fade * 0.5f);
            busyUntil = Time.time + st.length / Mathf.Max(0.01f, st.speed);
            locomotion = null;   // 끝나면 TickSpeed가 다시 고른다
        }

        bool Has(string name)
        {
            if (anim[name] != null) return true;
            if (warned.Add(name)) Debug.LogError($"[MonsterVisualController] {name}: 클립이 모델에 없습니다(view.anim을 확인).", this);
            return false;
        }

        static string Pick(string[] variants) => variants.Length == 0 ? null : variants[variants.Length > 1 ? Random.Range(0, variants.Length) : 0];

        // ── 이벤트 반응 ─────────────────────────────────────────────

        public void PlayAlert()
        {
            if (dead || anim == null) return;
            PlayOnce(clips.Alert);
        }

        private void PlayAttack()
        {
            if (dead || anim == null) return;
            PlayOnce(Pick(clips.Attack));
        }

        private void HandleHealthChanged(float current, float max)
        {
            bool damaged = lastHealth >= 0f && current < lastHealth;
            lastHealth = current;
            if (!damaged || dead || anim == null) return;
            if (Time.time < lastHitTime + hitReactionCooldown) return;
            lastHitTime = Time.time;
            PlayOnce(Pick(clips.Hit));
        }

        // ── 사망 ────────────────────────────────────────────────

        private void PlayDeath()
        {
            if (dead) return;
            dead = true;
            deadElapsed = 0f;
            if (anim == null) return;
            // 사망 클립이 없는 종은 마지막으로 피격 모션을 한 번 보여주고 가라앉기로 넘긴다.
            if (deathStyle == DeathStyle.SinkAway) PlayOnce(Pick(clips.Hit));
            else PlayOnce(clips.Death);
            busyUntil = float.MaxValue;   // 죽은 뒤엔 이동 클립으로 돌아가지 않는다
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
