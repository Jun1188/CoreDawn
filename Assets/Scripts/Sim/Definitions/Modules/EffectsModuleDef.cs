using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>지속 효과·받는 배율. Health가 있는 엔티티는 전부 갖는다 — json에 명시한다(암묵 부착 없음).</summary>
    public sealed class EffectsModuleDef : EntityModuleDef
    {
        public override EntityModule Create(Entity entity) => new EffectsModule();
    }
}
