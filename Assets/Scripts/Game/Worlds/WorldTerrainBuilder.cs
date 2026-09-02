using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using CoreDawn.Sim;
using Debug = UnityEngine.Debug;

namespace CoreDawn.Worlds
{
    /// <summary>
    /// 지형을 <b>부팅 때 맵에서 세운다</b>(5a-4e) — Unity Terrain·구운 에셋의 후계.
    ///
    /// 구조(사용자 설계 2026-09-02): 높이차는 물가에만 있으므로 청크를 둘로 가른다 —
    ///   · <b>평지 청크</b>: 정점 4개짜리 판 하나(높이 0).
    ///   · <b>정밀 청크</b>(강·절벽·맵 가장자리 근처): 64×64 격자 + <b>물가 정점 스냅</b> —
    ///     등수위선(WaterlineIso)에 가까운 정점을 거리장 기울기를 따라 그 선 위로 옮겨,
    ///     해상도와 무관하게 물가가 매끄러운 곡선이 된다(구 2049² 높이맵의 존재 이유였던 계단 해소).
    /// 높이는 TerrainForm의 해석 함수라 저장물이 없다. 절벽 벽·발치(프리팹)와 풀은 후속 커밋.
    /// </summary>
    public static class WorldTerrainBuilder
    {
        public const string RootName = "Terrain (Runtime)";
        const string BakedRootName = "Terrain (Generated)";   // 과도기 — 구운 지형이 있으면 세우지 않는다

        const int ChunkCells = 8;      // 청크 한 변(칸)
        const int FineRes = 64;        // 강·가장자리 청크 격자(사용자: "1x1 쿼드 하나랑 64x64 하나면 될 듯")
        const int CliffTintRes = 16;   // 절벽 경계 청크 — 높이는 평평하고 바위색 번짐만 필요해서 성기게

        /// <summary>지형을 세운다. 이미 있으면(런타임/구운 것) 그대로 두고 null.</summary>
        public static GameObject Build(World world)
        {
            if (world == null || world.Map == null) { Debug.LogError("[WorldTerrain] World/맵이 없어 지형을 세울 수 없습니다."); return null; }
            if (world.transform.Find(RootName) != null || world.transform.Find(BakedRootName) != null) return null;

            var s = TerrainGenSettings.LoadOrCreate();
            if (s == null) return null;

            var sw = Stopwatch.StartNew();
            var map = world.Map;
            float cell = world.CellSize;
            var form = TerrainForm.Build(map, s, cell);
            long formMs = sw.ElapsedMilliseconds;

            var root = new GameObject(RootName);
            root.transform.SetParent(world.transform, false);

            int fine = BuildGroundChunks(root.transform, world, map, form, s);
            BuildWater(root.transform, world, map, form, s);
            BuildBounds(root.transform, world, map, s);
            var (walls, feet) = WorldTerrainCliffs.Build(root.transform, world, map, form, s);
            WorldTerrainGrass.Attach(root, world, map, form, s);

            StaticBatchingUtility.Combine(root);
            Debug.Log($"[WorldTerrain] '{map.Id}' 생성 {sw.ElapsedMilliseconds}ms (거리장 {formMs}ms) — " +
                      $"정밀 청크 {fine}개 / 전체 {Mathf.CeilToInt(map.width / (float)ChunkCells) * Mathf.CeilToInt(map.height / (float)ChunkCells)}개, " +
                      $"절벽 벽 {walls} + 발치 {feet}");
            return root;
        }

        // ── 지면 청크 ───────────────────────────────────────────────

