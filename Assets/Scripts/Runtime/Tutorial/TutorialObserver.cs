using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 세계를 관측만 하는 눈 — "플레이어가 무엇을 얼마나 했는가"의 원본 수치.
///
/// <b>스텝을 모른다.</b> 어떤 수치가 어떤 안내를 끝내는지는 조건 모듈(<see cref="TutorialConditionSO"/>)이,
/// 기준점("뜬 뒤로 n번 더")은 <see cref="TutorialProgress"/>가 안다. 여기는 세기만 한다 —
/// 그래서 새 조건을 더할 때 이 파일은 새 신호가 필요할 때만 손댄다.
///
/// 설계 원칙(구 TutorialConditions에서 계승): <b>게임플레이 코드를 한 줄도 고치지 않는다.</b>
/// 전부 기존 public 이벤트 구독이거나 기존 public 상태 폴링이다.
///
/// 세이브 복원이 이벤트를 재발화시키는 함정(GameManager.RestoreTier → TierUnlocked,
/// ItemContainer.RestoreSlotsRaw → Changed)은 <b>애초에 이벤트를 안 쓰는 쪽</b>으로 피했다 —
/// 코어 티어·일차·인벤토리는 전부 폴링한다. 폴링은 복원 후의 값을 그대로 읽으므로
/// 복원 중 오발화라는 개념 자체가 없다.
/// </summary>
public sealed class TutorialObserver
{
    // ── 누적 카운터 (단조 증가 — Progress가 기준점을 빼서 "뜬 뒤로 n번 더"를 만든다) ──
    public int MinedTotal { get; private set; }
    public int PlacedCount { get; private set; }
    public int PlacedBelts { get; private set; }
    public int DemolishedCount { get; private set; }
    public int BeltShapeCycles { get; private set; }
    public int HotbarSwitches { get; private set; }
    public int JumpCount { get; private set; }
    public int SlideCount { get; private set; }

    /// <summary>달리기 누적 초 — 모션 상태의 IsSprinting을 매 프레임 적산한다.</summary>
    public float SprintSeconds { get; private set; }

    /// <summary>손 제작으로 만들어 낸 개수 — 분류별. 없는 키는 0.</summary>
    readonly Dictionary<ItemType, int> _crafted = new();
    public int CraftedOfType(ItemType t) => _crafted.TryGetValue(t, out int n) ? n : 0;

    // ── 상태 플래그 / 스냅샷 ──
    public float MoveSeconds { get; private set; }
    public float LookSeconds { get; private set; }
    public bool InventoryOpened { get; private set; }
    public bool BuildModeEntered { get; private set; }
    public bool WeaponEquipped { get; private set; }
    public int CoreTier { get; private set; }
    public int NightsStarted { get; private set; }
    public int NightsSurvived { get; private set; }

    // ── 캐시 ──
    PlayerController _player;
    InventoryPanelView _inventoryView;
    BuildMenuView _buildMenuView;
    PlacementSystem _placement;
    FactorySim _hookedSim;
    PlayerMotionState _hookedMotion;
    TutorialInputProbe _probe;

    int _lastBuildingCount = -1;   // -1 = 아직 기준을 못 잡음 (다음 폴링이 기준만 잡고 넘어간다)
    int _lastBeltCount;
    int _lastHotbarIndex = -1;     // -1 = 아직 기준을 못 잡음

    // ─────────────────────────── 배선 ───────────────────────────

    public void AttachProbe(TutorialInputProbe probe)
    {
        if (_probe != null) _probe.Performed -= OnPerformed;
        _probe = probe;
        if (_probe != null) _probe.Performed += OnPerformed;

        InventoryPanelView.HandCrafted -= OnHandCrafted;
        InventoryPanelView.HandCrafted += OnHandCrafted;
    }

    public void Detach()
    {
        if (_probe != null) _probe.Performed -= OnPerformed;
        _probe = null;
        InventoryPanelView.HandCrafted -= OnHandCrafted;
        UnhookSim();
        UnhookMotion();
    }

    // ── 모션 이벤트 — 점프·슬라이드는 이산 사건이라 폴링으로 엣지를 추측하지 않는다(PlayerMotionState 규칙) ──

    void HookMotion(PlayerMotionState motion)
    {
        if (motion == _hookedMotion) return;
        UnhookMotion();
        _hookedMotion = motion;
        if (_hookedMotion == null) return;
        _hookedMotion.Jumped += OnJumped;
        _hookedMotion.SlideStarted += OnSlideStarted;
    }

    void UnhookMotion()
    {
        if (_hookedMotion != null)
        {
            _hookedMotion.Jumped -= OnJumped;
            _hookedMotion.SlideStarted -= OnSlideStarted;
        }
        _hookedMotion = null;
    }

    void OnJumped(float launchSpeed) => JumpCount++;
    void OnSlideStarted(float entrySpeed) => SlideCount++;

