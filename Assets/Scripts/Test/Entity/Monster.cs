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

    public override MovementComponent Movement => movement;
    public override CombatComponent Combat => combat;
    public StateMachineComponent StateMachine => stateMachine;

    protected override void Awake()
    {
        base.Awake();
        movement.Initialize(transform);
        combat.Initialize(this);
        stateMachine = new StateMachineComponent(this);
        Health.OnDeath += HandleMonsterDeath;
    }

    // 군중 시스템(겹침 해소) 등록부 — BuildingEntity.All과 같은 패턴
    private void OnEnable() => CrowdSystem.Register(this);
    private void OnDisable() => CrowdSystem.Unregister(this);

    protected override void Start()
    {
        base.Start();
        stateMachine.SetState(new IdleState());
    }

    protected override void Update()
    {
        base.Update();
        stateMachine.Tick();
        movement.Tick(Time.deltaTime);

        // 추적 사슬(Chase/Attack)이 끝나 기본 상태로 돌아왔으면 어그로 해제 —
        // 플레이어 사망 등으로 OnLostByPlayer가 오지 않아도 자연 복구된다
        if (aggroOnPlayer &&
            (stateMachine.CurrentState is IdleState || stateMachine.CurrentState is FlowFieldState))
        {
            aggroOnPlayer = false;
        }
    }

    private void HandleMonsterDeath()
    {
        aggroOnPlayer = false;
        stateMachine.SetState(new DeadState());
    }

    // ── Player 센서 콜백 ──
    // 몬스터가 각자 플레이어를 스캔하는 대신, 플레이어가 자기 센서 범위의
    // 몬스터를 찾아 아래 두 메서드를 호출해준다 (개체 수만큼의 OverlapSphere 제거)

    public void OnDetectedByPlayer(Player player)
    {
        if (IsDead || aggroOnPlayer || !player.IsValidTarget()) return;
        aggroOnPlayer = true;
        stateMachine.SetState(new ChaseState(player));
    }

    public void OnLostByPlayer()
    {
        if (!aggroOnPlayer) return;
        aggroOnPlayer = false;
        if (IsDead) return;
        // 추적/전투를 끊고 기본 이동(Idle → 플로우필드)으로 복귀
        stateMachine.SetState(new IdleState());
    }

    // ── 총기 시스템 통합 ──
    // 총알 피격 판정은 발사체 쪽이 수행한다 (Bullet 스윕 → ProjectileSystem.ApplyHit → ApplyEffects).
    // 몬스터 쪽 충돌 코드는 필요 없다.
}
