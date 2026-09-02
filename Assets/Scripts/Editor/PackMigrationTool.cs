using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 5a-4c 이관 — 뷰 카탈로그(guid)로 참조하던 모델을 팩 파일(glb)과 팩 재질(materials)로 옮긴다.
    /// 항목마다: 카탈로그 모델 에셋 → glb(PackModelExporter, 슬롯만) → glb의 재질 슬롯 이름으로 Unity 머티리얼을 찾아 v1 materials로 거두고(PackMaterialHarvester)
    /// → v1의 models[{file, materials}]를 채운다. 그다음 v2 내보내기(텍스처 복사)·카탈로그 베이크는 호출자가.
    /// 애니메이션이 있는 것(벨트·몬스터)은 glTFast가 못 굽는다 — Blender 경로.
    /// </summary>
    public static class PackMigrationTool
    {
        const string V1Path = "Assets/Data/Import/GameData.json";

        /// <summary>정적 모델 전부(타워·건물·총). 벨트·몬스터 제외.</summary>
        [MenuItem("Tools/Factory/Migrate static models to pack (5a-4c)")]
        public static async void MigrateStaticMenu() => Debug.Log(await MigrateStatic());

        public static async Task<string> MigrateStatic()
        {
            var log = new System.Text.StringBuilder("[PackMigrationTool] 정적 모델 → 팩" + System.Environment.NewLine);
            var v1 = JObject.Parse(File.ReadAllText(V1Path));
            var materials = v1["materials"] as JArray ?? (JArray)(v1["materials"] = new JArray());
            int done = 0;
            // 원본은 v1의 guid 참조(건물 modelGuid, 총 view.modelGuid) — 카탈로그는 v2 재베이크 뒤 배열 모델을 건너뛰어 믿을 수 없다
            var targets = new List<(JObject dto, string id, string guid, bool gun)>();
            foreach (var b in v1["buildings"] as JArray ?? new JArray()) if (!string.IsNullOrEmpty((string)b["modelGuid"])) targets.Add(((JObject)b, (string)b["id"], (string)b["modelGuid"], false));
            foreach (var g in v1["guns"] as JArray ?? new JArray()) if (g["view"] is JObject gv && !string.IsNullOrEmpty((string)gv["modelGuid"])) targets.Add(((JObject)g, (string)g["id"], (string)gv["modelGuid"], true));
            foreach (var (dto, id, guid, gun) in targets)
            {
                string key = GameDataExporterV2.PackIdOf(id).Split('/').Last();
                if (key == "belt" || key == "basic" || key == "spitter" || key == "boss") { log.AppendLine($"  skip {key} (애니메이션 — Blender 경로)"); continue; }
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) { log.AppendLine($"  !! {id}: modelGuid {guid}의 에셋이 없습니다"); continue; }
                string file = $"models/{key}.glb";
                string outPath = $"{PackModelExporter.ModelsFolder}/{key}.glb";
                var inst = Object.Instantiate(asset);
                var byName = new Dictionary<string, Material>();
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m != null) byName[m.name] = m;
                string report;
                try { report = await PackModelExporter.ExportObject(inst, outPath, 1f); }
                finally { Object.DestroyImmediate(inst); }
                log.AppendLine("  " + report);
                if (report.StartsWith("FAIL")) continue;

                // glb 재질 슬롯 순서 = glb materials 배열 순서. 이름으로 Unity 머티리얼을 찾아 거둔다
                var slots = new JArray();
                foreach (var slotName in PackModelExporter.MaterialNamesOf(outPath))
                {
                    if (!byName.TryGetValue(slotName, out var mat)) { log.AppendLine($"    !! 슬롯 '{slotName}'에 맞는 머티리얼이 없습니다"); slots.Add("Material:Missing"); continue; }
                    string mid = "Material:" + Pascal(mat.name);
                    var existing = materials.FirstOrDefault(x => (string)x["id"] == mid);
                    var harvested = PackMaterialHarvester.ToV1(mat, mid);
                    if (existing != null) existing.Replace(harvested); else materials.Add(harvested);
                    slots.Add(mid);
                }
                var models = new JArray { new JObject { ["file"] = file, ["materials"] = slots } };
                if (gun) ((JObject)dto["view"])["models"] = models; else dto["models"] = models;
                done++;
            }
            File.WriteAllText(V1Path, v1.ToString(Newtonsoft.Json.Formatting.Indented) + "\n");
            AssetDatabase.Refresh();
            log.AppendLine($"  {done}개 이관, v1 저장. 다음: v2 내보내기 + 카탈로그 베이크");
            return log.ToString();
        }

        static JToken FindV1(JObject v1, string packId)
        {
            foreach (var sec in new[] { "buildings", "guns", "monsters" })
                foreach (var e in v1[sec] as JArray ?? new JArray())
                    if (GameDataExporterV2.PackIdOf((string)e["id"]) == packId) return e;
            return null;
        }

        static string Pascal(string s)
        {
            var sb = new System.Text.StringBuilder(); bool up = true;
            foreach (var c in s)
            {
                if (!char.IsLetterOrDigit(c)) { up = true; continue; }
                sb.Append(up ? char.ToUpperInvariant(c) : c); up = false;
            }
            return sb.ToString();
        }
    }
}