    void OnPerformed(InputActionId id)
    {
        // 폴링으로도 잡히지만, 눌린 그 프레임에 반응해야 안내가 굼떠 보이지 않는다
        if (id == InputActionId.ToggleBuild) BuildModeEntered = true;
        if (id == InputActionId.ToggleInventory) InventoryOpened = true;

        // T는 벨트를 든 배치 모드에서만 실제로 모양을 바꾼다(PlacementSystem.CycleBeltShape).
        // 조건을 똑같이 걸어야 "아무 데서나 T를 눌렀더니 통과됐다"가 안 생긴다.
        if (id == InputActionId.CycleShape
            && _placement != null
            && _placement.Mode == PlacementSystem.BuildMode.Placing
            && _placement.CurrentBuilding is BeltDataSO)
            BeltShapeCycles++;
    }

    /// <summary>손 제작 1회. 레시피가 여러 개를 내면 산출 개수만큼 센다.</summary>
    void OnHandCrafted(RecipeDataSO r)
    {
        if (r == null || r.outputs == null) return;

        foreach (var o in r.outputs)
        {
            if (o.item == null || o.amount <= 0) continue;
            _crafted.TryGetValue(o.item.type, out int had);
            _crafted[o.item.type] = had + o.amount;
        }
    }

    void UnhookSim()
    {
        if (_hookedSim != null) _hookedSim.Removed -= OnBuildingRemoved;
        _hookedSim = null;
    }

    /// <summary>
    /// 철거는 폴링으로 셀 수 없다 — 건물 수는 밤에 괴수가 부숴도 줄어든다.
    /// 그래서 유일하게 이벤트를 쓰되, <b>철거 모드였을 때만</b> 센다. 이러면 전투 파괴와 갈린다.
    /// </summary>
    void OnBuildingRemoved(Building b)
    {
        if (_placement == null) return;
        if (_placement.Mode != PlacementSystem.BuildMode.Demolishing) return;
        DemolishedCount++;
    }

    // ─────────────────── 매 프레임 (가벼운 것만) ───────────────────

    /// <summary>이동·시점은 프레임 단위 값이라 0.2초 폴링으로는 대부분을 놓친다. 여기만 매 프레임 본다.</summary>
    public void UpdateFast(float dt)
    {
        if (_player == null) return;

        var m = _player.Motion;
        if (m == null) return;

        if (m.MoveInput.sqrMagnitude > 0.01f) MoveSeconds += dt;
        if (m.LookDelta.sqrMagnitude > 0.0001f) LookSeconds += dt;
        if (m.IsSprinting) SprintSeconds += dt;
    }

    // ───────────────────── 주기 폴링 (0.2초) ─────────────────────

    public void Tick()
    {
        if (_player == null || !_player.isActiveAndEnabled)
        {
            _player = Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
            // 플레이어가 갈렸거나 잠시 꺼졌다 켜졌다 — 그쪽 ClearSubscribers가 우리 구독을 이미
            // 날렸을 수 있어, 같은 객체라도 다시 건다 (Jumped/SlideStarted는 폴링으로 못 세는 엣지다)
            UnhookMotion();
        }
        HookMotion(_player != null ? _player.Motion : null);

        if (_placement == null)
            _placement = Object.FindFirstObjectByType<PlacementSystem>(FindObjectsInactive.Include);

        if (_inventoryView == null)
            _inventoryView = Object.FindFirstObjectByType<InventoryPanelView>(FindObjectsInactive.Include);

        if (_buildMenuView == null)
            _buildMenuView = Object.FindFirstObjectByType<BuildMenuView>(FindObjectsInactive.Include);

        // 씬이 바뀌면 심도 새로 생긴다 — 같은 심이 아니면 다시 건다
        var sim = FactoryBootstrap.Instance != null ? FactoryBootstrap.Instance.Sim : null;
        if (sim != _hookedSim)
        {
            UnhookSim();
            _hookedSim = sim;
            if (_hookedSim != null) _hookedSim.Removed += OnBuildingRemoved;
            _lastBuildingCount = -1;   // 새 씬의 건물 수를 증설로 오해하지 않게 기준을 다시 잡는다
        }

        if (_inventoryView != null && _inventoryView.isActiveAndEnabled) InventoryOpened = true;

        // B는 배치 모드가 아니라 <b>건설 메뉴</b>를 연다(BuildController) — 거기서 건물을 고르면
        // 그때서야 Mode가 Placing이 된다. 둘 다 "건설을 시작했다"로 친다.
        if (_buildMenuView != null && _buildMenuView.isActiveAndEnabled) BuildModeEntered = true;
        if (_placement != null && _placement.Mode != PlacementSystem.BuildMode.None) BuildModeEntered = true;

        WeaponEquipped = _player != null && _player.weaponManager != null
                         && _player.weaponManager.CurrentWeapon != null;

        CoreTier = GameManager.Instance != null ? GameManager.Instance.UnlockedTier : 0;

        PollDayNight();
        PollBuildings();
        PollMining();
        PollHotbar();
    }

