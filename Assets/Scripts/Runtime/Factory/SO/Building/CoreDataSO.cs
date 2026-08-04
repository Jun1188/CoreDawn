using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 팩토리 코어 — Satisfactory의 우주 엘리베이터/마일스톤에 해당.
/// 티어별로 정해진 아이템을 정해진 개수만큼 모으면 다음 티어로 진화하며,
/// 그 결과(GameManager.UnlockedTier 증가)로 상위 레시피/건물이 해금된다.
///
/// 디펜스 코어(BuildingEntity.isCore)와 같은 오브젝트에 합체돼 배치된다 —
/// 낮에는 이 SO의 행동(CoreBehavior)으로 자원을 납품받고, 밤에는 몬스터의 공격 대상이 된다.
/// 씬에 미리 배치된 싱글턴이므로 빌드 메뉴에는 노출하지 않는다(hideFromBuildMenu = true 권장).
/// </summary>
[CreateAssetMenu(fileName = "NewCore", menuName = "Factory/Buildings/Core")]
public class CoreDataSO : BuildingDataSO
{
    [Header("티어 — tiers[N] = Tier N → N+1 진화에 필요한 요구량")]
    public CoreTierDefinition[] tiers;

    public override IBuildingBehavior CreateBehavior(Building building) => new CoreBehavior(building, this);

    protected override void OnValidate()
    {
        base.OnValidate();
        if (tiers == null) return;
        foreach (var t in tiers)
        {
            int need = t?.requirements?.Length ?? 0;
            if (need > inputSlots)
                Debug.LogError($"[Core] '{name}' 요구 아이템 종류({need})가 입력 슬롯({inputSlots})보다 많음 — " +
                                "슬롯을 늘리거나 티어 요구량을 줄일 것.", this);
        }
    }
}

[System.Serializable]
public class CoreTierRequirement
{
    public ItemDataSO item;
    public int amount;
}

[System.Serializable]
public class CoreTierDefinition
{
    [Tooltip("단계 이름 — \"선체 봉합\" 등. GameData.json 의 tiers[].name.")]
    public string tierLabel;

    [Tooltip("이 단계가 무엇을 하는 일인지 한 줄. 코어 패널이 이름 아래 붙인다.")]
    [TextArea] public string description;

    public CoreTierRequirement[] requirements;

    [Tooltip("이 단계를 마치면 열리는 것들 — 표시 전용 문자열. 실제 해금은 requiredCoreTier가 한다.")]
    public string[] unlocks;

    [Tooltip("완료 시 코어 최대 체력 증가분.")]
    public int maxHpBonus;

    [Tooltip("마지막 단계 — 완료하면 탈출(엔딩)로 이어진다.")]
    public bool isFinal;
}

// ─── 행동 ──────────────────────────────────────────────────────

/// <summary>
/// 입력 버퍼(=플레이어 납품 + 벨트 자동 투입 공용 창구)에 현재 티어가 요구하는 아이템만 받는다.
/// 요구량이 전부 채워지면 소비하고 GameManager.AdvanceTier로 다음 티어를 해금한다.
///
/// 투입 경로는 둘인데 반응 경로는 하나로 통일한다:
///   - 벨트가 TryAdd로 채우면 Building.TryPushOutput이 이미 Sim.MarkDirty를 호출 → 다음 틱에 Tick.
///   - 플레이어가 인벤토리 UI로 직접 넣으면(TryPutAt/TryExchangeAt) 벨트 경로를 거치지 않으므로,
///     ItemContainer.Changed 이벤트를 구독해 그 자리에서 Sim.MarkDirty를 걸어 같은 결과를 만든다.
/// </summary>
public class CoreBehavior : IBuildingBehavior, IInteractiveBehavior
{
    readonly Building _b;
    readonly CoreDataSO _so;

    public CoreBehavior(Building b, CoreDataSO so)
    {
        _b = b;
        _so = so;
        _b.Input.SingleStackPerType = true; // 한 아이템이 슬롯 전부를 독점 못하게 (Assembler와 동일 이유)
        _b.Input.Changed += () => _b.Sim.MarkDirty(_b); // 수동 투입도 Tick 재평가 트리거
        RefreshAcceptFilter();
    }

    /// <summary>플레이어 상호작용/UI가 바인딩할 컨테이너 — 벨트도 여기로 들어온다.</summary>
    public ItemContainer Container => _b.Input;

    /// <summary>수리 단계 목록을 읽기 위한 UI용 접근자.</summary>
    public CoreDataSO Data => _so;

