using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.Managers
{
    /// <summary>
    /// 팩 파일 자원(5a-4c) — <c>StreamingAssets/packs/&lt;pack&gt;/</c>의 모델(glb)·텍스처(png)를 런타임에 읽는다.
    /// <list type="bullet">
    /// <item>모델: glb를 glTFast로 읽어 비활성 템플릿으로 들고, 조립기가 복제한다. glb에는 형상과 재질 <b>슬롯</b>(프리미티브가 가리키는 재질 인덱스)만 있다 —
    /// 로드 때 슬롯마다 자리표시 머티리얼을 만들어 인덱스를 기억하고(<see cref="BindSlots"/>), 정의의 <c>view.model[i].materials[슬롯]</c>이 가리키는 팩 재질을 꽂는다.</item>
    /// <item>재질: 팩 <c>materials</c> 섹션(<see cref="MaterialDef"/>) — 셰이더는 내장(이름으로 찾음), 값(색·float·키워드)과 텍스처(팩 png, <c>LoadImage</c>)는 데이터.</item>
    /// </list>
    /// 로드는 비동기다. 게임 부팅(GameBootstrap)이 <see cref="PreloadAsync"/>를 시작하고, 굳은 씬의 마커는 preload가 끝난 뒤 뷰를 입는다(WorldPopulator.DressWhenReady).
    /// 굳지 않은 경로(런타임 나무 조립·건물 설치)는 <see cref="IsReady"/>를 보고 준비 전이면 소리 낸다(폴백 없음) — 로딩 화면 게이트는 4c 후속.
    /// </summary>
    public static class PackAssets
    {
        static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        static readonly Dictionary<Material, int> slotIndex = new Dictionary<Material, int>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        static Transform root;
        static Task preloading;
        static Material missing;

        public static bool IsReady { get; private set; }

        /// <summary>preload 진행도(읽은 파일 수 / 전체) — 로딩 화면용.</summary>
        public static (int done, int total) Progress { get; private set; }

        /// <summary>팩 상대 경로("models/tree_broadleaf_01.glb")인가 — 아니면 옛 guid 참조.</summary>
        public static bool IsPackPath(string s) => !string.IsNullOrEmpty(s) && s.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

        public static string FullPath(string pack, string relative) => Path.Combine(Application.streamingAssetsPath, "packs", pack, relative.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>정의들의 view.model에 적힌 glb와 그 재질을 전부 읽어 둔다. 두 번 불러도 한 번만.</summary>
        public static Task PreloadAsync(SimDatabase db)
        {
            if (preloading == null) preloading = PreloadCore(db);
            return preloading;
        }

        static async Task PreloadCore(SimDatabase db)
        {
            if (db == null) { Debug.LogError("[PackAssets] 팩 정의가 없어 자원을 읽을 수 없습니다."); IsReady = true; return; }
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var materialIds = new HashSet<string>(StringComparer.Ordinal);
            void Collect(Def def)
            {
                // 검증(ViewSchema.Of)은 쓰는 쪽 몫 — 여기서는 경로만 모은다(아이템 view는 type이 없어 검증이 소리 낸다)
                if (def.View == null) return;
                foreach (var key in new[] { "model", "modelCurveL", "modelCurveR" })
                    foreach (var m in ViewSpec.ModelsOf(def.View, key))
                    {
                        if (!m.IsPack) continue;
                        paths.Add(m.File);
                        foreach (var id in m.Materials) materialIds.Add(id);
                    }
            }
            foreach (var d in db.Entities.Values) Collect(d);
            foreach (var d in db.Guns.Values) Collect(d);
            foreach (var d in db.Items.Values) Collect(d);

            int ok = 0, done = 0;
            Progress = (0, paths.Count + materialIds.Count);
            foreach (var rel in paths)
            {
                if (await Load(db.Pack, rel) != null) ok++;
                Progress = (++done, Progress.total);
            }
            int mats = 0;
            foreach (var id in materialIds)
            {
                if (MaterialOf(id) != null) mats++;
                Progress = (++done, Progress.total);
            }
            IsReady = true;
            Debug.Log($"[PackAssets] {db.Pack}: glb {ok}/{paths.Count} · 재질 {mats}/{materialIds.Count} 로드");
        }

        static async Task<GameObject> Load(string pack, string relative)
        {
            if (models.TryGetValue(relative, out var cached)) return cached;
            string full = FullPath(pack, relative);
            if (!File.Exists(full)) { Debug.LogError($"[PackAssets] 팩 파일이 없습니다: {relative} ({full})"); return null; }
            var gltf = new GltfImport(deferAgent: new UninterruptedDeferAgent(), materialGenerator: new SlotGenerator());
            bool loaded = await gltf.LoadFile(full);
            if (!loaded) { Debug.LogError($"[PackAssets] glb를 읽지 못했습니다: {relative}"); return null; }
            EnsureRoot();
            var holder = new GameObject(Path.GetFileNameWithoutExtension(relative));
            holder.transform.SetParent(root, false);
            bool inst = await gltf.InstantiateMainSceneAsync(holder.transform);
            if (!inst) { Debug.LogError($"[PackAssets] glb 장면을 세우지 못했습니다: {relative}"); Destroy(holder); return null; }
            holder.SetActive(false);   // 템플릿 — 조립기가 Instantiate로 복제한다
            models[relative] = holder;
            return holder;
        }

        /// <summary>읽어 둔 템플릿. 없으면 오류 로그 + null — 호출부는 자리표시로 넘어간다.</summary>
        public static GameObject ModelOf(string relative)
        {
            if (models.TryGetValue(relative, out var m) && m != null) return m;
            Debug.LogError($"[PackAssets] 모델 '{relative}'이 로드돼 있지 않습니다 — 부팅 preload 목록(view.model)에 있는지, 파일이 있는지 확인하세요.");
            return null;
        }

        /// <summary>
        /// 복제한 모델의 재질 슬롯(glb 재질 인덱스)에 팩 재질을 꽂는다 — <paramref name="materialIds"/>[슬롯]. 모자라면 오류 + 자홍 자리표시가 남는다.
        /// 옛 카탈로그 모델(슬롯 자리표시가 없음)은 그대로 둔다.
        /// </summary>
        public static void BindSlots(GameObject inst, IReadOnlyList<string> materialIds, string owner)
        {
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !slotIndex.TryGetValue(mats[i], out int slot)) continue;
                    if (slot < 0 || slot >= materialIds.Count)
                    {
                        Debug.LogError($"[PackAssets] {owner}: 재질 슬롯 {slot}에 대응하는 view.model.materials 항목이 없습니다(항목 {materialIds.Count}개).", inst);
                        continue;
                    }
                    var m = MaterialOf(materialIds[slot]);
                    if (m == null) continue;
                    mats[i] = m; changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>팩 재질(materials 섹션) → Unity Material. 셰이더는 내장 이름으로 찾고, 텍스처는 팩 png. 없거나 틀리면 오류 + null.</summary>
        public static Material MaterialOf(string id)
        {
            if (materials.TryGetValue(id, out var cached) && cached != null) return cached;
            var db = SimHost.Database;
            var def = db?.Material(id);
            if (def == null) { Debug.LogError($"[PackAssets] 재질 '{id}'가 팩 materials에 없습니다."); return null; }
            var v = def.View;
            string shaderName = (string)v?["shader"];
            var shader = string.IsNullOrEmpty(shaderName) ? null : Shader.Find(shaderName);
            if (shader == null) { Debug.LogError($"[PackAssets] 재질 '{id}': 셰이더 '{shaderName}'를 찾지 못했습니다(내장 셰이더 이름이어야 하고, 빌드에 포함돼 있어야 합니다)."); return null; }

            var m = new Material(shader) { name = id };
            if (v["textures"] is JObject texs)
                foreach (var p in texs.Properties())
                {
                    var t = TextureOf(db.Pack, (string)p.Value["file"], (bool?)p.Value["linear"] ?? false);
                    if (t != null) m.SetTexture(p.Name, t);
                }
            if (v["colors"] is JObject cols)
                foreach (var p in cols.Properties()) { var a = (JArray)p.Value; m.SetColor(p.Name, new Color((float)a[0], (float)a[1], (float)a[2], (float)a[3])); }
            if (v["vectors"] is JObject vecs)
                foreach (var p in vecs.Properties()) { var a = (JArray)p.Value; m.SetVector(p.Name, new Vector4((float)a[0], (float)a[1], (float)a[2], (float)a[3])); }
            if (v["floats"] is JObject fls)
                foreach (var p in fls.Properties()) m.SetFloat(p.Name, (float)p.Value);
            if (v["keywords"] is JArray kws)
                foreach (var k in kws) m.EnableKeyword((string)k);
            if (v["renderQueue"] != null) m.renderQueue = (int)v["renderQueue"];
            if (v["tags"] is JObject tags)
                foreach (var p in tags.Properties()) m.SetOverrideTag(p.Name, (string)p.Value);
            materials[id] = m;
            return m;
        }

        /// <summary>팩 png → Texture2D(밉맵, 런타임 DXT 압축). 노멀맵·마스크는 linear.</summary>
        public static Texture2D TextureOf(string pack, string relative, bool linear)
        {
            string key = relative + (linear ? "|linear" : "|srgb");
            if (textures.TryGetValue(key, out var cached) && cached != null) return cached;
            if (string.IsNullOrEmpty(relative)) { Debug.LogError("[PackAssets] 텍스처 경로가 비었습니다."); return null; }
            string full = FullPath(pack, relative);
            if (!File.Exists(full)) { Debug.LogError($"[PackAssets] 텍스처 파일이 없습니다: {relative} ({full})"); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = relative, wrapMode = TextureWrapMode.Repeat };
            if (!tex.LoadImage(File.ReadAllBytes(full))) { Debug.LogError($"[PackAssets] 텍스처를 읽지 못했습니다: {relative}"); Destroy(tex); return null; }
            if (tex.width % 4 == 0 && tex.height % 4 == 0) tex.Compress(true);   // DXT는 4의 배수 크기만 — 아니면 비압축(RGBA32)으로 든다
            else Debug.LogWarning($"[PackAssets] 텍스처 '{relative}'({tex.width}x{tex.height})는 4의 배수 크기가 아니라 압축하지 못합니다 — 메모리를 4배 더 씁니다. 팩 텍스처는 4의 배수로 만드세요.");
            tex.Apply(false, true);
            textures[key] = tex;
            return tex;
        }

        static void EnsureRoot()
        {
            if (root != null) return;
            var go = new GameObject("[PackAssets]");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(go);
            else go.hideFlags = HideFlags.HideAndDontSave;   // 에디터 도구가 읽을 때 — 씬에 저장되지 않는다
            root = go.transform;
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { models.Clear(); slotIndex.Clear(); materials.Clear(); textures.Clear(); root = null; preloading = null; missing = null; IsReady = false; Progress = (0, 0); }

        /// <summary>에디터 도구용 — 읽어 둔 것을 전부 버린다(팩이 바뀌었을 때).</summary>
        public static void Clear()
        {
            if (root != null) Destroy(root.gameObject);
            foreach (var m in slotIndex.Keys) Destroy(m);
            foreach (var m in materials.Values) Destroy(m);
            foreach (var t in textures.Values) Destroy(t);
            Destroy(missing);
            Reset();
        }

        static Material Missing()
        {
            if (missing == null) { missing = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "PackMaterialMissing" }; missing.SetColor("_BaseColor", Color.magenta); }
            return missing;
        }

        /// <summary>
        /// glb 재질 슬롯 → 자리표시 머티리얼(자홍). 슬롯 인덱스(glb 재질 배열 순서)를 기억해 두면 <see cref="BindSlots"/>가 정의의 materials[슬롯]으로 바꿔 끼운다.
        /// glb의 재질 이름·값은 보지 않는다 — 재질은 팩 데이터(materials 섹션)다.
        /// </summary>
        sealed class SlotGenerator : GLTFast.Materials.IMaterialGenerator
        {
            public Material GetDefaultMaterial(bool pointsSupport = false)
            {
                Debug.LogError("[PackAssets] 재질 슬롯이 없는 프리미티브 — glb의 프리미티브마다 재질 인덱스가 있어야 합니다.");
                return Missing();
            }

            public Material GenerateMaterial(GLTFast.Schema.MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false)
            {
                int slot = -1;
                for (int i = 0; i < gltf.MaterialCount; i++)
                    if (ReferenceEquals(gltf.GetSourceMaterial(i), gltfMaterial)) { slot = i; break; }
                var placeholder = new Material(Missing()) { name = "slot:" + slot };
                slotIndex[placeholder] = slot;
                return placeholder;
            }

            public void SetLogger(GLTFast.Logging.ICodeLogger logger) { }
        }
    }
}
