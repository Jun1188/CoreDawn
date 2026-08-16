using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>조합기. 입력 버퍼에 재료가 모이면 레시피대로 조합해 출력한다.</summary>
[CreateAssetMenu(fileName = "NewAssembler", menuName = "Factory/Buildings/Assembler")]
public class AssemblerDataSO : BuildingDataSO
{
    [Header("레시피")]
    public RecipeDataSO[] availableRecipes;

    public override IBuildingBehavior CreateBehavior(Building building)
        => new AssemblerBehavior(building, this);

    protected override void OnValidate()
    {
        base.OnValidate();
        // 슬롯이 부족한 레시피는 런타임에 조용히 영구 stall되므로 에디터에서 잡는다.
        // (자동 확장은 하지 않음 — 슬롯 수는 디자이너가 의도한 값이어야 함)
        if (availableRecipes == null) return;
        foreach (var r in availableRecipes)
        {
            if (r == null) continue;
            int needIn  = r.inputs?.Length  ?? 0;
            int needOut = r.outputs?.Length ?? 0;
            if (needIn > inputSlots || needOut > outputSlots)
                Debug.LogError($"[Assembler] '{name}'의 슬롯이 레시피 '{r.name}'에 부족함 — " +
                               $"입력 {inputSlots}/{needIn}칸, 출력 {outputSlots}/{needOut}칸. " +
                               "슬롯을 늘리거나 레시피를 제거할 것.", this);
        }
    }
}

// ─── 행동 ──────────────────────────────────────────────────────

/// <summary>
/// 입력 버퍼에 재료가 모이면 조합 시작 → 완료 후 출력 버퍼로.
/// 조합 완료 시점은 ScheduleWake로 예약한다.
///
/// stall 정책:
///   - 결과물이 출력 버퍼에 들어갈 자리가 없으면 조합을 시작하지 않는다.
///   - 완료 시점에 자리가 없으면 완료를 보류한다 (재료·결과물 유실 없음).
///   - 하류가 아이템을 소비하면 NotifyUpstream으로 깨어나 재개한다.
/// </summary>
public enum MachineState { Running, WaitingInput, OutputBlocked, Stopped }

public class AssemblerBehavior : IBuildingBehavior, IInteractiveBehavior
{
    readonly Building _b;
    readonly AssemblerDataSO _data;
    RecipeDataSO _recipe;           // 다음 조합이 따를 레시피 (UI에서 교체 가능)
    RecipeDataSO _craftingRecipe;   // 진행 중인 1회가 따르는 레시피 — 교체돼도 이 1회는 이것으로 끝난다
    float        _readyAt;          // 조합 완료 예정 시각
    bool         _crafting;
    float        _pausedRemaining;  // 중지 시점의 잔여 시간 — 진행률은 보존된다

    public AssemblerBehavior(Building b, AssemblerDataSO data)
    {
        _b = b;
        _data = data;
        // 한 재료가 입력 슬롯 전부를 독점해 다른 재료가 못 들어오는 데드락 방지
        _b.Input.SingleStackPerType = true;
        // 입력 버퍼는 현재 레시피의 재료만 받는다 (포트 필터 AcceptedTypes 대체)
        _b.Input.AcceptFilter = IsIngredient;
        SetRecipe(data.availableRecipes?.FirstOrDefault());
    }

    // ── 설비 UI(SCR-09)가 읽는 표면 ──────────────────────────

    public Building Building => _b;
    public AssemblerDataSO Data => _data;
    public RecipeDataSO CurrentRecipe => _recipe;

    /// <summary>사람이 세워둔 상태. 진행률과 버퍼는 보존된다 — "잠깐 자원을 아끼려고
    /// 세워둔다"가 안전한 조작이어야 한다 (재료가 귀한 라인에서 실제로 쓰는 기능).</summary>
    public bool Paused { get; private set; }

    public void SetPaused(bool paused)
    {
        if (Paused == paused) return;
        Paused = paused;

        if (paused)
        {
            if (_crafting) _pausedRemaining = Mathf.Max(0f, _readyAt - _b.Sim.Now);
        }
        else
        {
            if (_crafting)
            {
                _readyAt = _b.Sim.Now + _pausedRemaining;
                _b.Sim.ScheduleWake(_b, _pausedRemaining);
            }
            _b.Sim.MarkDirty(_b);
        }
    }

    /// <summary>현재 1회분 진행률 0~1. 중지 중에도 멈춘 값을 그대로 보여준다.</summary>
    public float Progress
    {
        get
        {
            if (!_crafting || _craftingRecipe == null || _craftingRecipe.craftTime <= 0f) return 0f;
            float remaining = Paused ? _pausedRemaining : Mathf.Max(0f, _readyAt - _b.Sim.Now);
            return Mathf.Clamp01(1f - remaining / _craftingRecipe.craftTime);
        }
    }

    public float RemainingTime =>
        !_crafting ? 0f : Paused ? _pausedRemaining : Mathf.Max(0f, _readyAt - _b.Sim.Now);

    /// <summary>막힘의 원인은 셋뿐 — 가동 중 · 재료 대기 · 출력 막힘. 넷째는 사람이 세운 것.</summary>
    public MachineState State
    {
        get
        {
            if (Paused) return MachineState.Stopped;
            if (_recipe == null) return MachineState.Stopped;
            if (_crafting)
                return _b.Sim.Now < _readyAt || CanStoreOutputs(_craftingRecipe)
                    ? MachineState.Running
                    : MachineState.OutputBlocked;
            if (!HasIngredients()) return MachineState.WaitingInput;
            if (!CanStoreOutputs(_recipe)) return MachineState.OutputBlocked;
            return MachineState.Running;
        }
    }

