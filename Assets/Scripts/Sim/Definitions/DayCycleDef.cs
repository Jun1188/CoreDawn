using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 주야 시계 — 팩 최상위 dayCycle 블록 하나. 낮 길이와 밤(달이 뜨고 지는) 길이.
    /// 밤 자체는 웨이브를 다 잡아야 끝나므로 nightDuration은 웨이브 규칙의 targetNightLength와 다른 값이다.
    /// TimeManager가 읽는다(인스펙터 값 없음).
    /// </summary>
    public sealed class DayCycleDef : Def
    {
        /// <summary>낮 길이(초).</summary>
        [JsonProperty("dayDuration")] public float DayDuration = 360f;
        /// <summary>밤 길이(초) — 달이 뜨고 지는 시간.</summary>
        [JsonProperty("nightDuration")] public float NightDuration = 10f;

        public override void Resolve(SimDatabase db, List<string> errors)
        {
            if (DayDuration <= 0f || NightDuration <= 0f) errors.Add($"{Id}: dayDuration·nightDuration은 0보다 커야 합니다");
        }
    }
}
