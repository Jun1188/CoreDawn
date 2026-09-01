using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CoreDawn.Data;
using CoreDawn.FPS;
using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 5a-3e-1 일회성 이관 — 씬·프리팹·맵 에셋에 직렬화된 데이터 SO 참조를 같은 자리의 팩 id 문자열로 옮긴다.
    ///
    /// 규칙: SO 참조 필드 이름 → 형제 문자열 필드 이름의 표(<see cref="TargetOf"/>). 프리팹 인스턴스에서는
    /// <b>오버라이드된</b> 참조만 옮긴다(프리팹 원본이 이미 옮겨졌으므로 상속값까지 오버라이드로 박으면 잡음).
    /// 총(Gun)은 SO가 들고 있던 뷰 값(소리·볼륨·피격 레이어)도 컴포넌트로 복사한다.
    /// SO 클래스·에셋이 지워지면 이 도구도 함께 지운다.
    /// </summary>
    public static class SoRefMigrator
    {
        static readonly Dictionary<string, string> TargetOf = new()
        {
            ["authoredItem"] = "authoredItemId", ["item"] = "itemId", ["resource"] = "resourceId", ["data"] = "dataId",
            ["coreData"] = "coreId", ["_coreData"] = "_coreId", ["bossData"] = "bossId", ["defenderMonster"] = "defenderId",
            ["unlockedRecipe"] = "unlockedRecipeId", ["alternativeFor"] = "alternativeForId",
            ["gunData"] = "gunId", ["knockbackEffect"] = "knockbackEffectId",
            ["startingWeapon"] = "startingWeaponId", ["ironOreSO"] = "ironOreId",
        };

        [MenuItem("Tools/Factory/Migrate SO refs to pack ids (5a-3e)")]
        public static void Run()
        {
            var db = SimDatabase.Load(File.ReadAllText(PackLoader.PathOf(PackLoader.DefaultPack)), PackLoader.DefaultPack);
            var report = new StringBuilder();
            int files = 0;

            // 프리팹 먼저 — 씬의 인스턴스는 원본을 상속한다
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool dirty = false;
                    foreach (var comp in root.GetComponentsInChildren<Component>(true))
                        if (comp != null && Migrate(new SerializedObject(comp), db, path + ":" + comp.GetType().Name, report, isInstance: false)) dirty = true;
                    if (dirty) { PrefabUtility.SaveAsPrefabAsset(root, path); files++; report.AppendLine("저장: " + path); }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            // 맵 에셋(광맥 자원)
            foreach (var guid in AssetDatabase.FindAssets("t:MapDataSO", new[] { "Assets/Data/Maps" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var map = AssetDatabase.LoadAssetAtPath<MapDataSO>(path);
                if (map != null && Migrate(new SerializedObject(map), db, path, report, isInstance: false)) { EditorUtility.SetDirty(map); files++; report.AppendLine("저장: " + path); }
            }

            // 공용 드롭 프리팹: ItemDatabase → ViewCatalog
            var catalog = ViewCatalogSO.LoadDefault();
            var itemDb = ItemDatabaseSO.LoadDefault();
            if (catalog != null && itemDb != null && itemDb.droppedItemPrefab != null && catalog.droppedItemPrefab == null)
            {
                catalog.droppedItemPrefab = itemDb.droppedItemPrefab.gameObject;
                EditorUtility.SetDirty(catalog); files++;
                report.AppendLine("ViewCatalog.droppedItemPrefab ← " + AssetDatabase.GetAssetPath(itemDb.droppedItemPrefab));
            }
            AssetDatabase.SaveAssets();

            // 씬 — 열려 있던 씬 구성을 기억했다가 되돌린다(미저장 작업이 있으면 먼저 저장을 묻는다)
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { Debug.LogWarning("[SoRefMigrator] 취소됨(씬 저장 거부)."); return; }
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("/Generated/")) continue;
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    bool dirty = false;
                    foreach (var rootGo in scene.GetRootGameObjects())
                        foreach (var comp in rootGo.GetComponentsInChildren<Component>(true))
                        {
                            if (comp == null) continue;
                            bool inst = PrefabUtility.IsPartOfPrefabInstance(comp);
                            if (Migrate(new SerializedObject(comp), db, path + ":" + comp.GetType().Name, report, inst)) dirty = true;
                        }
                    if (dirty) { EditorSceneManager.SaveScene(scene); files++; report.AppendLine("저장: " + path); }
                }
            }
            finally
            {
                if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            Debug.Log($"[SoRefMigrator] 파일 {files}개 갱신\n{report}");
        }

        /// <summary>한 오브젝트의 직렬화 트리를 훑어 SO 참조 → 형제 id 문자열. 바뀐 게 있으면 true.</summary>
        static bool Migrate(SerializedObject so, SimDatabase db, string where, StringBuilder report, bool isInstance)
        {
            bool changed = false;
            var it = so.GetIterator();
            bool enter = true;
            while (it.Next(enter))
            {
                enter = it.propertyType != SerializedPropertyType.String;   // 문자열 안으로는 안 들어간다
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (!(it.objectReferenceValue is GameDataSO gd)) continue;
                if (isInstance && !it.prefabOverride) continue;

                if (!TargetOf.TryGetValue(it.name, out var targetName))
                {
                    report.AppendLine($"  건너뜀(표에 없음): {where}.{it.propertyPath} = {gd.name}");
                    continue;
                }
                string siblingPath = it.propertyPath.Substring(0, it.propertyPath.Length - it.name.Length) + targetName;
                var sibling = so.FindProperty(siblingPath);
                if (sibling == null || sibling.propertyType != SerializedPropertyType.String)
                {
                    report.AppendLine($"  실패(형제 필드 없음): {where}.{siblingPath}");
                    continue;
                }
                string id = db.LegacyId(gd.Id);
                if (!Exists(db, id))
                {
                    Debug.LogError($"[SoRefMigrator] {where}.{it.propertyPath}: '{gd.Id}' → '{id}'가 팩에 없습니다 — 옮기지 않았습니다.");
                    continue;
                }
                if (sibling.stringValue != id) { sibling.stringValue = id; changed = true; }

                if (gd is GunData gun && it.name == "gunData")
                {
                    Set(so, "fireSound", p => p.objectReferenceValue = gun.fireSound);
                    Set(so, "reloadSound", p => p.objectReferenceValue = gun.reloadSound);
                    Set(so, "fireVolume", p => p.floatValue = gun.fireVolume);
                    Set(so, "reloadVolume", p => p.floatValue = gun.reloadVolume);
                    Set(so, "enemyLayer", p => p.intValue = gun.enemyLayer.value);
                    changed = true;
                }
            }
            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                if (isInstance && so.targetObject is Component c) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            }
            return changed;
        }

        static void Set(SerializedObject so, string name, System.Action<SerializedProperty> apply)
        {
            var p = so.FindProperty(name);
            if (p != null) apply(p);
        }

        static bool Exists(SimDatabase db, string id)
        {
            if (id.Contains(":item/")) return db.Item(id) != null;
            if (id.Contains(":entity/")) return db.Entity(id) != null;
            if (id.Contains(":recipe/")) return db.Recipe(id) != null;
            if (id.Contains(":gun/")) return db.Gun(id) != null;
            if (id.Contains(":effect/")) return db.Effect(id) != null;
            return false;
        }
    }
}
