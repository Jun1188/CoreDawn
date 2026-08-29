using System;
using System.Collections.Generic;
using CoreDawn.Combat;
using CoreDawn.Data;

namespace CoreDawn.Factory
{
    /// <summary>
    /// Runtime-only additive recipe unlock policy used by wave rewards.
    /// Tier unlocks remain authoritative; this service only grants explicit exceptions.
    /// </summary>
    public static class RecipeRewardUnlockService
    {
        private static readonly HashSet<string> unlockedRecipeIds = new HashSet<string>();

        public static event Action<RecipeDataSO> RecipeUnlocked;

        public static bool IsUnlocked(RecipeDataSO recipe)
        {
            if (recipe == null) return false;
            return unlockedRecipeIds.Contains(GetStableId(recipe));
        }

        public static bool Unlock(RecipeDataSO recipe)
        {
            if (recipe == null) return false;
            if (!unlockedRecipeIds.Add(GetStableId(recipe))) return false;
            RecipeUnlocked?.Invoke(recipe);
            return true;
        }

        public static void Clear()
        {
            unlockedRecipeIds.Clear();
        }

        private static string GetStableId(RecipeDataSO recipe)
        {
            return string.IsNullOrEmpty(recipe.Id) ? recipe.name : recipe.Id;
        }
    }
}
