using UnityEngine;

/// <summary>채굴기. 주기적으로 아이템을 생산해 출력 포트로 내보낸다.</summary>
[CreateAssetMenu(fileName = "NewMiner", menuName = "Factory/Buildings/Miner")]
public class MinerDataSO : BuildingDataSO
{
    [Header("채굴")]
    [Tooltip("채굴 속도 배율. Mk.1 = 1, Mk.2 = 2.\n" +
             "실제 시간 = 광맥의 extractInterval ÷ 이 값.\n" +
             "\"얼마나 좋은 채굴기인가\"는 건물이, \"얼마나 캐기 어려운 광맥인가\"는 땅이 갖는다.")]
    public float speedMultiplier = 1f;

    public override IBuildingBehavior CreateBehavior(Building building)
        => new MinerBehavior(building, this);
}

// ─── 행동 ──────────────────────────────────────────────────────

/// <summary>
/// 주기적으로 아이템을 생산해 출력 포트로 Push.
/// 어떤 아이템을 채굴할지는 OnAfterPlaced에서 FactorySim.GetResourceAt으로 결정.
/// (ResourceGrid 같은 공간 시스템은 이 파일의 관심사가 아님)
///
/// 채굴은 ScheduleWake로 완료 시점에만 깨어난다.
/// 출력 버퍼가 가득 차면 채굴을 멈추고(stall), 하류가 아이템을 소비해
/// NotifyUpstream으로 깨워줄 때 재개한다 → 아이템 유실 없음.
/// </summary>
public class MinerBehavior : IBuildingBehavior
{
    readonly Building    _b;
    readonly MinerDataSO _data;
    ItemDataSO _target;
    float      _readyAt = -1f;   // 채굴 완료 예정 시각 (-1 = 예약 없음 = 정지 상태)

    public MinerBehavior(Building b, MinerDataSO data) { _b = b; _data = data; }

    // 외부(ResourceGrid 등)에서 OnAfterPlaced 이후 주입
    public void SetTarget(ItemDataSO item)
    {
        _target = item;
        _b.Sim.MarkDirty(_b);
    }

    public void OnAfterPlaced()
    {
        // 심에 자원 조회 서비스가 주입되어 있으면 사용
        // 없으면 SetTarget()으로 직접 설정
        if (_b.Sim.GetResourceAt != null)
            _target = _b.Sim.GetResourceAt(_b.Origin);
    }

    public void Tick(float dt)
    {
        if (_target == null) return;
        var sim = _b.Sim;

        // 1. 밀려 있던 출력 버퍼부터 배출 (하류가 받는 만큼 전부)
        _b.FlushOutputs();

        // 2. 채굴 완료 판정
        if (_readyAt >= 0f && sim.Now >= _readyAt)
        {
            _readyAt = -1f;

            // 광맥 재고에서 1개를 꺼내온다. 소켓이 비어 있으면(테스트 등) 기존처럼 무한 채굴.
            // 재고가 없으면(0) 이번 주기는 공치고, 아래 3번에서 다시 예약해 대기한다.
            int taken = sim.TryExtractResourceAt?.Invoke(_b.Origin, 1) ?? 1;

            // 예약 시점에 버퍼 여유를 확인했으므로 여기서 유실될 수 없다
            if (taken > 0 && !_b.TryPushOutput(_target))
                _b.Output.TryAdd(_target);
        }

        // 3. 다음 채굴 예약 — 출력 버퍼에 자리가 있을 때만.
        //    자리가 없으면 정지(stall); 하류의 NotifyUpstream이 다시 깨운다.
        if (_readyAt < 0f && _b.Output.HasRoomFor(_target))
        {
            // 광맥 난이도 ÷ 채굴기 배율 — 같은 채굴기라도 크리스탈 광맥에서는 느리다
            float baseInterval = sim.GetExtractIntervalAt?.Invoke(_b.Origin) ?? 1f;
            float interval = baseInterval / Mathf.Max(0.01f, _data.speedMultiplier);

            _readyAt = sim.Now + interval;
            sim.ScheduleWake(_b, interval);
        }
    }
}
