using System;
using System.Collections.Generic;
using CoreDawn.Sim;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 웨이브 보상의 추가 해금 정책 — 런타임 전용(세이브에 실리지 않는 기존 동작 유지, 씬의
    /// NightWaveRewardManager가 누적 클리어 수로 다시 건다). 티어 해금이 정본이고,
    /// 이 서비스는 명시적 예외만 얹는다. 키는 팩 id(구 SO id 키를 5a-3c에서 교체).
    /// </summary>
    public static class RecipeRewardUnlockService
    {
        private static readonly HashSet<string> unlockedRecipeIds = new HashSet<string>();

        public static event Action<RecipeDef> RecipeUnlocked;

        public static bool IsUnlocked(RecipeDef recipe)
            => recipe != null && unlockedRecipeIds.Contains(recipe.Id);

        public static bool Unlock(RecipeDef recipe)
        {
            if (recipe == null) return false;
            if (!unlockedRecipeIds.Add(recipe.Id)) return false;
            RecipeUnlocked?.Invoke(recipe);
            return true;
        }

        public static void Clear()
        {
            unlockedRecipeIds.Clear();
        }
    }
}
