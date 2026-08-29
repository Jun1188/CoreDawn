using UnityEngine;
using CoreDawn.Managers;
using CoreDawn.Factory;

namespace CoreDawn.Data
{
    /// <summary>
    /// 프로젝트의 모든 레시피 SO 레지스트리 — Building/ItemDatabaseSO와 같은 패턴.
    /// 에디터 스캐너(Editor/BuildingDatabaseScanner)가 RecipeDataSO 에셋을 만들거나
    /// 지울 때마다 자동으로 이 목록을 갱신한다 (티어 → 표시명 순 정렬).
    ///
    /// 구 RecipeManager(씬 싱글턴)를 대체한다 — 그쪽은 Resources.LoadAll로 읽어
    /// 에디터 폴백이 없는 빌드에서는 목록이 비었고, UITest 씬에만 있어 다른 씬에서는
    /// Instance가 null이었다. 티어 해금 검사·해금 이벤트는 GameManager가 이미 갖고 있다.
    /// </summary>
    [CreateAssetMenu(fileName = "RecipeDatabase", menuName = "Factory/Recipe Database")]
    public class RecipeDatabaseSO : ScriptableObject
    {
        [Tooltip("자동 수집됨 — 직접 편집하지 말 것 (Tools/Factory/Rebuild Data Databases로 재수집)")]
        public RecipeDataSO[] recipes;

        /// <summary>Resources의 기본 데이터베이스. 씬 연결 없이도 어디서든 접근 가능.</summary>
        public static RecipeDatabaseSO LoadDefault()
            => Resources.Load<RecipeDatabaseSO>("RecipeDatabase");

        /// <summary>이 레시피가 현재 코어 티어에서 해금됐는가. GameManager 없는 씬은 전부 해금 취급 (건설 메뉴와 같은 규칙).</summary>
        public static bool IsUnlocked(RecipeDataSO r) =>
            r != null && ((GameManager.Instance == null || GameManager.Instance.IsTierUnlocked(r.tier)) ||
                          RecipeRewardUnlockService.IsUnlocked(r));
    }
}
