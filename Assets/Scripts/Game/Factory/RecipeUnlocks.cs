using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 레시피 해금 판정 — 티어 게이트(GameManager) + 웨이브 보상 예외(구 RecipeDatabaseSO.IsUnlocked).
    /// 게임 규칙이라 심 밖이다. GameManager 없는 씬은 전부 해금 취급(건설 메뉴와 같은 규칙 —
    /// 테스트·부분 씬에서 제작이 영영 잠기지 않게).
    /// </summary>
    public static class RecipeUnlocks
    {
        public static bool IsUnlocked(RecipeDef r) =>
            r != null && ((GameManager.Instance == null || GameManager.Instance.IsTierUnlocked(r.Tier)) ||
                          RecipeRewardUnlockService.IsUnlocked(r));
    }
}