    /// <summary>현재 진행 중인 단계 인덱스. tiers.Length와 같으면 전 단계 완료.</summary>
    public int CurrentTierIndex => TierIndex;

    /// <summary>전체 수리 단계 수.</summary>
    public int TierCount => _so.tiers != null ? _so.tiers.Length : 0;

    int TierIndex => GameManager.Instance != null ? GameManager.Instance.UnlockedTier : 0;

    public bool HasNextTier => _so.tiers != null && TierIndex < _so.tiers.Length;

    CoreTierDefinition CurrentTier => HasNextTier ? _so.tiers[TierIndex] : null;

    public string InteractPrompt => HasNextTier ? "코어에 자원 납품" : "코어 (최고 티어 달성)";

    public void Interact(PlayerController player)
    {
        // 씬에 UITK 코어 패널이 있으면 그쪽을 연다. 없으면 기존 uGUI 화면으로 —
        // 씬 내용만으로 전환이 결정되므로 이관 중에도 두 경로가 공존한다.
        if (CorePanelView.TryOpen(this)) return;
        InventoryManager.Instance?.OpenCoreScreen(this);
    }

    /// <summary>현재 티어 요구 아이템별 (아이템, 필요량, 현재량) — 진행률 UI용.</summary>
    public IReadOnlyList<(ItemDataSO item, int required, int current)> GetProgress()
    {
        var list = new List<(ItemDataSO, int, int)>();
        var reqs = CurrentTier?.requirements;
        if (reqs == null) return list;
        foreach (var r in reqs) list.Add((r.item, r.amount, _b.Input.CountOf(r.item)));
        return list;
    }

    public void OnAfterPlaced() => RefreshAcceptFilter(); // 배치 시점에 이미 진행된 티어 반영(재시작 등)

    // ── 수리 확인 (SCR-01b)
    //
    // 마지막 부품을 넣자마자 수리가 시작되면 플레이어가 준비할 틈이 없다. 특히 마지막 단계는
    // 곧바로 최종 방어전이라, 확인창을 거쳐야 한다. 그래서 요구 충족과 실제 진행을 갈라 둔다.

    bool _ready;

    /// <summary>요구가 전부 채워졌는가 — UI가 "납품" 버튼을 "수리 시작"으로 바꾸는 신호.</summary>
    public bool IsReadyToRepair => _ready;

    /// <summary>준비 상태가 바뀔 때만 발화. UI가 매 프레임 폴링하지 않게 한다.</summary>
    public event System.Action ReadyChanged;

    public void Tick(float dt)
    {
        var reqs = CurrentTier?.requirements;
        if (reqs == null) { SetReady(false); return; }

        foreach (var r in reqs)
            if (_b.Input.CountOf(r.item) < r.amount) { SetReady(false); return; } // 아직 미충족

        SetReady(true);

        // 확인창을 띄울 UI가 없는 씬에서는 예전처럼 즉시 진행한다.
        // 그러지 않으면 UITK 패널이 아직 안 들어간 씬에서 코어 진행이 영영 멈춘다 —
        // Interact가 이미 쓰고 있는 "씬 내용이 경로를 결정한다" 방침과 같다.
        if (!CorePanelView.ExistsInScene()) TryStartRepair();
    }

    /// <summary>
    /// 확인창의 "수리 시작"이 호출. 요구를 소비하고 다음 단계를 연다.
    /// 호출 시점에 요구가 다시 미달일 수 있으므로(벨트가 도로 빼갔다든지) 여기서 한 번 더 검사한다.
    /// </summary>
    public bool TryStartRepair()
    {
        var reqs = CurrentTier?.requirements;
        if (reqs == null) return false;

        foreach (var r in reqs)
            if (_b.Input.CountOf(r.item) < r.amount) { SetReady(false); return false; }

        foreach (var r in reqs) _b.Input.TryConsume(r.item, r.amount);

        GameManager.Instance.AdvanceTier(TierIndex + 1);
        SetReady(false);
        RefreshAcceptFilter();
        _b.NotifyUpstream(); // 자리 비었으니 막혀있던 상류(벨트) 재개
        return true;
    }

    void SetReady(bool on)
    {
        if (_ready == on) return;
        _ready = on;
        ReadyChanged?.Invoke();
    }

    void RefreshAcceptFilter()
    {
        var reqs = CurrentTier?.requirements;
        _b.Input.AcceptFilter = item => reqs != null && System.Array.Exists(reqs, r => r.item == item);
    }
}
