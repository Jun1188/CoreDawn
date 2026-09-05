using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>광맥 — 자원별 하나. 매장량은 없다(바닥나지 않음). 값은 채굴 시간(extractInterval) 하나 — 손은 그대로, 채굴기는 배율로 줄인다.</summary>
    public sealed class ResourceDepositModuleDef : EntityModuleDef
    {
        [JsonProperty("resource")] public string ResourceId;
        /// <summary>1개를 캐는 데 걸리는 시간(초) — 손으로 캘 때 그대로, 채굴기는 이 값 ÷ 배율(Extractor.speedMultiplier). "얼마나 캐기 어려운 광맥인가"는 땅이 갖는다.</summary>
        [JsonProperty("extractInterval")] public float ExtractInterval = 3f;

        [JsonIgnore] public ItemDef Resource { get; set; }   // json은 Resolve가, 코드 조립(테스트)은 직접

        public override void Resolve(SimDatabase db, List<string> errors, string owner) => Resource = db.ResolveItem(ResourceId, errors, owner);

        public override EntityModule Create(Entity entity) => new ResourceDepositModule(this);
    }
}
