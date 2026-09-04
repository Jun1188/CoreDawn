using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 아이콘 스프라이트를 <b>두께 있는 납작한 판</b>으로 — 벨트 위·바닥에 눕는 아이템(2026-09-04 사용자 "아이템 눕히고 두께를 줄 수 있나?").
    /// 실루엣은 Unity 의 스프라이트 타이트 메시(볼록에 가깝게 뭉개져 윤곽선이 이상했다) 대신 <b>알파 채널에서 직접</b> 뽑는다:
    /// 불투명 픽셀의 경계 변을 이어 폐곡선을 만들고(픽셀 계단), Douglas-Peucker 로 단순화한 뒤 귀 자르기(ear clipping)로 삼각화한다.
    /// 위·아래 면 + 실루엣 경계 변마다 옆면. 서브메시 0 = 위·아래 면(아이콘 텍스처, 알파 클립), 1 = 옆면(단색).
    /// 메시는 XZ 평면에 눕고 +Y 가 위, 아이콘의 위쪽이 +Z. 면 감기는 기하에서 계산해 뒤집히지 않는다.
    /// 단위는 스프라이트 단위(pixelsPerUnit) — 호출자가 스케일한다. 스프라이트·두께마다 한 번 만들어 캐시.
    /// </summary>
    public static class ItemSlabMesh
    {
        /// <summary>실루엣 단순화 허용 오차(px). 클수록 정점이 줄고 윤곽이 뭉개진다.</summary>
        public const float SimplifyPx = 1.25f;
        /// <summary>불투명 판정 알파.</summary>
        public const float AlphaCut = 0.5f;

        static readonly Dictionary<(Sprite, float), Mesh> cache = new();
        static readonly Dictionary<Texture, Material> faceMaterials = new();
        static readonly Dictionary<Texture, (int w, int h, Color32[] px)> readable = new();   // 시트별 읽을 수 있는 알파 사본
        static Material sideMaterial;

        public static Mesh Of(Sprite sprite, float thickness)
        {
            if (sprite == null) return null;
            thickness = Mathf.Max(0.0005f, thickness);
            if (cache.TryGetValue((sprite, thickness), out var cached) && cached != null) return cached;
            var mesh = Build(sprite, thickness);
            cache[(sprite, thickness)] = mesh;
            return mesh;
        }

        /// <summary>위·아래 면 재질 — 아이콘 시트 텍스처를 알파 클립으로. 시트마다 하나.</summary>
        public static Material FaceMaterial(Sprite sprite)
        {
            var tex = sprite != null ? sprite.texture : null;
            if (tex == null) return MissingAssets.Material;
            if (faceMaterials.TryGetValue(tex, out var m) && m != null) return m;
            var shader = BuiltinShaders.Of("Universal Render Pipeline/Lit");
            if (shader == null) return MissingAssets.Material;
            m = new Material(shader) { name = "ItemSlab face (" + tex.name + ")", hideFlags = HideFlags.DontSave };
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.SetFloat("_Smoothness", 0.1f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            faceMaterials[tex] = m;
            return m;
        }

        /// <summary>옆면 재질 — 어두운 단색. 아이콘 가장자리 픽셀은 알파가 0이라 텍스처를 쓸 수 없다.</summary>
        public static Material SideMaterial()
        {
            if (sideMaterial != null) return sideMaterial;
            var shader = BuiltinShaders.Of("Universal Render Pipeline/Lit");
            if (shader == null) return MissingAssets.Material;
            sideMaterial = new Material(shader) { name = "ItemSlab side", hideFlags = HideFlags.DontSave };
            sideMaterial.SetColor("_BaseColor", new Color(0.22f, 0.2f, 0.19f, 1f));
            sideMaterial.SetFloat("_Smoothness", 0.15f);
            return sideMaterial;
        }

        // ── 알파 읽기 ──────────────────────────────────────────

        /// <summary>시트의 픽셀 — 팩 텍스처는 GPU 업로드 뒤 읽을 수 없으므로 파일을 한 번 더 읽어 둔다(시트당 한 번).</summary>
        static bool TryPixels(Texture tex, out int w, out int h, out Color32[] px)
        {
            if (readable.TryGetValue(tex, out var r)) { (w, h, px) = r; return px != null; }
            w = h = 0; px = null;
            var db = SimHost.Database;
            string relative = tex != null ? tex.name : null;   // TextureOf 가 이름을 팩 상대 경로로 둔다
            if (db == null || string.IsNullOrEmpty(relative)) { readable[tex] = (0, 0, null); return false; }
            string full = PackAssets.FullPath(db.Pack, relative);
            if (!File.Exists(full)) { readable[tex] = (0, 0, null); return false; }
            var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!tmp.LoadImage(File.ReadAllBytes(full))) { Object.Destroy(tmp); readable[tex] = (0, 0, null); return false; }
            w = tmp.width; h = tmp.height; px = tmp.GetPixels32();
            Object.Destroy(tmp);
            readable[tex] = (w, h, px);
            return true;
        }

        // ── 실루엣 → 폴리곤 ───────────────────────────────────

        /// <summary>불투명 픽셀 경계 변을 이어 만든 폐곡선들(픽셀 격자 좌표, 스프라이트 rect 기준, 반시계 = 바깥 경계). 구멍(시계)은 버린다.</summary>
        static List<List<Vector2>> Outlines(Sprite sprite)
        {
            var loops = new List<List<Vector2>>();
            var rect = sprite.rect;
            int rx = Mathf.RoundToInt(rect.x), ry = Mathf.RoundToInt(rect.y), rw = Mathf.RoundToInt(rect.width), rh = Mathf.RoundToInt(rect.height);
            if (!TryPixels(sprite.texture, out int tw, out int th, out var px))
            {
                // 알파를 못 읽으면 사각형 — 그래도 판은 선다
                loops.Add(new List<Vector2> { new(0, 0), new(rw, 0), new(rw, rh), new(0, rh) });
                return loops;
            }
            bool Opaque(int x, int y)
            {
                if (x < 0 || y < 0 || x >= rw || y >= rh) return false;
                int tx = rx + x, ty = ry + y;
                if (tx < 0 || ty < 0 || tx >= tw || ty >= th) return false;
                return px[ty * tw + tx].a >= AlphaCut * 255f;
            }
            // 변: 시작점 → 끝점 (안쪽이 왼쪽이 되도록 = 바깥 경계는 반시계)
            var edges = new Dictionary<(int, int), List<(int, int)>>();
            void Edge(int x0, int y0, int x1, int y1)
            {
                if (!edges.TryGetValue((x0, y0), out var list)) edges[(x0, y0)] = list = new List<(int, int)>();
                list.Add((x1, y1));
            }
            for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                {
                    if (!Opaque(x, y)) continue;
                    if (!Opaque(x, y - 1)) Edge(x, y, x + 1, y);           // 아래 변 →
                    if (!Opaque(x + 1, y)) Edge(x + 1, y, x + 1, y + 1);   // 오른 변 ↑
                    if (!Opaque(x, y + 1)) Edge(x + 1, y + 1, x, y + 1);   // 위 변 ←
                    if (!Opaque(x - 1, y)) Edge(x, y + 1, x, y);           // 왼 변 ↓
                }
            while (edges.Count > 0)
            {
                (int, int) start = default;
                foreach (var k in edges.Keys) { start = k; break; }
                var loop = new List<Vector2>();
                var cur = start;
                int guard = 0;
                while (guard++ < 1_000_000)
                {
                    if (!edges.TryGetValue(cur, out var outs) || outs.Count == 0) break;
                    var next = outs[outs.Count - 1]; outs.RemoveAt(outs.Count - 1);   // 대각 접점(출구 둘)은 아무거나 — 아이콘엔 드물다
                    if (outs.Count == 0) edges.Remove(cur);
                    loop.Add(new Vector2(cur.Item1, cur.Item2));
                    cur = next;
                    if (cur == start) break;
                }
                if (loop.Count >= 3 && SignedArea(loop) > 0f) loops.Add(Simplify(loop, SimplifyPx));   // 시계(구멍)는 버림 — 판은 채운다
            }
            if (loops.Count == 0) loops.Add(new List<Vector2> { new(0, 0), new(rw, 0), new(rw, rh), new(0, rh) });
            return loops;
        }

        static float SignedArea(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) a += (p[j].x * p[i].y - p[i].x * p[j].y);
            return a * 0.5f;
        }

        /// <summary>Douglas-Peucker — 폐곡선은 가장 먼 두 점에서 갈라 두 열린 곡선으로 단순화한 뒤 잇는다.</summary>
        static List<Vector2> Simplify(List<Vector2> loop, float eps)
        {
            if (loop.Count < 8) return loop;
            int a = 0, b = 0; float far = -1f;
            for (int i = 1; i < loop.Count; i++) { float d = (loop[i] - loop[0]).sqrMagnitude; if (d > far) { far = d; b = i; } }
            var first = new List<Vector2>(); var second = new List<Vector2>();
            for (int i = a; i <= b; i++) first.Add(loop[i]);
            for (int i = b; i < loop.Count; i++) second.Add(loop[i]);
            second.Add(loop[0]);
            var s1 = DP(first, eps); var s2 = DP(second, eps);
            var outp = new List<Vector2>(s1.Count + s2.Count);
            outp.AddRange(s1);
            for (int i = 1; i < s2.Count - 1; i++) outp.Add(s2[i]);
            return outp.Count >= 3 ? outp : loop;
        }

        static List<Vector2> DP(List<Vector2> pts, float eps)
        {
            if (pts.Count < 3) return new List<Vector2>(pts);
            var keep = new bool[pts.Count]; keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<(int, int)>(); stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (s, e) = stack.Pop();
                float best = -1f; int idx = -1;
                Vector2 a = pts[s], b = pts[e]; Vector2 ab = b - a; float len2 = ab.sqrMagnitude;
                for (int i = s + 1; i < e; i++)
                {
                    float d;
                    if (len2 < 1e-8f) d = (pts[i] - a).magnitude;
                    else { float t = Mathf.Clamp01(Vector2.Dot(pts[i] - a, ab) / len2); d = (pts[i] - (a + ab * t)).magnitude; }
                    if (d > best) { best = d; idx = i; }
                }
                if (idx >= 0 && best > eps) { keep[idx] = true; stack.Push((s, idx)); stack.Push((idx, e)); }
            }
            var r = new List<Vector2>();
            for (int i = 0; i < pts.Count; i++) if (keep[i]) r.Add(pts[i]);
            return r;
        }

        /// <summary>귀 자르기 — 단순 다각형(반시계) 삼각화. 인덱스 삼중항을 돌려준다.</summary>
        static List<int> Triangulate(List<Vector2> poly)
        {
            var tris = new List<int>();
            var idx = new List<int>(); for (int i = 0; i < poly.Count; i++) idx.Add(i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool cut = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count], i1 = idx[i], i2 = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[i0], b = poly[i1], c = poly[i2];
                    if (Cross(b - a, c - a) <= 1e-6f) continue;   // 오목한 귀(반시계 기준)
                    bool inside = false;
                    for (int k = 0; k < idx.Count && !inside; k++)
                    {
                        int q = idx[k]; if (q == i0 || q == i1 || q == i2) continue;
                        if (PointInTri(poly[q], a, b, c)) inside = true;
                    }
                    if (inside) continue;
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    idx.RemoveAt(i); cut = true; break;
                }
                if (!cut) break;   // 퇴화 — 남은 것은 부채꼴로
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            else for (int i = 1; i + 1 < idx.Count; i++) { tris.Add(idx[0]); tris.Add(idx[i]); tris.Add(idx[i + 1]); }
            return tris;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        // ── 메시 ───────────────────────────────────────────────

        static Mesh Build(Sprite sprite, float thickness)
        {
            float h = thickness * 0.5f;
            float ppu = sprite.pixelsPerUnit;
            var rect = sprite.rect; var pivot = sprite.pivot;   // 픽셀, rect 기준
            var tex = sprite.texture;
            float tw = tex != null ? tex.width : rect.width, th = tex != null ? tex.height : rect.height;

            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>();
            var faceTris = new List<int>(); var sideTris = new List<int>();

            Vector3 P(Vector2 px, float y) => new((px.x - pivot.x) / ppu, y, (px.y - pivot.y) / ppu);   // 아이콘 y → 월드 z
            Vector2 UV(Vector2 px) => new((rect.x + px.x) / tw, (rect.y + px.y) / th);

            void AddTri(int a, int b, int c, Vector3 want, List<int> into)
            {
                // Unity(왼손 좌표계): 앞면 법선 = Cross(b-a, c-a). 원하는 쪽을 향하도록 감기를 고른다 — 스프라이트 감기 규약에 안 기댄다
                var n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                if (Vector3.Dot(n, want) >= 0f) { into.Add(a); into.Add(b); into.Add(c); }
                else { into.Add(a); into.Add(c); into.Add(b); }
            }

            foreach (var loop in Outlines(sprite))
            {
                var tri = Triangulate(loop);
                int n = loop.Count;
                int top = verts.Count;
                for (int i = 0; i < n; i++) { verts.Add(P(loop[i], h)); norms.Add(Vector3.up); uvs.Add(UV(loop[i])); }
                int bottom = verts.Count;
                for (int i = 0; i < n; i++) { verts.Add(P(loop[i], -h)); norms.Add(Vector3.down); uvs.Add(UV(loop[i])); }
                for (int t = 0; t < tri.Count; t += 3)
                {
                    AddTri(top + tri[t], top + tri[t + 1], top + tri[t + 2], Vector3.up, faceTris);
                    AddTri(bottom + tri[t], bottom + tri[t + 1], bottom + tri[t + 2], Vector3.down, faceTris);
                }
                // 옆면 — 반시계 폐곡선의 변 a→b 는 안쪽이 왼쪽, 바깥 법선은 오른쪽 (dy, -dx)
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = loop[i], b = loop[(i + 1) % n];
                    Vector2 d = b - a; if (d.sqrMagnitude < 1e-8f) continue;
                    Vector3 nrm = new Vector3(d.y, 0f, -d.x).normalized;
                    int i0 = verts.Count;
                    verts.Add(P(a, h)); verts.Add(P(b, h)); verts.Add(P(b, -h)); verts.Add(P(a, -h));
                    for (int k = 0; k < 4; k++) { norms.Add(nrm); uvs.Add(Vector2.zero); }
                    AddTri(i0, i0 + 1, i0 + 2, nrm, sideTris);
                    AddTri(i0, i0 + 2, i0 + 3, nrm, sideTris);
                }
            }

            var mesh = new Mesh { name = "ItemSlab " + sprite.name, hideFlags = HideFlags.DontSave };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(faceTris, 0);
            mesh.SetTriangles(sideTris, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>에디터 도구용 — 캐시를 버린다(팩 리로드).</summary>
        public static void Clear()
        {
            foreach (var m in cache.Values) if (m != null) Object.Destroy(m);
            cache.Clear();
            foreach (var m in faceMaterials.Values) if (m != null) Object.Destroy(m);
            faceMaterials.Clear();
            readable.Clear();
            if (sideMaterial != null) Object.Destroy(sideMaterial);
            sideMaterial = null;
        }
    }
}
