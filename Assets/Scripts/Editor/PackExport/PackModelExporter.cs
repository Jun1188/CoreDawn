using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Export;
using UnityEditor;
using UnityEngine;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 5a-4c — Unity 에셋(프리팹·fbx)을 팩 파일(glb)로 굽는다. 팩은 <c>StreamingAssets/packs/&lt;pack&gt;/models/*.glb</c>를 런타임에 읽는다(PackAssets).
    /// LOD가 있으면 LOD0만, 콜라이더·게임 컴포넌트는 싣지 않는다(조립기가 붙인다).
    /// 재질은 싣지 않는다 — glb에는 재질 <b>슬롯</b>(프리미티브 → 재질 인덱스)만 남고, 로드할 때 PackAssets가 정의의 view.model[i].materials[슬롯](팩 materials 섹션)을 꽂는다.
    /// 셰이더는 내장, 값·텍스처는 팩 데이터 — 우리 셰이더(바람·그라데이션)는 glTF PBR로 표현이 안 된다.
    /// 첫 대상은 나무(Vegetation 프리팹 5종) — view.model 배열의 변형 규약을 굳히는 파일럿.
    /// </summary>
    public static class PackModelExporter
    {
        public const string ModelsFolder = "Assets/StreamingAssets/packs/coredawn/models";

        /// <summary>
        /// 원본 프리팹은 월드 미터 기준(칸 4m)으로 만들어졌다. 팩 모델은 칸 단위(1칸 = 1)로 굽는다 — 조립기(BuildingAssembler)가 맵의 cellSize를 루트에 곱한다.
        /// </summary>
        public const float SourceCellSize = 4f;

        static readonly (string asset, string file)[] Trees =
        {
            ("Assets/Prefabs/Vegetation/BroadleafTree_01_Green.prefab", "tree_broadleaf_01"),
            ("Assets/Prefabs/Vegetation/BroadleafTree_02_Green.prefab", "tree_broadleaf_02"),
            ("Assets/Prefabs/Vegetation/BroadleafTree_03_Green.prefab", "tree_broadleaf_03"),
            ("Assets/Prefabs/Vegetation/BroadleafTree_04_Green.prefab", "tree_broadleaf_04"),
            ("Assets/Prefabs/Vegetation/BroadleafTree_05_Green.prefab", "tree_broadleaf_05"),
        };

        [MenuItem("Tools/Factory/Export trees to pack glb (5a-4c pilot)")]
        public static async void ExportTreesMenu()
        {
            var log = await ExportTrees();
            Debug.Log(log);
        }

        public static async Task<string> ExportTrees()
        {
            Directory.CreateDirectory(ModelsFolder);
            var sb = new System.Text.StringBuilder("[PackModelExporter] 나무 → glb\n");
            foreach (var (asset, file) in Trees)
                sb.AppendLine("  " + await ExportOne(asset, $"{ModelsFolder}/{file}.glb"));
            AssetDatabase.Refresh();
            return sb.ToString();
        }

        /// <summary>프리팹 하나를 glb로. 반환은 한 줄 보고(성공/실패·렌더러 수).</summary>
        public static async Task<string> ExportOne(string assetPath, string outPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) return $"FAIL {assetPath}: 없음";
            var inst = Object.Instantiate(prefab);
            var temps = new List<Material>();
            inst.name = Path.GetFileNameWithoutExtension(outPath);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one / SourceCellSize;   // 칸 단위로 굽는다 — 조립기가 맵 cellSize를 곱해 되돌린다
            try
            {
                StripToLod0(inst);
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var lod in inst.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
                int renderers = inst.GetComponentsInChildren<Renderer>(true).Length;
                temps.AddRange(StripMaterialsToNames(inst));
                var export = new GameObjectExport(
                    new ExportSettings { Format = GltfFormat.Binary, ImageDestination = ImageDestination.MainBuffer, FileConflictResolution = FileConflictResolution.Overwrite },
                    new GameObjectExportSettings { OnlyActiveInHierarchy = false });
                export.AddScene(new[] { inst }, inst.name);
                bool ok = await export.SaveToFileAndDispose(outPath);
                return (ok ? "OK " : "FAIL ") + outPath + $" (renderers {renderers}, {new FileInfo(outPath).Length / 1024} KB)";
            }
            finally { Object.DestroyImmediate(inst); foreach (var m in temps) Object.DestroyImmediate(m); }
        }

        /// <summary>
        /// 재질을 빈 임시 머티리얼로 바꿔 끼운다(인스턴스에만) — glb에 텍스처·색이 실리지 않고 재질 슬롯만 남는다(이름은 사람이 읽는 용도, 바인딩은 인덱스).
        /// 로드 쪽(PackAssets.SlotGenerator)이 슬롯 인덱스로 팩 재질을 꽂는다.
        /// </summary>
        static List<Material> StripMaterialsToNames(GameObject root)
        {
            var temps = new List<Material>();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var t = new Material(lit) { name = mats[i].name };
                    temps.Add(t); mats[i] = t;
                }
                r.sharedMaterials = mats;
            }
            return temps;
        }

        /// <summary>LODGroup이 있으면 LOD0 렌더러만 남기고 나머지 LOD의 오브젝트를 지운다.</summary>
        static void StripToLod0(GameObject root)
        {
            foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                var keep = new HashSet<Renderer>();
                if (lods.Length > 0) foreach (var r in lods[0].renderers) if (r != null) keep.Add(r);
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                        if (r != null && !keep.Contains(r)) Object.DestroyImmediate(r.gameObject);
            }
        }
    }
}