        static int BuildGroundChunks(Transform root, World world, MapDef map, TerrainForm form, TerrainGenSettings s)
        {
            var parent = new GameObject("Ground").transform;
            parent.SetParent(root, false);

            int ground = LayerMask.NameToLayer("Ground");
            var mat = GroundMaterial(s);

            // 물가 띠(칸) — 파임이 미치는 폭 + 여유. 이 밖의 평지는 완전한 0이다.
            float band = (s.shapeInsetM + s.shelfWidthM + s.riverFalloffM) / world.CellSize + 1f;
            float edgeBand = s.shoreWidth + s.riverFalloffM / world.CellSize + 1f;

            int cx = Mathf.CeilToInt(map.width / (float)ChunkCells);
            int cy = Mathf.CeilToInt(map.height / (float)ChunkCells);
            int fineCount = 0;

            for (int j = 0; j < cy; j++)
                for (int i = 0; i < cx; i++)
                {
                    int x0 = i * ChunkCells, y0 = j * ChunkCells;
                    int w = Mathf.Min(ChunkCells, map.width - x0), h = Mathf.Min(ChunkCells, map.height - y0);

                    // 분류 — 높이가 변하는 곳(강·맵 가장자리)만 고정밀, 절벽 <b>경계</b>는 색 번짐용
                    // 저정밀, 나머지는 판 하나. 절벽 덩어리 안쪽도 평평하다(벽은 프리팹의 몫) —
                    // 판을 바위색으로 칠해 벽 틈으로 잔디가 비치지 않게 한다.
                    bool nearEdge = x0 < edgeBand || y0 < edgeBand ||
                                    map.width - (x0 + w) < edgeBand || map.height - (y0 + h) < edgeBand;
                    bool hasRiver = false, cliffBoundary = false;
                    bool allCliff = true;
                    for (int ty = y0 - 1; ty <= y0 + h; ty++)
                        for (int tx = x0 - 1; tx <= x0 + w; tx++)
                        {
                            bool inChunk = tx >= x0 && ty >= y0 && tx < x0 + w && ty < y0 + h;
                            if (!map.InBounds(tx, ty)) { continue; }
                            var t = map.TileAt(tx, ty);
                            if (inChunk && t != MapTile.Cliff) allCliff = false;
                            if (t == MapTile.River) hasRiver = true;
                            else if (t == MapTile.Cliff && !cliffBoundary)
                            {
                                if (map.TileAt(tx + 1, ty) != MapTile.Cliff || map.TileAt(tx - 1, ty) != MapTile.Cliff ||
                                    map.TileAt(tx, ty + 1) != MapTile.Cliff || map.TileAt(tx, ty - 1) != MapTile.Cliff)
                                    cliffBoundary = true;
                            }
                        }

                    var go = new GameObject($"Chunk_{i}_{j}");
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = world.CellToWorld(new Vector2Int(x0, y0)) - world.Origin;
                    if (ground >= 0) go.layer = ground;

                    bool needFine = nearEdge || hasRiver || cliffBoundary;
                    int res = nearEdge || hasRiver ? FineRes : CliffTintRes;
                    Mesh mesh = needFine ? FineChunk(map, form, x0, y0, w, h, world.CellSize, res)
                                         : FlatChunk(w, h, world.CellSize, x0, y0, s,
                                                     allCliff ? new Color32(0, 255, 0, 255) : default);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    if (needFine) fineCount++;
                }
            return fineCount;
        }

        /// <summary>평지 — 정점 4개, 높이 0. 절벽 덩어리 안쪽이면 바위색(정점색 G)을 준다.</summary>
        static Mesh FlatChunk(int w, int h, float cell, int x0, int y0, TerrainGenSettings s, Color32 tint)
        {
            float sx = w * cell, sz = h * cell;
            var mesh = new Mesh { name = "flat" };
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(sx, 0, 0), new Vector3(sx, 0, sz), new Vector3(0, 0, sz) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = GroundUvs(mesh.vertices, x0, y0, cell, s);
            mesh.colors32 = new[] { tint, tint, tint, tint };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return mesh;
        }

