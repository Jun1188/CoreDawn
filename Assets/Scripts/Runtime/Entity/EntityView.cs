using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.Pings;
using CoreDawn.Sim;
using SimEntity = CoreDawn.Sim.Entity;

namespace CoreDawn.Entities
{
    // EntityView 확장 유틸 — 타겟 유효성/거리 판정 (구 IInteractable 확장의 Entity 버전)
    public static class EntityViewExtensions
    {
        // 순수 null 체크만으로는 Destroy된 MonoBehaviour(가짜 null)를 걸러내지 못하므로
        // UnityEngine.Object의 == 오버로드를 활용. SetActive(false)로 내려간 엔티티도 무효 처리.
        public static bool IsValidTarget(this EntityView target)
        {
            if (target == null) return false;
            if (!target.gameObject.activeInHierarchy) return false;
            if (target.Entity == null) return false;   // 심이 아직 안 붙은 건물(배치 직후·씬 굳힘)은 세상에 없는 것과 같다
            return !target.IsDead;
        }

        // 멀티타일 건물처럼 부피가 있는 대상은 중심점 대신 콜라이더 표면까지의 거리를 사용
        public static float DistanceTo(this EntityView target, Vector3 from)
        {
            // 심 건물은 점유 풋프린트(칸) 기준으로 잰다 — 모델(콜라이더)이 풋프린트보다 한참
            // 작을 수 있는데(3×3칸 코어의 우주선), 몬스터의 길을 막는 것은 풋프린트라서
            // 풋프린트 경계까지 붙은 몬스터의 공격이 닿으려면 거리의 정본도 풋프린트여야 한다.
            if (target is BuildingView be && be.TryGetFootprintRect(out Vector3 min, out Vector3 max))
                return GridGeometry.DistanceToRect(from, min, max);   // 지상전이라 높이는 무시한다

            var col = target.GetComponentInChildren<Collider>();
            if (col == null) return Vector3.Distance(from, target.GetPosition());

            // 논컨벡스 MeshCollider는 ClosestPoint를 지원하지 않는다 — 입력점을 그대로 돌려줘
            // 거리가 항상 0이 되고, 몬스터가 맵 반대편에서 "닿았다"며 공격 상태로 굳는다.
            // 건물(전부 논컨벡스 메시)은 전 콜라이더 AABB 합의 최근접점으로 근사한다 —
            // 구 루트 박스 콜라이더 시절과 같은 감각의 거리라 전투 밸런스도 그대로다.
            if (col is MeshCollider mesh && !mesh.convex)
            {
                var cols = target.GetComponentsInChildren<Collider>();
                var b = cols[0].bounds;
                for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
                return Vector3.Distance(from, b.ClosestPoint(from));
            }

            return Vector3.Distance(from, col.ClosestPoint(from));
        }
    }

    /// <summary>
    /// 게임 개체의 씬 표현(뷰) — 몬스터·플레이어·건물·둥지의 MonoBehaviour 베이스.
    ///
    /// 정본은 심 <see cref="SimEntity"/>다: 체력·편·번호는 그쪽에 있다. 이 컴포넌트가 하는 일은 셋뿐이다 —
    /// ① 그 엔티티를 가리키고(<see cref="Entity"/>) ② 이벤트를 받아 연출(체력바·사망)을 하며
    /// ③ 물리가 굴린 위치를 심에 돌려준다(서버 권위 모델에서 물리는 뷰에 남기는 하이브리드).
    ///
    /// 과도기(리팩토링 2단계): 몬스터·플레이어·둥지는 뷰가 Awake에서 심 엔티티를 만들어 붙인다(뷰 우선).
    /// 건물은 FactorySystem이 먼저 만들고 뷰가 <see cref="AttachEntity"/>로 받는다(심 우선).
    /// 3·4단계에서 생성 주체가 전부 심으로 가고, 효과(EffectController)·이동·전투 컴포넌트도 그때 옮긴다.
    /// </summary>
    public class EntityView : MonoBehaviour, IPingable
    {
        // ── 핑 대상 (IPingable) — 몬스터·건물·둥지·플레이어 공통. 표시 이름은 하위가 덮어쓴다.
        //    자기 자신(로컬 플레이어)은 여기서 거르지 않는다 — 다른 플레이어는 찍혀야 하므로 조준이 계층으로 뺀다.
        public virtual string PingLabel => name;
        public GameObject PingRoot => gameObject;
        public virtual bool CanBePinged => isActiveAndEnabled && !IsDead;

        [Header("Entity Settings (Compatibility)")]
        [Tooltip("PlayerController 등 기존 코드 호환용. 몬스터 이동 속도는 심 Movement 쪽 값을 사용한다.")]
        public float moveSpeed = 5f;

