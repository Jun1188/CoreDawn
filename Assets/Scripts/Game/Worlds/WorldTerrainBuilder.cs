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

        /// <summary>미리보기 재료 — 씬을 전혀 건드리지 않는 순수 데이터(메시·위치·재질·절벽 배치 계획).</summary>
        public sealed class PreviewData
        {
            public List<(Mesh mesh, Vector3 localPos, Vector3 scale)> ground;
            public Material groundMat;
            public Mesh water;                 // null이면 물 없음(재질 부재 등)
            public Vector3 waterPos;
            public Material waterMat;
            public List<WorldTerrainCliffs.Placement> cliffs;
        }

        /// <summary>지형을 세운다(런타임). 이미 있으면(런타임/구운 것) 그대로 두고 null.</summary>
        public static GameObject Build(World world)
        {
            if (world == null || world.Map == null) { Debug.LogError("[WorldTerrain] World/맵이 없어 지형을 세울 수 없습니다."); return null; }
            if (world.transform.Find(RootName) != null || world.transform.Find(BakedRootName) != null) return null;

            var s = TerrainGenSettings.LoadOrCreate();
            if (s == null) return null;

            var sw = Stopwatch.StartNew();
            var map = world.Map;
            var form = TerrainForm.Build(map, s, world.CellSize);
            long formMs = sw.ElapsedMilliseconds;

            var root = new GameObject(RootName);
            root.transform.SetParent(world.transform, false);

            int ground = LayerMask.NameToLayer("Ground");
            var groundParent = new GameObject("Ground").transform;
            groundParent.SetParent(root.transform, false);
            var chunks = GroundMeshes(world, map, form, s, out int fine);
            var mat = GroundMaterial(s);
            for (int i = 0; i < chunks.Count; i++)
            {
                var go = new GameObject($"Chunk_{i}");
                go.transform.SetParent(groundParent, false);
                go.transform.localPosition = chunks[i].localPos;
                go.transform.localScale = chunks[i].scale;
                if (ground >= 0) go.layer = ground;
                go.AddComponent<MeshFilter>().sharedMesh = chunks[i].mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.AddComponent<MeshCollider>().sharedMesh = chunks[i].mesh;
            }

            var water = WaterMesh(world, map, form, s, out Vector3 waterPos, out Material waterMat);
            if (water != null)
            {
                var go = new GameObject("Water (Sea)");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = waterPos;
                go.AddComponent<MeshFilter>().sharedMesh = water;
                go.AddComponent<MeshRenderer>().sharedMaterial = waterMat;
            }

            BuildBounds(root.transform, world, map, s);
            var (walls, feet) = WorldTerrainCliffs.Build(root.transform, world, map, form, s);
            WorldTerrainGrass.Attach(root, world, map, form, s);
            StaticBatchingUtility.Combine(root);
            Debug.Log($"[WorldTerrain] '{map.Id}' 생성 {sw.ElapsedMilliseconds}ms (거리장 {formMs}ms) — " +
                      $"정밀 청크 {fine}/{chunks.Count}개, 절벽 벽 {walls} + 발치 {feet}");
            return root;
        }

        /// <summary>미리보기 재료를 만든다 — GameObject를 하나도 만들지 않는다(에디터 DrawMesh 전용).</summary>
        public static PreviewData BuildPreviewData(World world)
        {
            if (world == null || world.Map == null) return null;
            var s = TerrainGenSettings.LoadOrCreate();
            if (s == null) return null;

            var map = world.Map;
            var form = TerrainForm.Build(map, s, world.CellSize);
            var data = new PreviewData
            {
                ground = GroundMeshes(world, map, form, s, out _),
                groundMat = GroundMaterial(s),
                cliffs = new List<WorldTerrainCliffs.Placement>(),
            };
            data.water = WaterMesh(world, map, form, s, out data.waterPos, out data.waterMat);
            var (walls, feet) = WorldTerrainCliffs.Plan(world, map, form, s);
            data.cliffs.AddRange(walls);
            data.cliffs.AddRange(feet);
            return data;
        }

        // ── 지면 청크 ───────────────────────────────────────────────

        /// <summary>지면 청크 메시들 — 씬을 건드리지 않는 순수 생성. 위치·스케일은 World 루트 기준 로컬.
        /// 평지는 <b>공유 단위 쿼드 하나</b>(사용자 설계)를 스케일로 늘려 쓴다 — UV는 셰이더가 월드좌표에서 만든다.</summary>
        static List<(Mesh mesh, Vector3 localPos, Vector3 scale)> GroundMeshes(World world, MapDef map, TerrainForm form, TerrainGenSettings s, out int fineCount)
        {
            var result = new List<(Mesh, Vector3, Vector3)>();

            // 물가 띠(칸) — 파임이 미치는 폭 + 여유. 이 밖의 평지는 완전한 0이다.
            float edgeBand = s.shoreWidth + s.riverFalloffM / world.CellSize + 1f;

            int cx = Mathf.CeilToInt(map.width / (float)ChunkCells);
            int cy = Mathf.CeilToInt(map.height / (float)ChunkCells);
            fineCount = 0;

            for (int j = 0; j < cy; j++)
                for (int i = 0; i < cx; i++)
                {
                    int x0 = i * ChunkCells, y0 = j * ChunkCells;
                    int w = Mathf.Min(ChunkCells, map.width - x0), h = Mathf.Min(ChunkCells, map.height - y0);

                    // 분류 — 높이가 변하는 곳(강·맵 가장자리)만 고정밀, 나머지는 판 하나.
                    // 절벽도 평평하다(벽은 프리팹의 몫) — 바위색 정점색은 폐기(사용자: 정점색은 물가에만,
                    // 바위 틈에 잔디가 자라는 게 오히려 자연스럽다).
                    bool nearEdge = x0 < edgeBand || y0 < edgeBand ||
                                    map.width - (x0 + w) < edgeBand || map.height - (y0 + h) < edgeBand;
                    bool hasRiver = false;
                    for (int ty = y0 - 1; ty <= y0 + h && !hasRiver; ty++)
                        for (int tx = x0 - 1; tx <= x0 + w; tx++)
                            if (map.InBounds(tx, ty) && map.TileAt(tx, ty) == MapTile.River) { hasRiver = true; break; }

                    bool needFine = nearEdge || hasRiver;
                    Vector3 pos = world.CellToWorld(new Vector2Int(x0, y0)) - world.Origin;
                    if (needFine)
                    {
                        result.Add((FineChunk(map, form, x0, y0, w, h, world.CellSize, FineRes), pos, Vector3.one));
                        fineCount++;
                    }
                    else
                    {
                        result.Add((SharedQuad(), pos, new Vector3(w * world.CellSize, 1f, h * world.CellSize)));
                    }
                }
            return result;
        }

        static Mesh sharedQuad;

        /// <summary>평지가 공유하는 단위 쿼드(1×1, 높이 0) — 스케일은 트랜스폼이 준다.
        /// 정점색을 검정으로 명시한다 — 색 채널이 없으면 Unity가 흰색을 넣어 모래(bed=1)로 칠해 버린다.</summary>
        static Mesh SharedQuad()
        {
            if (sharedQuad != null) return sharedQuad;
            sharedQuad = new Mesh { name = "flat (shared)" };
            sharedQuad.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) };
            sharedQuad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            sharedQuad.colors32 = new[] { new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255) };
            sharedQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return sharedQuad;
        }

        /// <summary>정밀 청크 — 격자 + 물가 정점을 등수위선으로 스냅, 높이는 해석 함수.</summary>
        static Mesh FineChunk(MapDef map, TerrainForm form, int x0, int y0, int w, int h, float cell, int res)
        {
            float step = ChunkCells / (float)res;          // 정점 간격(칸)
            int nx = Mathf.CeilToInt(w / step), ny = Mathf.CeilToInt(h / step);
            var verts = new Vector3[(nx + 1) * (ny + 1)];
            var cols = new Color32[verts.Length];

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

                    // 표면 가중치(정점색) — 물가에만 쓴다: R = 강바닥(모래). 셰이더(CoreDawn/Ground)가 읽는다.
                    float bed = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, -0.45f, y));
                    cols[v] = new Color32((byte)(bed * 255f), 0, 0, 255);
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
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material groundMat;

        /// <summary>지면 재질 — CoreDawn/Ground(잔디 ↔ 강바닥을 정점색 R로 블렌드). 텍스처·타일 크기는 설정의 직접 참조.</summary>
        static Material GroundMaterial(TerrainGenSettings s)
        {
            if (groundMat != null) return groundMat;
            var shader = Managers.BuiltinShaders.Of("CoreDawn/Ground");
            if (shader == null) shader = Managers.BuiltinShaders.Of("Universal Render Pipeline/Lit");
            groundMat = new Material(shader) { name = "Ground (Runtime)" };
            if (s.grassTexture != null) groundMat.mainTexture = s.grassTexture;
            if (s.grassTileM > 0f) groundMat.SetFloat("_GrassTileM", s.grassTileM);
            if (s.bedTexture != null)
            {
                groundMat.SetTexture("_BedMap", s.bedTexture);
                if (s.bedTileM > 0f && s.grassTileM > 0f)
                    groundMat.SetFloat("_BedUvScale", s.grassTileM / s.bedTileM);
            }
            return groundMat;
        }

        // ── 물 (구 생성기 CreateWater 포팅 — 에셋 없이 메모리 메시) ──

        /// <summary>물 메시 — 순수 생성. 재질이 없으면 오류 로그 + null.</summary>
        static Mesh WaterMesh(World world, MapDef map, TerrainForm form, TerrainGenSettings s, out Vector3 localPos, out Material mat)
        {
            localPos = new Vector3(0f, s.waterLevel, 0f);
            // 다른 재료(텍스처·프리팹)와 같은 직접 참조 — 옛 Resources.Load("Builtin/Water") 경로 로드는
            // 설정의 waterMaterialSource(죽은 필드)와 실제 쓰는 재질이 달라지는 혼선을 낳았다.
            mat = s.waterMaterial;
            if (mat == null)
            {
                Debug.LogError("[WorldTerrain] TerrainGenSettings.waterMaterial 이 비어 있습니다 — 물을 세우지 못했습니다.", s);
                return null;
            }
            // 2단 격자(사용자 결정): 맵 안(강가·거품)은 32×32, 바깥 바다는 성긴 조각 8개.
            // 구 512×512(26만 정점)의 대부분이 빈 바다에 낭비되고 있었다. 바깥 조각의 경계 정점은
            // 맵 안 격자와 간격을 맞춰 T-정션(파도 변위가 달라 갈라지는 이음매)을 피한다.
            const int InnerRes = 32;
            const int CoarseRes = 4;

            float w = map.width * world.CellSize, h = map.height * world.CellSize;
            float margin = Mathf.Max(w, h) * s.seaMargin;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var norms = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();

            void Grid(float gx, float gz, float sizeX, float sizeZ, int cols, int rows)
            {
                int start = verts.Count;
                for (int j = 0; j <= rows; j++)
                    for (int i = 0; i <= cols; i++)
                    {
                        float wx = gx + (float)i / cols * sizeX, wz = gz + (float)j / rows * sizeZ;
                        verts.Add(new Vector3(wx, 0f, wz));
                        norms.Add(Vector3.up);
                        uvs.Add(new Vector2(wx / world.CellSize, wz / world.CellSize));

                        float ttx = wx / world.CellSize, ttz = wz / world.CellSize;
                        bool overMap = ttx >= 0f && ttz >= 0f && ttx <= map.width && ttz <= map.height;
                        float bed = overMap ? form.Height(ttx, ttz) : -99f;
                        float depth = s.waterLevel - bed;
                        float foam = Mathf.Clamp01(1f - depth / s.foamDepth) * Mathf.Clamp01(depth / 0.05f);
                        colors.Add(new Color(foam, 0f, 0f, 1f));
                    }
                for (int j = 0; j < rows; j++)
                    for (int i = 0; i < cols; i++)
                    {
                        int a = start + j * (cols + 1) + i, b = a + 1, c = a + cols + 1, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(c); tris.Add(d); tris.Add(b);
                    }
            }

            Grid(0f, 0f, w, h, InnerRes, InnerRes);                               // 맵 안 — 거품·파도
            Grid(0f, -margin, w, margin, InnerRes, CoarseRes);                    // 남
            Grid(0f, h, w, margin, InnerRes, CoarseRes);                          // 북
            Grid(-margin, 0f, margin, h, CoarseRes, InnerRes);                    // 서
            Grid(w, 0f, margin, h, CoarseRes, InnerRes);                          // 동
            Grid(-margin, -margin, margin, margin, CoarseRes, CoarseRes);         // 모서리 4
            Grid(w, -margin, margin, margin, CoarseRes, CoarseRes);
            Grid(-margin, h, margin, margin, CoarseRes, CoarseRes);
            Grid(w, h, margin, margin, CoarseRes, CoarseRes);

            var mesh = new Mesh { name = "water" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();
            return mesh;
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
