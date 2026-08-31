using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using CoreDawn.Data;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 뷰 카탈로그 베이커 — 팩 v2 view 블록(이름 + guid)을 읽어 <see cref="ViewCatalogSO"/>
    /// (id → 직접 에셋 참조)를 굽는다. 팩이 정본, 카탈로그는 산출물 — 런타임은 GUID 로드가
    /// 안 되므로 여기(에디터)서 참조로 바꿔 둔다. exporter가 v2를 다시 낼 때마다 같이 굽는다.
    ///
    /// 아이콘 규약: iconGuid = 스프라이트를 담은 에셋(시트)의 guid, icon = 그 안의 스프라이트 이름.
    /// 프리팹 규약: prefabGuid = 프리팹 에셋 guid(이름은 사람이 읽는 폴백 — guid가 진실).
    /// 해석 실패는 항목별로 소리 내고(경고) 그 칸만 비운다 — 카탈로그 전체를 세우진 않는다.
    /// </summary>
    public static class ViewCatalogBaker
    {
        const string PacksFolder = "Assets/StreamingAssets/packs";
        const string CatalogPath = "Assets/Resources/ViewCatalog.asset";

        [MenuItem("Tools/Factory/Bake ViewCatalog")]
        public static void BakeMenu() => Bake();

        public static ViewCatalogSO Bake()
        {
            var entries = new List<ViewCatalogSO.Entry>();
            int warnings = 0;

            foreach (var dir in Directory.GetDirectories(PacksFolder))
            {
                string jsonPath = Path.Combine(dir, "data.json").Replace('\\', '/');
                if (!File.Exists(jsonPath)) continue;

                var d = JObject.Parse(File.ReadAllText(jsonPath));
                string pack = (string)d["pack"];
                if (string.IsNullOrEmpty(pack))
                {
                    Debug.LogError($"[ViewCatalog] {jsonPath}: pack 이름이 없습니다 — 이 팩은 건너뜁니다.");
                    continue;
                }

                CollectSection(entries, d, pack, "items", "item", ref warnings);
                CollectSection(entries, d, pack, "entities", "entity", ref warnings);
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.id, b.id));   // 같은 팩 → 같은 에셋 (diff 안정)

            var so = AssetDatabase.LoadAssetAtPath<ViewCatalogSO>(CatalogPath);
            bool isNew = so == null;
            if (isNew) so = ScriptableObject.CreateInstance<ViewCatalogSO>();
            so.entries = entries;
            if (isNew)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);
                AssetDatabase.CreateAsset(so, CatalogPath);
            }
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ViewCatalog] {entries.Count}개 항목을 구웠습니다" +
                      (warnings > 0 ? $" (해석 실패 {warnings}건 — 위 경고 참조)" : "") + $" → {CatalogPath}");
            return so;
        }

        static void CollectSection(List<ViewCatalogSO.Entry> entries, JObject d, string pack,
            string section, string singular, ref int warnings)
        {
            if (d[section] is not JObject sec) return;

            foreach (var prop in sec.Properties())
            {
                if (prop.Value is not JObject o || o["view"] is not JObject view) continue;

                string id = CoreDawn.Sim.SimDatabase.IdOf(pack, singular, prop.Name);
                var e = new ViewCatalogSO.Entry { id = id };

                e.icon              = LoadSprite(view, "icon", "iconGuid", id, ref warnings);
                e.prefab            = LoadPrefab(view, "prefab", "prefabGuid", id, ref warnings);
                e.curveLPrefab      = LoadPrefab(view, "prefabCurveL", "prefabCurveLGuid", id, ref warnings);
                e.curveRPrefab      = LoadPrefab(view, "prefabCurveR", "prefabCurveRGuid", id, ref warnings);
                e.bulletPrefab      = LoadPrefab(view, "bullet", "bulletGuid", id, ref warnings);
                e.muzzleFlashPrefab = LoadPrefab(view, "muzzleFlash", "muzzleFlashGuid", id, ref warnings);
                e.hitEffectPrefab   = LoadPrefab(view, "hitEffect", "hitEffectGuid", id, ref warnings);

                // 모델(fbx)만 있고 실을 게 없는 항목(예: 광맥 — 뷰는 씬에 굽는다)은 카탈로그에 안 담는다
                if (e.icon != null || e.prefab != null || e.curveLPrefab != null || e.curveRPrefab != null ||
                    e.bulletPrefab != null || e.muzzleFlashPrefab != null || e.hitEffectPrefab != null)
                    entries.Add(e);
            }
        }

        static Sprite LoadSprite(JObject view, string nameKey, string guidKey, string id, ref int warnings)
        {
            string name = (string)view[nameKey], guid = (string)view[guidKey];
            if (string.IsNullOrEmpty(guid)) return null;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning($"[ViewCatalog] {id}: {guidKey} '{guid}' 에셋이 없습니다 ('{name}') — 아이콘을 비웁니다.");
                warnings++; return null;
            }

            // 시트면 이름으로 스프라이트를 고르고, 단일 스프라이트면 그것을 쓴다
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToList();
            var found = sprites.FirstOrDefault(s => s.name == name) ?? (sprites.Count == 1 ? sprites[0] : null);
            if (found == null)
            {
                Debug.LogWarning($"[ViewCatalog] {id}: '{path}'에서 스프라이트 '{name}'을 찾지 못했습니다 — 아이콘을 비웁니다.");
                warnings++;
            }
            return found;
        }

        static GameObject LoadPrefab(JObject view, string nameKey, string guidKey, string id, ref int warnings)
        {
            string name = (string)view[nameKey], guid = (string)view[guidKey];
            if (string.IsNullOrEmpty(guid)) return null;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[ViewCatalog] {id}: {guidKey} '{guid}' 프리팹이 없습니다 ('{name}') — 그 칸을 비웁니다.");
                warnings++;
            }
            return prefab;
        }
    }
}
