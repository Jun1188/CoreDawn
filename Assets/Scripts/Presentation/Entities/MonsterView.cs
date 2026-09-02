using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.Sim;
using CoreDawn.UI;
using SimEntity = CoreDawn.Sim.Entity;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 몬스터의 씬 표현 — 심 엔티티(Health·Movement·Attack·MonsterBrain)를 그린다. 구 Monster(뷰가 두뇌·이동을 가짐)의 후신.
    ///
    /// 하는 일: ① 매 프레임 심 위치·방향을 트랜스폼에 옮긴다(LateUpdate) ② 두뇌 이벤트(각성)를 연출로 ③ 심 Attack이
    /// "때린다"고 하면 대상 뷰를 찾아 명중 효과를 건다(효과 시스템이 뷰에 있는 4단계까지의 다리) ④ 체력바.
    /// 둥지·스포너·세이브가 부르던 옛 표면(SetAsBoss·SetAsNestDefender·Provoke·IsBoss…)은 두뇌로 위임해 그대로 둔다.
    /// </summary>
    public class MonsterView : EntityView
    {
        private MonsterVisualController visual; // 연출 담당 — 없을 수 있다(자리표시 프리팹)
        private MonsterBrainModule brain;
        private MovementModule movement;

        // 심(MonsterSystem.Spawn)이 먼저 만든다 — 뷰는 MonsterSpawner가 붙여 준 것을 받는다

        /// <summary>심 이동 모듈 — 연출(애니 속도)·곡사 예측(타워)이 읽는다. 심이 안 붙었으면 null.</summary>
        public MovementModule SimMovement => movement;

        public MonsterBrainModule Brain => brain;

        // ── 옛 표면 (둥지·스포너·프로브·세이브가 쓴다) — 두뇌 위임
        public bool IsBoss => brain != null && brain.IsBoss;
        public bool HasBeenAttacked => brain != null && brain.HasBeenAttacked;
        public bool IsNestDefender => brain != null && brain.IsNestDefender;
        public Vector3 DefendOrigin => brain != null ? brain.DefendOrigin : transform.position;
        public float PatienceRatio => brain != null ? brain.PatienceRatio : 1f;
        public bool IsReturningHome => brain != null && brain.IsReturningHome;

        protected override void Awake()
        {
            base.Awake();
            visual = GetComponent<MonsterVisualController>();
        }

        protected override void OnEntityAttached()
        {
            base.OnEntityAttached();
            brain = Entity.Get<MonsterBrainModule>();
            movement = Entity.Get<MovementModule>();

            if (movement != null) movement.PivotToBottom = MeasurePivotToBottom(transform);
            if (brain != null) brain.Alerted += OnAlerted;
            Entity.Removed += OnEntityRemoved;

            SyncTransform();
        }

        protected override void OnDestroy()
        {
            if (brain != null) brain.Alerted -= OnAlerted;
            var e = Entity;
            if (e != null) e.Removed -= OnEntityRemoved;
            base.OnDestroy();
            // 뷰가 먼저 사라지면(사망 연출 끝·씬 언로드) 심 엔티티도 치운다 — 심이 지운 경우(복귀 소멸)는 이미 제거돼 있다
            if (e != null && !e.IsRemoved && !ApplicationQuitting) SimRunner.Monsters.Despawn(e);
        }


        // 심이 먼저 지웠다(복귀 도착 소멸 등) — 껍데기도 사라진다
        void OnEntityRemoved(SimEntity _)
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        /// <summary>
        /// 피벗에서 콜라이더 바닥까지 얼마나 내려가는지. 몬스터의 피벗은 캡슐 중앙이라 이 값을 더해 줘야 발이 지면에 놓인다.
        /// Collider.bounds(월드 AABB)를 쓰지 않고 치수에서 직접 계산한다 — bounds는 물리 동기화 전에는 낡은 값을 준다.
        /// </summary>
        static float MeasurePivotToBottom(Transform t)
        {
            float scale = Mathf.Abs(t.lossyScale.y);
            var capsule = t.GetComponent<CapsuleCollider>();
            if (capsule != null) return (capsule.height * 0.5f - capsule.center.y) * scale;
            var box = t.GetComponent<BoxCollider>();
            if (box != null) return (box.size.y * 0.5f - box.center.y) * scale;
            return 0f;
        }

        protected override void Start()
        {
            base.Start();

            // 머리 위 HP 바 — 보스는 크게. SetAsBoss가 Instantiate 직후(Start 이전)에 불리므로 여기서 IsBoss는 이미 확정돼 있다.
            var bar = WorldHealthBar.Attach(this, large: IsBoss);

            // 보스만 체력바 아래에 인내심 바를 단다 — 만땅일 때는 알아서 숨는다.
            if (IsBoss && bar != null)
                bar.EnableSecondaryBar(() => PatienceRatio, new Color(1f, 0.78f, 0.2f, 0.95f));
        }

        protected override void Update()
        {
            base.Update();
            // 효과 시스템(감속)은 아직 뷰에 있다 — 심 이동에 배율을 밀어 넣는다
        }

        // 심이 옮긴 위치·방향을 그린다. 군중 겹침 해소까지 끝난 뒤(시스템 Update)라 LateUpdate.
        protected override void LateUpdate() => SyncTransform();

        void SyncTransform()
        {
            if (Entity == null) return;
            transform.position = Entity.Position;
            var facing = Entity.Facing;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(facing);
        }

        void OnAlerted() => visual?.PlayAlert();

        // ── 옛 표면 → 두뇌 ──────────────────────────────────────────

        /// <summary>보스를 각성시킨다 — 피해·명백한 적의(빗맞은 사격). attacker가 null이면 플레이어를 찾는다.</summary>
        public void Provoke(PlayerView attacker) => brain?.Provoke(attacker != null ? attacker.Entity : null);

        // 플레이어 센서 콜백 — 플레이어가 자기 범위의 몬스터를 찾아 부른다
        public void OnDetectedByPlayer(PlayerView player)
        {
            if (player == null || player.Entity == null) return;
            brain?.OnDetected(player.Entity);
        }

        public void OnLostByPlayer() => brain?.OnLost();

        public void SetAsBoss() => SetAsBoss(null);

        /// <summary>보스로 배선한다. 교전 반경의 중심은 지금 서 있는 자리다.</summary>
        public void SetAsBoss(NestEngagementZone zone) => brain?.SetAsBoss(ZoneOf(zone));

        public void SetAsNestDefender(PlayerView target) => SetAsNestDefender(target, null, null);
        public void SetAsNestDefender(PlayerView target, NestEngagementZone zone) => SetAsNestDefender(target, zone, null);

        /// <summary>둥지 방어자로 배선한다. escort(보스전 지원군)가 있으면 거리 규칙을 건너뛰고 보스의 교전에 종속된다.</summary>
        public void SetAsNestDefender(PlayerView target, NestEngagementZone zone, MonsterView escort)
            => brain?.SetAsNestDefender(target != null ? target.Entity : null, ZoneOf(zone), escort != null ? escort.Entity : null);

        /// <summary>세이브 복원 전용 — 정체성(보스/방어자)과 각성 여부를 되돌린다.</summary>
        public void RestoreSaveState(bool boss, bool awakened, bool nestDefender, Vector3 origin)
            => brain?.RestoreSaveState(boss, awakened, nestDefender, origin);

        /// <summary>뷰의 교전 구역(MonoBehaviour)에서 심 구조체로 — 숫자만 넘어간다.</summary>
        static EngagementZone? ZoneOf(NestEngagementZone zone)
            => zone != null ? new EngagementZone(zone.ChaseRange, zone.LeashRange, zone.DayOnly) : (EngagementZone?)null;
    }
}
