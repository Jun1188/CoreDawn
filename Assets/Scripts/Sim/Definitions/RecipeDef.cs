using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>레시피 — 입력 아이템 묶음을 seconds 동안 출력 묶음으로. 수제작·조합기가 같은 정의를 쓴다.</summary>
    public sealed class RecipeDef : Def
    {
        [JsonProperty("tier")] public int Tier = 1;
        [JsonProperty("seconds")] public float Seconds = 1f;
        [JsonProperty("inputs")] public List<ItemAmount> Inputs = new List<ItemAmount>();
        [JsonProperty("outputs")] public List<ItemAmount> Outputs = new List<ItemAmount>();

        public override void Resolve(SimDatabase db, List<string> errors)
        {
            foreach (var i in Inputs) i.Resolve(db, errors, Id);
            foreach (var o in Outputs) o.Resolve(db, errors, Id);
        }
    }
}
