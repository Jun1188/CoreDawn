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

    // 보스 전용 변수
    private bool isBoss = false;
    private bool hasBeenAttacked = false;

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
    {
        isBoss = true;
        hasBeenAttacked = false;
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
        if (isBoss && !hasBeenAttacked)
        {
            hasBeenAttacked = true;
            Debug.Log($"[Monster] 보스 몬스터가 공격을 받아 깨어났습니다!");
        }
    }

    protected override void Start()
    {
        base.Start();
        StateMachine.SetState(new IdleState());
    }

    protected override void Update()
    {
        base.Update();

        if (IsDead) return;

        // 보스몹이고 아직 선제 공격을 받지 않았다면 아무것도 하지 않음 (제자리 대기)
        if (isBoss && !hasBeenAttacked)
        {
            if (!(StateMachine.CurrentState is IdleState))
                StateMachine.SetState(new IdleState());
            return;
        }

        StateMachine.Tick();
        movement.Tick(Time.deltaTime);

        // 추적 사슬(Chase/Attack)이 끝나 기본 상태로 돌아왔으면 어그로 해제 —
        // 플레이어 사망 등으로 OnLostByPlayer가 오지 않아도 자연 복구된다
        if (aggroOnPlayer &&
            (StateMachine.CurrentState is IdleState || StateMachine.CurrentState is FlowFieldState))
        {
            aggroOnPlayer = false;
        }

        // 방어자 모드일 경우 플레이어가 죽으면 동반 자살, 또는 거리가 너무 멀어지면 복귀
        if (isNestDefender)
        {
            if (currentTarget != null && currentTarget.IsDead)
            {
                Health.Kill();
                currentTarget = null;
            }
            else if (currentTarget != null && aggroOnPlayer)
            {
                float distFromOrigin = Vector3.Distance(transform.position, defendOrigin);
                if (distFromOrigin > defendRadius)
                {
                    aggroOnPlayer = false;
                    currentTarget = null;
                    StateMachine.SetState(new ReturnToOriginState(defendOrigin));
                }
            }
        }
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
        aggroOnPlayer = true;
        currentTarget = player;
        StateMachine.SetState(new ChaseState(player));
    }

    public void OnLostByPlayer()
    {
        if (!aggroOnPlayer) return;
        aggroOnPlayer = false;
        currentTarget = null;
        if (IsDead) return;

        // 방어자는 제자리 대기 대신 원위치로 돌아간 뒤 사라진다
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
        currentTarget = target;
        aggroOnPlayer = true;
        StateMachine.SetState(new ChaseState(target));
    }

    // 방어자 전용 대기 상태 (FlowFieldState로 넘어가지 않음)
    private class DefenderIdleState : IEntityState
    {
        public void Enter(StateMachineComponent sm) => sm.Movement?.StopMoving();
        public void Update(StateMachineComponent sm) { }
        public void Exit(StateMachineComponent sm) { }
    }

    // 원위치 복귀 후 사라지는 상태
    private class ReturnToOriginState : IEntityState
    {
        private Vector3 origin;
        public ReturnToOriginState(Vector3 origin) { this.origin = origin; }

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
                Destroy(sm.Owner.gameObject);
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
