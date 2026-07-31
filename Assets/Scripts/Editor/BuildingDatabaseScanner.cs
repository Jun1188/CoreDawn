using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 데이터 레지스트리 자동 수집기 — "SO를 만들면 목록에 저절로 나타난다"의 에디터 쪽 절반.
/// 건물/아이템 SO가 임포트/삭제/이동될 때마다 해당 데이터베이스를 재수집한다.
/// 정렬: 분류(enum 순서) → displayName. 수동 연결·정리 작업 불필요.
/// </summary>
public class BuildingDatabaseScanner : AssetPostprocessor
{
    static void OnPostprocessAllAssets(
        string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        bool building = false, item = false;
        foreach (var p in imported.Concat(deleted).Concat(moved))
        {
            if (!p.EndsWith(".asset")) continue;
            var type = AssetDatabase.GetMainAssetTypeAtPath(p);
            if (type == null) { building = item = true; break; }   // 삭제된 에셋은 타입 조회 불가 — 둘 다 재수집
            if (typeof(BuildingDataSO).IsAssignableFrom(type) || type == typeof(BuildingDatabaseSO)) building = true;
            if (typeof(ItemDataSO).IsAssignableFrom(type) || type == typeof(ItemDatabaseSO)) item = true;
        }

        if (!building && !item) return;

        // 재수집은 Refresh가 끝난 뒤로 미룬다.
        // OnPostprocessAllAssets는 임포트 파이프라인 안이라, 여기서 FindAssets로 프로젝트를 훑으면
        // 같은 배치의 아직 임포트 안 된 에셋까지 강제 로드된다 → "scheduled for reimport ...
        // returning two versions of the same asset" 경고. 건물 SO는 프리팹을 참조하므로
        // Assembler.prefab / Minor.prefab 같은 프리팹이 딸려 들어온다.
        bool rebuildBuildings = building, rebuildItems = item;
        EditorApplication.delayCall += () =>
        {
            if (rebuildBuildings) RebuildBuildings();
            if (rebuildItems)     RebuildItems();
        };
    }

    [MenuItem("Tools/Factory/Rebuild Data Databases")]
    public static void RebuildAll()
    {
        RebuildBuildings();
        RebuildItems();
    }

    public static void RebuildBuildings()
    {
        var all = AssetDatabase.FindAssets("t:BuildingDataSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<BuildingDataSO>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(b => b != null)
            .OrderBy(b => (int)b.category)
            .ThenBy(b => b.displayName, System.StringComparer.Ordinal)
            .ToArray();

        foreach (var guid in AssetDatabase.FindAssets("t:BuildingDatabaseSO"))
        {
            var db = AssetDatabase.LoadAssetAtPath<BuildingDatabaseSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (db == null || (db.buildings != null && db.buildings.SequenceEqual(all))) continue;

            db.buildings = all;
            EditorUtility.SetDirty(db);
            Debug.Log($"[BuildingDatabase] '{db.name}' 재수집 — 건물 {all.Length}종", db);
        }
    }

    public static void RebuildItems()
    {
        var all = AssetDatabase.FindAssets("t:ItemDataSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<ItemDataSO>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(i => i != null)
            .OrderBy(i => (int)i.type)
            .ThenBy(i => i.displayName, System.StringComparer.Ordinal)
            .ToArray();

        foreach (var guid in AssetDatabase.FindAssets("t:ItemDatabaseSO"))
        {
            var db = AssetDatabase.LoadAssetAtPath<ItemDatabaseSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (db == null || (db.items != null && db.items.SequenceEqual(all))) continue;

            db.items = all;
            EditorUtility.SetDirty(db);
            Debug.Log($"[ItemDatabase] '{db.name}' 재수집 — 아이템 {all.Length}종", db);
        }
    }
}
