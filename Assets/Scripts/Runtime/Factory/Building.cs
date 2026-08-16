using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 배치된 건물의 심(시뮬레이션) 엔티티 — plain C#, MonoBehaviour 아님.
/// BuildingDataSO = 설계도 (공유됨), Building = 실물 (각자 독립적 상태).
///
/// 이 클래스가 건물 데이터의 원본(source of truth)이다 — Data/Origin/회전/버퍼/연결/행동/IsRemoved.
/// 씬 표현은 BuildingEntity(MonoBehaviour)이 껍데기로 맡고, HP·피격 같은 전투 상태만 그쪽이 원본이다.
/// 매핑은 FactoryBootstrap이 들고, 이 건물이 제거되면 FactorySim.Removed를 타고 껍데기도 함께 정리된다.
/// (같은 이름의 두 클래스를 물리적으로 합치지 않는 이유: 순수 C#이라 씬에 못 붙고,
///  MonoBehaviour로 만들면 씬·프레임 없이 돌리는 헤드리스 시뮬레이션·테스트가 불가능해진다)
///
/// 연결 목록(InputConnections/OutputConnections)은 BuildingGraph가 채우고,
/// 행동(IBuildingBehavior)은 SO의 CreateBehavior()가 결정한다.
/// </summary>
public class Building
{
    public readonly FactorySim Sim;

    // 불변 데이터 (생성 이후 변경 안 됨)
    public BuildingDataSO Data { get; }
    public Vector2Int Origin { get; }
    public int RotationSteps { get; }

    // 인스턴스별 포트 형상 (벨트 커브 등). null이면 SO의 회전 포트 사용.
    public PortDefinition[] PortOverride { get; }

    /// <summary>
    /// 벨트 모양 (직선/커브L/커브R) — 배치 시 결정되는 인스턴스 상태. 벨트가 아닌 건물에는 의미가 없다.
    ///
    /// 포트는 PortOverride로도 알 수 있지만 커브 메시 프리팹은 모양으로만 고를 수 있어서
    /// (BeltDataSO.PrefabFor), 세이브가 이 값을 그대로 되살릴 수 있게 여기 남겨둔다.
    /// </summary>
    public BeltShape Shape { get; }

    // 런타임 상태 — 입력/출력 버퍼 분리 (슬롯 기반, 플레이어 인벤토리와 같은 모델)
    public ItemContainer Input  { get; }
    public ItemContainer Output { get; }

    /// <summary>FactorySim.Remove가 설정. 제거 후 큐/힙에 남은 참조를 걸러낸다.</summary>
    public bool IsRemoved { get; set; }

    // 연결 목록 — BuildingGraph가 OnPlaced/OnRemoved 시 수정
    public readonly List<BuildingConnection> InputConnections  = new();
    public readonly List<BuildingConnection> OutputConnections = new();

    readonly IBuildingBehavior _behavior;

    public Building(FactorySim sim, BuildingDataSO data, Vector2Int origin, int rotSteps,
        PortDefinition[] portOverride = null, BeltShape shape = BeltShape.Straight)
    {
        Sim           = sim;
        Data          = data;
        Origin        = origin;
        RotationSteps = rotSteps;
        PortOverride  = portOverride;
        Shape         = shape;
        Input         = new ItemContainer(data.inputSlots,  data.bufferStackCap);
        Output        = new ItemContainer(data.outputSlots, data.bufferStackCap);
        _behavior     = data.CreateBehavior(this);
    }

    /// <summary>회전/모양이 적용된 실제 포트 목록. BuildingGraph가 이걸 사용한다.</summary>
    public PortDefinition[] GetEffectivePorts() => PortOverride ?? Data.GetRotatedPorts(RotationSteps);

    /// <summary>
    /// 이 건물이 지정한 월드 칸에서 지정 방향을 향하는 포트를 갖고 있는가.
    /// 순수 기하 질의다 — 연결 성립 규칙(입출력 짝) 자체는 BuildingGraph가 소유하므로
    /// 여기에 다시 짓지 말 것. 포트 시각화가 "이미 맞물린 자리"를 걸러낼 때 쓴다.
    /// </summary>
    /// <param name="isInput">null이면 입출력을 가리지 않는다.</param>
    public bool HasPortAt(Vector2Int cell, Direction dir, bool? isInput = null)
    {
        var ports = GetEffectivePorts();
        if (ports == null) return false;

        foreach (var p in ports)
        {
            if (p == null || p.Direction != dir) continue;
            if (Origin + p.LocalOffset != cell) continue;
            if (isInput.HasValue && p.IsInput != isInput.Value) continue;
            return true;
        }
        return false;
    }

    /// <summary>BuildingGraph.OnPlaced() 완료 후 호출 — 연결이 확정된 뒤 초기화.</summary>
    public void OnAfterConnected() => _behavior?.OnAfterPlaced();

    /// <summary>FactorySim이 이 건물이 깨어 있는 틱에 호출.</summary>
    public void Tick(float dt) => _behavior?.Tick(dt);

    /// <summary>행동 객체 조회 (레시피 지정 등 외부 설정용).</summary>
    public IBuildingBehavior Behavior => _behavior;

    /// <summary>
    /// 출력 버퍼의 아이템을 연결된 다음 건물로 Push.
    /// 성공하면 수신 건물을 Dirty 마킹 → 다음 틱에 처리됨.
    /// </summary>
    public bool TryPushOutput(ItemDataSO item)
    {
        foreach (var c in OutputConnections)
        {
            if (!c.To.Input.TryAdd(item)) continue;
            Sim.MarkDirty(c.To);
            return true;
        }
        return false; // 모든 출력 막힘
    }

    /// <summary>출력 버퍼의 아이템을 하류가 받는 만큼 전부 배출. 행동들의 공용 루틴.</summary>
    public void FlushOutputs()
    {
        foreach (var (item, count) in Output.Snapshot())
            for (int k = 0; k < count && TryPushOutput(item); k++)
                Output.TryConsume(item);
    }

    /// <summary>
    /// 이 건물의 입력 버퍼에 자리가 생겼음을 상류에 알린다.
    /// 출력이 막혀 정지(stall)해 있던 상류 건물이 다음 틱에 재시도한다.
    /// </summary>
    public void NotifyUpstream()
    {
        foreach (var c in InputConnections)
            Sim.MarkDirty(c.From);
    }
}
