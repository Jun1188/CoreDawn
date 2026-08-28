using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Factory;

namespace CoreDawn.Save
{
    /// <summary>
    /// 세이브 데이터에 적힌 안정 ID(<see cref="GameDataSO.Id"/>)를 실제 에셋으로 되돌린다.
    ///
    /// 세이브에는 SO 참조를 넣을 수 없으므로 전부 문자열 id로 적고, 로드할 때 여기서 해석한다.
    /// 해석 실패(에셋이 지워졌거나 id가 바뀐 경우)는 조용히 넘기지 않고 한 번씩 경고를 남긴다 —
    /// 아이템이 슬그머니 사라지는 것보다 로그에 남는 편이 낫다.
    /// </summary>
    public static class SaveRefs
    {
        static Dictionary<string, ItemDataSO> _items;
        static Dictionary<string, BuildingDataSO> _buildings;
        static Dictionary<string, RecipeDataSO> _recipes;
        static readonly HashSet<string> _warned = new();

        /// <summary>데이터베이스 에셋이 다시 로드된 경우(에디터 재생 등) 캐시를 버린다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearCache()
        {
            _items = null;
            _buildings = null;
            _recipes = null;
            _warned.Clear();
        }

        // ── 아이템 ────────────────────────────────────────────────────

        public static ItemDataSO Item(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (_items == null)
            {
                _items = new Dictionary<string, ItemDataSO>();
                var db = ItemDatabaseSO.LoadDefault();
                if (db?.items != null)
                    foreach (var it in db.items) Index(_items, it);
                else WarnOnce("__itemdb", "Resources/ItemDatabase.asset 을 찾지 못했습니다 — 아이템 복원이 전부 실패합니다.");
            }

            return Lookup(_items, id, "아이템");
        }

        public static string IdOf(ItemDataSO item) => item != null ? item.Id : null;

        // ── 건물 ──────────────────────────────────────────────────────

        public static BuildingDataSO Building(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (_buildings == null)
            {
                _buildings = new Dictionary<string, BuildingDataSO>();
                var db = BuildingDatabaseSO.LoadDefault();
                if (db?.buildings != null)
                    foreach (var b in db.buildings) Index(_buildings, b);
                else WarnOnce("__buildingdb", "Resources/BuildingDatabase.asset 을 찾지 못했습니다 — 건물 복원이 전부 실패합니다.");
            }

            return Lookup(_buildings, id, "건물");
        }

        public static string IdOf(BuildingDataSO building) => building != null ? building.Id : null;

        // ── 레시피 ────────────────────────────────────────────────────

        public static RecipeDataSO Recipe(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (_recipes == null)
            {
                _recipes = new Dictionary<string, RecipeDataSO>();
                var db = RecipeDatabaseSO.LoadDefault();
                if (db?.recipes != null)
                    foreach (var r in db.recipes) Index(_recipes, r);
                else WarnOnce("__recipedb", "Resources/RecipeDatabase.asset 을 찾지 못했습니다 — 레시피 복원이 전부 실패합니다.");
            }

            return Lookup(_recipes, id, "레시피");
        }

        public static string IdOf(RecipeDataSO recipe) => recipe != null ? recipe.Id : null;

        // ── 공통 ──────────────────────────────────────────────────────

        static void Index<T>(Dictionary<string, T> map, T so) where T : GameDataSO
        {
            if (so == null || string.IsNullOrEmpty(so.Id)) return;
            map[so.Id] = so;
        }

        static T Lookup<T>(Dictionary<string, T> map, string id, string kind) where T : GameDataSO
        {
            if (map.TryGetValue(id, out var so)) return so;
            WarnOnce(id, $"{kind} id '{id}' 를 찾지 못했습니다 — 해당 데이터는 복원되지 않습니다 " +
                         "(에셋이 삭제됐거나 id가 바뀌었을 수 있습니다).");
            return null;
        }

        static void WarnOnce(string key, string message)
        {
            if (!_warned.Add(key)) return;
            Debug.LogWarning($"[Save] {message}");
        }
    }

    /// <summary>
    /// 씬에 미리 놓여 있는 오브젝트(상자 등)를 세이브에서 다시 찾기 위한 열쇠.
    ///
    /// 계층 경로를 쓰는 이유: 씬 배치물은 인스턴스 ID가 실행마다 달라지고 좌표는 사람이 옮길 수 있지만,
    /// 경로는 씬 파일에 그대로 적혀 있어 다시 열어도 같다. 이름을 바꾸면 열쇠가 끊기는데,
    /// 그 경우는 조용히 넘기지 않고 복원 쪽에서 경고를 남긴다.
    /// </summary>
    public static class SaveScenePath
    {
        public static string Of(Transform t)
        {
            if (t == null) return null;

            var sb = new System.Text.StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        public static string Of(Component c) => c != null ? Of(c.transform) : null;
    }
}
