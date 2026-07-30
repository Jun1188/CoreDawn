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
public class AssemblerBehavior : IBuildingBehavior
{
    readonly Building _b;
    readonly AssemblerDataSO _data;
    RecipeDataSO _recipe;
    float        _readyAt;   // 조합 완료 예정 시각
    bool         _crafting;

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

    // TODO(레시피 UI): 레시피 변경 시 이전 재료가 입력 버퍼에 남으면 새 재료를 막는다.
    //   잔여물을 출력 버퍼로 밀어내거나(하류 배출) 플레이어 인벤토리로 환불할 것.
    public void SetRecipe(RecipeDataSO r)
    {
        if (r != null && r.inputs != null && r.inputs.Length > _b.Input.SlotCount)
        {
            Debug.LogWarning($"[Assembler] 레시피 '{r.displayName}'의 재료 종류({r.inputs.Length})가 " +
                             $"입력 슬롯({_b.Input.SlotCount})보다 많아 거부됨");
            return;
        }
        if (r != null && GameManager.Instance != null && !GameManager.Instance.IsTierUnlocked(r.requiredCoreTier))
        {
            Debug.LogWarning($"[Assembler] 레시피 '{r.displayName}'는 아직 해금되지 않음 (요구 Tier {r.requiredCoreTier})");
            return;
        }
        _recipe = r;
    }

    /// <summary>현재 해금된 레시피만 — 향후 레시피 선택 UI가 이걸로 목록을 채우면 게이팅이 자동 반영된다.</summary>
    public IEnumerable<RecipeDataSO> GetUnlockedRecipes() =>
        _data.availableRecipes.Where(r => r != null &&
            (GameManager.Instance == null || GameManager.Instance.IsTierUnlocked(r.requiredCoreTier)));

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
        if (_recipe == null) return;
        var sim = _b.Sim;

        // 1. 출력 배출 시도 — 완료 판정보다 먼저 버퍼를 비워야 stall이 풀린다
        _b.FlushOutputs();

        // 2. 조합 완료 판정
        if (_crafting)
        {
            if (sim.Now < _readyAt) return;  // 이른 기상 (재료 도착 등) → 완료 시각에 다시 깨어남
            if (!CanStoreOutputs()) return;  // 출력 버퍼 막힘 → 완료 보류 (stall)

            foreach (var o in _recipe.outputs)
                _b.Output.TryAdd(o.item, o.amount);
            _crafting = false;
            _b.FlushOutputs();
        }

        // 3. 다음 조합 시작 — 재료가 모였고 결과물 들어갈 자리가 있을 때만
        if (!HasIngredients() || !CanStoreOutputs()) return;
        ConsumeIngredients();
        _b.NotifyUpstream();   // 입력 버퍼에 자리 생김 → 막혀 있던 상류 깨움
        _crafting = true;
        _readyAt  = sim.Now + _recipe.craftTime;
        sim.ScheduleWake(_b, _recipe.craftTime);
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
    bool CanStoreOutputs()
    {
        foreach (var o in _recipe.outputs)
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
