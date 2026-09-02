using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Worlds
{
    /// <summary>
    /// 풀·꽃 — Unity Terrain의 디테일 시스템 대체(5a-4e ④). 심는 규칙은 구 생성기(PaintDetails) 그대로:
    /// 물가 위·완경사 지면에만, 절벽 타일은 경계 앞줄만, 풀은 빈 곳 없이 밀도만 흔들고 꽃은 흩뿌린다.
    ///
    /// 그리는 방식은 Terrain과 같은 원리다 — 카메라 근처 청크만, 행렬 배열로 <see cref="Graphics.DrawMeshInstanced"/>.
    /// 배치는 좌표 해시로 결정적이라 저장물이 없고, 청크가 시야에 들어올 때 만들어 캐시하고 멀어지면 버린다.
    /// 콜라이더 없음 — 길찾기·건설·사격 판정에 관여하지 않는다(구 시스템과 동일).
    /// </summary>
    [ExecuteAlways]   // 에디터에서 지형을 미리 세워 볼 때도 풀이 보이게
    public sealed class WorldTerrainGrass : MonoBehaviour
    {
        const int ChunkCells = 8;            // WorldTerrainBuilder와 같은 청크 격자
        const int MaxCachedChunks = 64;

        struct Proto { public Mesh mesh; public Material mat; public Vector2 size; }

        World world;
        MapDef map;
        TerrainForm form;
        TerrainGenSettings s;

        Proto[] grass, flowers;
        int chunksX, chunksY;
        float chunkSize;

        readonly Dictionary<int, List<Matrix4x4>[]> cache = new Dictionary<int, List<Matrix4x4>[]>();
        readonly List<int> cacheOrder = new List<int>();
        readonly Matrix4x4[] batch = new Matrix4x4[1023];

        public static WorldTerrainGrass Attach(GameObject root, World world, MapDef map, TerrainForm form, TerrainGenSettings s)
        {
            var g = root.AddComponent<WorldTerrainGrass>();
            g.world = world; g.map = map; g.form = form; g.s = s;
            g.grass = Protos(s.grassSet, s.grassSize);
            g.flowers = Protos(s.flowerSet, s.flowerSize);
            g.chunksX = Mathf.CeilToInt(map.width / (float)ChunkCells);
            g.chunksY = Mathf.CeilToInt(map.height / (float)ChunkCells);
            g.chunkSize = ChunkCells * world.CellSize;
            if (g.grass.Length == 0)
                Debug.LogWarning("[WorldTerrain] 풀 프리팹이 하나도 없습니다 — TerrainGenSettings의 Grass Set 확인.");
            return g;
        }

        static Proto[] Protos(GameObject[] set, Vector2 size)
        {
            var list = new List<Proto>();
            if (set == null) return list.ToArray();
            foreach (var p in set)
            {
                if (p == null) continue;
                var mf = p.GetComponentInChildren<MeshFilter>();
                var mr = p.GetComponentInChildren<MeshRenderer>();
                if (mf == null || mr == null || mf.sharedMesh == null) continue;
                var mat = mr.sharedMaterial;
                if (mat != null && !mat.enableInstancing) mat.enableInstancing = true;   // 수만 포기 — 인스턴싱 필수
                list.Add(new Proto { mesh = mf.sharedMesh, mat = mat, size = size });
            }
            return list.ToArray();
        }

        void LateUpdate()
        {
            if (grass.Length == 0) return;
            var cam = Camera.main;
#if UNITY_EDITOR
            if (cam == null && UnityEditor.SceneView.lastActiveSceneView != null)
                cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            if (cam == null) return;

            float range = s.detailDistance + chunkSize;
            Vector3 eye = cam.transform.position;

            int ci0 = Mathf.Max(0, Mathf.FloorToInt(((eye.x - world.Origin.x) - range) / chunkSize));
            int ci1 = Mathf.Min(chunksX - 1, Mathf.FloorToInt(((eye.x - world.Origin.x) + range) / chunkSize));
            int cj0 = Mathf.Max(0, Mathf.FloorToInt(((eye.z - world.Origin.z) - range) / chunkSize));
            int cj1 = Mathf.Min(chunksY - 1, Mathf.FloorToInt(((eye.z - world.Origin.z) + range) / chunkSize));

            for (int j = cj0; j <= cj1; j++)
                for (int i = ci0; i <= ci1; i++)
                {
                    Vector3 centre = world.Origin + new Vector3((i + 0.5f) * chunkSize, 0f, (j + 0.5f) * chunkSize);
                    float dx = Mathf.Abs(centre.x - eye.x), dz = Mathf.Abs(centre.z - eye.z);
                    if (dx > range || dz > range) continue;

                    var lists = ChunkInstances(j * chunksX + i, i, j);
                    for (int proto = 0; proto < lists.Length; proto++)
                    {
                        var pr = proto < grass.Length ? grass[proto] : flowers[proto - grass.Length];
                        var l = lists[proto];
                        for (int at = 0; at < l.Count; at += batch.Length)
                        {
                            int n = Mathf.Min(batch.Length, l.Count - at);
                            l.CopyTo(at, batch, 0, n);
                            Graphics.DrawMeshInstanced(pr.mesh, 0, pr.mat, batch, n,
                                null, UnityEngine.Rendering.ShadowCastingMode.Off, false, gameObject.layer);
                        }
                    }
                }
        }

        /// <summary>청크의 풀·꽃 행렬 — 좌표 해시로 결정적이라 언제 다시 만들어도 같은 배치가 나온다.</summary>
        List<Matrix4x4>[] ChunkInstances(int key, int ci, int cj)
        {
            if (cache.TryGetValue(key, out var cached)) return cached;

            var lists = new List<Matrix4x4>[grass.Length + flowers.Length];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<Matrix4x4>();

            float cell = world.CellSize;
            float grassWaterLine = s.waterLevel + s.grassWaterLineOffset;
            float pointM = Mathf.Max(0.25f, s.detailPointM);
            int points = Mathf.Max(1, Mathf.RoundToInt(chunkSize / pointM));

            for (int pj = 0; pj < points; pj++)
                for (int pi = 0; pi < points; pi++)
                {
                    // 심는 점(타일 좌표) + 지터 — 격자가 그대로 보이지 않게
                    int hx = ci * points + pi, hy = cj * points + pj;
                    float jx = WorldTerrainCliffs.Hash(hx, hy, 611) % 1000 / 1000f - 0.5f;
                    float jy = WorldTerrainCliffs.Hash(hx, hy, 613) % 1000 / 1000f - 0.5f;
                    float tx = ci * ChunkCells + (pi + 0.5f + jx * 0.9f) / points * ChunkCells;
                    float ty = cj * ChunkCells + (pj + 0.5f + jy * 0.9f) / points * ChunkCells;

                    int cxi = Mathf.FloorToInt(tx), cyi = Mathf.FloorToInt(ty);
                    if (!map.InBounds(cxi, cyi)) continue;

                    // 절벽 타일에도 그대로 심는다 — 바위 틈이라고 잔디가 안 자라는 게 아니다(사용자).
                    // 구 생성기의 "경계 앞줄만" 규칙은 벽이 빈틈없다는 가정의 절약이었는데, 그 절약이
                    // 마당 관리한 것처럼 잘린 선을 만들었다. 벽에 가린 포기는 어차피 안 보인다.

                    float y = form.Height(tx, ty);
                    if (y < grassWaterLine) continue;   // 물가 아래는 비운다 — 끊기는 선이 물가 곡선을 따라간다

                    // 경사 — 내륙은 완전 평면이라 물가 근처에서만 실제로 걸린다
                    if (y < -0.001f)
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
                        int gi = WorldTerrainCliffs.Hash(hx, hy, 17 + a) % grass.Length;
                        float sc = Mathf.Lerp(grass[gi].size.x, grass[gi].size.y,
                                              WorldTerrainCliffs.Hash(hx, hy, 41 + a) % 1000 / 1000f);
                        float yaw = WorldTerrainCliffs.Hash(hx, hy, 59 + a) % 3600 / 10f;
                        float ox = (WorldTerrainCliffs.Hash(hx, hy, 71 + a) % 1000 / 1000f - 0.5f) * pointM;
                        float oz = (WorldTerrainCliffs.Hash(hx, hy, 73 + a) % 1000 / 1000f - 0.5f) * pointM;
                        lists[gi].Add(Matrix4x4.TRS(pos + new Vector3(ox, 0f, oz),
                                                    Quaternion.Euler(0f, yaw, 0f), Vector3.one * sc));
                    }

                    // 꽃 — 셀 단위로 흩뿌린다(칸 단위로 고르면 줄무늬가 된다 — 구 생성기 교훈)
                    if (flowers.Length > 0)
                    {
                        float bloom = Mathf.PerlinNoise(tx * 0.04f + 31.7f, ty * 0.04f + 12.9f);
                        if (bloom > 0.58f && WorldTerrainCliffs.Hash(hx, hy, 91) % 8 == 0)
                        {
                            int fi = WorldTerrainCliffs.Hash(hx, hy, 53) % flowers.Length;
                            float sc = Mathf.Lerp(flowers[fi].size.x, flowers[fi].size.y,
                                                  WorldTerrainCliffs.Hash(hx, hy, 43) % 1000 / 1000f);
                            float yaw = WorldTerrainCliffs.Hash(hx, hy, 61) % 3600 / 10f;
                            lists[grass.Length + fi].Add(Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one * sc));
                        }
                    }
                }

            cache[key] = lists;
            cacheOrder.Add(key);
            if (cacheOrder.Count > MaxCachedChunks)
            {
                cache.Remove(cacheOrder[0]);
                cacheOrder.RemoveAt(0);
            }
            return lists;
        }
    }
}
