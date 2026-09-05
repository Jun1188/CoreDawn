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
        /// <summary>
        /// 사망 뒤 심에서 제거되기까지(초) — 뷰가 사망 연출(클립·가라앉기)을 보여주는 시간. 제거는 심(DeadState)이 정하고
        /// 뷰는 Entity.Removed 를 받아 사라진다. 옛 view.deathDelay(뷰가 자기를 부수며 심을 지우던 역방향)를 대체(2026-09-04).
        /// </summary>
        [JsonProperty("corpseSeconds")] public float CorpseSeconds = 2f;

        public override EntityModule Create(Entity entity) => new MonsterBrainModule(this);

        public override void Resolve(SimDatabase db, System.Collections.Generic.List<string> errors, string owner)
        {
            if (CorpseSeconds < 0f) errors.Add($"'{owner}': MonsterBrain.corpseSeconds 는 0 이상이어야 합니다 ({CorpseSeconds}).");
        }
    }
}