        [Header("Death Settings")]
        [Tooltip("사망 연출(애니메이션 등)에 쓸 지연 시간(초). 이후 오브젝트가 소멸/비활성화된다.")]
        [SerializeField] private float deathDelay = 2f;
        [Tooltip("true면 사망 연출 후 Destroy로 완전 소멸, false면 SetActive(false)로 비활성화만 한다.")]
        [SerializeField] private bool destroyOnDeath = true;

        /// <summary>
        /// 심 정본. 만드는 쪽(FactorySystem·MonsterSystem·PlayerSystem·WorldPopulator)이 <see cref="AttachEntity"/>로 채운다.
        /// 그 전에는 null — 뷰는 스스로 엔티티를 만들지 않는다(HP 정본은 데이터 maxHp, 뷰 프리팹에 HP는 없다).
        /// </summary>
        public SimEntity Entity { get; private set; }

        /// <summary>체력 — 심 Health 모듈로 곧장 간다. 심이 아직 안 붙었으면 null.</summary>
        public Health Health => Entity?.Health;

        /// <summary>심 효과 모듈 — 활성 지속 효과·배율. 심이 아직 안 붙었으면 null.</summary>
        public Effects Effects => Entity?.Get<Effects>();

        /// <summary>죽었거나 심에서 빠졌는가. 심이 아직 안 붙은 뷰는 죽은 것이 아니다(IsValidTarget이 따로 거른다).</summary>
        public virtual bool IsDead =>
            Entity != null && (Entity.IsRemoved || (Entity.Health != null && Entity.Health.IsDead));

        // ── 이벤트 릴레이 — 구독자는 뷰에 걸고, 뷰가 심 Health에 이어 준다.
        //    심이 나중에 붙는 건물(브리지가 AttachEntity)이나 부착 전 구독이 안전하도록 뷰가 이벤트를 소유한다.
        public event Action<float, float> OnHealthChanged;
        public event Action OnDeath;

        /// <summary>이 엔티티가 공격했다 — 심 Attack.Attacked의 릴레이(애니메이션·소리). 효과는 이미 심에서 처리됐다.</summary>
        public event Action OnAttackAction;

        // 뷰는 Awake에서 심을 건드리지 않는다 — 엔티티는 만드는 쪽이 붙여 준다. 하위가 base.Awake()를 부르는 계약만 남긴다.
        protected virtual void Awake() { }

        /// <summary>
        /// 심 엔티티 연결 — 만드는 쪽(브리지·스포너·시스템·WorldPopulator)이 부른다.
        /// 심 Health의 이벤트를 뷰 이벤트로 이어 주고, 이미 구독한 쪽에 현재 체력을 한 번 알린다.
        /// </summary>
        public void AttachEntity(SimEntity entity)
        {
            if (entity == null || ReferenceEquals(Entity, entity)) return;
            DetachEntity();

            Entity = entity;
            EntityViewRegistry.Register(this, entity);   // 심 엔티티 → 뷰 (심 질의 결과를 화면이 다룰 때)
            var h = entity.Health;
            if (h != null)
            {
                h.OnHealthChanged += RelayHealthChanged;
                h.OnDeath += RelayDeath;
            }

            var a = entity.Get<Attack>();
            if (a != null) a.Attacked += RelayAttacked;   // 공격 연출 이벤트 — 심이 때렸다고 하면 뷰가 애니메이션
            OnEntityAttached();
            if (h != null) OnHealthChanged?.Invoke(h.CurrentHealth, h.MaxHealth);
        }

        void DetachEntity()
        {
            if (Entity == null) return;
            var h = Entity.Health;
            if (h != null)
            {
                h.OnHealthChanged -= RelayHealthChanged;
                h.OnDeath -= RelayDeath;
            }
            var a = Entity.Get<Attack>();
            if (a != null) a.Attacked -= RelayAttacked;
            EntityViewRegistry.Unregister(this, Entity);
            Entity = null;
        }

        /// <summary>심 엔티티가 붙은 직후 — 하위가 심 상태(풋프린트 등)에 의존하는 초기화를 여기서 한다.</summary>
        protected virtual void OnEntityAttached() { }

        void RelayHealthChanged(float current, float max) => OnHealthChanged?.Invoke(current, max);
        void RelayAttacked(SimEntity target) => OnAttackAction?.Invoke();

        // 순서: 심(EffectSystem이 효과 정리, 공장이 건물 제거)이 먼저 결정하고 → 사망 연출 → 외부 구독자
        void RelayDeath()
        {
            HandleDeath();
            OnDeath?.Invoke();
        }

