using fNbt;
namespace CoreDawn.Save
{
    /// <summary>
    /// 세이브 대상 시스템 한 덩어리 — 시간, 진행도, 공장, 플레이어, 월드, 전투 등.
    ///
    /// 확장 방법: 이 인터페이스를 구현하고 매개변수 없는 생성자를 두면 끝이다.
    /// <see cref="SaveManager"/>가 리플렉션으로 찾아 자동 등록하므로 SaveManager를 고칠 일이 없다.
    ///
    /// 구현 시 지켜야 할 것:
    ///   - 대상 시스템이 씬에 없으면 Capture는 null을, Restore는 조용히 반환할 것
    ///     (테스트 씬처럼 일부 시스템만 있는 씬에서도 세이브가 동작해야 한다)
    ///   - Capture가 돌려준 객체는 SaveNbt 가 NBT compound 로 옮긴다(public 필드, [JsonProperty] 이름) — Unity 오브젝트 참조가 아니라
    ///     안정 ID(팩 id, coredawn:item/…)와 값으로만 채울 것
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
        void Restore(NbtCompound data);
    }
}
