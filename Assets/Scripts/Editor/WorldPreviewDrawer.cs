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
    /// 월드 미리보기(5a-4c) — 맵의 배치물(나무·광맥·둥지·코어)과 <b>타일(강·절벽)</b>을 씬 뷰에
    /// GameObject 없이 그린다(<see cref="Graphics.DrawMesh"/>). 씬에는 아무것도 굳히지 않는다 —
    /// 배치의 정본은 맵이고 모양은 팩(PackAssets)이라, 둘을 읽어 그리기만 한다. 재생 중엔 그리지 않는다(실물이 있다).
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

        static Mesh tileMesh;         // 맵 오버레이 메시 — 지면 판 + 물 칸 + 수직 절벽 기둥(전부 반투명)
        static Material tileMat;      // Sprites/Default(정점색 반투명) — 에디터 전용이라 빌드 포함을 고민할 필요 없음

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
            EditorApplication.hierarchyChanged += () => { cachedWorld = null; };   // World가 바뀌었을 수 있다 — 다음 프레임에 다시 찾는다
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
            if (tileMesh != null) { Object.DestroyImmediate(tileMesh); tileMesh = null; }
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

            if (tileMesh != null) Graphics.DrawMesh(tileMesh, Matrix4x4.identity, TileMat, 0, cam);

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

        static Material TileMat
        {
            get
            {
                if (tileMat == null)
                    tileMat = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
                return tileMat;
            }
        }


        static void Rebuild(World world)
        {
            cachedWorld = world; cachedMap = world.Map;
            placements = new List<(string, Matrix4x4)>();
            var map = world.Map; float cell = world.CellSize;
            var db = SimHost.Database;

            BuildTileMesh(world, map, cell);

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
        /// 맵 오버레이 메시(반투명 하나) — 지면 한 판(초록) + 물 칸(파랑) + <b>수직으로만 솟은</b> 절벽 기둥(회색,
        /// 윗면과 노출된 옆면만). 맵(정본)의 땅 모양이 지형을 굽기 전에도 씬 뷰에서 그대로 읽힌다.
        /// 높이는 평지 기준(y0 + 살짝) — 구운 지형이 있어도 그 위에 은은하게 얹힌다.
        /// </summary>
        static void BuildTileMesh(World world, MapDef map, float cell)
        {
            if (tileMesh != null) { Object.DestroyImmediate(tileMesh); tileMesh = null; }
            if (map.tiles == null) return;

            var verts = new List<Vector3>();
            var cols = new List<Color32>();
            var tris = new List<int>();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }

            // 윗면(시계 방향이 위를 보게) — o는 칸의 왼쪽 아래, y는 높이
            void Top(Vector3 o, float size, float y, Color32 col) =>
                Quad(new Vector3(o.x, y, o.z), new Vector3(o.x + size, y, o.z),
                     new Vector3(o.x + size, y, o.z + size), new Vector3(o.x, y, o.z + size), col);

            Color32 ground = new Color32(110, 160, 88, 60);
            Color32 water = new Color32(60, 130, 230, 120);
            Color32 rock = new Color32(150, 140, 160, 120);
            Color32 rockSide = new Color32(110, 100, 122, 120);
            const float Lift = 0.15f;      // 구운 지형 표면과 겹치지 않게 살짝 띄운다
            float wallH = cell * 2.25f;    // 실제 절벽 조각(~10m)과 비슷한 눈높이 벽

            Vector3 origin = world.CellToWorld(Vector2Int.zero);
            Quad(new Vector3(origin.x, Lift, origin.z),
                 new Vector3(origin.x + map.width * cell, Lift, origin.z),
                 new Vector3(origin.x + map.width * cell, Lift, origin.z + map.height * cell),
                 new Vector3(origin.x, Lift, origin.z + map.height * cell), ground);

            for (int y = 0; y < map.height; y++)
                for (int x = 0; x < map.width; x++)
                {
                    var tile = map.TileAt(x, y);
                    if (tile == MapTile.Ground) continue;
                    Vector3 o = world.CellToWorld(new Vector2Int(x, y));

                    if (tile == MapTile.River) { Top(o, cell, Lift + 0.03f, water); continue; }

                    // 절벽 — 윗면 + 이웃이 절벽이 아닌 쪽에만 수직 벽
                    Top(o, cell, wallH, rock);
                    if (map.TileAt(x, y - 1) != MapTile.Cliff)
                        Quad(new Vector3(o.x, Lift, o.z), new Vector3(o.x + cell, Lift, o.z),
                             new Vector3(o.x + cell, wallH, o.z), new Vector3(o.x, wallH, o.z), rockSide);
                    if (map.TileAt(x, y + 1) != MapTile.Cliff)
                        Quad(new Vector3(o.x + cell, Lift, o.z + cell), new Vector3(o.x, Lift, o.z + cell),
                             new Vector3(o.x, wallH, o.z + cell), new Vector3(o.x + cell, wallH, o.z + cell), rockSide);
                    if (map.TileAt(x - 1, y) != MapTile.Cliff)
                        Quad(new Vector3(o.x, Lift, o.z + cell), new Vector3(o.x, Lift, o.z),
                             new Vector3(o.x, wallH, o.z), new Vector3(o.x, wallH, o.z + cell), rockSide);
                    if (map.TileAt(x + 1, y) != MapTile.Cliff)
                        Quad(new Vector3(o.x + cell, Lift, o.z), new Vector3(o.x + cell, Lift, o.z + cell),
                             new Vector3(o.x + cell, wallH, o.z + cell), new Vector3(o.x + cell, wallH, o.z), rockSide);
                }

            if (verts.Count == 0) return;
            tileMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            tileMesh.SetVertices(verts);
            tileMesh.SetColors(cols);
            tileMesh.SetTriangles(tris, 0);
            tileMesh.RecalculateNormals();
            tileMesh.RecalculateBounds();
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
