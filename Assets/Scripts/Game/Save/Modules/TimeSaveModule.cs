using Newtonsoft.Json;
using fNbt;
using CoreDawn.DayTime;

namespace CoreDawn.Save
{
    /// <summary>
    /// 낮/밤 주기 — 며칠째인지, 지금 낮인지 밤인지, 페이즈가 얼마나 남았는지.
    ///
    /// 가장 먼저 복원된다(Order 0). 둥지 복구 판정처럼 "며칠째인가"에 의존하는
    /// 다른 모듈들이 올바른 날짜를 보고 복원돼야 하기 때문이다.
    /// </summary>
    public class TimeSaveModule : ISaveModule
    {
        public string ModuleId => "time";
        public int Order => 0;

        public class Dto
        {
            [JsonProperty("phase")] public DayPhase Phase;
            [JsonProperty("day")] public int DayNumber;
            [JsonProperty("remaining")] public float PhaseRemaining;
        }

        public object Capture()
        {
            var cycle = TimeManager.Instance?.Cycle;
            if (cycle == null) return null;

            return new Dto
            {
                Phase = cycle.Phase,
                DayNumber = cycle.DayNumber,
                PhaseRemaining = cycle.PhaseRemaining,
            };
        }

        public void Restore(NbtCompound data)
        {
            var cycle = TimeManager.Instance?.Cycle;
            if (cycle == null) return;

            var dto = SaveNbt.FromTag<Dto>(data);
            if (dto == null) return;

            cycle.RestoreState(dto.Phase, dto.DayNumber, dto.PhaseRemaining);
        }
    }
}
