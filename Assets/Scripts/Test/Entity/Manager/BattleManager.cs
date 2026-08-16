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
    [SerializeField] private NightSpawnPointProvider nightSpawnPointProvider;

    [Header("Night Wave Completion")]
    [Tooltip("MainScene opt-in. When enabled, night ends only after the finite WaveDataSO.baseAmount quota is defeated. Legacy scenes keep timed nights by default.")]
    [SerializeField] private bool quantityBasedNightWaves;

    [Tooltip("런타임 부착되는 Player 엔티티의 최대 체력. 0 이하면 HealthComponent 기본값(100)을 쓴다.")]
    [SerializeField] private float playerMaxHealth = 300f;

    [Tooltip("런타임 부착 Player의 몬스터 감지 범위. 기본값(10)이면 밤에 몬스터 전원이 플레이어에게 몰리므로 좁힌다. 0 이하면 기본값 유지.")]
    [SerializeField] private float playerDetectionRange = 5f;

    [Tooltip("런타임 부착 플레이어의 근접 자동 반격 피해 (공격 정의를 인스펙터로 못 만지므로 여기서).")]
    [SerializeField] private float playerMeleeDamage = 10f;

    private Player playerEntity; // 아침 부활 처리용 캐시
    private GameObject playerSceneRoot;

    public GridManager Grid => gridManager;
    public FlowFieldManager FlowField => flowFieldManager;
    public WaveSpawnManager Spawner => spawnManager;
    public bool UsesQuantityBasedNightWaves => quantityBasedNightWaves;
    public event System.Action<int, int> NightWaveCleared;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // BattleManager가 다른 시스템과 같은 루트에 붙어 있을 수 있으므로
            // 중복 컴포넌트만 제거하고 형제 시스템은 보존한다.
            Destroy(this);
            return;
        }

        Instance = this;

        // 밤 스폰 지점은 <b>자기 자식</b>이라 씬 경계를 넘지 않는다 — 여기서 직접 잡는다.
        // 씬을 건너 찾아야 하는 것(그리드·플로우필드)만 GameBootstrap이 주입한다.
        if (nightSpawnPointProvider == null)
            nightSpawnPointProvider = GetComponentInChildren<NightSpawnPointProvider>(true);
    }

    /// <summary>
    /// 길찾기 그리드·플로우필드 주입 — 둘 다 <b>월드(맵)가 소유</b>하므로, 전투를 별도 씬
    /// (Combat 부트스트랩)으로 얹으면 인스펙터 참조가 씬 경계를 넘지 못한다. GameBootstrap이
    /// 월드에서 찾아 꽂아준다. 씬에 직접 둔 경우엔 인스펙터 배선이 이미 있어 덮지 않는다.
    /// 그리드가 없는 씬(아이템 테스트 등)에서는 스폰만 쉬고 나머지 전투는 정상 동작한다.
    /// </summary>
    public void Inject(GridManager grid, FlowFieldManager flowField)
    {
        if (gridManager == null) gridManager = grid;
        if (flowFieldManager == null) flowFieldManager = flowField;
    }

    // 코어 파괴로 게임이 끝났는지 여부. UI/연출은 GameOver 이벤트를 구독하면 된다.
    public bool IsGameOver { get; private set; }
    public event System.Action GameOver;

    private void Start()
    {
        EnsurePlayerEntity();
        BuildingEntity.CoreDestroyed += OnCoreDestroyed;

        spawnManager.SetQuantityBasedMode(quantityBasedNightWaves);
        spawnManager.QuantityWaveCompleted += OnQuantityWaveCompleted;

        if (nightSpawnPointProvider != null)
            spawnManager.Initialize(gridManager, transform, nightSpawnPointProvider.SpawnPoints);
        else
            spawnManager.Initialize(gridManager, transform);

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.SetNightCompletionControlled(quantityBasedNightWaves);
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
        spawnManager.QuantityWaveCompleted -= OnQuantityWaveCompleted;
        if (TimeManager.Instance != null)
        {
            if (quantityBasedNightWaves)
                TimeManager.Instance.SetNightCompletionControlled(false);
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
        playerSceneRoot = controller.transform.root.gameObject;

        var player = controller.GetComponent<Player>();
        bool attachedNow = player == null;
        if (attachedNow) player = controller.gameObject.AddComponent<Player>();

        // 사망 문구는 별도 GameplayHUD UIDocument에 남고, FPS 플레이어/카메라는 기존처럼 비활성화한다.
        player.SetDeathBehavior(destroy: false, delay: 2f);
        // 런타임 부착이라 인스펙터로 HP/감지 범위를 못 만지므로 여기서 설정
        if (playerMaxHealth > 0f) player.Health.SetMaxHealth(playerMaxHealth);
        if (playerDetectionRange > 0f && player.Sensor != null)
            player.Sensor.SetDetectionRange(playerDetectionRange);

        // 근접 자동 반격의 공격 정의 — 런타임 부착이라 인스펙터 배선이 불가능해 여기서 만든다.
        // 효과 에셋은 EffectDatabase(Resources)에서 집는다 — 개별 에셋을 Resources에 두지 않는다.
        var damageEffect = EffectDatabaseSO.LoadDefault()?.FindFirst<DamageEffectSO>();
        if (damageEffect != null && player.Combat != null)
            player.Combat.SetAttackEffects(new[] { new EffectEntry(damageEffect, playerMeleeDamage) });
        else if (damageEffect == null)
            Debug.LogWarning("[BattleManager] EffectDatabase에서 피해 효과를 찾지 못해 플레이어 근접 공격이 무효과입니다.");
        playerEntity = player;
        if (attachedNow)
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
        RestorePlayerSceneRootIfNeeded();
        spawnManager.Tick();
    }

    // FPS 카메라가 PlayerControl 계층 아래 있으므로 부모가 꺼지면 모든 카메라가 함께 사라진다.
    // 플레이어 사망은 Player 자식 자체를 끄는 기존 흐름이 담당하므로 부모 컨테이너는 항상 살아 있어야 한다.
    private void RestorePlayerSceneRootIfNeeded()
    {
        if (IsGameOver || playerSceneRoot == null || playerSceneRoot.activeSelf)
            return;

        playerSceneRoot.SetActive(true);
        Debug.LogWarning("[BattleManager] 비활성화된 플레이어 씬 루트를 복구했습니다. 카메라 렌더링을 계속 유지합니다.", playerSceneRoot);
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

    private void OnQuantityWaveCompleted(int defeatedAmount)
    {
        int day = TimeManager.Instance != null ? TimeManager.Instance.DayNumber : 1;
        NightWaveCleared?.Invoke(day, defeatedAmount);

        if (quantityBasedNightWaves && TimeManager.Instance != null &&
            TimeManager.Instance.Phase == DayPhase.Night)
        {
            TimeManager.Instance.EndNightEarly();
        }
    }

    // 밤에 전사한 플레이어와 카메라 계층을 아침에 다시 활성화하고 부활시킨다.
    private void RevivePlayerIfDead()
    {
        if (playerEntity == null || !playerEntity.IsDead) return;
        if (playerSceneRoot != null && !playerSceneRoot.activeSelf)
            playerSceneRoot.SetActive(true);
        playerEntity.gameObject.SetActive(true);
        playerEntity.Health.Initialize(); // IsDead 해제 + HP 전량 회복
        Debug.Log("[BattleManager] 아침 — 플레이어 부활 (HP 전량 회복)");
    }
}
