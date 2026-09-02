using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Worlds
{
    /// <summary>
    /// 절벽 세우기 — 구 에디터 생성기(WorldTerrainGenerator)의 벽 배치를 런타임으로 포팅(5a-4e ③).
    ///
    /// 원리 그대로: 절벽 경계를 마칭 스퀘어로 <b>순서 있는 폴리라인</b>으로 뽑아 호길이를 따라
    /// 벽 조각(프리팹, 한 개가 벽 높이 통째)을 이어 붙인다 — 래스터 순서로 놓으면 굽은 곳이
    /// 쐐기꼴로 벌어지던 문제의 해법이 "경계 걷기"였다. 조각의 네 귀퉁이는 절벽 타일 안으로
    /// 밀어 넣는다(절벽 밖은 건설·통행 칸이라 걸치면 길이 막힌다). 발치는 반쯤 묻힌 납작한
    /// 판을 경계 바깥 띠에 흩는다. 볼록 메시 콜라이더 — 길찾기는 타일을 보지만 플레이어는
    /// 물리로 막혀야 한다.
    /// </summary>
    public static class WorldTerrainCliffs
    {
        const float InsetLimitM = 8f;   // 앞면을 안쪽으로 밀어 볼 수 있는 한계 — 더 들어가면 벽이 아니라 언덕

        readonly struct WallPiece
        {
            public readonly GameObject Prefab;
            public readonly Vector3 Centre;
            public readonly Vector3 Size;
            public readonly float Cover;     // 실제로 벽이 되는 폭(높이 겹별 단면 폭의 최솟값)

            public float Width => Size.x;
            public float Depth => Size.z;
            public float FrontReach => Size.z * 0.5f - Centre.z;
            public float Bottom => Centre.y - Size.y * 0.5f;

            public WallPiece(GameObject p, Vector3 c, Vector3 s, float cover)
            { Prefab = p; Centre = c; Size = s; Cover = cover; }
        }

        /// <summary>벽·발치를 세운다. 반환은 (벽, 발치) 개수.</summary>
        public static (int walls, int feet) Build(Transform root, World world, MapDef map, TerrainForm form, TerrainGenSettings s)
        {
            float cell = world.CellSize;
            var walls = MeasureSet(s.cliffWallSet);
            if (walls.Count == 0)
            {
                Debug.LogWarning("[WorldTerrain] 절벽 벽 조각이 없습니다 — Terrain Gen Settings의 Cliff Wall Set 확인.");
                return (0, 0);
            }
            walls.Sort((a, b) => b.Width.CompareTo(a.Width));

            var field = form.CliffFieldRaw;
            int fw = field.GetLength(0), fh = field.GetLength(1);
            float px = cell / form.FieldSubDiv;
            Vector3 origin = world.CellToWorld(Vector2Int.zero);

            float FieldAt(Vector2 p) => form.CliffDistance((p.x - origin.x) / cell, (p.y - origin.z) / cell);

            bool OnCliff(Vector2 wp)
            {
                int cx = Mathf.FloorToInt((wp.x - origin.x) / cell);
                int cy = Mathf.FloorToInt((wp.y - origin.z) / cell);
                return map.InBounds(cx, cy) && map.TileAt(cx, cy) == MapTile.Cliff;
            }

            float GroundAt(Vector2 p) => world.Origin.y + form.Height((p.x - origin.x) / cell, (p.y - origin.z) / cell);

            var loops = TraceContours(field, fw, fh, px, origin);
            var parent = new GameObject("Cliffs").transform;
            parent.SetParent(root, false);

            float overlap = Mathf.Clamp01(s.cliffWallOverlap);
            int placedCount = 0;

            float wSum = 0f;
            foreach (var w in walls) wSum += w.Width;

            foreach (var line in loops)
            {
                int n = line.Count;
                var cum = new float[n];
                for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector2.Distance(line[i - 1], line[i]);
                float total = cum[n - 1];
                if (total < 2f) continue;

                Vector2 PosAt(float t)
                {
                    t = Mathf.Clamp(t, 0f, total);
                    int i = 1;
                    while (i < n - 1 && cum[i] < t) i++;
                    float seg = Mathf.Max(1e-4f, cum[i] - cum[i - 1]);
                    return Vector2.Lerp(line[i - 1], line[i], (t - cum[i - 1]) / seg);
                }

                // 모서리 감지 — 꺾인 곳을 넘어가는 조각을 만들지 않는다(구 생성기 주석 참조)
                var corners = new List<float>();
                {
                    float lookM = Mathf.Max(1f, s.cliffWallCornerLookM);
                    for (int i = 1; i < n - 1; i++)
                    {
                        var a0 = PosAt(cum[i] - lookM) - line[i];
                        var a1 = PosAt(cum[i] + lookM) - line[i];
                        if (a0.sqrMagnitude < 1e-6f || a1.sqrMagnitude < 1e-6f) continue;
                        if (Vector2.Angle(-a0, a1) > s.cliffWallCornerDeg) corners.Add(cum[i]);
                    }
                    var merged = new List<float>();
                    foreach (var c in corners)
                        if (merged.Count == 0 || c - merged[merged.Count - 1] > lookM) merged.Add(c);
                    corners = merged;
                }
                int cornerIdx = 0;

                bool Inside(Vector3 pivot, Quaternion prot, WallPiece w, float sc)
                {
                    float hx = w.Width * 0.5f * sc, hz = w.Depth * 0.5f * sc;
                    var c3 = pivot + prot * new Vector3(w.Centre.x * sc, 0f, w.Centre.z * sc);
                    var tanW3 = prot * Vector3.right; var depW3 = prot * Vector3.forward;
                    var c = new Vector2(c3.x, c3.z);
                    var tanW = new Vector2(tanW3.x, tanW3.z); var depW = new Vector2(depW3.x, depW3.z);
                    for (int sx = -1; sx <= 1; sx++)
                        for (int sz = -1; sz <= 1; sz += 2)
                            if (!OnCliff(c + tanW * (hx * sx) + depW * (hz * sz))) return false;
                    return true;
                }

                for (float t = 0f; t < total; )
                {
                    var p = PosAt(t);
                    int seedX = Mathf.RoundToInt(p.x * 4f), seedY = Mathf.RoundToInt(p.y * 4f);

                    float scale = Mathf.Lerp(s.cliffWallScale.x, s.cliffWallScale.y,
                                             Hash(seedX, seedY, 71) % 1000 / 1000f);

                    // 뽑기는 폭 가중 — 균등하면 폭 좁은 기둥이 절반을 차지해 빗처럼 보인다
                    float roll = Hash(seedX, seedY, 131) % 1000 / 1000f * wSum;
                    int pick = walls.Count - 1;
                    for (int i = 0; i < walls.Count; i++) { roll -= walls[i].Width; if (roll <= 0f) { pick = i; break; } }
                    var piece = walls[pick];

                    while (cornerIdx < corners.Count && corners[cornerIdx] <= t + 0.01f) cornerIdx++;
                    float limit = cornerIdx < corners.Count ? corners[cornerIdx] - t : total - t;

                    float span = piece.Cover * scale;
                    if (span > limit)
                    {
                        scale = Mathf.Max(s.cliffWallScale.x, limit / piece.Cover);
                        span = piece.Cover * scale;
                    }
                    float step = span * (1f - overlap);

                    var pEnd = PosAt(t + span);
                    var chord = pEnd - p;
                    if (chord.sqrMagnitude < 1e-6f) chord = PosAt(t + 1f) - p;
                    var tan = chord.normalized;

                    var nrm = new Vector2(tan.y, -tan.x);
                    var mid = Vector2.Lerp(p, pEnd, 0.5f);
                    if (FieldAt(mid + nrm * cell * 0.5f) < FieldAt(mid - nrm * cell * 0.5f)) nrm = -nrm;

                    var look = Quaternion.LookRotation(new Vector3(-nrm.x, 0f, -nrm.y), Vector3.up);
                    float yaw = look.eulerAngles.y
                              + (Hash(seedX, seedY, 89) % 1000 / 1000f - 0.5f) * 2f * s.cliffWallYawJitter;
                    var rot = Quaternion.Euler(0f, yaw, 0f);
                    var dir = rot * Vector3.back;

                    Vector3 pos = Vector3.zero;
                    float useScale = scale;
                    bool seated = false;
                    float minScale = s.cliffWallScale.x * s.cliffWallMinShrink;

                    for (float sc = scale; sc >= minScale - 1e-4f && !seated; sc *= 0.85f)
                    {
                        for (float inset = s.cliffWallOverhangM; inset > -InsetLimitM; inset -= 0.4f)
                        {
                            Vector3 f3 = new Vector3(mid.x, 0f, mid.y) + dir * inset;
                            var pv = f3 - dir * (piece.FrontReach * sc);
                            if (!Inside(pv, rot, piece, sc)) continue;
                            pos = pv; useScale = sc; seated = true;
                            break;
                        }
                    }

                    if (!seated)
                    {
                        // 띠가 조각 깊이보다 얇다 — 앞모서리만 지키고 뒤는 넘어가게 둔다
                        for (float sc = scale; sc >= minScale - 1e-4f && !seated; sc *= 0.85f)
                        {
                            float hx = piece.Width * 0.5f * sc;
                            var tanW3 = rot * Vector3.right;
                            var tanW = new Vector2(tanW3.x, tanW3.z);
                            for (float inset = s.cliffWallOverhangM; inset > -InsetLimitM; inset -= 0.4f)
                            {
                                Vector3 f3 = new Vector3(mid.x, 0f, mid.y) + dir * inset;
                                var pv = f3 - dir * (piece.FrontReach * sc);
                                var fc = pv + rot * new Vector3(piece.Centre.x * sc, 0f,
                                                                (piece.Centre.z - piece.Size.z * 0.5f) * sc);
                                var fc2 = new Vector2(fc.x, fc.z);
                                bool all = true;
                                for (int k = -1; k <= 1 && all; k++) if (!OnCliff(fc2 + tanW * (hx * k))) all = false;
                                if (!all) continue;
                                pos = pv; useScale = sc; seated = true;
                                break;
                            }
                        }
                    }

                    if (!seated) { t += Mathf.Max(0.5f, step); continue; }   // 경계가 타일 밖으로 튄 곳 — 놓으면 길을 막는다

                    pos.y = GroundAt(mid) - s.cliffWallSinkM - piece.Bottom * useScale;

                    var go = Object.Instantiate(piece.Prefab, parent);
                    go.transform.SetPositionAndRotation(pos, rot);
                    go.transform.localScale = Vector3.one * useScale;
                    AddConvexCollider(go);

                    placedCount++;
                    t += Mathf.Max(0.5f, piece.Cover * useScale * (1f - overlap));
                }
            }

            int foot = PlaceFoot(field, fw, fh, px, origin, GroundAt, parent, s);
            return (placedCount, foot);
        }

        /// <summary>발치 — 반쯤 묻힌 납작한 판을 경계 바깥 띠에 흩는다.</summary>
        static int PlaceFoot(float[,] field, int fw, int fh, float px, Vector3 origin,
                             System.Func<Vector2, float> GroundAt, Transform parent, TerrainGenSettings s)
        {
            var rocks = MeasureSet(s.cliffFootSet);
            if (rocks.Count == 0 || s.cliffFootDensity <= 0f) return 0;

            float band = Mathf.Max(0.05f, s.cliffFootBandCells);
            int gate = Mathf.RoundToInt(Mathf.Clamp01(s.cliffFootDensity) * 1000f);
            var placed = new List<(Vector2 c, float r)>();
            int count = 0;

            for (int fy = 0; fy < fh; fy++)
                for (int fx = 0; fx < fw; fx++)
                {
                    float v = field[fx, fy];
                    if (v < -band * 0.4f || v > band) continue;
                    if (Hash(fx, fy, 307) % 1000 >= gate) continue;

                    var p = new Vector2(origin.x + (fx + 0.5f) * px, origin.z + (fy + 0.5f) * px);
                    float scale = Mathf.Lerp(s.cliffFootScale.x, s.cliffFootScale.y,
                                             Hash(fx, fy, 311) % 1000 / 1000f);

                    int pick = (int)(Hash(fx, fy, 23) % (uint)rocks.Count);
                    var rock = rocks[pick];
                    float r = 0.5f * Mathf.Max(rock.Size.x, rock.Size.z) * scale;

                    bool clash = false;
                    foreach (var o in placed)
                    {
                        float min = (r + o.r) * s.cliffFootSpacing;
                        if ((o.c - p).sqrMagnitude < min * min) { clash = true; break; }
                    }
                    if (clash) continue;
                    placed.Add((p, r));

                    float yaw = Hash(fx, fy, 313) % 3600 / 10f;
                    var go = Object.Instantiate(rock.Prefab, parent);
                    go.transform.SetPositionAndRotation(
                        new Vector3(p.x, GroundAt(p) - s.cliffFootSinkM - rock.Bottom * scale, p.y),
                        Quaternion.Euler(0f, yaw, 0f));
                    go.transform.localScale = Vector3.one * scale;
                    count++;
                }

            return count;
        }

        // ── 실측·기하 (구 생성기 포팅) ──────────────────────────────

        static List<WallPiece> MeasureSet(GameObject[] set)
        {
            var list = new List<WallPiece>();
            if (set == null) return list;

            foreach (var pf in set)
            {
                if (pf == null) continue;
                var inst = Object.Instantiate(pf);
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                inst.transform.localScale = Vector3.one;

                var rends = inst.GetComponentsInChildren<MeshRenderer>();
                if (rends.Length == 0) { Object.DestroyImmediate(inst); continue; }

                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                float cover = CoverWidth(inst, b);
                Object.DestroyImmediate(inst);

                if (b.size.x < 0.01f || b.size.z < 0.01f) continue;
                list.Add(new WallPiece(pf, b.center, b.size, cover));
            }
            return list;
        }

        /// <summary>높이를 겹으로 잘라 단면 폭의 최솟값 — 벽으로 쓸 수 있는 폭은 제일 좁은 데가 정한다.</summary>
        static float CoverWidth(GameObject inst, Bounds b)
        {
            const int Slices = 6;
            var lo = new float[Slices];
            var hi = new float[Slices];
            for (int i = 0; i < Slices; i++) { lo[i] = float.MaxValue; hi[i] = float.MinValue; }

            float y0 = b.min.y, h = Mathf.Max(0.01f, b.size.y);
            const float top = 0.7f;   // 위 30%는 스카이라인 몫

            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var verts = mf.sharedMesh.vertices;
                var xf = mf.transform;
                for (int i = 0; i < verts.Length; i++)
                {
                    var w = xf.TransformPoint(verts[i]);
                    float f = (w.y - y0) / h;
                    if (f < 0f || f > top) continue;
                    int k = Mathf.Clamp((int)(f / top * Slices), 0, Slices - 1);
                    if (w.x < lo[k]) lo[k] = w.x;
                    if (w.x > hi[k]) hi[k] = w.x;
                }
            }

            float best = float.MaxValue;
            for (int i = 0; i < Slices; i++)
            {
                if (lo[i] > hi[i]) continue;
                best = Mathf.Min(best, hi[i] - lo[i]);
            }
            return best == float.MaxValue ? b.size.x : best;
        }

        /// <summary>절벽 경계를 순서 있는 폴리라인으로 — 마칭 스퀘어(구 생성기 그대로).</summary>
        static List<List<Vector2>> TraceContours(float[,] field, int fw, int fh, float px, Vector3 origin)
        {
            var segs = new List<(Vector2 a, Vector2 b)>();

            Vector2 P(int x, int y) => new Vector2(origin.x + (x + 0.5f) * px, origin.z + (y + 0.5f) * px);
            Vector2 Lerp(Vector2 p0, float v0, Vector2 p1, float v1)
            {
                float t = Mathf.Abs(v0 - v1) < 1e-6f ? 0.5f : Mathf.Clamp01(v0 / (v0 - v1));
                return Vector2.Lerp(p0, p1, t);
            }

            for (int y = 0; y < fh - 1; y++)
                for (int x = 0; x < fw - 1; x++)
                {
                    float v00 = field[x, y], v10 = field[x + 1, y];
                    float v11 = field[x + 1, y + 1], v01 = field[x, y + 1];

                    int k = (v00 <= 0f ? 1 : 0) | (v10 <= 0f ? 2 : 0) | (v11 <= 0f ? 4 : 0) | (v01 <= 0f ? 8 : 0);
                    if (k == 0 || k == 15) continue;

                    Vector2 p00 = P(x, y), p10 = P(x + 1, y), p11 = P(x + 1, y + 1), p01 = P(x, y + 1);
                    Vector2 eB = Lerp(p00, v00, p10, v10);
                    Vector2 eR = Lerp(p10, v10, p11, v11);
                    Vector2 eT = Lerp(p01, v01, p11, v11);
                    Vector2 eL = Lerp(p00, v00, p01, v01);

                    switch (k)
                    {
                        case 1: case 14: segs.Add((eL, eB)); break;
                        case 2: case 13: segs.Add((eB, eR)); break;
                        case 3: case 12: segs.Add((eL, eR)); break;
                        case 4: case 11: segs.Add((eR, eT)); break;
                        case 6: case 9: segs.Add((eB, eT)); break;
                        case 7: case 8: segs.Add((eL, eT)); break;
                        case 5: segs.Add((eL, eB)); segs.Add((eR, eT)); break;
                        case 10: segs.Add((eL, eT)); segs.Add((eB, eR)); break;
                    }
                }

            float q = px * 0.01f;
            Vector2Int Key(Vector2 v) => new Vector2Int(Mathf.RoundToInt(v.x / q), Mathf.RoundToInt(v.y / q));

            var at = new Dictionary<Vector2Int, List<int>>();
            void Reg(Vector2 v, int i)
            {
                var kk = Key(v);
                if (!at.TryGetValue(kk, out var l)) at[kk] = l = new List<int>();
                l.Add(i);
            }
            for (int i = 0; i < segs.Count; i++) { Reg(segs[i].a, i); Reg(segs[i].b, i); }

            var used = new bool[segs.Count];
            var loops = new List<List<Vector2>>();

            for (int i = 0; i < segs.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;

                var line = new List<Vector2> { segs[i].a, segs[i].b };

                for (int side = 0; side < 2; side++)
                {
                    while (true)
                    {
                        Vector2 tip = side == 0 ? line[line.Count - 1] : line[0];
                        if (!at.TryGetValue(Key(tip), out var cand)) break;

                        int next = -1;
                        foreach (int j in cand) if (!used[j]) { next = j; break; }
                        if (next < 0) break;

                        used[next] = true;
                        var seg = segs[next];
                        Vector2 far = Key(seg.a) == Key(tip) ? seg.b : seg.a;
                        if (side == 0) line.Add(far); else line.Insert(0, far);
                    }
                }

                if (line.Count >= 3) loops.Add(line);
            }

            return loops;
        }

        static void AddConvexCollider(GameObject go)
        {
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mc = mf.gameObject.GetComponent<MeshCollider>();
                if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = true;
            }
        }

        /// <summary>좌표를 잘 섞인 값으로 — 좌표 곱셈 합은 대각선 줄무늬를 만든다(구 생성기 그대로).</summary>
        internal static int Hash(int x, int y, int salt)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + salt * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) & 0x7fffffff;
            }
        }
    }
}
