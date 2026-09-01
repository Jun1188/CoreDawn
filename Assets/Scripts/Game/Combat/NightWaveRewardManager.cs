using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Data;
using CoreDawn.Save;
using CoreDawn.Sim;

namespace CoreDawn.Combat
{
    [Serializable]
    public sealed class NightWaveRecipeReward
    {
        [Min(1)] public int requiredClearedNights = 1;
        [Tooltip("해금할 레시피의 팩 id(coredawn:recipe/dense_ammo).")]
        public string unlockedRecipeId;

        [Tooltip("표시용 메타: 이 보상이 어느 레시피의 대체 레시피인지(팩 id). 비워도 된다.")]
        public string alternativeForId;

        public RecipeDef UnlockedRecipe => SaveRefs.Recipe(unlockedRecipeId);
        public RecipeDef AlternativeFor => SaveRefs.Recipe(alternativeForId);
    }

    /// <summary>
    /// Scene-local reward policy. It listens to BattleManager wave completion and grants
    /// explicit recipe exceptions without changing recipe assets, tiers, or databases.
    /// </summary>
    public sealed class NightWaveRewardManager : MonoBehaviour
    {
        [SerializeField] private BattleManager battleManager;
        [SerializeField] private bool resetRuntimeUnlocksOnAwake = true;
        [Min(0)] [SerializeField] private int clearedNightCount;
        [SerializeField] private List<NightWaveRecipeReward> recipeRewards = new List<NightWaveRecipeReward>();

        public int ClearedNightCount => clearedNightCount;
        public IReadOnlyList<NightWaveRecipeReward> RecipeRewards => recipeRewards;

        public event Action<int> ClearedNightCountChanged;
        public event Action<NightWaveRecipeReward> RewardGranted;

        private void Awake()
        {
            if (battleManager == null)
                battleManager = GetComponent<BattleManager>() ?? FindFirstObjectByType<BattleManager>();

            if (resetRuntimeUnlocksOnAwake)
                RecipeRewardUnlockService.Clear();

            ApplyRewardsForClearCount(clearedNightCount, false);
        }

        private void OnEnable()
        {
            if (battleManager != null)
                battleManager.NightWaveCleared += OnNightWaveCleared;
        }

        private void OnDisable()
        {
            if (battleManager != null)
                battleManager.NightWaveCleared -= OnNightWaveCleared;
        }

        private void OnNightWaveCleared(int day, int defeatedAmount)
        {
            clearedNightCount++;
            Debug.Log($"[NightWaveRewardManager] Day {day} 물량 웨이브 생존: {defeatedAmount}마리 처치, 누적 {clearedNightCount}회");
            ApplyRewardsForClearCount(clearedNightCount, true);
            ClearedNightCountChanged?.Invoke(clearedNightCount);
        }

        public void ApplyRewardsForClearCount(int clearCount, bool notify)
        {
            for (int i = 0; i < recipeRewards.Count; i++)
            {
                NightWaveRecipeReward reward = recipeRewards[i];
                if (reward == null || string.IsNullOrEmpty(reward.unlockedRecipeId)) continue;
                if (clearCount < Mathf.Max(1, reward.requiredClearedNights)) continue;
                var recipe = reward.UnlockedRecipe;
                if (recipe == null || !RecipeRewardUnlockService.Unlock(recipe)) continue;

                var alt = reward.AlternativeFor;
                string alternative = alt != null
                    ? $" ('{alt.DisplayName ?? alt.Id}'의 대체 레시피)"
                    : string.Empty;
                Debug.Log($"[NightWaveRewardManager] 생존 보상 해금: {recipe.DisplayName ?? recipe.Id}{alternative}");
                if (notify) RewardGranted?.Invoke(reward);
            }
        }
    }
}
