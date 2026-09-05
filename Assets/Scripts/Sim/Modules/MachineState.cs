namespace CoreDawn.Sim
{
    /// <summary>자동 제작기(CrafterModule)의 상태 — UI가 "왜 멈춰 있나"를 말할 때 쓴다.</summary>
    public enum MachineState { Running, WaitingInput, OutputBlocked, Stopped }
}
