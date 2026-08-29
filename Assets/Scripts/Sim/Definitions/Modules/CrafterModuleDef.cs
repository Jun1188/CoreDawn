using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>제작 — 수제작(manual)과 조합기가 같은 모듈. 레시피 목록·속도.</summary>
    public sealed class CrafterModuleDef : EntityModuleDef
    {
        [JsonProperty("manual")] public bool Manual;
        [JsonProperty("speed")] public float Speed = 1f;
        [JsonProperty("recipes")] public List<string> RecipeIds = new List<string>();

        [JsonIgnore] public List<RecipeDef> Recipes { get; } = new List<RecipeDef>();

        public override void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            Recipes.Clear();
            foreach (var id in RecipeIds)
            {
                var r = db.ResolveRecipe(id, errors, owner);
                if (r != null) Recipes.Add(r);
            }
        }
    }
}
