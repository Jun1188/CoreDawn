using UnityEngine;

// 전투 총괄 매니저 — 전투에 필요한 구성요소를 한곳에서 멤버로 통합한다.
//   Grid    : 길찾기 그리드 (GridManager, 씬 컴포넌트 참조)
//   FlowField : 플로우필드 구동 (FlowFieldManager, 씬 컴포넌트 참조)
//   Spawner : 몬스터 군집 생명주기 (WaveSpawnManager, 순수 C# — 여기서 소유/Tick 구동)
// 낮/밤 전환(TimeManager)에 맞춰 스폰을 켜고 끄며, 아침에는 군집을 일괄 소멸시킨다.
public class BattleManager : MonoBehaviour
{
    public static BattleManager Instance { get; private set; }

    [Header("Battle Members")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private FlowFieldManager flowFieldManager;
    [SerializeField] private WaveSpawnManager spawnManager = new WaveSpawnManager();

    [Tooltip("런타임 부착되는 Player 엔티티의 최대 체력. 0 이하면 HealthComponent 기본값(100)을 쓴다.")]
    [SerializeField] private float playerMaxHealth = 300f;

    [Tooltip("런타임 부착 Player의 몬스터 감지 범위. 기본값(10)이면 밤에 몬스터 전원이 플레이어에게 몰리므로 좁힌다. 0 이하면 기본값 유지.")]
    [SerializeField] private float playerDetectionRange = 5f;

    [Tooltip("런타임 부착 플레이어의 근접 자동 반격 피해 (공격 정의를 인스펙터로 못 만지므로 여기서).")]
    [SerializeField] private float playerMeleeDamage = 10f;

    private Player playerEntity; // 아침 부활 처리용 캐시

    public GridManager Grid => gridManager;
    public FlowFieldManager FlowField => flowFieldManager;
    public WaveSpawnManager Spawner => spawnManager;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 인스펙터에서 비워두면 씬에서 자동 해결
        if (gridManager == null)
            gridManager = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        if (flowFieldManager == null)
            flowFieldManager = FlowFieldManager.Instance != null ? FlowFieldManager.Instance : FindFirstObjectByType<FlowFieldManager>();
    }

    // 코어 파괴로 게임이 끝났는지 여부. UI/연출은 GameOver 이벤트를 구독하면 된다.
    public bool IsGameOver { get; private set; }
    public event System.Action GameOver;

    private void Start()
    {
        EnsurePlayerEntity();
        BuildingEntity.CoreDestroyed += OnCoreDestroyed;

        spawnManager.Initialize(gridManager, transform);

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.Cycle.NightStarted += OnNightStarted;
            TimeManager.Instance.Cycle.DayStarted += OnDayStarted;
            spawnManager.SetSpawningEnabled(TimeManager.Instance.Phase == DayPhase.Night);
        }
        else
        {
            // 주야 매니저가 없는 테스트 씬에서는 항상 스폰
            spawnManager.SetSpawningEnabled(true);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        BuildingEntity.CoreDestroyed -= OnCoreDestroyed;
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.Cycle.NightStarted -= OnNightStarted;
            TimeManager.Instance.Cycle.DayStarted -= OnDayStarted;
        }
    }

    // ── 외부 시스템 통합 ──
    // MainScene 등 기존 씬의 플레이어(PlayerController)에는 Player 엔티티가 없다.
    // 씬 에셋을 수정하지 않고 런타임에 Player 컴포넌트를 자동 부착해 몬스터 감지/피격을 연결한다.
    private void EnsurePlayerEntity()
    {
        var controller = FindFirstObjectByType<PlayerController>();
        if (controller == null) return;
        if (controller.GetComponent<Player>() != null) return;

        var player = controller.gameObject.AddComponent<Player>();
        // FPS 플레이어는 카메라/UI가 하위에 있어 Destroy 대신 비활성화로 사망 처리
        player.SetDeathBehavior(destroy: false, delay: 2f);
        // 런타임 부착이라 인스펙터로 HP/감지 범위를 못 만지므로 여기서 설정
        if (playerMaxHealth > 0f) player.Health.SetMaxHealth(playerMaxHealth);
        if (playerDetectionRange > 0f && player.Sensor != null)
            player.Sensor.SetDetectionRange(playerDetectionRange);

        // 근접 자동 반격의 공격 정의 — 런타임 부착이라 인스펙터 배선이 불가능해 여기서 만든다.
        // 공용 피해 효과는 Resources에 있다 (RecipeDatabase와 같은 패턴).
        var damageEffect = Resources.Load<DamageEffectSO>("Effect_Damage");
        if (damageEffect != null && player.Combat != null)
            player.Combat.SetAttackEffects(new[] { new EffectEntry(damageEffect, playerMeleeDamage) });
        else if (damageEffect == null)
            Debug.LogWarning("[BattleManager] Resources/Effect_Damage를 찾지 못해 플레이어 근접 공격이 무효과입니다.");
        playerEntity = player;
        Debug.Log("[BattleManager] PlayerController에 Player 엔티티를 런타임 부착했습니다.");
    }

    private void OnCoreDestroyed(BuildingEntity core)
    {
        if (IsGameOver) return;
        IsGameOver = true;
        spawnManager.SetSpawningEnabled(false);
        Debug.Log("====== 💀 게임오버 — 코어가 파괴되었습니다! ======");
        GameOver?.Invoke();
    }

    private void Update()
    {
        spawnManager.Tick();
    }

    private void OnNightStarted(int day)
    {
        spawnManager.SetSpawningEnabled(true);
    }

    private void OnDayStarted(int day)
    {
        // 아침 — 스폰 중단 + 살아남은 군집 일괄 소멸
        spawnManager.SetSpawningEnabled(false);
        spawnManager.DespawnAll();
        RevivePlayerIfDead();
    }

    // 밤에 전사한 플레이어를 아침에 부활 — FPS 카메라가 플레이어 하위라 죽은 채 두면 시점이 사라진다
    private void RevivePlayerIfDead()
    {
        if (playerEntity == null || !playerEntity.IsDead) return;
        playerEntity.gameObject.SetActive(true);
        playerEntity.Health.Initialize(); // IsDead 해제 + HP 전량 회복
        Debug.Log("[BattleManager] 아침 — 플레이어 부활 (HP 전량 회복)");
    }
}