        /// <summary>정밀 청크 — 격자 + 물가 정점을 등수위선으로 스냅, 높이는 해석 함수.</summary>
        static Mesh FineChunk(MapDef map, TerrainForm form, int x0, int y0, int w, int h, float cell, int res)
        {
            float step = ChunkCells / (float)res;          // 정점 간격(칸)
            int nx = Mathf.CeilToInt(w / step), ny = Mathf.CeilToInt(h / step);
            var verts = new Vector3[(nx + 1) * (ny + 1)];
            var cols = new Color32[verts.Length];
            var uvs = new Vector2[verts.Length];

            float iso = form.WaterlineIso;
            float snapHalf = step * 0.75f;                  // 이 안이면 물가선으로 옮긴다

            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float tx = x0 + Mathf.Min(i * step, w);
                    float ty = y0 + Mathf.Min(j * step, h);

                    // 물가 정점 스냅 — 청크 경계 정점은 이웃과 어긋나지 않게 그대로 둔다
                    bool border = i == 0 || j == 0 || i == nx || j == ny;
                    if (!border)
                    {
                        float d = form.RiverDistance(tx, ty);
                        if (Mathf.Abs(d - iso) < snapHalf)
                            for (int it = 0; it < 2; it++)
                            {
                                var g = form.RiverGradient(tx, ty);
                                float len = g.magnitude;
                                if (len < 1e-4f) break;
                                float err = form.RiverDistance(tx, ty) - iso;
                                tx -= g.x / len * err; ty -= g.y / len * err;
                            }
                    }

                    int v = j * (nx + 1) + i;
                    float y = form.Height(tx, ty);
                    verts[v] = new Vector3((tx - x0) * cell, y, (ty - y0) * cell);

                    // 표면 가중치(정점색) — 후속 셰이더가 쓴다: R = 강바닥(모래), G = 절벽 번짐
                    float bed = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, -0.45f, y));
                    float cliff = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, -0.3f, form.CliffDistance(tx, ty)));
                    cols[v] = new Color32((byte)(bed * 255f), (byte)(cliff * 255f), 0, 255);
                }

            var tris = new int[nx * ny * 6];
            int ti = 0;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = c; tris[ti++] = d; tris[ti++] = b;
                }

            var mesh = new Mesh { name = "fine" };
            if (verts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.colors32 = cols;
            mesh.triangles = tris;
            mesh.uv = uvs;   // 지면 UV는 아래에서 월드 기준으로 다시 채운다
            FillGroundUvs(mesh, x0, y0, cell);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void FillGroundUvs(Mesh mesh, int x0, int y0, float cell)
        {
            var s = TerrainGenSettings.LoadOrCreate();
            var verts = mesh.vertices;
            var uvs = new Vector2[verts.Length];
            Vector2 tile = TileSize(s);
            for (int i = 0; i < verts.Length; i++)
                uvs[i] = new Vector2((x0 * cell + verts[i].x) / tile.x, (y0 * cell + verts[i].z) / tile.y);
            mesh.uv = uvs;
        }

        static Vector2[] GroundUvs(Vector3[] verts, int x0, int y0, float cell, TerrainGenSettings s)
        {
            var uvs = new Vector2[verts.Length];
            Vector2 tile = TileSize(s);
            for (int i = 0; i < verts.Length; i++)
                uvs[i] = new Vector2((x0 * cell + verts[i].x) / tile.x, (y0 * cell + verts[i].z) / tile.y);
            return uvs;
        }

        static Vector2 TileSize(TerrainGenSettings s)
        {
            var layer = s.terrainLayers != null && s.terrainLayers.Length > 0 ? s.terrainLayers[0] : null;
            return layer != null && layer.tileSize.x > 0f ? layer.tileSize : new Vector2(4f, 4f);
        }

        static Material groundMat;

        /// <summary>지면 재질 — 우선은 지면 레이어 텍스처 한 장짜리 URP/Lit. 3층 블렌드 셰이더는 후속.</summary>
        static Material GroundMaterial(TerrainGenSettings s)
        {
            if (groundMat != null) return groundMat;
            var shader = Managers.BuiltinShaders.Of("Universal Render Pipeline/Lit");
            groundMat = new Material(shader) { name = "Ground (Runtime)" };
            var layer = s.terrainLayers != null && s.terrainLayers.Length > 0 ? s.terrainLayers[0] : null;
            if (layer != null && layer.diffuseTexture != null) groundMat.mainTexture = layer.diffuseTexture;
            groundMat.SetFloat("_Smoothness", 0f);
            return groundMat;
        }

        // ── 물 (구 생성기 CreateWater 포팅 — 에셋 없이 메모리 메시) ──

        static void BuildWater(Transform root, World world, MapDef map, TerrainForm form, TerrainGenSettings s)
        {
            float w = map.width * world.CellSize, h = map.height * world.CellSize;
            float margin = Mathf.Max(w, h) * s.seaMargin;
            float x0 = -margin, z0 = -margin;
            float sizeX = w + margin * 2f, sizeZ = h + margin * 2f;

            int cols = Mathf.Clamp(Mathf.CeilToInt(sizeX / s.waterVertexSpacing), 1, 512);
            int rows = Mathf.Clamp(Mathf.CeilToInt(sizeZ / s.waterVertexSpacing), 1, 512);

            var verts = new Vector3[(cols + 1) * (rows + 1)];
            var uvs = new Vector2[verts.Length];
            var norms = new Vector3[verts.Length];
            var colors = new Color[verts.Length];

            for (int j = 0; j <= rows; j++)
                for (int i = 0; i <= cols; i++)
                {
                    int v = j * (cols + 1) + i;
                    float fx = (float)i / cols, fz = (float)j / rows;
                    float wx = x0 + fx * sizeX, wz = z0 + fz * sizeZ;

                    verts[v] = new Vector3(wx, 0f, wz);
                    norms[v] = Vector3.up;
                    uvs[v] = new Vector2(fx * sizeX / world.CellSize, fz * sizeZ / world.CellSize);

                    float ttx = wx / world.CellSize, ttz = wz / world.CellSize;
                    bool overMap = ttx >= 0f && ttz >= 0f && ttx <= map.width && ttz <= map.height;
                    float bed = overMap ? form.Height(ttx, ttz) : -99f;
                    float depth = s.waterLevel - bed;
                    float foam = Mathf.Clamp01(1f - depth / s.foamDepth) * Mathf.Clamp01(depth / 0.05f);
                    colors[v] = new Color(foam, 0f, 0f, 1f);
                }

            var tris = new int[cols * rows * 6];
            int ti = 0;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    int a = j * (cols + 1) + i, b = a + 1, c = a + cols + 1, d = c + 1;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = c; tris[ti++] = d; tris[ti++] = b;
                }

            var mesh = new Mesh { name = "water" };
            if (verts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts; mesh.triangles = tris; mesh.normals = norms; mesh.uv = uvs; mesh.colors = colors;
            mesh.RecalculateBounds();

            var mat = Resources.Load<Material>("Builtin/Water");
            if (mat == null)
            {
                Debug.LogError("[WorldTerrain] Resources/Builtin/Water.mat 이 없습니다 — 물을 세우지 못했습니다.");
                return;
            }

            var go = new GameObject("Water (Sea)");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, s.waterLevel, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ── 경계벽 (구 생성기 CreateBounds 포팅) ────────────────────

        static void BuildBounds(Transform root, World world, MapDef map, TerrainGenSettings s)
        {
            int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycast < 0) { Debug.LogWarning("[WorldTerrain] 'Ignore Raycast' 레이어가 없어 경계벽을 세우지 못했습니다."); return; }

            var parent = new GameObject("Bounds").transform;
            parent.SetParent(root, false);

            float w = map.width * world.CellSize, h = map.height * world.CellSize;
            float t = s.boundsThickness, half = s.boundsHeight * 0.5f;

            Wall("Bounds_-X", new Vector3(-t * 0.5f, half, h * 0.5f), new Vector3(t, s.boundsHeight, h + t * 2f));
            Wall("Bounds_+X", new Vector3(w + t * 0.5f, half, h * 0.5f), new Vector3(t, s.boundsHeight, h + t * 2f));
            Wall("Bounds_-Z", new Vector3(w * 0.5f, half, -t * 0.5f), new Vector3(w + t * 2f, s.boundsHeight, t));
            Wall("Bounds_+Z", new Vector3(w * 0.5f, half, h + t * 0.5f), new Vector3(w + t * 2f, s.boundsHeight, t));

            void Wall(string name, Vector3 center, Vector3 size)
            {
                var go = new GameObject(name) { layer = ignoreRaycast };
                go.transform.SetParent(parent, false);
                go.transform.localPosition = center - new Vector3(0f, s.boundsSink, 0f);
                go.AddComponent<BoxCollider>().size = size;
            }
        }
    }
}
