using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Worlds
{
    /// <summary>
    /// 지형의 <b>형상 수학</b> — 맵(타일)에서 거리장을 뜨고, 높이를 해석 함수로 답한다(5a-4e).
    ///
    /// 구 에디터 생성기(WorldTerrainGenerator)의 절차에서 <b>도메인 워핑과 미세 노이즈를 뺀</b> 것이다
    /// (사용자 결정 2026-09-02: "일단 빼고 필요하면 다시 넣는다"). 그러면 높이가
    /// 순수하게 "강까지의 거리 → 파임 곡선(Submerge)"의 함수가 되어, 저장할 높이맵이 없고
    /// 어떤 좌표에서든 즉석 계산이 된다. 지면은 완전한 평면(0), 절벽 칸도 평면(벽은 프리팹의 몫).
    ///
    /// 좌표는 전부 <b>타일(칸) 단위</b>다 — 셀 크기를 바꿔도 물가의 실제 형상(m)이 유지되도록
    /// 미터 선언 상수를 칸으로 환산해 들고 있는다(구 생성기의 스케일 환산 규칙 그대로).
    /// </summary>
    public sealed class TerrainForm
    {
        public readonly MapDef Map;
        public readonly TerrainGenSettings S;
        public readonly float Cell;

        readonly float[,] riverField;   // 강까지의 부호 거리(칸) — 안이 음수
        readonly float[,] cliffField;   // 절벽까지 — 절벽 배치·표면 칠(rock 가중치)이 쓴다
        readonly int fieldSubDiv;

        readonly float riverFalloff, shelfWidth, shapeInset;

        /// <summary>물가선의 강 거리장 값(칸) — 이 등고선 위에서 높이 == 수면(waterLevel).</summary>
        public readonly float WaterlineIso;

        TerrainForm(MapDef map, TerrainGenSettings s, float cell)
        {
            Map = map; S = s; Cell = cell;
            // 런타임은 0.5m 픽셀이면 충분하다 — 거리장은 어차피 블러로 뭉개지고, 물가의 최종
            // 매끄러움은 정점 스냅이 보장한다. 0.25m(에디터 굽기 값)로 두면 이 생성이 4배 느리다.
            fieldSubDiv = Mathf.Max(2, Mathf.RoundToInt(cell / Mathf.Max(s.fieldPixelM, 0.5f)));
            riverFalloff = s.riverFalloffM / cell;
            shelfWidth = s.shelfWidthM / cell;
            shapeInset = s.shapeInsetM / cell;

            int smoothRadius = Mathf.Max(1, Mathf.RoundToInt(s.smoothRadiusM / s.fieldPixelM));
            riverField = SignedDistance(map, MapTile.River, fieldSubDiv, smoothRadius, s.smoothPasses);
            cliffField = SignedDistance(map, MapTile.Cliff, fieldSubDiv, smoothRadius, s.smoothPasses);

            WaterlineIso = SolveWaterlineIso();
        }

        public static TerrainForm Build(MapDef map, TerrainGenSettings s, float cellSize) =>
            new TerrainForm(map, s, cellSize);

        // ── 높이 ────────────────────────────────────────────────────

        /// <summary>
        /// 물가에서 <paramref name="into"/>칸 들어간 지점이 얼마나 파이는가(m). 뭍이면 0.
        /// 넓고 완만한 여울이 먼저 눕고, 그 뒤에 골이 파인다(구 생성기와 같은 두 단 곡선).
        /// </summary>
        public float Submerge(float into)
        {
            float shelf = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, shelfWidth, into)) * S.shelfDepth;
            float trough = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shelfWidth, shelfWidth + riverFalloff, into))
                         * (S.riverDepth - S.shelfDepth);
            return shelf + trough;
        }

        /// <summary>타일 좌표의 지면 높이(m, 월드 y). 지면·절벽 0, 강·맵 가장자리만 판다.</summary>
        public float Height(float tx, float ty)
        {
            float dig = Submerge(-RiverDistance(tx, ty) - shapeInset);
            float edge = Mathf.Min(Mathf.Min(tx, ty), Mathf.Min(Map.width - tx, Map.height - ty));
            dig = Mathf.Max(dig, Submerge(S.shoreWidth - edge));
            return -dig;
        }

        /// <summary>강 거리장(칸) — 안이 음수. 물가 정점 스냅·물가 띠 판정이 쓴다.</summary>
        public float RiverDistance(float tx, float ty) => SampleField(riverField, tx, ty);

        public float CliffDistance(float tx, float ty) => SampleField(cliffField, tx, ty);

        /// <summary>강 거리장의 기울기(타일 좌표계) — 정점을 물가선으로 미는 방향.</summary>
        public Vector2 RiverGradient(float tx, float ty)
        {
            const float d = 0.25f;
            float dx = SampleField(riverField, tx + d, ty) - SampleField(riverField, tx - d, ty);
            float dy = SampleField(riverField, tx, ty + d) - SampleField(riverField, tx, ty - d);
            return new Vector2(dx, dy) / (2f * d);
        }

        internal float[,] RiverFieldRaw => riverField;
        internal float[,] CliffFieldRaw => cliffField;
        internal int FieldSubDiv => fieldSubDiv;

        /// <summary>height == waterLevel 이 되는 강 거리값을 이분법으로 푼다 — 프로파일이 바뀌어도 맞는다.</summary>
        float SolveWaterlineIso()
        {
            // Submerge(into) == -waterLevel 인 into 를 찾는다 (waterLevel은 음수)
            float target = -S.waterLevel;
            float lo = 0f, hi = shelfWidth + riverFalloff;
            if (Submerge(hi) < target) return -(hi + shapeInset);   // 수면이 골보다 깊다 — 이론상 없음
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Submerge(mid) < target) lo = mid; else hi = mid;
            }
            float into = (lo + hi) * 0.5f;
            return -(into + shapeInset);   // dig = Submerge(-river - inset) ⇒ river = -(into + inset)
        }

        // ── 거리장 (구 생성기 포팅 — 체임퍼 변환 + 분리형 블러) ──────

        /// <summary>거리장을 칸 좌표(실수)에서 읽는다. smoothstep 보간으로 격자 선의 꺾임을 없앤다.</summary>
        float SampleField(float[,] field, float x, float y)
        {
            int w = field.GetLength(0), h = field.GetLength(1);
            x = Mathf.Clamp(x * fieldSubDiv - 0.5f, 0f, w - 1.001f);
            y = Mathf.Clamp(y * fieldSubDiv - 0.5f, 0f, h - 1.001f);
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            int x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
            float fx = Mathf.SmoothStep(0f, 1f, x - x0), fy = Mathf.SmoothStep(0f, 1f, y - y0);
            float a = Mathf.Lerp(field[x0, y0], field[x1, y0], fx);
            float b = Mathf.Lerp(field[x0, y1], field[x1, y1], fx);
            return Mathf.Lerp(a, b, fy);
        }

        static float[,] SignedDistance(MapDef map, MapTile tile, int subDiv, int smoothRadius, int smoothPasses)
        {
            int w = map.width * subDiv, h = map.height * subDiv;
            var inside = new float[w, h];
            var outside = new float[w, h];

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool isTile = map.TileAt(x / subDiv, y / subDiv) == tile;
                    outside[x, y] = isTile ? 0f : float.MaxValue;
                    inside[x, y] = isTile ? float.MaxValue : 0f;
                }

            Chamfer(outside, w, h);
            Chamfer(inside, w, h);

            var signed = new float[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    signed[x, y] = (outside[x, y] - inside[x, y]) / subDiv;

            Smooth(signed, w, h, smoothRadius, smoothPasses);
            return signed;
        }

        static void Smooth(float[,] d, int w, int h, int r, int passes)
        {
            if (r <= 0) return;
            var tmp = new float[w, h];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f; int n = 0;
                        for (int k = -r; k <= r; k++) { sum += d[Mathf.Clamp(x + k, 0, w - 1), y]; n++; }
                        tmp[x, y] = sum / n;
                    }
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f; int n = 0;
                        for (int k = -r; k <= r; k++) { sum += tmp[x, Mathf.Clamp(y + k, 0, h - 1)]; n++; }
                        d[x, y] = sum / n;
                    }
            }
        }

        static void Chamfer(float[,] d, int w, int h)
        {
            const float O = 1f, D = 1.41421f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = d[x, y];
                    if (x > 0) v = Mathf.Min(v, d[x - 1, y] + O);
                    if (y > 0) v = Mathf.Min(v, d[x, y - 1] + O);
                    if (x > 0 && y > 0) v = Mathf.Min(v, d[x - 1, y - 1] + D);
                    if (x < w - 1 && y > 0) v = Mathf.Min(v, d[x + 1, y - 1] + D);
                    d[x, y] = v;
                }
            for (int y = h - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    float v = d[x, y];
                    if (x < w - 1) v = Mathf.Min(v, d[x + 1, y] + O);
                    if (y < h - 1) v = Mathf.Min(v, d[x, y + 1] + O);
                    if (x < w - 1 && y < h - 1) v = Mathf.Min(v, d[x + 1, y + 1] + D);
                    if (x > 0 && y < h - 1) v = Mathf.Min(v, d[x - 1, y + 1] + D);
                    d[x, y] = v;
                }
        }
    }
}
