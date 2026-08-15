using UnityEngine;

// 몬스터 — 밤에 스폰되어 플로우필드를 따라 코어/타워로 전진·공격한다.
// 플레이어 센서에 감지되면(OnDetectedByPlayer) 런타임 A* 추적으로 전환하고,
// 범위를 벗어나면(OnLostByPlayer) 다시 플로우필드로 복귀한다.
// 이동/전투 컴포넌트는 순수 C#이라 별도 AddComponent 없이 이 스크립트 하나면 된다.
public class Monster : Entity
{
    [SerializeField] private MovementComponent movement = new MovementComponent();
    [SerializeField] private CombatComponent combat = new CombatComponent();

    private StateMachineComponent stateMachine;
    private bool aggroOnPlayer;
    private bool isNestDefender;
    private Vector3 defendOrigin;
    private float defendRadius = 30f;
    private Player currentTarget;
    private Vector3 nestOrigin;
    private NestEngagementZone engagementZone;

    // 보스 전용 변수
    private bool isBoss = false;
    private bool hasBeenAttacked = false;
    private bool isReturningToNest;

    public override MovementComponent Movement => movement;
    public override CombatComponent Combat => combat;
    public StateMachineComponent StateMachine
    {
        get
        {
            EnsureInitialized();
            return stateMachine;
        }
    }

    private void EnsureInitialized()
    {
        if (stateMachine != null) return;

        movement.Initialize(transform);
        combat.Initialize(this); // 효과 시스템 — 공격의 출처(Source)·버프 베이크 주입
        stateMachine = new StateMachineComponent(this);
        Health.OnDeath += HandleMonsterDeath;

        Health.OnHealthChanged += (current, max) =>
        {
            if (current < max)
            {
                HandleDamageTaken();
            }
        };
    }

    public void SetAsBoss()
        => SetAsBoss(transform.position, null);

    public void SetAsBoss(Vector3 ownerNestOrigin, NestEngagementZone zone)
    {
        isBoss = true;
        hasBeenAttacked = false;
        isReturningToNest = false;
        defendOrigin = transform.position;
        nestOrigin = ownerNestOrigin;
        engagementZone = zone;
        defendRadius = zone != null ? zone.LeashRange : defendRadius;
    }

    protected override void Awake()
    {
        base.Awake();
        EnsureInitialized();
    }

    // 군중 시스템(겹침 해소) 등록부 — BuildingEntity.All과 같은 패턴
    private void OnEnable() => CrowdSystem.Register(this);
    private void OnDisable() => CrowdSystem.Unregister(this);

    private void HandleDamageTaken()
    {
        if (!isBoss) return;

        if (!hasBeenAttacked)
        {
            hasBeenAttacked = true;
            Debug.Log($"[Monster] 보스 몬스터가 공격을 받아 깨어났습니다!");
        }

        isReturningToNest = false;
        var player = FindFirstObjectByType<Player>();
        if (player.IsValidTarget() &&
            (engagementZone == null || engagementZone.CanChase(nestOrigin, player.transform.position)))
        {
            aggroOnPlayer = true;
            currentTarget = player;
            StateMachine.SetState(new ChaseState(player));
        }
        else
        {
            BeginReturnToNest();
        }
    }

    protected override void Start()
    {
        base.Start();
        if (StateMachine.CurrentState == null)
            StateMachine.SetState(new IdleState());
    }

    protected override void Update()
    {
        base.Update();

        if (IsDead) return;

        // 비선공 보스는 복귀 중일 때만 이동하고, 그 외에는 제자리에서 대기한다.
        if (isBoss && !hasBeenAttacked)
        {
            if (isReturningToNest)
            {
                StateMachine.Tick();
                movement.Tick(Time.deltaTime);
            }
            else if (!(StateMachine.CurrentState is IdleState))
                StateMachine.SetState(new IdleState());
            return;
        }

        // 낮 둥지 소속 몬스터는 NestCore의 추적/이탈 범위를 벗어나면 교전을 포기한다.
        if ((isBoss || isNestDefender) && aggroOnPlayer && ShouldAbandonNestFight())
        {
            BeginReturnToNest();
            StateMachine.Tick();
            movement.Tick(Time.deltaTime);
            return;
        }

        // 둥지 소속 몬스터가 경로 탐색 중 방해 건물(타워)을 임시 타깃으로 바꾸더라도
        // 즉시 플레이어 타깃으로 복원한다. 밤 웨이브 몬스터에는 적용되지 않는다.
        EnforceNestPlayerOnlyTarget();

        StateMachine.Tick();
        EnforceNestPlayerOnlyTarget();
        movement.Tick(Time.deltaTime);

        // 추적 사슬(Chase/Attack)이 끝나 기본 상태로 돌아왔으면 어그로 해제 —
        // 플레이어 사망 등으로 OnLostByPlayer가 오지 않아도 자연 복구된다
        if (aggroOnPlayer &&
            (StateMachine.CurrentState is IdleState || StateMachine.CurrentState is FlowFieldState))
        {
            aggroOnPlayer = false;
        }

        // 방어자 모드에서 플레이어가 죽으면 둥지로 복귀한다.
        if (isNestDefender)
        {
            if (currentTarget != null && currentTarget.IsDead)
            {
                BeginReturnToNest();
            }
        }
    }

    private bool ShouldAbandonNestFight()
    {
        if (!currentTarget.IsValidTarget()) return true;
        if (Vector3.Distance(transform.position, nestOrigin) > defendRadius) return true;
        return engagementZone != null &&
               !engagementZone.CanChase(nestOrigin, currentTarget.transform.position);
    }

