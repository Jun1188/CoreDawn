using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Data;

namespace CoreDawn.Combat
{
    [Serializable]
    public sealed class NightWaveRecipeReward
    {
        [Min(1)] public int requiredClearedNights = 1;
        public RecipeDataSO unlockedRecipe;

        [Tooltip("Optional metadata: the rewarded recipe is presented as an alternative to this recipe.")]
        public RecipeDataSO alternativeFor;
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
                if (reward == null || reward.unlockedRecipe == null) continue;
                if (clearCount < Mathf.Max(1, reward.requiredClearedNights)) continue;
                if (!RecipeRewardUnlockService.Unlock(reward.unlockedRecipe)) continue;

                string alternative = reward.alternativeFor != null
                    ? $" ('{reward.alternativeFor.displayName}'의 대체 레시피)"
                    : string.Empty;
                Debug.Log($"[NightWaveRewardManager] 생존 보상 해금: {reward.unlockedRecipe.displayName}{alternative}");
                if (notify) RewardGranted?.Invoke(reward);
            }
        }
    }
}
