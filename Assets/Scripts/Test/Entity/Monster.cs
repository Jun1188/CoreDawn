using UnityEngine;

// 몬스터 — 밤에 스폰되어 플로우필드를 따라 코어/타워로 전진·공격한다.
// 플레이어 센서에 감지되면(OnDetectedByPlayer) 런타임 A* 추적으로 전환하고,
// 범위를 벗어나면(OnLostByPlayer) 다시 플로우필드로 복귀한다.
// 이동/전투 컴포넌트는 순수 C#이라 별도 AddComponent 없이 이 스크립트 하나면 된다.
public class Monster : Entity
{
    [SerializeField] private MovementComponent movement = new MovementComponent();
    [SerializeField] private CombatComponent combat = new CombatComponent();

    [Header("Boss Leash / Patience")]
    [Tooltip("보스가 교전을 버티는 최대 인내심(초). 둥지 밖으로 나가 있으면 닳는다.")]
    [SerializeField] private float maxPatience = 3f;
    [Tooltip("이 반경(둥지 중심 기준) 밖으로 나가면 인내심이 닳는다. 0이면 NestEngagementZone.ChaseRange(없으면 25m). " +
             "주의: 스폰 포인트가 둥지 중심에서 최대 18m까지 떨어져 있으므로 이 값을 그보다 작게 두면 " +
             "보스가 제 자리에 서 있는 동안에도 인내심이 닳아 교전 자체가 불가능해진다.")]
    [SerializeField] private float patienceRadius = 0f;
    [Tooltip("보스가 둥지 밖으로 끌려나가 있을 때 인내심이 닳는 속도(초당).")]
    [SerializeField] private float outsidePatienceDrain = 2f;
    [Tooltip("보스는 둥지 안인데 표적이 밖에서 찔러댈 때(원거리 카이팅) 닳는 속도(초당).")]
    [SerializeField] private float rangedPokePatienceDrain = 3f;
    [Tooltip("둥지 안에서 표적과 제대로 붙어 싸울 때 인내심이 차오르는 속도(초당).")]
    [SerializeField] private float patienceRecoverRate = 2f;
    [Tooltip("강제 귀환 거리 = 리쉬 거리 × 이 배수. 둥지 중심에서 이만큼 벗어나면 인내심이 남아 있어도 " +
             "즉시 복귀한다. 보스가 둥지 밖으로 멀리 나도는 걸 막는 가장 단단한 제동이다.")]
    [SerializeField] private float absoluteLeashMultiplier = 1f;
    [Tooltip("둥지로 복귀하는 동안의 체력 재생 — 최대 체력 대비 초당 비율(0.12 = 12%/s).")]
    [SerializeField] private float returnRegenPerSecond = 0.12f;

    private StateMachineComponent stateMachine;
    private MonsterVisualController visual; // 연출 담당 — 없을 수 있다(자리표시 프리팹)
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

    // 인내심 런타임 상태 — 마지막으로 타격을 주고받은 시각과 남은 인내심(초).
    private float patience;

    // 이 몬스터가 호위하는 보스(둥지가 보스전 지원군으로 뱉은 개체만 값이 있다).
    // 값이 있으면 거리가 아니라 보스의 교전 지속 여부로 이탈을 판정한다.
    private Monster escortBoss;

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
        visual = GetComponent<MonsterVisualController>();
        Health.OnDeath += HandleMonsterDeath;

        patience = maxPatience;

