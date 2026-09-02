using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using CoreDawn.Data;
using CoreDawn.Managers;
using CoreDawn.Sim;
using CoreDawn.Worlds;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 월드 미리보기(5a-4c·4e) — 맵의 배치물(나무·광맥·둥지·코어)과 <b>지형(지면·물·절벽)</b>을 씬 뷰에
    /// GameObject 없이 그린다(<see cref="Graphics.DrawMesh"/>). 씬에는 아무것도 굳히지 않는다 —
    /// 배치·지형의 정본은 맵이고 모양은 팩·생성기라, 읽어서 그리기만 한다. 재생 중엔 그리지 않는다(실물이 있다).
    /// 지형은 배치물과 같은 방식이다: 런타임 빌더를 미리보기 모드로 잠시 세워 렌더러의 메시·재질·행렬만
    /// 수확하고 바로 부순다(절벽 수천 조각은 재질별로 결합해 드로우 몇 번으로). 풀만 생략한다.
    /// 팩 자원은 처음 필요할 때 에디트 모드에서 읽는다(비동기) — 진행은 에디터 우하단 백그라운드 태스크
    /// (<see cref="Progress"/>)로 보인다. 맵을 다시 내보내면 <see cref="Invalidate"/>.
    /// </summary>
    [InitializeOnLoad]
    public static class WorldPreviewDrawer
    {
        sealed class Part { public Mesh mesh; public Material[] mats; public Matrix4x4 local; }

        static readonly Dictionary<string, List<Part>> parts = new Dictionary<string, List<Part>>();
        static List<(string key, Matrix4x4 m)> placements;
        static World cachedWorld;
        static MapDef cachedMap;
        static bool loading;
        static int progressId = -1;   // 에디터 우하단 백그라운드 태스크 표시(Progress API)
        static bool enabled = true;

        sealed class TerrainPart { public Mesh mesh; public Material mat; public Matrix4x4 m; public int sub; }

        static readonly List<TerrainPart> terrainParts = new List<TerrainPart>();
        static readonly List<Mesh> ownedMeshes = new List<Mesh>();   // 청크·물·결합 절벽 메시 — Invalidate 때 부순다

        const string MenuToggle = "Tools/CoreDawn/World preview (scene view)";

        static WorldPreviewDrawer()
        {
            // 그리기는 카메라가 그리기 직전(beginCameraRendering)에 — SceneView OnGUI(Repaint)에서 DrawMesh를 부르면 "다음 렌더"에 큐잉돼
            // 리페인트마다 한 번 걸러 보여 깜빡인다. 팩 로드 트리거·재빌드 판단도 여기서 한다.
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;   // 팩 로드·재빌드(템플릿 생성/파괴)는 렌더 콜백 밖에서
            EditorApplication.playModeStateChanged += _ => Invalidate();
            // World가 죽었을 때만 다시 찾는다. 무조건 비우면 우리가 만든 임시 오브젝트(지형 수확·팩 템플릿)의
            // 생성·파괴가 hierarchyChanged를 울려 "캐시 무효 → 재빌드(수확 2초+) → 또 무효"의 되먹임이 된다
            // (미리보기가 안 보이고 에디터가 멈추던 원인).
            EditorApplication.hierarchyChanged += () => { if (foundWorld == null) cachedWorld = null; };
            enabled = EditorPrefs.GetBool(MenuToggle, true);
        }

        [MenuItem(MenuToggle)]
        static void Toggle() { enabled = !enabled; EditorPrefs.SetBool(MenuToggle, enabled); Menu.SetChecked(MenuToggle, enabled); SceneView.RepaintAll(); }
        [MenuItem(MenuToggle, true)]
        static bool ToggleValidate() { Menu.SetChecked(MenuToggle, enabled); return true; }

        [MenuItem("Tools/CoreDawn/World preview — reload pack")]
        public static void ReloadPack() { PackAssets.Clear(); PackMaps.Clear(); SimHost.Database = null; Invalidate(); SceneView.RepaintAll(); }

        /// <summary>맵·팩이 바뀌었다 — 다음 그리기 때 다시 읽는다.</summary>
        public static void Invalidate()
        {
            placements = null; cachedWorld = null; cachedMap = null;
            foreach (var list in parts.Values) list.Clear();
            parts.Clear();
            terrainParts.Clear();
            foreach (var m in ownedMeshes) if (m != null) Object.DestroyImmediate(m);
            ownedMeshes.Clear();
        }

        static World foundWorld;

        static void Tick()
        {
            if (!enabled || Application.isPlaying) return;
            if (foundWorld == null || cachedWorld == null) foundWorld = Object.FindFirstObjectByType<World>();
            var world = foundWorld;
            if (world == null || world.Map == null) return;
            if (SimHost.Database == null) SimHost.DatabaseLoader = () => PackLoader.Load();
            if (!PackAssets.IsReady) { EnsureLoading(); ReportProgress(); return; }
            FinishProgress();
            if (placements == null || cachedWorld != world || cachedMap != world.Map) { Rebuild(world); SceneView.RepaintAll(); }
        }

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!enabled || Application.isPlaying || cam.cameraType != CameraType.SceneView) return;
            if (placements == null || cachedWorld == null || cachedWorld.Map != cachedMap) return;

            foreach (var p in terrainParts)
                Graphics.DrawMesh(p.mesh, p.m, p.mat, 0, cam, p.sub);

            foreach (var (key, m) in placements)
            {
                if (!parts.TryGetValue(key, out var list)) continue;
                foreach (var part in list)
                    for (int i = 0; i < part.mats.Length; i++)
                        Graphics.DrawMesh(part.mesh, m * part.local, part.mats[i], 0, cam, i);
            }
        }

        static void EnsureLoading()
        {
            if (loading) return;
            loading = true;
            var db = SimHost.Database;
            if (db == null) { loading = false; return; }
            if (progressId < 0 || !Progress.Exists(progressId))
                progressId = Progress.Start("CoreDawn 팩 로드", PackLoader.CurrentPack, Progress.Options.None);
            var ctx = SynchronizationContext.Current;
            PackAssets.PreloadAsync(db).ContinueWith(_ => { loading = false; FinishProgress(); SceneView.RepaintAll(); },
                ctx != null ? TaskScheduler.FromCurrentSynchronizationContext() : TaskScheduler.Default);
        }

        static void ReportProgress()
        {
            if (progressId < 0 || !Progress.Exists(progressId)) return;
            var (done, total) = PackAssets.Progress;
            Progress.Report(progressId, total > 0 ? (float)done / total : 0f, $"{done}/{total}");
        }

        static void FinishProgress()
        {
            if (progressId < 0) return;
            if (Progress.Exists(progressId)) Progress.Finish(progressId, Progress.Status.Succeeded);
            progressId = -1;
        }


        static void Rebuild(World world)
        {
            // 지형 수확은 비싸다(~2초) — 맵이 바뀌었거나 아직 없을 때만. 배치물 재빌드와 분리.
            bool terrainStale = cachedMap != world.Map || (terrainParts.Count == 0 && ownedMeshes.Count == 0);

            cachedWorld = world; cachedMap = world.Map;
            placements = new List<(string, Matrix4x4)>();
            var map = world.Map; float cell = world.CellSize;
            var db = SimHost.Database;

            if (terrainStale) BuildTerrainParts(world);

            EntityDef Find(string key) => db.Entities.TryGetValue($"{db.Pack}:entity/{key}", out var d) ? d : null;

            // 나무 — 런타임과 같은 결정적 배치(변형·각도·크기)
            var tree = Find(WorldPopulator.TreeEntityKey);
            if (tree != null && map.trees != null)
            {
                int variants = Mathf.Max(1, ViewSchema.Of(tree).Models().Count);
                foreach (var c in map.trees)
                {
                    WorldPopulator.TreePose(world, c, variants, out int pi, out Vector3 pos, out float yaw, out float scale);
                    placements.Add((Key(tree, pi), Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one * (cell * scale))));
                }
            }
            // 코어(3×3 — 원점 칸에서 1.5칸이 가운데)
            EntityDef core = null;
            foreach (var e in db.Entities.Values) if (e.Has<CoreModuleDef>()) { core = e; break; }
            if (core != null)
            {
                Vector3 pos = world.CellToWorld(map.core) + new Vector3(1.5f, 0f, 1.5f) * cell;
                pos.y = WorldPopulator.GroundYAt(world, pos);
                placements.Add((Key(core, 0), Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * cell)));
            }
            // 광맥 — 아이템별 정의(entities/<item>_deposit), 칸 중앙
            if (map.nodes != null)
                foreach (var n in map.nodes)
                {
                    if (string.IsNullOrEmpty(n.itemId)) continue;
                    var def = Find(n.itemId.Substring(n.itemId.LastIndexOf('/') + 1) + "_deposit");
                    if (def == null) continue;
                    placements.Add((Key(def, 0), Matrix4x4.TRS(world.CellToWorld(n.cell) + new Vector3(0.5f, 0f, 0.5f) * cell, Quaternion.identity, Vector3.one * cell)));
                }
            // 둥지
            EntityDef nest = null;
            foreach (var e in db.Entities.Values) if (e.Has<NestModuleDef>()) { nest = e; break; }
            if (nest != null && map.nests != null)
                foreach (var spec in map.nests)
                    placements.Add((Key(nest, 0), Matrix4x4.TRS(world.CellToWorldCenter(spec.cell), Quaternion.identity, Vector3.one * cell)));
        }

        /// <summary>
        /// 지형 그리기 재료 — 배치물(Key)과 같은 문법이되 <b>GameObject를 하나도 만들지 않는다</b>:
        /// 빌더의 순수 데이터(<see cref="WorldTerrainBuilder.BuildPreviewData"/> — 청크·물 메시, 절벽 배치 계획)를
        /// 그대로 그린다. 절벽은 프리팹 <b>에셋</b>의 렌더러를 읽어(인스턴스화 없이) 배치 행렬과 곱해
        /// 재질별로 결합한다 — 수천 조각이 드로우 몇 번이 된다. 즉석 메시는 우리 소유(Invalidate 때 파괴).
        /// 씬에 실물 지형(런타임/미리보기 메뉴)이 있으면 그리지 않는다.
        /// </summary>
        static void BuildTerrainParts(World world)
        {
            terrainParts.Clear();
            foreach (var m in ownedMeshes) if (m != null) Object.DestroyImmediate(m);
            ownedMeshes.Clear();

            if (world.transform.Find(WorldTerrainBuilder.RootName) != null) return;

            var data = WorldTerrainBuilder.BuildPreviewData(world);
            if (data == null) return;
            Vector3 origin = world.Origin;

            foreach (var (mesh, localPos, scale) in data.ground)
            {
                if (!ownedMeshes.Contains(mesh)) ownedMeshes.Add(mesh);   // 공유 쿼드는 한 번만
                terrainParts.Add(new TerrainPart { mesh = mesh, mat = data.groundMat, m = Matrix4x4.TRS(origin + localPos, Quaternion.identity, scale), sub = 0 });
            }
            if (data.water != null)
            {
                ownedMeshes.Add(data.water);
                terrainParts.Add(new TerrainPart { mesh = data.water, mat = data.waterMat, m = Matrix4x4.Translate(origin + data.waterPos), sub = 0 });
            }

            // 절벽 — 프리팹 에셋의 (메시, 재질, 루트 로컬 행렬) 조각을 한 번만 뽑아 두고,
            // 배치 계획의 행렬과 곱해 재질별 CombineInstance로 쌓는다.
            var prefabParts = new Dictionary<GameObject, List<(Mesh mesh, Material mat, int sub, Matrix4x4 local)>>();
            var combine = new Dictionary<Material, List<CombineInstance>>();
            foreach (var p in data.cliffs)
            {
                if (p.prefab == null) continue;
                if (!prefabParts.TryGetValue(p.prefab, out var pieces))
                {
                    pieces = new List<(Mesh, Material, int, Matrix4x4)>();
                    var rootInv = p.prefab.transform.worldToLocalMatrix;
                    foreach (var r in p.prefab.GetComponentsInChildren<MeshRenderer>())
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        var mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length && i < mf.sharedMesh.subMeshCount; i++)
                            if (mats[i] != null)
                                pieces.Add((mf.sharedMesh, mats[i], i, rootInv * r.transform.localToWorldMatrix));
                    }
                    prefabParts[p.prefab] = pieces;
                }

                var world2Root = Matrix4x4.TRS(p.pos, p.rot, Vector3.one * p.scale);
                foreach (var piece in pieces)
                {
                    if (!combine.TryGetValue(piece.mat, out var list)) combine[piece.mat] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = piece.mesh, subMeshIndex = piece.sub, transform = world2Root * piece.local });
                }
            }
            foreach (var kv in combine)
            {
                var merged = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                merged.CombineMeshes(kv.Value.ToArray(), true, true);
                ownedMeshes.Add(merged);
                terrainParts.Add(new TerrainPart { mesh = merged, mat = kv.Key, m = Matrix4x4.identity, sub = 0 });
            }
        }

        /// <summary>정의(+변형)의 그릴 조각들 — 팩 템플릿을 잠시 세워 렌더러의 메시·재질·루트 기준 행렬을 뽑는다(칸 단위, 루트 배율 1).</summary>
        static string Key(EntityDef def, int variant)
        {
            string key = def.Id + "#" + variant;
            if (parts.ContainsKey(key)) return key;
            var list = new List<Part>();
            parts[key] = list;
            var view = ViewSchema.Of(def);
            var refs = view.Models();
            if (refs.Count == 0) return key;
            var chosen = refs[((variant % refs.Count) + refs.Count) % refs.Count];
            if (!chosen.IsPack) return key;
            var tpl = PackAssets.ModelOf(chosen.File);
            if (tpl == null) return key;

            var root = new GameObject("__preview") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var inst = Object.Instantiate(tpl, root.transform);
                inst.SetActive(true);
                PackAssets.BindSlots(inst, chosen.Materials, def.Id);
                var (pos, rot, scale) = view.PoseFor(BeltShape.Straight);
                inst.transform.localPosition = pos; inst.transform.localRotation = rot; inst.transform.localScale = Vector3.one * scale;
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    list.Add(new Part { mesh = mesh, mats = r.sharedMaterials, local = root.transform.worldToLocalMatrix * r.transform.localToWorldMatrix });
                }
            }
            finally { Object.DestroyImmediate(root); }
            return key;
        }
    }
}
