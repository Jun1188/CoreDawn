using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    /// <summary>심 정의(RecipeDef) → 에셋(RecipeDataSO). ItemAssets와 같은 규칙.</summary>
    public static class RecipeAssets
    {
        static Dictionary<string, RecipeDataSO> byDefId;

        public static RecipeDataSO Of(RecipeDef def)
        {
            if (def == null) return null;
            var db = SimHost.Database;
            if (byDefId == null)
            {
                byDefId = new Dictionary<string, RecipeDataSO>();
                var so = RecipeDatabaseSO.LoadDefault();
                if (so?.recipes != null && db != null)
                    foreach (var r in so.recipes)
                        if (r != null && !string.IsNullOrEmpty(r.Id)) byDefId[db.LegacyId(r.Id)] = r;
            }
            return byDefId.TryGetValue(def.Id, out var found) ? found : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => byDefId = null;
    }
}
