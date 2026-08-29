using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.FPS;
using CoreDawn.Save;
using CoreDawn.UI;
using CoreDawn.Factory;

namespace CoreDawn.Factory
{
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
}
