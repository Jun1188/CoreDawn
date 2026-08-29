using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>몬스터 두뇌 — 보스 인내심·복귀 규칙. 시스템 참조는 조립 뒤 MonsterSystem이 Bind한다.</summary>
    public sealed class MonsterBrainModuleDef : EntityModuleDef
    {
        [JsonProperty("maxPatience")] public float MaxPatience = 3f;
        [JsonProperty("patienceRadius")] public float PatienceRadius;
        [JsonProperty("outsidePatienceDrain")] public float OutsidePatienceDrain = 2f;
        [JsonProperty("rangedPokePatienceDrain")] public float RangedPokePatienceDrain = 3f;
        [JsonProperty("patienceRecoverRate")] public float PatienceRecoverRate = 1f;
        [JsonProperty("absoluteLeashMultiplier")] public float AbsoluteLeashMultiplier = 2f;
        [JsonProperty("returnRegenPerSecond")] public float ReturnRegenPerSecond;
        [JsonProperty("returnTimeout")] public float ReturnTimeout = 40f;

        public override EntityModule Create(Entity entity) => new MonsterBrainModule(this);
    }
}
