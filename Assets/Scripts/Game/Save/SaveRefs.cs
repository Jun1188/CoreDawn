using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Sim;

namespace CoreDawn.Save
{
    /// <summary>
    /// 팩 id(<c>coredawn:item/iron_ore</c>)를 정의로 되돌린다 — 세이브 데이터와 씬·프리팹 저작 필드가 공용으로 쓴다.
    ///
    /// Unity는 정의(plain C#)를 직렬화하지 않으므로 세이브도 인스펙터도 문자열 id를 적고, 읽을 때 여기서 해석한다.
    /// 해석 실패(팩에서 지워졌거나 id가 바뀐 경우)는 조용히 넘기지 않고 한 번씩 경고를 남긴다 —
    /// 아이템이 슬그머니 사라지는 것보다 로그에 남는 편이 낫다.
    /// </summary>
    public static class SaveRefs
    {
        static readonly HashSet<string> _warned = new();

        /// <summary>플레이가 다시 시작되면(도메인 리로드 없음) 경고 기록을 비운다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearCache()
        {
            _warned.Clear();
        }

        // ── 아이템 ────────────────────────────────────────────────────

        /// <summary>아이템 — 정의의 정본은 팩(json). 옛 SO id(Item:…)는 받지 않는다 — 세이브는 SaveMigrations가, 씬은 SoRefMigrator가 이미 바꿨다.</summary>
        public static ItemDef Item(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var db = SimHost.Database;
            if (db == null) { WarnOnce("__pack", "팩 정의가 로드되지 않아 아이템을 복원할 수 없습니다."); return null; }
            var def = db.Item(id);
            if (def == null) WarnOnce(id, $"아이템 id \"{id}\"가 팩에 없습니다 — 그 항목은 건너뜁니다.");
            return def;
        }

        public static string IdOf(ItemDef item) => item != null ? item.Id : null;

        // ── 엔티티(건물·몬스터·광맥) ────────────────────────────────

        /// <summary>엔티티 정의 — 건물·몬스터·광맥이 한 섹션(entities)이다.</summary>
        public static EntityDef Entity(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var db = SimHost.Database;
            if (db == null) { WarnOnce("__pack", "팩 정의(SimHost.Database)가 없습니다 — 엔티티 복원이 전부 실패합니다."); return null; }
            var def = db.Entity(id);
            if (def == null) WarnOnce(id, $"엔티티 '{id}' 정의를 찾지 못했습니다 — 이 항목은 복원되지 않습니다.");
            return def;
        }

        // ── 총·효과 ───────────────────────────────────────────────

        public static GunDef Gun(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var db = SimHost.Database;
            if (db == null) { WarnOnce("__pack", "팩 정의가 로드되지 않아 총을 해석할 수 없습니다."); return null; }
            var def = db.Gun(id);
            if (def == null) WarnOnce(id, $"총 id \"{id}\"가 팩에 없습니다.");
            return def;
        }

        public static EffectSpec Effect(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var db = SimHost.Database;
            if (db == null) { WarnOnce("__pack", "팩 정의가 로드되지 않아 효과를 해석할 수 없습니다."); return null; }
            var def = db.Effect(id);
            if (def == null) WarnOnce(id, $"효과 id \"{id}\"가 팩에 없습니다.");
            return def;
        }

        // ── 건물 ──────────────────────────────────────────────────────

        /// <summary>건물 정의 — <see cref="Entity"/>와 같다(건물은 엔티티 섹션의 Building 모듈 조합). 세이브 코드의 읽기 편의용 이름.</summary>
        public static EntityDef Building(string id) => Entity(id);

        public static string IdOf(EntityDef building) => building != null ? building.Id : null;

        // ── 레시피 ────────────────────────────────────────────────────

        public static RecipeDef Recipe(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var db = SimHost.Database;
            if (db == null) { WarnOnce("__pack", "팩 정의가 로드되지 않아 레시피를 복원할 수 없습니다."); return null; }
            var def = db.Recipe(id);
            if (def == null) WarnOnce(id, $"레시피 id \"{id}\"가 팩에 없습니다 — 그 항목은 건너뜁니다.");
            return def;
        }

        public static string IdOf(RecipeDef recipe) => recipe != null ? recipe.Id : null;

        // ── 공통 ──────────────────────────────────────────────────────

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