    /// <summary>
    /// 핫바 <b>선택 칸이 바뀌었는가</b>. 장착 여부가 아니라 이걸 보는 이유:
    /// 제작·습득한 물건은 빈 핫바 칸부터 채우므로, 무기를 만들자마자 아무것도 누르지 않았는데
    /// 손에 들려 버린다(HotbarController가 활성 칸이 바뀌면 스스로 장착을 맞춘다).
    /// 그러면 "숫자키로 골라 보세요" 안내가 뜨자마자 통과돼 사라진다.
    ///
    /// 같은 칸을 다시 눌러도(=아무 일도 안 일어나도) 세지 않는다 — 화면에 변화가 없었으니 맞다.
    /// </summary>
    void PollHotbar()
    {
        var hb = HotbarController.Instance;
        if (hb == null) return;

        int idx = hb.CurrentHotbarIndex;
        if (_lastHotbarIndex < 0) { _lastHotbarIndex = idx; return; }   // 기준만 잡고 넘어간다
        if (idx != _lastHotbarIndex) { HotbarSwitches++; _lastHotbarIndex = idx; }
    }

    /// <summary>
    /// 밤은 이벤트가 있지만 폴링으로 읽는다 — DayCycle.RestoreState는 <b>의도적으로 이벤트를
    /// 발화하지 않으므로</b>, 세이브를 불러오면 "몇 번째 밤인가"를 이벤트로는 영영 알 수 없다.
    /// N일차의 밤이 N번째 밤이고, N+1일차 아침을 맞았다면 N번째 밤을 넘긴 것이다.
    /// </summary>
    void PollDayNight()
    {
        var tm = TimeManager.Instance;
        if (tm == null || tm.Cycle == null) return;

        int day = tm.DayNumber;
        bool night = tm.Phase == DayPhase.Night;

        NightsStarted = Mathf.Max(NightsStarted, night ? day : day - 1);
        NightsSurvived = Mathf.Max(NightsSurvived, day - 1);
    }

    void PollBuildings()
    {
        var boot = FactoryBootstrap.Instance;
        if (boot == null) { _lastBuildingCount = -1; return; }

        int n = 0, belts = 0;
        foreach (var b in boot.Buildings)
        {
            if (b == null) continue;
            n++;
            if (b.Data is BeltDataSO) belts++;
        }

        // 기준만 잡고 넘어간다
        if (_lastBuildingCount < 0) { _lastBuildingCount = n; _lastBeltCount = belts; return; }

        // 증가분만 더한다 — 괴수가 부숴 줄어든 것을 나중에 "새로 지었다"로 오해하지 않기 위해서다
        if (n > _lastBuildingCount) PlacedCount += n - _lastBuildingCount;
        if (belts > _lastBeltCount) PlacedBelts += belts - _lastBeltCount;

        _lastBuildingCount = n;
        _lastBeltCount = belts;
    }

    /// <summary>
    /// 광맥의 누적 산출량 총합. 손 채굴과 채굴기가 같은 관문(ResourceNode.TryExtract)을 지나므로
    /// 둘이 섞인다 — 채굴 안내는 채굴기를 짓기 한참 전에 나오므로 실질적인 문제는 없다.
    /// </summary>
    void PollMining()
    {
        int total = 0;
        var nodes = ResourceNodeRegistry.Nodes;
        for (int i = 0; i < nodes.Count; i++)
            if (nodes[i] != null) total += nodes[i].TotalExtracted;

        if (total > MinedTotal) MinedTotal = total;
    }

    // ──────────────────── 인벤토리 조회 ────────────────────

    public static int CountOfItem(ItemDataSO item)
    {
        var h = PlayerInventoryHolder.Instance;
        if (h == null) return 0;

        int n = 0;
        if (h.MainContainer != null) n += h.MainContainer.CountOf(item);
        if (h.HotbarContainer != null) n += h.HotbarContainer.CountOf(item);
        return n;
    }

    public static int CountOfType(ItemType type)
    {
        var h = PlayerInventoryHolder.Instance;
        if (h == null) return 0;
        return CountOfTypeIn(h.MainContainer, type) + CountOfTypeIn(h.HotbarContainer, type);
    }

    /// <summary>Snapshot()은 호출마다 리스트를 만든다 — 0.2초마다 도는 경로라 칸을 직접 훑는다.</summary>
    static int CountOfTypeIn(ItemContainer c, ItemType type)
    {
        if (c == null) return 0;

        int n = 0;
        for (int i = 0; i < c.SlotCount; i++)
        {
            var s = c.PeekAt(i);
            if (s == null || s.item == null || s.amount <= 0) continue;
            if (s.item.type == type) n += s.amount;
        }
        return n;
    }
}