    private void BeginReturnToNest()
    {
        aggroOnPlayer = false;
        currentTarget = null;

        if (isBoss)
        {
            // 보스전 포기: 비선공 상태로 되돌리고 체력을 전량 회복한다.
            hasBeenAttacked = false;
            isReturningToNest = true;
            Health.Initialize();
        }

        StateMachine.SetState(new ReturnToOriginState(defendOrigin, despawnOnArrival: !isBoss));
    }

    private void EnforceNestPlayerOnlyTarget()
    {
        if ((!isBoss && !isNestDefender) || !aggroOnPlayer || !currentTarget.IsValidTarget()) return;

        bool targetsPlayer =
            StateMachine.CurrentState is ChaseState chase && ReferenceEquals(chase.Target, currentTarget) ||
            StateMachine.CurrentState is AttackState attack && ReferenceEquals(attack.Target, currentTarget);

        if (!targetsPlayer)
            StateMachine.SetState(new ChaseState(currentTarget));
    }

    private void HandleMonsterDeath()
    {
        aggroOnPlayer = false;
        StateMachine.SetState(new DeadState());
    }

    // ── Player 센서 콜백 ──
    // 몬스터가 각자 플레이어를 스캔하는 대신, 플레이어가 자기 센서 범위의
    // 몬스터를 찾아 아래 두 메서드를 호출해준다 (개체 수만큼의 OverlapSphere 제거)

    public void OnDetectedByPlayer(Player player)
    {
        if (IsDead || aggroOnPlayer || !player.IsValidTarget()) return;
        if (isBoss && !hasBeenAttacked) return;
        if (isNestDefender && engagementZone != null && !engagementZone.CanChase(nestOrigin, player.transform.position)) return;
        aggroOnPlayer = true;
        currentTarget = player;
        StateMachine.SetState(new ChaseState(player));
    }

    public void OnLostByPlayer()
    {
        if (!aggroOnPlayer) return;
        // 깨어난 보스는 플레이어 센서 반경이 아니라 둥지의 chase/leash 범위로 교전을 유지한다.
        if (isBoss && hasBeenAttacked) return;
        aggroOnPlayer = false;
        currentTarget = null;
        if (IsDead) return;

        // 방어자는 플로우필드(건물 공격)로 복귀하지 않고 둥지 쪽으로 돌아간다.
        if (isNestDefender)
        {
            StateMachine.SetState(new ReturnToOriginState(defendOrigin));
        }
        else
        {
            // 추적/전투를 끊고 기본 이동(Idle → 플로우필드)으로 복귀
            StateMachine.SetState(new IdleState());
        }
    }

    public void SetAsNestDefender(Player target)
    {
        isNestDefender = true;
        defendOrigin = transform.position;
        nestOrigin = defendOrigin;
        currentTarget = target;
        aggroOnPlayer = true;
        StateMachine.SetState(new ChaseState(target));
    }

    public void SetAsNestDefender(Player target, Vector3 origin, NestEngagementZone zone)
    {
        isNestDefender = true;
        defendOrigin = transform.position;
        nestOrigin = origin;
        engagementZone = zone;
        defendRadius = zone != null ? zone.LeashRange : defendRadius;
        currentTarget = target;
        aggroOnPlayer = zone == null || zone.CanChase(nestOrigin, target.transform.position);
        StateMachine.SetState(aggroOnPlayer
            ? (IEntityState)new ChaseState(target)
            : new ReturnToOriginState(defendOrigin));
    }

    // 방어자 전용 대기 상태 (FlowFieldState로 넘어가지 않음)
    private class DefenderIdleState : IEntityState
    {
        public void Enter(StateMachineComponent sm) => sm.Movement?.StopMoving();
        public void Update(StateMachineComponent sm) { }
        public void Exit(StateMachineComponent sm) { }
    }

    // 원위치 복귀. 일회성 방어 몬스터는 도착 후 정리하고, 보스는 비선공 대기로 남는다.
    private class ReturnToOriginState : IEntityState
    {
        private Vector3 origin;
        private bool despawnOnArrival;

        public ReturnToOriginState(Vector3 origin, bool despawnOnArrival = true)
        {
            this.origin = origin;
            this.despawnOnArrival = despawnOnArrival;
        }

        public void Enter(StateMachineComponent sm)
        {
            System.Collections.Generic.List<Node> path = PathFinder.FindPath(sm.Transform.position, origin);
            if (path != null && path.Count > 0)
            {
                sm.Movement?.StartMoving(path);
            }
            else
            {
                sm.Movement?.SetDirection((origin - sm.Transform.position).normalized);
            }
        }

        public void Update(StateMachineComponent sm)
        {
            float dist = Vector3.Distance(sm.Transform.position, origin);
            if (dist < 1.5f)
            {
                sm.Movement?.StopMoving();
                if (despawnOnArrival) Destroy(sm.Owner.gameObject);
                else sm.SetState(new DefenderIdleState());
            }
        }

        public void Exit(StateMachineComponent sm)
        {
            sm.Movement?.StopMoving();
        }
    }

    // ── 총기 시스템 통합 ──
    // 총알 피격 판정은 Bullet(스윕)이 직접 수행한다 — 명중 시 효과 목록 적용(Entity.ApplyEffects).
    // 몬스터 쪽 충돌 코드는 필요 없다.
}