        // 어그로를 OnHealthChanged("HP가 max 미만")에 걸면 안 된다 — 복귀 중 체력 재생이
        // 매 틱 그 조건을 만족시켜 보스가 무한히 다시 깨어난다. 피해라는 사건 자체를
        // 받는 ReceiveDamage 오버라이드가 유일한 각성 경로다.
    }

    public void SetAsBoss()
        => SetAsBoss(transform.position, null);

    public void SetAsBoss(Vector3 ownerNestOrigin, NestEngagementZone zone)
    {
        EnsureInitialized();

        isBoss = true;
        hasBeenAttacked = false;
        isReturningToNest = false;
        defendOrigin = transform.position;
        nestOrigin = ownerNestOrigin;
        engagementZone = zone;
        defendRadius = zone != null ? zone.LeashRange : defendRadius;
        patience = maxPatience;
    }

    /// <summary>보스인가 — 세이브가 읽는다.</summary>
    public bool IsBoss => isBoss;

    /// <summary>보스가 공격을 받아 깨어났는가 — 저장하지 않으면 로드 후 다시 잠들어 버린다.</summary>
    public bool HasBeenAttacked => hasBeenAttacked;

    public bool IsNestDefender => isNestDefender;
    public Vector3 DefendOrigin => defendOrigin;

    /// <summary>남은 인내심 비율(0~1) — 체력바 아래 인내심 바가 읽는다.</summary>
    public float PatienceRatio => maxPatience > 0f ? Mathf.Clamp01(patience / maxPatience) : 1f;

    /// <summary>인내심이 닳기 시작하는 둥지 반경. 0이면 교전 구역의 추적 반경을 쓴다.</summary>
    private float EffectivePatienceRadius =>
        patienceRadius > 0f ? patienceRadius
        : (engagementZone != null ? engagementZone.ChaseRange : 25f);

    /// <summary>절대 상한 계산의 기준이 되는 리쉬 거리.</summary>
    private float EffectiveLeashRange =>
        engagementZone != null ? engagementZone.LeashRange : defendRadius;

    /// <summary>
    /// 세이브 복원 전용 — 정체성(보스/방어자)과 각성 여부를 되돌린다.
    ///
    /// AI 상태는 되돌리지 않고 Idle에서 다시 시작한다. 추적 대상은 씬 오브젝트 참조라
    /// 저장할 수 없고, 이동 경로도 지형이 같으면 곧바로 다시 계산된다 —
    /// 플레이어 센서가 다음 스캔에서 어그로를 다시 걸어 주므로 몇 프레임이면 제자리를 찾는다.
    /// </summary>
    public void RestoreSaveState(bool boss, bool awakened, bool nestDefender, Vector3 origin)
    {
        EnsureInitialized();

        isBoss = boss;
        hasBeenAttacked = awakened;
        isNestDefender = nestDefender;
        defendOrigin = origin;

        aggroOnPlayer = false;
        currentTarget = null;
        escortBoss = null;
        patience = maxPatience;

        // 즉시 풀피가 사라졌으므로, 각성하지 않았는데 체력이 깎여 있는 보스는
        // 저장 시점에 둥지로 복귀하던 중이었다는 뜻이다. 그대로 Idle로 두면
        // 반피인 채 영영 그 자리에 서 있게 되므로 복귀 상태를 이어 붙인다.
        isReturningToNest = boss && !awakened && Health.CurrentHealth < Health.MaxHealth;

        StateMachine.SetState(isReturningToNest
            ? (IEntityState)new ReturnToOriginState(defendOrigin, despawnOnArrival: false)
            : new IdleState());
    }

    protected override void Awake()
    {
        base.Awake();
        EnsureInitialized();
    }

    // 군중 시스템(겹침 해소) 등록부 — BuildingEntity.All과 같은 패턴
    private void OnEnable() => CrowdSystem.Register(this);
    private void OnDisable() => CrowdSystem.Unregister(this);

    /// <summary>
    /// 받는 피해의 수렴점을 가로채 각성 판정을 건다. OnHealthChanged가 아니라 여기인 이유는
    /// 그쪽이 "HP가 max 미만"이라는 프록시라서, 복귀 중 체력 재생이 매 틱 재각성을
    /// 일으키기 때문이다. 피해라는 사건 자체는 이 경로로만 들어온다
    /// (효과 시스템·DoT·총알 모두 ReceiveDamage로 수렴한다).
    /// </summary>
    public override void ReceiveDamage(float amount)
    {
        base.ReceiveDamage(amount);

        if (amount <= 0f || IsDead) return;

        if (isBoss) Provoke(null);
    }

    /// <summary>
    /// 보스를 각성시킨다 — 피해를 입었을 때와, 명백한 적의(빗맞은 사격)를 감지했을 때
    /// 모두 이 경로로 들어온다. attacker가 null이면 씬의 플레이어를 찾는다.
    /// </summary>
    public void Provoke(Player attacker)
    {
        if (!isBoss || IsDead) return;

        EnsureInitialized();

        // 인내심은 <b>교전이 새로 시작될 때만</b> 채운다. 매 피격마다 채우면
        // 멀리서 계속 쏘는 것만으로 인내심이 영원히 만땅이 되어 리쉬가 없는 것과 같아진다.
        bool newEngagement = !hasBeenAttacked;

        if (newEngagement)
        {
            hasBeenAttacked = true;
            patience = maxPatience;
            Debug.Log("[Monster] 보스 몬스터가 공격을 받아 깨어났습니다!");
        }

        isReturningToNest = false;

        Player player = attacker.IsValidTarget() ? attacker : FindFirstObjectByType<Player>();
        if (!player.IsValidTarget()) return;

        aggroOnPlayer = true;
        currentTarget = player;

        // 이미 같은 대상을 쫓거나 때리는 중이면 상태를 갈아엎지 않는다 —
        // 맞을 때마다 ChaseState를 새로 깔면 경로가 초기화되고 공격이 끊긴다.
        bool alreadyEngaged =
            StateMachine.CurrentState is ChaseState chase && ReferenceEquals(chase.Target, player) ||
            StateMachine.CurrentState is AttackState attack && ReferenceEquals(attack.Target, player);

        if (alreadyEngaged) return;

        visual?.PlayAlert();
        StateMachine.SetState(new ChaseState(player));
    }

    protected override void Start()
    {
        base.Start();
        if (StateMachine.CurrentState == null)
            StateMachine.SetState(new IdleState());

        // 머리 위 HP 바 — 보스는 크게. SetAsBoss가 Instantiate 직후(Start 이전)에 불리므로
        // 여기서 isBoss는 이미 확정돼 있다.
        var bar = WorldHealthBar.Attach(this, large: isBoss);

        // 보스만 체력바 아래에 인내심 바를 단다 — 만땅일 때는 알아서 숨는다.
        if (isBoss && bar != null)
            bar.EnableSecondaryBar(() => PatienceRatio, new Color(1f, 0.78f, 0.2f, 0.95f));
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
                TickReturnRegen();
                StateMachine.Tick();
                movement.Tick(Time.deltaTime);
            }
            else if (!(StateMachine.CurrentState is IdleState))
                StateMachine.SetState(new IdleState());
            return;
        }

        // 보스는 거리 한 줄이 아니라 인내심으로 교전을 유지·포기한다.
        // 어그로가 잠깐 끊긴 순간에도(아래 어그로 해제 블록) 판정이 돌아야 한다 —
        // 안 그러면 각성한 보스가 Idle → FlowFieldState로 새서 밤 웨이브 몬스터처럼
        // 플레이어 코어를 향해 행군해 버린다.
        if (isBoss && hasBeenAttacked && !TickBossPatience())
        {
            StateMachine.Tick();
            movement.Tick(Time.deltaTime);
            return;
        }

        // 둥지 방어자는 이탈 조건(호위 대상 이탈 또는 추적 범위 초과)을 만나면 교전을 포기한다.
        if (isNestDefender && aggroOnPlayer && ShouldAbandonNestFight())
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
        // 플레이어 사망 등으로 OnLostByPlayer가 오지 않아도 자연 복구된다.
        // 단 각성한 보스는 제외한다 — 여기서 풀어 주면 EnforceNestPlayerOnlyTarget이
        // 재추적을 못 걸고 보스가 플로우필드로 새 나간다. 보스의 교전 종료는 인내심 전담이다.
        if (aggroOnPlayer && !(isBoss && hasBeenAttacked) &&
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

    /// <summary>
    /// 롤 정글몹의 인내심에 해당하는 판정. 둥지 밖에 나가 있는 시간과 서로 타격이
    /// 오가지 않는 시간(카이팅 당하는 중)이 인내심을 깎고, 정상 교전 중에는 다시 차오른다.
    /// 다 닳으면 복귀한다. 교전을 계속하면 true, 복귀로 전환했으면 false를 반환한다.
    /// </summary>
    private bool TickBossPatience()
    {
        float fromNest = Vector3.Distance(transform.position, nestOrigin);

        // 절대 상한 — 맵 밖까지 끌려나가는 것만 막는 안전장치다. 평상시 포기 판정은
        // 인내심이 전담하므로 여기 걸리는 일은 거의 없어야 한다.
        if (!currentTarget.IsValidTarget() ||
            fromNest > EffectiveLeashRange * Mathf.Max(1f, absoluteLeashMultiplier))
        {
            BeginReturnToNest();
            return false;
        }

        // 인내심이 차오르는 조건은 오직 하나 — <b>둥지 안에서 붙어 싸우는 것</b>이다.
        // 피격을 회복 신호로 쓰면 안 된다: 멀리서 계속 쏘기만 해도 인내심이 리셋돼
        // 보스가 영영 제자리에서 두들겨 맞는, 리쉬가 없는 것과 같은 상태가 된다.
        float radius = EffectivePatienceRadius;
        bool bossInside = fromNest <= radius;
        bool targetInside = Vector3.Distance(currentTarget.transform.position, nestOrigin) <= radius;

        if (bossInside && targetInside)
            patience = Mathf.Min(maxPatience, patience + Time.deltaTime * patienceRecoverRate);
        else
            patience -= Time.deltaTime * (bossInside ? rangedPokePatienceDrain : outsidePatienceDrain);

        if (patience <= 0f)
        {
            patience = 0f;
            BeginReturnToNest();
            return false;
        }
        return true;
    }

    /// <summary>복귀 중 고속 체력 재생 — 즉시 풀피를 대신한다. 도착 시 남은 분은 OnReturnedHome이 채운다.</summary>
    private void TickReturnRegen()
    {
        if (returnRegenPerSecond <= 0f || Health.CurrentHealth >= Health.MaxHealth) return;
        Health.Heal(Health.MaxHealth * returnRegenPerSecond * Time.deltaTime);
    }

    private bool ShouldAbandonNestFight()
    {
        if (!currentTarget.IsValidTarget()) return true;

        // 보스전 지원군은 보스의 교전에 종속된다 — 보스가 아직 싸우는 한
        // 플레이어가 아무리 멀리 물러나도 거리를 이유로 포기하지 않는다.
        if (escortBoss != null)
            return escortBoss.IsDead || !escortBoss.HasBeenAttacked;

        if (Vector3.Distance(transform.position, nestOrigin) > defendRadius) return true;
        return engagementZone != null &&
               !engagementZone.CanChase(nestOrigin, currentTarget.transform.position);
    }

    private void BeginReturnToNest()
    {
        aggroOnPlayer = false;
        currentTarget = null;
        escortBoss = null;

        if (isBoss)
        {
            // 보스전 포기: 비선공 상태로 되돌린다. 체력은 여기서 즉시 채우지 않고
            // 복귀하는 동안 고속으로 재생한다 — 도망가는 보스를 마저 잡을 여지를 남긴다.
            hasBeenAttacked = false;
            isReturningToNest = true;
            patience = maxPatience;
        }

        StateMachine.SetState(new ReturnToOriginState(defendOrigin, despawnOnArrival: !isBoss));
    }

    /// <summary>둥지에 도착했다 — 남은 체력을 마저 채우고 복귀 상태를 끝낸다.</summary>
    internal void OnReturnedHome()
    {
        isReturningToNest = false;
        patience = maxPatience;
        if (!IsDead) Health.Initialize();
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
        escortBoss = null;
        StateMachine.SetState(new DeadState());
    }

    // ── Player 센서 콜백 ──
    // 몬스터가 각자 플레이어를 스캔하는 대신, 플레이어가 자기 센서 범위의
    // 몬스터를 찾아 아래 두 메서드를 호출해준다 (개체 수만큼의 OverlapSphere 제거)

    public void OnDetectedByPlayer(Player player)
    {
        if (IsDead || aggroOnPlayer || !player.IsValidTarget()) return;
        if (isBoss && !hasBeenAttacked) return;
        if (isNestDefender && escortBoss == null && engagementZone != null &&
            !engagementZone.CanChase(nestOrigin, player.transform.position)) return;
        aggroOnPlayer = true;
        currentTarget = player;
        visual?.PlayAlert(); // 발견 모션 — 연출만, 추적 시작을 지연시키지 않는다
        StateMachine.SetState(new ChaseState(player));
    }

    public void OnLostByPlayer()
    {
        if (!aggroOnPlayer) return;
        // 깨어난 보스는 플레이어 센서 반경이 아니라 인내심으로 교전을 유지한다.
        if (isBoss && hasBeenAttacked) return;
        // 보스전 지원군도 마찬가지 — 이탈은 보스의 교전 지속 여부가 정한다.
        if (escortBoss != null && !escortBoss.IsDead && escortBoss.HasBeenAttacked) return;
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
        => SetAsNestDefender(target, origin, zone, null);

    /// <summary>
    /// 둥지 방어자로 배선한다. escort가 주어지면(보스전 지원군) 교전 구역의 거리 규칙을
    /// 건너뛰고 무조건 교전에 붙는다 — 지원군이 태어나자마자 "플레이어가 멀다"는 이유로
    /// 되돌아가면 스폰 자체가 낭비다. 이탈 판정도 거리가 아니라 보스가 대신한다.
    /// </summary>
    public void SetAsNestDefender(Player target, Vector3 origin, NestEngagementZone zone, Monster escort)
    {
        isNestDefender = true;
        defendOrigin = transform.position;
        nestOrigin = origin;
        engagementZone = zone;
        defendRadius = zone != null ? zone.LeashRange : defendRadius;
        currentTarget = target;
        escortBoss = escort;

        aggroOnPlayer = escort != null || zone == null || zone.CanChase(nestOrigin, target.transform.position);
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
            // 경로가 올 때까지는 둥지 쪽으로 곧장 걷는다 — 워커의 답을 기다리며 멈춰 서 있으면
            // 복귀가 한 박자 늦어 보인다. 답이 오면 그 경로로 갈아탄다.
            sm.Movement?.SetDirection((origin - sm.Transform.position).normalized);

            PathRequest.Find(sm.Transform.position, origin, false, path =>
            {
                if (sm == null || !ReferenceEquals(sm.CurrentState, this)) return;
                if (path != null && path.Count > 0) sm.Movement?.StartMoving(path);
            });
        }

        public void Update(StateMachineComponent sm)
        {
            float dist = Vector3.Distance(sm.Transform.position, origin);
            if (dist < 1.5f)
            {
                sm.Movement?.StopMoving();
                if (despawnOnArrival)
                {
                    Destroy(sm.Owner.gameObject);
                }
                else
                {
                    // 도착 — 복귀 재생의 마지막 한 뼘을 채운다(롤 캠프 리셋과 같은 마무리).
                    (sm.Owner as Monster)?.OnReturnedHome();
                    sm.SetState(new DefenderIdleState());
                }
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
