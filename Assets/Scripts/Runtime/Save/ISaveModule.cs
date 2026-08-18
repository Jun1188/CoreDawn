using Newtonsoft.Json.Linq;

/// <summary>
/// 세이브 대상 시스템 한 덩어리 — 시간, 진행도, 공장, 플레이어, 월드, 전투 등.
///
/// 확장 방법: 이 인터페이스를 구현하고 매개변수 없는 생성자를 두면 끝이다.
/// <see cref="SaveManager"/>가 리플렉션으로 찾아 자동 등록하므로 SaveManager를 고칠 일이 없다.
///
/// 구현 시 지켜야 할 것:
///   - 대상 시스템이 씬에 없으면 Capture는 null을, Restore는 조용히 반환할 것
///     (테스트 씬처럼 일부 시스템만 있는 씬에서도 세이브가 동작해야 한다)
///   - Capture가 돌려준 객체는 그대로 JSON이 된다 — Unity 오브젝트 참조가 아니라
///     안정 ID(<see cref="GameDataSO.Id"/>)와 값으로만 채울 것
/// </summary>
public interface ISaveModule
{
    /// <summary>세이브 파일에서 이 모듈이 차지하는 키. 한 번 정하면 바꾸지 말 것 (구세이브 호환).</summary>
    string ModuleId { get; }

    /// <summary>
    /// 복원 순서 — 작을수록 먼저. 저장 순서에는 영향이 없다.
    ///
    /// 기본 배치: 시간 0 · 진행도 10 · 공장 20 · 월드 30 · 전투 40 · 플레이어 50.
    /// 플레이어가 마지막인 이유는 BattleManager가 런타임에 Player 컴포넌트를 붙이기 때문이다
    /// (그 전에 체력을 복원하면 덮어써진다).
    /// </summary>
    int Order { get; }

    /// <summary>현재 상태를 직렬화 가능한 객체로. 대상 시스템이 없으면 null.</summary>
    object Capture();

    /// <summary>저장된 상태를 되돌린다. 이 모듈 키가 세이브에 없으면 아예 호출되지 않는다.</summary>
    void Restore(JToken data);
}

/// <summary>
/// 상태를 가진 건물 행동이 함께 구현하는 인터페이스.
///
/// 새 건물을 추가하는 사람은 자기 <see cref="IBuildingBehavior"/> 구현체에 이것만 덧붙이면
/// 세이브 시스템 쪽은 손대지 않아도 된다. 상태가 없는 행동(벨트·저장소·병합기·타워)은
/// 구현하지 않으면 그만이다.
/// </summary>
public interface ISaveableBehavior
{
    /// <summary>행동 고유 상태를 직렬화 가능한 객체로.</summary>
    object CaptureState();

    /// <summary>저장된 행동 상태를 되돌린다.</summary>
    void RestoreState(JToken state);
}
