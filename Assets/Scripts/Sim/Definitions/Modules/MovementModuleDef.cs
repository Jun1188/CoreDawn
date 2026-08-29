using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>이동 — 몬스터 값(기존 MonsterSpec). 런타임은 5a-2에서 이 정의로 만든다(내비게이션 주입 필요).</summary>
    public sealed class MovementModuleDef : EntityModuleDef
    {
        [JsonProperty("moveSpeed")] public float MoveSpeed = 4f;
        [JsonProperty("rotateSpeed")] public float RotateSpeed = 720f;
        [JsonProperty("crowdRadius")] public float CrowdRadius = 0.4f;
        [JsonProperty("knockbackDamping")] public float KnockbackDamping = 8f;
        [JsonProperty("stickToGround")] public bool StickToGround = true;
    }
}
