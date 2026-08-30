using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Data;
using CoreDawn.Sim;

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
        static readonly HashSet<string> _warned = new();

        /// <summary>데이터베이스 에셋이 다시 로드된 경우(에디터 재생 등) 캐시를 버린다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearCache()
        {
            _warned.Clear();
        }

        // ── 아이템 ────────────────────────────────────────────────────

        /// <summary>아이템 — 정의의 정본은 팩(json). 세이브에는 새 id(coredawn:item/…)를 쓰고, 옛 id(Item:…)도 같은 규칙으로 읽는다.</summary>
        public static ItemDef Item(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var db = SimHost.Database;
            if (db == null) { WarnOnce("__pack", "팩 정의가 로드되지 않아 아이템을 복원할 수 없습니다."); return null; }
            var def = db.Item(id);
            if (def == null) WarnOnce(id, $"세이브의 아이템 id \"{id}\"가 팩에 없습니다 — 그 항목은 건너뜁니다.");
            return def;
        }

        public static string IdOf(ItemDef item) => item != null ? item.Id : null;

        // ── 건물 ──────────────────────────────────────────────────────

        /// <summary>건물 정의 — 세이브의 id는 팩 id다. 옛 id는 SaveMigrations(v1→v2)가 이미 바꿔 놓았다 — 여기서 받아 주지 않는다.</summary>
        public static EntityDef Building(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var db = SimHost.Database;
            if (db == null) { WarnOnce("__pack", "팩 정의(SimHost.Database)가 없습니다 — 건물 복원이 전부 실패합니다."); return null; }
            var def = db.Entity(id);
            if (def == null) WarnOnce(id, $"건물 '{id}' 정의를 찾지 못했습니다 — 이 건물은 복원되지 않습니다.");
            return def;
        }

        public static string IdOf(EntityDef building) => building != null ? building.Id : null;

        // ── 레시피 ────────────────────────────────────────────────────

        public static RecipeDef Recipe(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var db = SimHost.Database;
            if (db == null) { WarnOnce("__pack", "팩 정의가 로드되지 않아 레시피를 복원할 수 없습니다."); return null; }
            var def = db.Recipe(id);
            if (def == null) WarnOnce(id, $"세이브의 레시피 id \"{id}\"가 팩에 없습니다 — 그 항목은 건너뜁니다.");
            return def;
        }

        public static string IdOf(RecipeDef recipe) => recipe != null ? recipe.Id : null;

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
