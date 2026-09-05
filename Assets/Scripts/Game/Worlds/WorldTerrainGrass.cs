using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Worlds
{
    /// <summary>
    /// 풀·꽃 — GPU 구동(5a-4e, 사용자 설계). 심는 규칙은 구 Terrain 디테일(PaintDetails) 그대로지만
    /// 파이프라인이 다르다:
    ///   · 전 맵 인스턴스(포기당 16B)를 지형 생성 때 <b>한 번</b> GPU에 상주시킨다.
    ///   · CPU는 매 프레임 카메라 위치·절두체 평면·절벽 오클루더 박스만 올린다.
    ///   · 컴퓨트(GrassCull)가 거리 감쇠·절두체·절벽 오클루전을 걸러 AppendBuffer에 쓰고,
    ///     프로토타입별 <see cref="Graphics.RenderMeshIndirect"/> 한 콜로 그린다 — 1023 제한 없음.
    /// 셰이더는 CoreDawn/Vegetation Lit의 procedural 경로(GrassProcedural.hlsl)를 쓴다.
    /// 콜라이더 없음 — 길찾기·건설·사격 판정에 관여하지 않는다.
    /// </summary>
    [ExecuteAlways]
    public sealed class WorldTerrainGrass : MonoBehaviour
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Instance
        {
            public Vector3 pos;
            public uint packed;   // 하위 16비트 배율(1/8192), 상위 16비트 yaw — GrassProcedural.hlsl과 동일
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OccluderBox
        {
            public Vector3 min; public float pad0;
            public Vector3 max; public float pad1;
        }

        sealed class Proto
        {
            public Mesh mesh;
            public Material mat;               // 원본 복제 + _GrassInstances 바인딩
            public GraphicsBuffer instances;   // 상주 전체
            public GraphicsBuffer visible;     // 컬링 통과분(Append)
            public GraphicsBuffer args;        // indirect args
            public int count;
        }

        TerrainGenSettings s;
        ComputeShader cull;
        int kernel;
        readonly List<Proto> protos = new List<Proto>();
        GraphicsBuffer occluders;
        OccluderBox[] allOccluders;                       // CPU 원본 — 매 프레임 프리컬링해서 일부만 올린다
        readonly List<OccluderBox> visibleOccluders = new List<OccluderBox>();
        Bounds worldBounds;
        readonly Vector4[] planeVec = new Vector4[6];
        readonly Plane[] planes = new Plane[6];

        public static WorldTerrainGrass Attach(GameObject root, World world, MapDef map, TerrainForm form, TerrainGenSettings s)
        {
            var g = root.AddComponent<WorldTerrainGrass>();
            g.Init(world, map, form, s);
            return g;
        }

        void Init(World world, MapDef map, TerrainForm form, TerrainGenSettings settings)
        {
            s = settings;
            cull = Resources.Load<ComputeShader>("Builtin/GrassCull");
            if (cull == null) { Debug.LogError("[WorldTerrain] Resources/Builtin/GrassCull.compute 가 없습니다 — 풀을 그릴 수 없습니다."); return; }
            kernel = cull.FindKernel("Cull");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var lists = Generate(world, map, form, out var protoSources);
            for (int i = 0; i < lists.Length; i++)
            {
                if (lists[i].Count == 0) continue;
                var (mesh, srcMat, _) = protoSources[i];
                var p = new Proto
                {
                    mesh = mesh,
                    count = lists[i].Count,
                    instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, lists[i].Count, 16),
                    visible = new GraphicsBuffer(GraphicsBuffer.Target.Append | GraphicsBuffer.Target.Structured, lists[i].Count, 16),
                    args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size),
                };
                p.instances.SetData(lists[i]);
                var arg = new GraphicsBuffer.IndirectDrawIndexedArgs { indexCountPerInstance = mesh.GetIndexCount(0) };
                p.args.SetData(new[] { arg });
                p.mat = new Material(srcMat) { hideFlags = HideFlags.HideAndDontSave };
                p.mat.SetBuffer("_GrassInstances", p.visible);
                protos.Add(p);
            }

            BuildOccluders(world, map, form);

            float cell = world.CellSize;
            worldBounds = new Bounds(
                world.Origin + new Vector3(map.width * cell * 0.5f, 5f, map.height * cell * 0.5f),
                new Vector3(map.width * cell, 30f, map.height * cell));

            long total = 0;
            foreach (var p in protos) total += p.count;
            Debug.Log($"[WorldTerrain] 풀 {total:N0}포기 상주({total * 16 / 1048576}MB) · 오클루더 {(allOccluders != null ? allOccluders.Length : 0)}박스 · 생성 {sw.ElapsedMilliseconds}ms");
        }

        /// <summary>심기 — 구 PaintDetails 규칙(물가 위·완경사, 밀도만 흔들고 꽃은 흩뿌림). 좌표 해시라 결정적.</summary>
        List<Instance>[] Generate(World world, MapDef map, TerrainForm form, out (Mesh, Material, Vector2)[] protoSources)
        {
            var grassSrc = Sources(s.grassSet, s.grassSize);
            var flowerSrc = Sources(s.flowerSet, s.flowerSize);
            protoSources = new (Mesh, Material, Vector2)[grassSrc.Count + flowerSrc.Count];
            for (int i = 0; i < grassSrc.Count; i++) protoSources[i] = grassSrc[i];
            for (int i = 0; i < flowerSrc.Count; i++) protoSources[grassSrc.Count + i] = flowerSrc[i];

            var lists = new List<Instance>[protoSources.Length];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<Instance>();
            if (grassSrc.Count == 0)
            {
                Debug.LogWarning("[WorldTerrain] 풀 프리팹이 하나도 없습니다 — TerrainGenSettings의 Grass Set 확인.");
                return lists;
            }

            float cell = world.CellSize;
            float grassWaterLine = s.waterLevel + s.grassWaterLineOffset;
            float pointM = Mathf.Max(0.25f, s.detailPointM);
            int pointsX = Mathf.Max(1, Mathf.RoundToInt(map.width * cell / pointM));
            int pointsY = Mathf.Max(1, Mathf.RoundToInt(map.height * cell / pointM));

            for (int pj = 0; pj < pointsY; pj++)
                for (int pi = 0; pi < pointsX; pi++)
                {
                    float jx = WorldTerrainCliffs.Hash(pi, pj, 611) % 1000 / 1000f - 0.5f;
                    float jy = WorldTerrainCliffs.Hash(pi, pj, 613) % 1000 / 1000f - 0.5f;
                    float tx = (pi + 0.5f + jx * 0.9f) / pointsX * map.width;
                    float ty = (pj + 0.5f + jy * 0.9f) / pointsY * map.height;

                    // 절벽 타일에도 그대로 심는다 — 바위 틈에 잔디가 자라는 게 자연스럽다(사용자).
                    // 이 포기들은 오클루전 면제(Pack의 플래그) — 벽 뒤를 거르는 건 지면 타일 몫이다.
                    bool onCliff = map.TileAt(Mathf.FloorToInt(tx), Mathf.FloorToInt(ty)) == MapTile.Cliff;

                    float y = form.Height(tx, ty);
                    if (y < grassWaterLine) continue;   // 물가 아래는 비운다 — 끊기는 선이 물가 곡선을 따른다

                    if (y < -0.001f)   // 경사 — 내륙은 완전 평면이라 물가 근처에서만 걸린다
                    {
                        const float d = 0.25f;
                        float sx = form.Height(tx + d, ty) - form.Height(tx - d, ty);
                        float sz = form.Height(tx, ty + d) - form.Height(tx, ty - d);
                        if (Mathf.Sqrt(sx * sx + sz * sz) / (2f * d * cell) > s.grassMaxSlope) continue;
                    }

                    Vector3 pos = world.Origin + new Vector3(tx * cell, y, ty * cell);

                    // 풀 — 빈 곳 없이 깔되 밀도만 흔든다(임계값으로 자르면 얼룩이 된다)
                    float patch = Mathf.PerlinNoise(tx * 0.06f, ty * 0.06f);
                    int amount = patch > 0.7f ? 3 : 2;
                    for (int a = 0; a < amount; a++)
                    {
                        int gi = WorldTerrainCliffs.Hash(pi, pj, 17 + a) % grassSrc.Count;
                        float sc = Mathf.Lerp(grassSrc[gi].Item3.x, grassSrc[gi].Item3.y,
                                              WorldTerrainCliffs.Hash(pi, pj, 41 + a) % 1000 / 1000f);
                        float yaw01 = WorldTerrainCliffs.Hash(pi, pj, 59 + a) % 1000 / 1000f;
                        float ox = (WorldTerrainCliffs.Hash(pi, pj, 71 + a) % 1000 / 1000f - 0.5f) * pointM;
                        float oz = (WorldTerrainCliffs.Hash(pi, pj, 73 + a) % 1000 / 1000f - 0.5f) * pointM;
                        lists[gi].Add(Pack(pos + new Vector3(ox, 0f, oz), sc, yaw01, onCliff));
                    }

                    // 꽃 — 셀 단위로 흩뿌린다(칸 단위 선택은 줄무늬가 된다 — 구 생성기 교훈)
                    if (flowerSrc.Count > 0)
                    {
                        float bloom = Mathf.PerlinNoise(tx * 0.04f + 31.7f, ty * 0.04f + 12.9f);
                        if (bloom > 0.58f && WorldTerrainCliffs.Hash(pi, pj, 91) % 8 == 0)
                        {
                            int fi = WorldTerrainCliffs.Hash(pi, pj, 53) % flowerSrc.Count;
                            float sc = Mathf.Lerp(flowerSrc[fi].Item3.x, flowerSrc[fi].Item3.y,
                                                  WorldTerrainCliffs.Hash(pi, pj, 43) % 1000 / 1000f);
                            float yaw01 = WorldTerrainCliffs.Hash(pi, pj, 61) % 1000 / 1000f;
                            lists[grassSrc.Count + fi].Add(Pack(pos, sc, yaw01, onCliff));
                        }
                    }
                }
            return lists;
        }

        static Instance Pack(Vector3 pos, float scale, float yaw01, bool onCliff)
        {
            // 최상위 비트 = 절벽 타일 위(틈새 풀) — 컴퓨트가 오클루전을 면제한다. 벽 파인 주머니의
            // 풀이 박스 몸통 안이라 "벽 뒤"로 오판되던 것의 해법: 거를 것은 지면 타일의 벽 뒤뿐이다.
            uint sc = (uint)Mathf.Clamp(Mathf.RoundToInt(scale * 8192f), 1, 65535);
            uint yaw = (uint)Mathf.Clamp(Mathf.RoundToInt(yaw01 * 32767f), 0, 32767);
            return new Instance { pos = pos, packed = sc | (yaw << 16) | (onCliff ? 0x80000000u : 0u) };
        }

        static List<(Mesh, Material, Vector2)> Sources(GameObject[] set, Vector2 size)
        {
            var list = new List<(Mesh, Material, Vector2)>();
            if (set == null) return list;
            foreach (var p in set)
            {
                if (p == null) continue;
                var mf = p.GetComponentInChildren<MeshFilter>();
                var mr = p.GetComponentInChildren<MeshRenderer>();
                if (mf == null || mr == null || mf.sharedMesh == null || mr.sharedMaterial == null) continue;
                list.Add((mf.sharedMesh, mr.sharedMaterial, size));
            }
            return list;
        }

        /// <summary>절벽 오클루더 — 절벽 타일 행 연속 구간을 세로로 그리디 병합한 박스(0.1칸 인셋, 바닥 0.8m — 틈새 풀 보호).</summary>
        void BuildOccluders(World world, MapDef map, TerrainForm form)
        {
            float cell = world.CellSize;
            float wallH = 9f;
            var boxes = new List<OccluderBox>();
            var used = new bool[map.width, map.height];

            for (int y = 0; y < map.height; y++)
                for (int x = 0; x < map.width; x++)
                {
                    if (used[x, y] || map.TileAt(x, y) != MapTile.Cliff) continue;

                    int w = 1;
                    while (x + w < map.width && !used[x + w, y] && map.TileAt(x + w, y) == MapTile.Cliff) w++;
                    int h = 1;
                    bool CanGrow()
                    {
                        if (y + h >= map.height) return false;
                        for (int i = 0; i < w; i++)
                            if (used[x + i, y + h] || map.TileAt(x + i, y + h) != MapTile.Cliff) return false;
                        return true;
                    }
                    while (CanGrow()) h++;
                    for (int j = 0; j < h; j++)
                        for (int i = 0; i < w; i++)
                            used[x + i, y + j] = true;

                    const float Inset = 0.45f;  // 0.1→0.2→0.4→0.45 상향(사용자) — 벽면 후퇴 최대(0.45칸)와 같은 값.
                                                // 벽이 타일 밖으로 튀어나온 곳의 보이는 풀은 살리고, 벽 "뒤"만 거른다.
                    Vector3 a = world.CellToWorld(new Vector2Int(x, y)) + new Vector3(Inset * cell, 0f, Inset * cell);
                    Vector3 b = world.CellToWorld(new Vector2Int(x + w, y + h)) - new Vector3(Inset * cell, 0f, Inset * cell);
                    boxes.Add(new OccluderBox { min = new Vector3(a.x, 0.8f, a.z), max = new Vector3(b.x, wallH, b.z) });
                }

            allOccluders = boxes.ToArray();
            if (allOccluders.Length > 0)
                occluders = new GraphicsBuffer(GraphicsBuffer.Target.Structured, allOccluders.Length, 32);
        }

        void LateUpdate()
        {
            if (protos.Count == 0 || cull == null) return;
            var cam = Camera.main;
#if UNITY_EDITOR
            if (cam == null && UnityEditor.SceneView.lastActiveSceneView != null)
                cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            if (cam == null) return;

            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            for (int i = 0; i < 6; i++)
                planeVec[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);

            // 오클루더 프리컬링(사용자 요청) — 절두체 밖·사거리 밖·카메라를 품은 박스는 올리지 않는다.
            Vector3 eye = cam.transform.position;
            visibleOccluders.Clear();
            if (allOccluders != null)
            {
                float maxSq = (s.detailDistance + 2f) * (s.detailDistance + 2f);
                for (int i = 0; i < allOccluders.Length; i++)
                {
                    var box = allOccluders[i];
                    var b = new Bounds((box.min + box.max) * 0.5f, box.max - box.min);
                    if (b.Contains(eye)) continue;
                    if (b.SqrDistance(eye) > maxSq) continue;
                    if (!GeometryUtility.TestPlanesAABB(planes, b)) continue;
                    visibleOccluders.Add(box);
                }
                if (visibleOccluders.Count > 0) occluders.SetData(visibleOccluders, 0, 0, visibleOccluders.Count);
            }

            cull.SetVectorArray("_FrustumPlanes", planeVec);
            cull.SetVector("_CameraPos", eye);
            cull.SetFloat("_MaxDist", s.detailDistance);
            cull.SetFloat("_FadeStart", s.detailDistance * 0.45f);
            cull.SetInt("_OccluderCount", visibleOccluders.Count);
            if (occluders != null) cull.SetBuffer(kernel, "_Occluders", occluders);

            foreach (var p in protos)
            {
                p.visible.SetCounterValue(0);
                cull.SetInt("_InstanceCount", p.count);
                cull.SetBuffer(kernel, "_Instances", p.instances);
                cull.SetBuffer(kernel, "_Visible", p.visible);
                cull.Dispatch(kernel, (p.count + 63) / 64, 1, 1);
                GraphicsBuffer.CopyCount(p.visible, p.args, 4);   // IndirectDrawIndexedArgs.instanceCount

                var rp = new RenderParams(p.mat)
                {
                    worldBounds = worldBounds,
                    shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = gameObject.layer,
                };
                Graphics.RenderMeshIndirect(rp, p.mesh, p.args);
            }
        }

        void OnDestroy() => Release();
        void OnDisable() => Release();

        void Release()
        {
            foreach (var p in protos)
            {
                p.instances?.Release();
                p.visible?.Release();
                p.args?.Release();
                if (p.mat != null) DestroyImmediate(p.mat);
            }
            protos.Clear();
            occluders?.Release();
            occluders = null;
        }
    }
}