    public string InteractPrompt => $"{_data.displayName} 열기";

    public void Interact(PlayerController player) => MachinePanelView.TryOpen(this);

    public void SetRecipe(RecipeDataSO r)
    {
        if (r != null && r.inputs != null && r.inputs.Length > _b.Input.SlotCount)
        {
            Debug.LogWarning($"[Assembler] 레시피 '{r.displayName}'의 재료 종류({r.inputs.Length})가 " +
                             $"입력 슬롯({_b.Input.SlotCount})보다 많아 거부됨");
            return;
        }
        if (r != null && !RecipeDatabaseSO.IsUnlocked(r))
        {
            Debug.LogWarning($"[Assembler] 레시피 '{r.displayName}'는 아직 해금되지 않음 (요구 Tier {r.tier})");
            return;
        }
        if (r == _recipe) return;

        // 진행 중인 1회는 취소하지 않는다 — 이미 소비된 재료는 옛 레시피(_craftingRecipe)의
        // 완성품이 되어 출구로 나간다. 건물의 물건은 건물의 출구로만 나간다 (가방 순간이동 없음).
        // 새 레시피에 안 쓰는 입력 잔여물은 Tick의 EvictForeignInputs가 출구로 밀어낸다.
        _recipe = r;
        _b.Sim.MarkDirty(_b);
    }

    /// <summary>현재 해금된 레시피만 — 향후 레시피 선택 UI가 이걸로 목록을 채우면 게이팅이 자동 반영된다.</summary>
    public IEnumerable<RecipeDataSO> GetUnlockedRecipes() =>
        _data.availableRecipes.Where(RecipeDatabaseSO.IsUnlocked);

    bool IsIngredient(ItemDataSO item)
    {
        if (_recipe?.inputs == null) return false;
        foreach (var i in _recipe.inputs)
            if (i.item == item) return true;
        return false;
    }

    public void OnAfterPlaced() { }

    public void Tick(float dt)
    {
        if (_recipe == null || Paused) return;   // 중지 — 진행률·버퍼 보존, 재개 시 MarkDirty로 깨어난다
        var sim = _b.Sim;

        // 1. 출력 배출 시도 — 완료 판정보다 먼저 버퍼를 비워야 stall이 풀린다
        _b.FlushOutputs();

        // 2. 조합 완료 판정 — 교체됐어도 진행 중인 1회는 시작 당시 레시피로 끝낸다
        if (_crafting)
        {
            if (sim.Now < _readyAt) return;  // 이른 기상 (재료 도착 등) → 완료 시각에 다시 깨어남
            if (!CanStoreOutputs(_craftingRecipe)) return;  // 출력 버퍼 막힘 → 완료 보류 (stall)

            foreach (var o in _craftingRecipe.outputs)
                _b.Output.TryAdd(o.item, o.amount);
            _crafting = false;
            _b.FlushOutputs();
        }

        // 3. 레시피 교체로 쓸모없어진 입력 잔여물을 출구로 — 완성품이 자리를 먼저 쓰고,
        //    남는 자리만큼 매 틱 재시도. 남겨두면 종류당 1스택 슬롯을 차지해
        //    새 재료가 못 들어온다 (구 TODO의 시나리오)
        EvictForeignInputs();

        // 4. 다음 조합 시작 — 재료가 모였고 결과물 들어갈 자리가 있을 때만
        if (!HasIngredients() || !CanStoreOutputs(_recipe)) return;
        ConsumeIngredients();
        _b.NotifyUpstream();   // 입력 버퍼에 자리 생김 → 막혀 있던 상류 깨움
        _crafting = true;
        _craftingRecipe = _recipe;
        _readyAt  = sim.Now + _recipe.craftTime;
        sim.ScheduleWake(_b, _recipe.craftTime);
    }

    /// <summary>
    /// 새 레시피의 재료가 아닌 입력 잔여물을 출력 버퍼로 옮긴다 — 건물의 물건은
    /// 건물의 출구로만 나간다. 출력에 자리가 없으면 입력 칸에 그대로 남아
    /// 화면(IN 칸)에 보이고, 하류가 소비해 자리가 나면 다음 틱에 마저 나간다.
    /// </summary>
    void EvictForeignInputs()
    {
        if (_recipe == null) return;

        foreach (var (item, n) in _b.Input.Snapshot())
        {
            if (IsIngredient(item)) continue;

            int move = Mathf.Min(n, _b.Output.RoomFor(item));
            if (move <= 0) continue;

            _b.Input.TryConsume(item, move);
            _b.Output.TryAdd(item, move);
            _b.NotifyUpstream();   // 입력에 자리 생김
        }
    }

    bool HasIngredients()
    {
        foreach (var i in _recipe.inputs)
            if (_b.Input.CountOf(i.item) < i.amount) return false;
        return true;
    }

    /// <summary>
    /// 레시피 출력 전량이 출력 버퍼에 들어갈 수 있는가.
    /// 주의: 출력이 여러 종류면 슬롯을 서로 나눠 써야 하므로 근사 검사 —
    /// 현재 레시피는 전부 단일 출력이라 정확하다.
    /// </summary>
    bool CanStoreOutputs(RecipeDataSO r)
    {
        foreach (var o in r.outputs)
            if (!_b.Output.HasRoomFor(o.item, o.amount))
                return false;
        return true;
    }

    void ConsumeIngredients()
    {
        foreach (var i in _recipe.inputs)
            _b.Input.TryConsume(i.item, i.amount);
    }
}
