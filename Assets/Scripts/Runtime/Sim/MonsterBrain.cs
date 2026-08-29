using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>몬스터 두뇌의 상태 — 매 틱 두뇌가 Update를 부른다. 구 IEntityState(뷰 상태기)의 심 판.</summary>
    public interface IEntityState
    {
        void Enter(MonsterBrain brain);
        void Update(MonsterBrain brain, float dt);
        void Exit(MonsterBrain brain);
    }

    /// <summary>
    /// 몬스터 두뇌 — 심 모듈. 상태기(대기·플로우필드 진격·추적·공격·복귀·사망)와 보스 인내심·둥지 방어 규칙.
    /// 구 Monster.cs(뷰, 621줄)의 로직을 <b>동작 변경 없이</b> 옮긴 것이다: 표적은 Player(뷰) 대신 Entity,
    /// 교전 구역은 NestEngagementZone(뷰) 대신 <see cref="EngagementZone"/> 구조체, Time.time 대신 시스템 시계,
    /// FlowFieldManager·PathRequest 대신 <see cref="INavigation"/>. 연출은 이벤트(<see cref="Alerted"/>)로 뷰에 알린다.
    ///
    /// 밤에 스폰되어 플로우필드를 따라 코어/타워로 전진·공격한다. 플레이어에 감지되면(OnDetected) 런타임 A* 추적으로
    /// 전환하고, 범위를 벗어나면(OnLost) 다시 플로우필드로 복귀한다.
    /// </summary>
    public sealed class MonsterBrain : EntityModule
    {
        readonly MonsterSystem system;
        readonly MonsterSpec spec;

        Movement movement;
        Attack attack;
        IEntityState currentState;

        public MonsterSystem System => system;
        public INavigation Nav => system.Nav;
        public float Now => system.Now;
        public Movement Movement => movement;
        public Attack Attack => attack;
        public IEntityState CurrentState => currentState;

        /// <summary>플레이어를 발견했거나 각성했다 — 뷰가 경계 모션을 튼다. 추적 시작을 지연시키지 않는다.</summary>
        public event Action Alerted;

        bool aggroOnPlayer;
        bool isNestDefender;

        /// <summary>
        /// 이 몬스터의 자리 — 교전 반경의 중심이자 복귀 목적지다. 배치될 때 한 번 기록하고 이후 바뀌지 않는다(끌려나가도 고정).
        /// 복귀 목적지와 거리 판정의 원점은 같아야 한다 — 스폰 포인트가 둥지에서 수십 m 떨어져 있어, 둥지 중심으로 재면
        /// 보스가 제 자리에 서 있는 것만으로 리쉬 밖이 됐다.
        /// </summary>
        Vector3 defendOrigin;
        float defendRadius = 30f;
        Entity currentTarget;
        EngagementZone? zone;

        // 보스 전용
        bool isBoss;
        bool hasBeenAttacked;
        bool isReturningToNest;
        float returnStartTime;

        // 인내심 런타임 상태 — 남은 인내심(초)
        float patience;

        // 이 몬스터가 호위하는 보스(둥지가 보스전 지원군으로 뱉은 개체만 값이 있다).
        // 값이 있으면 거리가 아니라 보스의 교전 지속 여부로 이탈을 판정한다.
        Entity escortBoss;

        public MonsterBrain(MonsterSystem system, in MonsterSpec spec)
        {
            this.system = system ?? throw new ArgumentNullException(nameof(system));
            this.spec = spec;
        }

        protected internal override void OnAttach()
        {
            movement = Owner.Get<Movement>();
            attack = Owner.Get<Attack>();
            patience = spec.MaxPatience;

            Owner.Died += OnDied;
            // 어그로를 OnHealthChanged("HP가 max 미만")에 걸면 안 된다 — 복귀 중 체력 재생이 매 틱 그 조건을 만족시켜
            // 보스가 무한히 다시 깨어난다. 피해라는 사건 자체(Health.Damaged)가 유일한 각성 경로다.
            if (Owner.Health != null) Owner.Health.Damaged += OnDamaged;

            SetState(new IdleState());
        }

        protected internal override void OnDetach()
        {
            Owner.Died -= OnDied;
            if (Owner.Health != null) Owner.Health.Damaged -= OnDamaged;
            currentState?.Exit(this);
            currentState = null;
        }

        // ── 정체성 ───────────────────────────────────────────────────

        public bool IsBoss => isBoss;
        public bool HasBeenAttacked => hasBeenAttacked;
        public bool IsNestDefender => isNestDefender;
        public Vector3 DefendOrigin => defendOrigin;

        /// <summary>남은 인내심 비율(0~1) — 체력바 아래 인내심 바가 읽는다.</summary>
        public float PatienceRatio => spec.MaxPatience > 0f ? Mathf.Clamp01(patience / spec.MaxPatience) : 1f;

        /// <summary>인내심이 다 닳아 자기 자리로 돌아가는 중인가. 이 동안에는 무슨 짓을 해도 다시 끌어당길 수 없다.</summary>
        public bool IsReturningHome => isReturningToNest;

        bool ReturnTimedOut => spec.ReturnTimeout > 0f && Now - returnStartTime > spec.ReturnTimeout;

        /// <summary>복귀 상태를 켜고 끄는 유일한 통로 — 넉백 면역이 여기에 딸려 다닌다(둘은 같은 규칙의 양면).</summary>
        bool IsReturning
        {
            get => isReturningToNest;
            set
            {
                isReturningToNest = value;
                if (movement != null) movement.IgnoreKnockback = value;
            }
        }

        float EffectivePatienceRadius =>
            spec.PatienceRadius > 0f ? spec.PatienceRadius : (zone.HasValue ? zone.Value.ChaseRange : 25f);

        float EffectiveLeashRange => zone.HasValue ? zone.Value.LeashRange : defendRadius;

        bool CanChase(Vector3 origin, Vector3 targetPos)
        {
            if (!zone.HasValue) return true;
            var z = zone.Value;
            if (z.DayOnly && !system.IsDay()) return false;
            return Vector3.Distance(origin, targetPos) <= z.ChaseRange;
        }

        // ── 배선 (스포너·둥지가 부른다) ─────────────────────────────

        /// <summary>보스로 배선한다. 교전 반경의 중심은 지금 서 있는 자리다 — 둥지 중심이 아니다.</summary>
        public void SetAsBoss(EngagementZone? engagementZone)
        {
            isBoss = true;
            hasBeenAttacked = false;
            IsReturning = false;
            defendOrigin = Owner.Position;
            zone = engagementZone;
            defendRadius = zone.HasValue ? zone.Value.LeashRange : defendRadius;
            patience = spec.MaxPatience;
        }

        /// <summary>
        /// 둥지 방어자로 배선한다. 보스와 마찬가지로 교전 반경의 중심은 지금 서 있는 자리다.
        /// escort가 주어지면(보스전 지원군) 교전 구역의 거리 규칙을 건너뛰고 무조건 교전에 붙는다 — 이탈 판정도 보스가 대신한다.
        /// </summary>
        public void SetAsNestDefender(Entity target, EngagementZone? engagementZone, Entity escort)
        {
            isNestDefender = true;
            defendOrigin = Owner.Position;
            zone = engagementZone;
            defendRadius = zone.HasValue ? zone.Value.LeashRange : defendRadius;
            currentTarget = target;
            escortBoss = escort;

            aggroOnPlayer = escort != null || !zone.HasValue || (target != null && CanChase(defendOrigin, target.Position));
            SetState(aggroOnPlayer ? (IEntityState)new ChaseState(target) : new ReturnToOriginState(defendOrigin));
        }

        /// <summary>
        /// 세이브 복원 전용 — 정체성(보스/방어자)과 각성 여부를 되돌린다.
        /// AI 상태는 Idle에서 다시 시작한다 — 추적 대상은 저장할 수 없고, 플레이어 센서가 다음 스캔에서 어그로를 다시 걸어 준다.
        /// </summary>
        public void RestoreSaveState(bool boss, bool awakened, bool nestDefender, Vector3 origin)
        {
            isBoss = boss;
            hasBeenAttacked = awakened;
            isNestDefender = nestDefender;
            defendOrigin = origin;

            aggroOnPlayer = false;
            currentTarget = null;
            escortBoss = null;
            patience = spec.MaxPatience;

            // 즉시 풀피가 사라졌으므로, 각성하지 않았는데 체력이 깎여 있는 보스는 저장 시점에 둥지로 복귀하던 중이었다는 뜻이다.
            var hp = Owner.Health;
            IsReturning = boss && !awakened && hp != null && hp.CurrentHealth < hp.MaxHealth;
            returnStartTime = Now;   // 복귀 제한 시간은 지금부터 다시 잰다

            SetState(isReturningToNest
                ? (IEntityState)new ReturnToOriginState(defendOrigin, despawnOnArrival: false)
                : new IdleState());
        }

        // ── 각성·어그로 ─────────────────────────────────────────────

        void OnDamaged(float amount, Entity source)
        {
            if (amount <= 0f || !Owner.IsAlive) return;
            // 때린 쪽이 플레이어면 그 플레이어를, 타워·환경이면 씬의 플레이어를 상대로 각성한다(구 Provoke(null))
            if (isBoss) Provoke(source != null && source == system.PlayerEntity ? source : null);
        }

        /// <summary>보스를 각성시킨다 — 피해를 입었을 때와, 명백한 적의(빗맞은 사격)를 감지했을 때. attacker가 null이면 플레이어를 찾는다.</summary>
        public void Provoke(Entity attacker)
        {
            if (!isBoss || !Owner.IsAlive) return;

            // 인내심이 다 닳아 돌아서는 순간 그 교전은 끝난 것이다 — 복귀는 되돌릴 수 없다.
            // 피해 자체는 계속 들어간다. 복귀 중 재생보다 빠르게 몰아치면 도망가는 보스를 마저 잡을 수 있다.
            if (isReturningToNest) return;

            // 싸울 상대부터 정한다 — 각성보다 먼저다. 상대를 못 찾았는데 깨우기만 하면 currentTarget이 null인 보스가 남아
            // 다음 틱 TickBossPatience에서 그대로 교전을 포기한다(= 제자리에서 풀피로 되돌아가는 증상).
            Entity player = IsValidTarget(attacker) ? attacker : system.PlayerEntity;
            if (!IsValidTarget(player)) return;

            // 인내심은 교전이 새로 시작될 때만 채운다 — 매 피격마다 채우면 멀리서 계속 쏘는 것만으로 리쉬가 없는 것과 같아진다.
            if (!hasBeenAttacked)
            {
                hasBeenAttacked = true;
                patience = spec.MaxPatience;
                Debug.Log("[MonsterBrain] 보스 몬스터가 공격을 받아 깨어났습니다!");
            }

            IsReturning = false;
            aggroOnPlayer = true;
            currentTarget = player;

            // 이미 같은 대상을 쫓거나 때리는 중이면 상태를 갈아엎지 않는다 — 경로가 초기화되고 공격이 끊긴다
            bool alreadyEngaged =
                currentState is ChaseState chase && ReferenceEquals(chase.Target, player) ||
                currentState is AttackState attack && ReferenceEquals(attack.Target, player);
            if (alreadyEngaged) return;

            Alerted?.Invoke();
            SetState(new ChaseState(player));
        }

        /// <summary>플레이어 센서 콜백 — 플레이어가 자기 범위의 몬스터를 찾아 부른다(개체별 스캔 없음).</summary>
        public void OnDetected(Entity player)
        {
            if (!Owner.IsAlive || aggroOnPlayer || !IsValidTarget(player)) return;
            if (isBoss && !hasBeenAttacked) return;
            if (isNestDefender && escortBoss == null && zone.HasValue && !CanChase(defendOrigin, player.Position)) return;
            aggroOnPlayer = true;
            currentTarget = player;
            Alerted?.Invoke();   // 발견 모션 — 연출만, 추적 시작을 지연시키지 않는다
            SetState(new ChaseState(player));
        }

        public void OnLost()
        {
            if (!aggroOnPlayer) return;
            // 깨어난 보스는 플레이어 센서 반경이 아니라 인내심으로 교전을 유지한다.
            if (isBoss && hasBeenAttacked) return;
            // 보스전 지원군도 마찬가지 — 이탈은 보스의 교전 지속 여부가 정한다.
            var escortBrain = escortBoss?.Get<MonsterBrain>();
            if (escortBoss != null && escortBoss.IsAlive && escortBrain != null && escortBrain.HasBeenAttacked) return;
            aggroOnPlayer = false;
            currentTarget = null;
            if (!Owner.IsAlive) return;

            // 방어자는 플로우필드(건물 공격)로 복귀하지 않고 둥지 쪽으로 돌아간다.
            SetState(isNestDefender ? (IEntityState)new ReturnToOriginState(defendOrigin) : new IdleState());
        }

        // ── 틱 (구 Monster.Update — 이동 틱은 시스템이 이어서 돌린다) ────

        public void Tick(float dt)
        {
            if (!Owner.IsAlive) return;

            // 비선공 보스는 복귀 중일 때만 이동하고, 그 외에는 제자리에서 대기한다.
            if (isBoss && !hasBeenAttacked)
            {
                // 복귀 중에는 어그로가 걸리지 않으므로 길이 막혀 집에 닿지 못하면 그대로 굳어 버린다.
                // 제한 시간을 넘기면 복귀만 접고 그 자리에 비선공으로 선다 — 체력은 채우지 않는다. 그건 도착의 몫이다.
                if (isReturningToNest && ReturnTimedOut) IsReturning = false;

                if (isReturningToNest)
                {
                    TickReturnRegen(dt);
                    currentState?.Update(this, dt);
                }
                else if (!(currentState is IdleState))
                    SetState(new IdleState());
                return;
            }

            // 보스는 거리 한 줄이 아니라 인내심으로 교전을 유지·포기한다. 어그로가 잠깐 끊긴 순간에도 판정이 돌아야 한다 —
            // 안 그러면 각성한 보스가 Idle → FlowFieldState로 새서 밤 웨이브 몬스터처럼 코어를 향해 행군해 버린다.
            if (isBoss && hasBeenAttacked && !TickBossPatience(dt))
            {
                currentState?.Update(this, dt);
                return;
            }

            // 둥지 방어자는 이탈 조건(호위 대상 이탈 또는 추적 범위 초과)을 만나면 교전을 포기한다.
            if (isNestDefender && aggroOnPlayer && ShouldAbandonNestFight())
            {
                BeginReturnToNest();
                currentState?.Update(this, dt);
                return;
            }

            // 둥지 소속 몬스터가 경로 탐색 중 방해 건물(타워)을 임시 타깃으로 바꾸더라도 즉시 플레이어 타깃으로 복원한다.
            EnforceNestPlayerOnlyTarget();
            currentState?.Update(this, dt);
            EnforceNestPlayerOnlyTarget();

            // 추적 사슬(Chase/Attack)이 끝나 기본 상태로 돌아왔으면 어그로 해제 — 플레이어 사망 등으로 OnLost가 오지 않아도 자연 복구된다.
            // 단 각성한 보스는 제외 — 보스의 교전 종료는 인내심 전담이다.
            if (aggroOnPlayer && !(isBoss && hasBeenAttacked) && (currentState is IdleState || currentState is FlowFieldState))
                aggroOnPlayer = false;

            // 방어자 모드에서 플레이어가 죽으면 둥지로 복귀한다.
            if (isNestDefender && currentTarget != null && !currentTarget.IsAlive)
                BeginReturnToNest();
        }

        /// <summary>
        /// 롤 정글몹의 인내심에 해당하는 판정. 둥지 밖에 나가 있는 시간과 서로 타격이 오가지 않는 시간(카이팅)이 인내심을 깎고,
        /// 정상 교전 중에는 다시 차오른다. 다 닳으면 복귀한다. 교전을 계속하면 true, 복귀로 전환했으면 false.
        /// </summary>
        bool TickBossPatience(float dt)
        {
            float fromHome = Vector3.Distance(Owner.Position, defendOrigin);

            // 절대 상한 — 맵 밖까지 끌려나가는 것만 막는 안전장치다.
            if (!IsValidTarget(currentTarget) || fromHome > EffectiveLeashRange * Mathf.Max(1f, spec.AbsoluteLeashMultiplier))
            {
                BeginReturnToNest();
                return false;
            }

            // 인내심이 차오르는 조건은 오직 하나 — 자기 자리 근처에서 붙어 싸우는 것이다.
            float radius = EffectivePatienceRadius;
            bool bossInside = fromHome <= radius;
            bool targetInside = Vector3.Distance(currentTarget.Position, defendOrigin) <= radius;

            if (bossInside && targetInside)
                patience = Mathf.Min(spec.MaxPatience, patience + dt * spec.PatienceRecoverRate);
            else
                patience -= dt * (bossInside ? spec.RangedPokePatienceDrain : spec.OutsidePatienceDrain);

            if (patience <= 0f)
            {
                patience = 0f;
                BeginReturnToNest();
                return false;
            }
            return true;
        }

        /// <summary>복귀 중 고속 체력 재생 — 즉시 풀피를 대신한다. 도착 시 남은 분은 OnReturnedHome이 채운다.</summary>
        void TickReturnRegen(float dt)
        {
            var hp = Owner.Health;
            if (hp == null || spec.ReturnRegenPerSecond <= 0f || hp.CurrentHealth >= hp.MaxHealth) return;
            hp.Heal(hp.MaxHealth * spec.ReturnRegenPerSecond * dt);
        }

        bool ShouldAbandonNestFight()
        {
            if (!IsValidTarget(currentTarget)) return true;

            // 보스전 지원군은 보스의 교전에 종속된다 — 보스가 아직 싸우는 한 거리를 이유로 포기하지 않는다.
            if (escortBoss != null)
            {
                var escortBrain = escortBoss.Get<MonsterBrain>();
                return !escortBoss.IsAlive || escortBrain == null || !escortBrain.HasBeenAttacked;
            }

            if (Vector3.Distance(Owner.Position, defendOrigin) > defendRadius) return true;
            return zone.HasValue && !CanChase(defendOrigin, currentTarget.Position);
        }

        void BeginReturnToNest()
        {
            aggroOnPlayer = false;
            currentTarget = null;
            escortBoss = null;

            if (isBoss)
            {
                // 보스전 포기: 비선공 상태로 되돌린다. 체력은 복귀하는 동안 고속으로 재생한다 — 도망가는 보스를 마저 잡을 여지를 남긴다.
                hasBeenAttacked = false;
                IsReturning = true;
                returnStartTime = Now;
                patience = spec.MaxPatience;
            }

            SetState(new ReturnToOriginState(defendOrigin, despawnOnArrival: !isBoss));
        }

        /// <summary>둥지에 도착했다 — 남은 체력을 마저 채우고 복귀 상태를 끝낸다.</summary>
        internal void OnReturnedHome()
        {
            IsReturning = false;
            patience = spec.MaxPatience;
            if (Owner.IsAlive) Owner.Health?.ResetToFull();
        }

        void EnforceNestPlayerOnlyTarget()
        {
            if ((!isBoss && !isNestDefender) || !aggroOnPlayer || !IsValidTarget(currentTarget)) return;

            bool targetsPlayer =
                currentState is ChaseState chase && ReferenceEquals(chase.Target, currentTarget) ||
                currentState is AttackState attack && ReferenceEquals(attack.Target, currentTarget);

            if (!targetsPlayer) SetState(new ChaseState(currentTarget));
        }

        void OnDied(Entity _)
        {
            aggroOnPlayer = false;
            escortBoss = null;
            SetState(new DeadState());
        }

        // ── 상태기 ─────────────────────────────────────────────────

        public void SetState(IEntityState next)
        {
            currentState?.Exit(this);
            currentState = next;
            currentState?.Enter(this);
        }

        public static bool IsValidTarget(Entity e) => e != null && e.IsAlive;

        /// <summary>
        /// 표적까지의 거리 — 부피가 있는 대상(건물)은 중심점 대신 풋프린트 경계까지(구 EntityViewExtensions.DistanceTo),
        /// 이동체는 중심 거리에서 반지름(군중 반지름, 없으면 0.5m — 플레이어 캡슐)을 뺀다. 지상전이라 높이는 무시한다.
        /// </summary>
        public static float DistanceTo(Entity target, Vector3 from)
        {
            if (target == null) return float.MaxValue;

            var footprint = target.Get<IFootprint>();
            if (footprint != null)
            {
                footprint.WorldRect(from.y, out var min, out var max);
                return GridGeometry.DistanceToRect(from, min, max);
            }

            Vector3 d = target.Position - from;
            d.y = 0f;
            float radius = target.Get<Movement>()?.CrowdRadius ?? 0.5f;
            return Mathf.Max(0f, d.magnitude - radius);
        }
    }

    // ── 상태들 (구 Runtime/Entity/State의 심 판) ───────────────────

    /// <summary>대기 — 플로우필드가 준비되면 기본 이동(FlowFieldState)으로 전환한다. 플레이어 감지는 플레이어 센서 콜백이 담당한다.</summary>
    public sealed class IdleState : IEntityState
    {
        public void Enter(MonsterBrain b) => b.Movement?.StopMoving();

        public void Update(MonsterBrain b, float dt)
        {
            if (b.Nav != null && b.Nav.HasFlowField) b.SetState(new FlowFieldState());
        }

        public void Exit(MonsterBrain b) { }
    }

    /// <summary>
    /// 기본 길찾기 — 플로우필드(벡터 필드)를 따라 목표(코어/타워)로 전진한다. 런타임 A*보다 훨씬 가볍다.
    /// 진격 경로 위의 건물이 사거리에 들어오면 공격으로 전환한다 — 사거리 안이라고 아무 건물이나 치지 않는다.
    /// </summary>
    public sealed class FlowFieldState : IEntityState
    {
        public void Enter(MonsterBrain b) { }

        public void Update(MonsterBrain b, float dt)
        {
            var nav = b.Nav;
            if (nav == null || !nav.HasFlowField)
            {
                b.SetState(new IdleState());
                return;
            }

            if (b.Attack != null)
            {
                var building = nav.FindBreachTarget(b.Owner.Position, b.Attack.Range);
                if (MonsterBrain.IsValidTarget(building))
                {
                    b.SetState(new AttackState(building));
                    return;
                }
            }

            // 방향은 매 틱 갱신 — 필드가 재계산돼도 자연스럽게 새 방향을 따른다
            b.Movement?.SetDirection(nav.FlowDirectionAt(b.Owner.Position));
        }

        public void Exit(MonsterBrain b) => b.Movement?.StopMoving();
    }

    /// <summary>런타임 A* 추적 — 플레이어 센서에 감지된 몬스터만 쓰는 무거운 길찾기. 추적 포기는 OnLost가 담당한다.</summary>
    public sealed class ChaseState : IEntityState
    {
        readonly Entity target;
        const float PathUpdateInterval = 0.5f;
        float lastPathUpdateTime;
        MonsterBrain owner;

        public Entity Target => target;

        public ChaseState(Entity target) => this.target = target;

        public void Enter(MonsterBrain b)
        {
            owner = b;
            if (b.Movement != null) b.Movement.OnPathBlocked += HandlePathBlocked;
            UpdatePath(b);
        }

        public void Update(MonsterBrain b, float dt)
        {
            if (!MonsterBrain.IsValidTarget(target))
            {
                b.SetState(new IdleState());
                return;
            }

            float distance = MonsterBrain.DistanceTo(target, b.Owner.Position);
            if (b.Attack != null && distance <= b.Attack.Range)
            {
                b.SetState(new AttackState(target));
                return;
            }

            if (b.Now >= lastPathUpdateTime + PathUpdateInterval) UpdatePath(b);
        }

        public void Exit(MonsterBrain b)
        {
            if (b.Movement != null)
            {
                b.Movement.OnPathBlocked -= HandlePathBlocked;
                b.Movement.StopMoving();
            }
        }

        void HandlePathBlocked()
        {
            if (owner != null) UpdatePath(owner);
        }

        void UpdatePath(MonsterBrain b)
        {
            if (!MonsterBrain.IsValidTarget(target) || b.Nav == null) return;

            lastPathUpdateTime = b.Now;

            // 계산은 워커에서 돈다 — 답은 다음 프레임 이후에 온다. 그 사이 상태가 갈렸으면 남의 경로를 들이밀지 않는다.
            b.Nav.FindPath(b.Owner.Position, target.Position, false, path =>
            {
                if (!IsStillCurrent(b)) return;

                if (path != null)
                {
                    if (path.Count > 0) b.Movement?.StartMoving(path);
                    else b.Movement?.StopMoving();   // 시작 셀과 목표 셀이 같으면 빈 경로 — 이미 도착. 사거리 진입은 Update의 거리 판정에
                    return;
                }

                HandleFullyBlocked(b);
            });
        }

        /// <summary>콜백이 도착했을 때 아직 이 상태가 돌고 있는가 — 늦게 온 답을 거르는 유일한 관문.</summary>
        bool IsStillCurrent(MonsterBrain b)
            => owner == b && b != null && b.Owner.IsAlive && ReferenceEquals(b.CurrentState, this) && MonsterBrain.IsValidTarget(target);

        /// <summary>길이 완전히 막혔다 → 경로를 막는 건물을 새 타겟으로 삼아 부순다.</summary>
        void HandleFullyBlocked(MonsterBrain b)
        {
            b.Nav.FindBlockingBuilding(b.Owner.Position, target.Position, blocker =>
            {
                if (!IsStillCurrent(b)) return;

                if (MonsterBrain.IsValidTarget(blocker) && !ReferenceEquals(blocker, target))
                {
                    b.SetState(new ChaseState(blocker));
                    return;
                }

                // 부술 건물조차 없으면(지형 막힘 등) Chase에 머물며 주기로 재시도 — Idle로 보내면 상태 진동이 생긴다
                b.Movement?.StopMoving();
            });
        }
    }

    public sealed class AttackState : IEntityState
    {
        // 추적 복귀 히스테리시스 — 진입(사거리)과 이탈 기준이 같으면 사거리 경계에서 Attack↔Chase가 매 틱 진동한다
        const float ExitRangeBuffer = 1.15f;

        readonly Entity target;
        public Entity Target => target;

        public AttackState(Entity target) => this.target = target;

        public void Enter(MonsterBrain b)
        {
            b.Movement?.StopMoving();
            TryAttackTarget(b);
        }

        public void Update(MonsterBrain b, float dt)
        {
            if (!MonsterBrain.IsValidTarget(target))
            {
                b.SetState(new IdleState());
                return;
            }

            float distance = MonsterBrain.DistanceTo(target, b.Owner.Position);
            if (b.Attack == null || distance > b.Attack.Range * ExitRangeBuffer)
            {
                b.SetState(new ChaseState(target));   // 같은 타겟을 유지한 채 재추적
                return;
            }

            // 사거리 안에 있는 동안은 쿨다운이 돌 때마다 계속 공격
            if (b.Attack.CanAttack(b.Now)) TryAttackTarget(b);
        }

        public void Exit(MonsterBrain b) { }

        void TryAttackTarget(MonsterBrain b)
        {
            if (!MonsterBrain.IsValidTarget(target) || b.Attack == null) return;

            // 수평으로만 타겟을 바라본다
            b.Movement?.FaceImmediately(target.Position - b.Owner.Position);
            b.Attack.TryAttack(target, b.Now);
        }
    }

    public sealed class DeadState : IEntityState
    {
        public void Enter(MonsterBrain b) => b.Movement?.StopMoving();
        public void Update(MonsterBrain b, float dt) { }
        public void Exit(MonsterBrain b) { }
    }

    /// <summary>방어자 전용 대기 상태 (FlowFieldState로 넘어가지 않음).</summary>
    public sealed class DefenderIdleState : IEntityState
    {
        public void Enter(MonsterBrain b) => b.Movement?.StopMoving();
        public void Update(MonsterBrain b, float dt) { }
        public void Exit(MonsterBrain b) { }
    }

    /// <summary>원위치 복귀. 일회성 방어 몬스터는 도착 후 소멸하고, 보스는 비선공 대기로 남는다.</summary>
    public sealed class ReturnToOriginState : IEntityState
    {
        readonly Vector3 origin;
        readonly bool despawnOnArrival;

        public ReturnToOriginState(Vector3 origin, bool despawnOnArrival = true)
        {
            this.origin = origin;
            this.despawnOnArrival = despawnOnArrival;
        }

        public void Enter(MonsterBrain b)
        {
            // 경로가 올 때까지는 둥지 쪽으로 곧장 걷는다 — 워커의 답을 기다리며 멈춰 서 있으면 복귀가 한 박자 늦어 보인다.
            b.Movement?.SetDirection((origin - b.Owner.Position).normalized);

            b.Nav?.FindPath(b.Owner.Position, origin, false, path =>
            {
                if (b == null || !b.Owner.IsAlive || !ReferenceEquals(b.CurrentState, this)) return;
                if (path != null && path.Count > 0) b.Movement?.StartMoving(path);
            });
        }

        public void Update(MonsterBrain b, float dt)
        {
            Vector3 pos = b.Owner.Position;
            float dist = Vector3.Distance(pos, origin);

            // 경로가 없거나 끝났는데 아직 집이 아니면, 남은 거리는 격자 판정을 거치지 않고 곧장 좁힌다.
            // 격자 경로의 마지막 노드는 칸 중심이라 자리와 몇 m 어긋나고, 자리가 걸을 수 없는 칸 위면 플로우필드 이동이
            // 진입을 거부한다 — 그대로 두면 도착 판정(1.5m)에 영영 못 닿아 무적 샌드백이 된다. 복귀만큼은 반드시 끝나야 한다.
            if (dist >= 1.5f && (b.Movement == null || !b.Movement.HasPath))
            {
                b.Movement?.StopMoving();

                float speed = b.Movement != null ? b.Movement.MoveSpeed : 3f;
                Vector3 target = new Vector3(origin.x, pos.y, origin.z);
                b.Owner.Position = Vector3.MoveTowards(pos, target, speed * dt);
                b.Movement?.FaceImmediately(target - pos);

                dist = Vector3.Distance(b.Owner.Position, origin);
            }

            if (dist < 1.5f)
            {
                b.Movement?.StopMoving();
                if (despawnOnArrival)
                {
                    b.System.Despawn(b.Owner);
                }
                else
                {
                    // 도착 — 복귀 재생의 마지막 한 뼘을 채운다(롤 캠프 리셋과 같은 마무리).
                    b.OnReturnedHome();
                    b.SetState(new DefenderIdleState());
                }
            }
        }

        public void Exit(MonsterBrain b) => b.Movement?.StopMoving();
    }
}
