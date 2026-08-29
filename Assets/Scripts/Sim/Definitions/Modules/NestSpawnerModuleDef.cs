using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>둥지 — 스폰 포인트·보스·방어자·복구일·교전 규칙. 값은 5a-2에서 NestView·맵 스펙에서 옮겨 온다.</summary>
    public sealed class NestSpawnerModuleDef : EntityModuleDef
    {
        [JsonProperty("defender")] public string DefenderId;
        [JsonProperty("boss")] public string BossId;
        [JsonProperty("bossRecoveryDays")] public int BossRecoveryDays = 2;
        [JsonProperty("nestRecoveryDays")] public int NestRecoveryDays = 3;
    }
}
