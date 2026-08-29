using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>이동 — 몬스터 값. 내비게이션은 조립 뒤 시스템이 꽂는다(정의는 씬을 모른다).</summary>
    public sealed class MovementModuleDef : EntityModuleDef
    {
        [JsonProperty("moveSpeed")] public float MoveSpeed = 4f;
        [JsonProperty("rotateSpeed")] public float RotateSpeed = 720f;
        [JsonProperty("crowdRadius")] public float CrowdRadius = 0.4f;
        [JsonProperty("knockbackDamping")] public float KnockbackDamping = 8f;
        [JsonProperty("stickToGround")] public bool StickToGround = true;

        public override EntityModule Create(Entity entity) => new MovementModule(this);
    }
}