        protected virtual void Start() { }

        protected virtual void Update() { }   // 효과 틱은 심(EffectSystem)이 돌린다

        /// <summary>뷰 → 심 위치 미러를 하는가. 기본 false(건물은 안 움직이고 몬스터는 심 → 뷰). 물리는 뷰가 굴리는 플레이어가 켠다.</summary>
        protected virtual bool PushesPositionToSim => false;

        // 위치 동기 — 물리는 뷰가 굴리고 심은 결과를 받는다. 건물은 움직이지 않고, 몬스터는 반대 방향(심 → 뷰).
        protected virtual void LateUpdate()
        {
            if (PushesPositionToSim && Entity != null) Entity.Position = transform.position;
        }

        /// <summary>
        /// 공격 명중의 단일 진입점 — 투사체·오라가 명중을 감지해(PhysX) 발사 시점에 이미 변환·베이크된 심 효과 목록을 넘긴다.
        /// 여기서부터는 심 안이다: 피해·넉백·지속 효과·사망이 Effects → Health/Movement에서 끝난다. 심이 없으면 무시.
        /// 근접 공격은 여길 거치지 않는다 — 심 Attack이 심 안에서 직접 건다.
        /// </summary>
        public void ApplyEffects(IReadOnlyList<Effect> effects, SimEntity source, Vector3 hitPoint, Vector3 hitDirection = default)
            => Effects?.Apply(effects, source, hitPoint, hitDirection);

        /// <summary>출처 없는 피해(환경 등). 새 코드는 출처를 넘기는 쪽을 쓸 것.</summary>
        public void ReceiveDamage(float amount) => ReceiveDamage(amount, null);

        /// <summary>
        /// 뷰에서 직접 피해를 넣는 얇은 호환 경로(환경·구 코드). 받는 배율(Effects)·보호막·무적·아군 무시는
        /// 전부 심 Health.Damage 안의 인터셉터가 거른다 — 뷰는 아무 규칙도 갖지 않는다.
        /// </summary>
        public virtual void ReceiveDamage(float amount, EntityView source)
        {
            var h = Health;
            if (h == null || amount <= 0f) return;
            h.Damage(amount, source != null ? source.Entity : null);
        }

        // 구 호환 — 출처·효과 없는 순수 피해. 새 코드는 ApplyEffects를 쓸 것.
        public virtual void TakeDamage(float damageAmount) => ReceiveDamage(damageAmount);

        // 즉시 사망 — HP를 0으로 만들고 사망 흐름(OnDeath → HandleDeath)을 태운다
        public void Die() => Health?.Kill();

        public virtual void Revive(Vector3 respawnPosition)
        {
            transform.position = respawnPosition;
            gameObject.SetActive(true);
            Health?.ResetToFull();   // 체력·사망 상태 초기화
        }

        // 런타임 부착 시 사망 방식 변경용 (예: FPS 플레이어는 Destroy 대신 비활성화)
        public void SetDeathBehavior(bool destroy, float delay)
        {
            destroyOnDeath = destroy;
            deathDelay = Mathf.Max(0f, delay);
        }

        public Vector3 GetPosition() => transform.position;

        // 기본 사망 처리: 연출(deathDelay) 후 소멸/비활성화. 즉시 소멸이 필요한 엔티티(건물)는 override.
        protected virtual void HandleDeath()
        {
            if (deathDelay <= 0f) FinishDeath();
            else Invoke(nameof(FinishDeath), deathDelay);
        }

        private void FinishDeath()
        {
            if (destroyOnDeath) Destroy(gameObject);
            else gameObject.SetActive(false);
        }

        // 게임 종료 중에는 심을 건드리지 않는다 — 그 시점의 제거는 드롭 같은 새 오브젝트 생성을 유발해 에러가 난다
        private static bool quitting;
        private void OnApplicationQuit() => quitting = true;

        // 뷰가 사라지면 뷰가 만든 심 엔티티도 같이 — 하위 클래스가 OnDestroy를 쓰면 반드시 base를 부를 것
        /// <summary>앱 종료 중인가 — 종료 시 파괴되는 뷰가 심을 건드리지 않게(정적 심이 먼저 사라질 수 있다). 하위 뷰(몬스터·플레이어)가 쓴다.</summary>
        protected static bool ApplicationQuitting => quitting;

        // 엔티티의 제거는 만든 쪽의 몫(공장·몬스터 시스템·플레이어 시스템·둥지 뷰) — 여기서는 연결만 끊는다
        protected virtual void OnDestroy()
        {
            if (Entity == null) return;
            DetachEntity();
        }
    }
}
