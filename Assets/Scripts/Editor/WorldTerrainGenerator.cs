using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.Worlds;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 맵 데이터로 Unity Terrain을 굽는다 — `Tools > Factory > Build World Terrain`.
    ///
    /// <b>타일 값을 높이로 직접 바꾸지 않는다.</b> 0→2로 튀는 값을 그대로 쓰면 격자 계단이 남는다.
    /// 대신 "가장 가까운 절벽까지의 거리"(거리장)를 구해 부드러운 곡선으로 높이에 매핑한다 —
    /// 절벽 중심부는 높고 가장자리로 갈수록 완만해지는, 자연 언덕의 단면이 나온다.
    ///
    /// 여기에 두 겹을 더한다:
    ///   · <b>도메인 워핑</b> — 샘플 좌표 자체를 노이즈로 흔들어 경계의 직선/직각을 깬다
    ///   · <b>미세 노이즈</b> — 지면의 "완벽한 평면"을 없앤다
    ///
    /// 게임 규칙은 흐려지지 않는다: 통행·건설·비용 판정은 MapDataSO(타일)가 그대로 들고 있고,
    /// Terrain은 <b>보이는 것</b>만 담당한다. 절벽은 급경사로 표현되며 통행 차단은 EnterCost가,
    /// 플레이어의 등반 차단은 PlayerController의 경사 제한이 맡는다.
    ///
    /// 물은 맵을 덮는 사각형 통메시 한 장이다 — 지형이 강 자리만 깊게 파여 있어서
    /// 거기서만 물이 보이고, 가장자리는 지형이 올라오며 저절로 얕아진다(여울).
    /// </summary>
    public static class WorldTerrainGenerator
    {
        const string TerrainRootName = "Terrain (Generated)";

        // ── 수치는 전부 에셋에 있다 ─────────────────────────────────
        // 물가 경사 하나 보려고 스크립트를 고치면 도메인 리로드가 돌고, 아트 쪽에서는 만질
        // 수도 없다. 값은 TerrainGenSettings 에셋이 들고, 이 파일은 <b>절차</b>만 갖는다.
        // 각 값이 왜 그 값인지는 그 에셋의 필드 주석에 있다.
        static TerrainGenSettings _settings;
        static TerrainGenSettings S => _settings != null ? _settings : (_settings = TerrainGenSettings.LoadOrCreate());

        // ── 스케일 환산 ─────────────────────────────────────────────
        // 물가·워핑·해상도는 에셋에 <b>미터</b>로 적혀 있고 계산은 칸 좌표계다. 그 환산이
        // 여기 모여 있다 — 셀 크기를 바꿔도 물가의 실제 형상(경사·여울 폭·해안 굴곡·블러
        // 반경)이 유지되는 이유가 이 한 겹이다.
        // (셀 2→4m 때 칸 단위 상수가 통째로 어긋나 물가가 두 배 넓어졌던 재발 방지.
        //  절벽은 반대로 바위 폭이 곧 칸이라 칸 비례가 자연스럽다 — 그쪽은 Cell을 곱한다)
        static float Cell = 2f;   // Build 시작에 world.CellSize로 갱신된다

        static float RiverFalloff => S.riverFalloffM / Cell;
        static float ShelfWidth   => S.shelfWidthM / Cell;
        static float ShapeInset   => S.shapeInsetM / Cell;

        static int FieldSubDiv    => Mathf.Max(2, Mathf.RoundToInt(Cell / S.fieldPixelM));
        static int SamplesPerCell => Mathf.Max(2, Mathf.RoundToInt(Cell / S.heightSampleM));

        static int SmoothRadius       => Mathf.Max(1, Mathf.RoundToInt(S.smoothRadiusM / S.fieldPixelM));
        static int HeightSmoothRadius => Mathf.Max(1, Mathf.RoundToInt(S.smoothRadiusM / S.heightSampleM));

        static float WarpStrength     => S.warpStrengthM / Cell;
        static float WarpFrequency    => Cell / S.warpWavelengthM;
        static float WarpMidStrength  => S.warpMidStrengthM / Cell;
        static float WarpMidFrequency => Cell / S.warpMidWavelengthM;
        static float WarpFineStrength => S.warpFineStrengthM / Cell;
        static float WarpFineFrequency=> Cell / S.warpFineWavelengthM;
        static float DetailFrequency  => Cell / S.detailWavelengthM;

        // 절벽은 칸 비례 — 바위의 xz 폭이 곧 칸이라 높이·후퇴량도 같이 커져야 비율이 유지된다

        /// <summary>물가에서 풀이 멈추는 높이(m). 수면보다 조금 위여야 물속에 잠긴 풀이 없다.</summary>
        static float GrassWaterLine => S.waterLevel + S.grassWaterLineOffset;

        [MenuItem("Tools/Factory/Build World Terrain")]
        public static void Build()
        {
            var world = Object.FindFirstObjectByType<World>();
            if (world == null || world.Map == null)
            {
                EditorUtility.DisplayDialog("지형 생성", "씬에서 World(맵이 배선된 것)를 찾지 못했습니다.", "확인");
                return;
            }

            var map = world.Map;
            Cell = world.CellSize;   // 미터 선언 상수들의 칸 환산 기준 — 반드시 형상 계산 전에
            EnsureFolder();

            // 기존 생성물 정리 — 다시 구울 때 겹치지 않게
            var old = world.transform.Find(TerrainRootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var root = new GameObject(TerrainRootName);
            root.transform.SetParent(world.transform, false);

            var height = BuildHeightmap(map);
            CreateTerrain(map, world, root.transform, height);
            CreateWater(map, world, root.transform, height);
            PlaceCliffs(map, world, root.transform, height);
            CreateBounds(map, world, root.transform);

            // 생성물은 전부 정적이다 — 맵이 바뀌기 전에는 움직이지 않으므로
            // 배칭·오클루전·라이트맵을 전부 받을 수 있다.
            MarkStatic(root.transform);

            // 디스크에 굳힌다. SetDirty만으로는 부족하다 — 디테일(풀)과 스캐터 모드는
            // 저장되지 않은 채 에셋이 다시 로드되는 순간 통째로 사라진다(알파맵은 별도
            // 경로라 살아남아서, 풀만 없어지는 것으로 보인다).
            AssetDatabase.SaveAssets();

            EditorUtility.SetDirty(world.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            Debug.Log($"[WorldTerrainGenerator] '{map.Id}' Terrain 생성 완료 ({map.width}×{map.height})", world);
        }

        // ── 높이맵 ──────────────────────────────────────────────────

        /// <summary>
        /// 물가에서 <paramref name="into"/>칸 들어간 지점이 얼마나 파이는가(m). 뭍이면 0.
        ///
        /// 두 단으로 나눈다 — 넓고 완만한 <b>여울</b>이 먼저 물속으로 눕고, 그 뒤에 골이 파인다.
        /// 여울은 걸어 들어갈 수 있는 완경사라 물가가 벼랑으로 보이지 않고,
        /// 골은 짧아서 폭 3칸짜리 물길도 제 깊이에 닿는다.
        /// </summary>
        static float Submerge(float into)
        {
            float shelf = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, ShelfWidth, into)) * S.shelfDepth;
            float trough = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ShelfWidth, ShelfWidth + RiverFalloff, into))
                         * (S.riverDepth - S.shelfDepth);
            return shelf + trough;
        }

        /// <summary>
        /// 타일 → 높이맵. 거리장을 부드러운 곡선으로 매핑하고 워핑·노이즈로 격자 흔적을 지운다.
        /// Terrain 높이맵은 0~1 정규화 값이라 마지막에 전체 높이로 나눈다.
        /// </summary>
        static float[,] BuildHeightmap(MapDataSO map)
        {
            int res = HeightmapResolution(map);

            // 두 거리장: 절벽까지, 강까지. 각각 "그 타일 안이면 음수(중심일수록 깊음)"다.
            var cliffField = SignedDistance(map, MapTile.Cliff);
            var riverField = SignedDistance(map, MapTile.River);

            var height = new float[res, res];
            float total = S.terrainHeightRange;   // 정규화 기준 (0 = 바닥, 1 = 천장)

            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    // 높이맵 픽셀 → 타일 좌표 (Terrain은 [y, x] 순서로 저장한다)
                    float tx = (float)i / (res - 1) * map.width;
                    float ty = (float)j / (res - 1) * map.height;

                    // 도메인 워핑 — 조회 좌표를 흔들어 경계의 직선을 깬다.
                    // 긴 파장(큰 굽이) + 잔물결 두 옥타브.
                    float fineX = (Mathf.PerlinNoise(tx * WarpMidFrequency + 58.3f, ty * WarpMidFrequency + 24.1f) - 0.5f) * 2f * WarpMidStrength
                                + (Mathf.PerlinNoise(tx * WarpFineFrequency + 91.7f, ty * WarpFineFrequency + 45.3f) - 0.5f) * 2f * WarpFineStrength;
                    float fineY = (Mathf.PerlinNoise(tx * WarpMidFrequency + 13.9f, ty * WarpMidFrequency + 82.7f) - 0.5f) * 2f * WarpMidStrength
                                + (Mathf.PerlinNoise(tx * WarpFineFrequency + 7.9f, ty * WarpFineFrequency + 63.1f) - 0.5f) * 2f * WarpFineStrength;
                    float wx = tx + (Mathf.PerlinNoise(tx * WarpFrequency, ty * WarpFrequency) - 0.5f) * 2f * WarpStrength + fineX;
                    float wy = ty + (Mathf.PerlinNoise(tx * WarpFrequency + 37.7f, ty * WarpFrequency + 12.3f) - 0.5f) * 2f * WarpStrength + fineY;

                    // 워핑된 좌표와 원래 좌표 중 <b>바깥쪽</b>을 택한다(거리장은 안이 음수).
                    // 워핑이 형상을 밖으로 밀어 지면을 파는 일을 막는 장치 —
                    // 흔들림은 자기 영역을 깎는 방향으로만 남는다.
                    float cliff = Mathf.Max(SampleField(cliffField, map, wx, wy),
                                            SampleField(cliffField, map, tx, ty));

                    // 강은 <b>중간+잔물결 옥타브만</b> 워핑한다. 긴 파장(36m)은 물길 폭보다 훨씬
                    // 길어 양쪽 기슭을 같은 방향으로 미는데, 바깥쪽 택하기가 밀려나간 쪽만
                    // 깎아내니 물길이 진폭(2.2m)만큼 통째로 한쪽에 쏠렸다 — 여울(0.9m)이 사라진
                    // 기슭은 잔디가 물 위에 걸린다. 파장이 폭보다 짧은 옥타브는 기슭을 따라
                    // 들쭉날쭉 번갈아 깎여 쏠림이 아니라 굴곡이 된다. 큰 굽이는 타일 경로가 그린다.
                    float river = Mathf.Max(SampleField(riverField, map, tx + fineX, ty + fineY),
                                            SampleField(riverField, map, tx, ty));

                    // 절벽 칸은 높이맵을 <b>평평하게</b> 둔다 — 벽은 프리팹이 맡는 몫이다.
                    // 높이맵으로 세운 벽은 결국 늘어난 텍스처라 가까이서 암벽으로 보이지 않는다.
                    // (배치 알고리즘은 걷어냈다. 이 자리는 지금 아무것도 없는 평지다.)
                    float h = 0f;

                    // 강만 판다. 경계에서 ShapeInset만큼 안쪽이 물가고, 거기서부터 여울·골 순으로
                    // 들어간다 — 발치가 타일 안이라 지면은 평평하게 남는다.
                    float dig = Submerge(-river - ShapeInset);

                    // 맵 가장자리도 같은 곡선으로 깎아 물에 잠근다 — 바다에 뜬 섬으로 보이게.
                    // 타일은 건드리지 않으므로 길찾기·건설 판정은 그대로다(외형만 바뀐다).
                    // 해안선은 <b>워핑된 좌표</b>로 잰다 — 원좌표로 재면 맵 테두리를 따라가는
                    // 자로 그은 직선이 된다. 워핑 최대 진폭(1.45칸) < 잠김 폭(1.8칸)이라
                    // 맵 최외곽은 언제나 물에 잠긴 채로 남는다(지형 절단면 노출 없음).
                    float edge = Mathf.Min(Mathf.Min(wx, wy), Mathf.Min(map.width - wx, map.height - wy));
                    dig = Mathf.Max(dig, Submerge(S.shoreWidth - edge));

                    h -= dig;

                    height[j, i] = h;   // 아직 미터 단위, 형상만
                }

            // 높이맵 자체를 한 번 더 다듬는다. 거리장을 아무리 부드럽게 만들어도 "타일 밖은 0"
            // 클램프가 칸 격자에서 일어나기 때문에 경계선이 다시 칸 모서리를 따라간다 —
            // 격자를 떠난 이 단계에서 뭉개야 그 직각이 실제로 없어진다.
            SmoothHeight(height, res, map, cliffField, riverField);

            // 미세 굴곡은 다듬은 뒤에 얹는다(먼저 넣으면 방금 한 블러가 도로 지운다)
            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    float tx = (float)i / (res - 1) * map.width;
                    float ty = (float)j / (res - 1) * map.height;

                    // 물 밑은 잔잔하게 — 파임을 흐리지 않도록
                    float detail = (Mathf.PerlinNoise(tx * DetailFrequency, ty * DetailFrequency) - 0.5f) * 2f * S.detailAmplitude;
                    float h = height[j, i] + detail * (height[j, i] < -0.05f ? 0.35f : 1f);

                    // 마른 지면은 노이즈가 아래로 파지 못하게 막는다. 굴곡 폭(±0.14m)이 잔디
                    // 컷라인(수면+0.08m)과 수면(-0.15m) 사이 틈보다 커서, 평지 곳곳이 "잔디는 안
                    // 자라는데 물도 안 덮이는" 깊이로 파였다 — 잔디 빈 얼룩과 모래 색 웅덩이의 원인.
                    // 형상(강·여울)으로 파인 곳은 원래 음수라 이 클램프에 걸리지 않는다.
                    if (height[j, i] > -0.02f) h = Mathf.Max(h, -0.02f);

                    height[j, i] = Mathf.Clamp01((h + S.riverDepth) / total);
                }

            return height;
        }

        /// <summary>
        /// 높이맵 다듬기 — 칸 경계에 남은 직각을 편다.
        /// 다만 블러는 절벽·강을 옆 지면으로 흘려보내므로, 타일 밖에서는 원래 높이로 되돌린다.
        /// 되돌림 여부는 <b>거리장</b>으로 판정한다 — 칸 중심/경계로 재면 그 되돌림이 칸 주기로
        /// 반복돼 물가에 가리비 같은 물결이 새로 생긴다(격자를 지우려다 다시 그리는 꼴).
        /// </summary>
        static void SmoothHeight(float[,] height, int res, MapDataSO map, float[,] cliffField, float[,] riverField)
        {
            int r = HeightSmoothRadius;
            if (r <= 0) return;

            var original = (float[,])height.Clone();
            var tmp = new float[res, res];

            for (int pass = 0; pass < S.heightSmoothPasses; pass++)
            {
                for (int j = 0; j < res; j++)
                    for (int i = 0; i < res; i++)
                    {
                        float sum = 0f;
                        for (int k = -r; k <= r; k++) sum += height[j, Mathf.Clamp(i + k, 0, res - 1)];
                        tmp[j, i] = sum / (2 * r + 1);
                    }

                for (int j = 0; j < res; j++)
                    for (int i = 0; i < res; i++)
                    {
                        float sum = 0f;
                        for (int k = -r; k <= r; k++) sum += tmp[Mathf.Clamp(j + k, 0, res - 1), i];
                        height[j, i] = sum / (2 * r + 1);
                    }
            }

            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    float tx = (float)i / (res - 1) * map.width;
                    float ty = (float)j / (res - 1) * map.height;

                    // 절벽·강 밖이면 원래 높이 그대로. 블러는 형상을 옆으로 흘려보내는데,
                    // 그러면 절벽이 깎이는 게 아니라 옆 땅이 차오르는 모양이 된다.
                    // 형상은 이미 ShapeInset만큼 안에서 시작하므로 여기서 잘라도 잃을 높이가 없다.
                    float outside = Mathf.Min(SampleField(cliffField, map, tx, ty),
                                              SampleField(riverField, map, tx, ty));
                    if (outside > 0f) height[j, i] = original[j, i];
                }
        }

        static int HeightmapResolution(MapDataSO map)
        {
            // Terrain 높이맵은 2ⁿ+1이어야 한다. 맵보다 촘촘하게 잡아 곡선이 뭉개지지 않게.
            int target = Mathf.Max(map.width, map.height) * SamplesPerCell;
            int res = 33;
            while (res - 1 < target && res < 2049) res = (res - 1) * 2 + 1;
            return res;
        }

        // ── 거리장 ──────────────────────────────────────────────────

        /// <summary>
        /// 부호 있는 거리장(타일 단위) — 해당 타일 안이면 음수, 밖이면 양수.
        /// 두 번 훑는 체임퍼 변환이라 맵 크기에 선형이다(브루트포스 O(n²)를 피한다).
        ///
        /// 칸이 아니라 <b>칸을 FieldSubDiv로 쪼갠 격자</b>에서 뜬다. 칸 단위로 뜨면 계단 하나가
        /// 곧 한 칸이라 아무리 보간해도 마인크래프트 같은 직각이 남는다.
        /// 뜬 뒤에는 블러로 그 직각을 뭉갠다(<see cref="Smooth"/>). 타일 밖을 잘라내지 않는 이유는
        /// 그 자르기가 칸 격자에서 일어나 경계선을 도로 칸 모서리에 붙여놓기 때문이다 —
        /// 대신 형상 자체를 <see cref="ShapeInset"/>만큼 안쪽에서 시작해 지면을 침범하지 않게 한다.
        /// </summary>
        static float[,] SignedDistance(MapDataSO map, MapTile tile)
        {
            int w = map.width * FieldSubDiv, h = map.height * FieldSubDiv;
            var inside = new float[w, h];
            var outside = new float[w, h];

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool isTile = map.TileAt(x / FieldSubDiv, y / FieldSubDiv) == tile;
                    outside[x, y] = isTile ? 0f : float.MaxValue;   // 타일까지의 거리
                    inside[x, y] = isTile ? float.MaxValue : 0f;    // 타일 밖까지의 거리
                }

            Chamfer(outside, w, h);
            Chamfer(inside, w, h);

            var signed = new float[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    signed[x, y] = (outside[x, y] - inside[x, y]) / FieldSubDiv;   // 안이면 음수, 칸 단위

            Smooth(signed, w, h);
            return signed;
        }

        /// <summary>분리형 박스 블러 — 거리장의 꺾인 등고선을 둥글게 편다.</summary>
        static void Smooth(float[,] d, int w, int h)
        {
            var tmp = new float[w, h];
            int r = SmoothRadius;
            if (r <= 0) return;

            for (int pass = 0; pass < S.smoothPasses; pass++)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f; int n = 0;
                        for (int k = -r; k <= r; k++)
                        {
                            int sx = Mathf.Clamp(x + k, 0, w - 1);
                            sum += d[sx, y]; n++;
                        }
                        tmp[x, y] = sum / n;
                    }

                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f; int n = 0;
                        for (int k = -r; k <= r; k++)
                        {
                            int sy = Mathf.Clamp(y + k, 0, h - 1);
                            sum += tmp[x, sy]; n++;
                        }
                        d[x, y] = sum / n;
                    }
            }
        }

        /// <summary>체임퍼 거리 변환 — 직교 1, 대각 √2로 두 방향 전파.</summary>
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

        /// <summary>
        /// 거리장을 칸 좌표(실수)에서 읽는다. 격자는 FieldSubDiv배로 촘촘하므로 먼저 환산한다.
        /// 보간 계수에 smoothstep을 물려 격자 선에서 기울기가 끊기지 않게 한다 — 선형 보간만 쓰면
        /// 샘플 사이는 곧게 이어져 미세한 각이 남는다.
        /// </summary>
        static float SampleField(float[,] field, MapDataSO map, float x, float y)
        {
            int w = field.GetLength(0), h = field.GetLength(1);

            x = Mathf.Clamp(x * FieldSubDiv - 0.5f, 0f, w - 1.001f);
            y = Mathf.Clamp(y * FieldSubDiv - 0.5f, 0f, h - 1.001f);

            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            int x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
            float fx = Mathf.SmoothStep(0f, 1f, x - x0), fy = Mathf.SmoothStep(0f, 1f, y - y0);

            float a = Mathf.Lerp(field[x0, y0], field[x1, y0], fx);
            float b = Mathf.Lerp(field[x0, y1], field[x1, y1], fx);
            return Mathf.Lerp(a, b, fy);
        }

        // ── Terrain 생성 ────────────────────────────────────────────

        static void CreateTerrain(MapDataSO map, World world, Transform root, float[,] height)
        {
            int res = height.GetLength(0);
            var data = new TerrainData
            {
                heightmapResolution = res,
                alphamapResolution = Mathf.Min(2048, Mathf.NextPowerOfTwo(Mathf.Max(map.width, map.height) * SamplesPerCell * 2)),
                baseMapResolution = 512,
                name = $"{map.Id.Replace(':', '_')}_TerrainData",
            };
            // size는 heightmapResolution 설정 뒤에 줘야 한다 (해상도 변경이 size를 되돌린다)
            data.size = new Vector3(map.width * world.CellSize, S.terrainHeightRange, map.height * world.CellSize);
            data.SetHeights(0, 0, height);
            data.terrainLayers = BuildLayers();

            AssetDatabase.CreateAsset(data, $"{S.assetFolder}/{data.name}.asset");

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(root, false);
            // 지면(타일 높이 0)이 월드 y=0에 오도록 강 깊이만큼 내린다
            go.transform.localPosition = new Vector3(0f, -S.riverDepth, 0f);

            int ground = LayerMask.NameToLayer("Ground");
            if (ground >= 0) go.layer = ground;

            var terrain = go.GetComponent<Terrain>();
            // URP에서는 전용 지형 셰이더를 명시해야 한다 — null로 두면 내장 파이프라인 머티리얼이라
            // 아무것도 그려지지 않는다(물만 보이는 증상).
            terrain.materialTemplate = TerrainMaterial();
            terrain.heightmapPixelError = 3f;
            terrain.drawInstanced = true;

            // 풀은 멀리서 보이지 않아도 된다 — 가까이서 발밑을 덮는 것이 목적이다
            terrain.detailObjectDistance = S.detailDistance;
            terrain.detailObjectDensity = 1f;

            // 스플랫은 반드시 에셋으로 굳힌 뒤에 칠한다. TerrainData를 CreateAsset 하는 순간
            // 알파맵 텍스처가 새로 만들어지면서 그 전에 칠한 내용이 전부 첫 레이어로 초기화된다.
            PaintSplat(data, map, height);
            PaintDetails(data, map, height, world.CellSize);
            EditorUtility.SetDirty(data);

            // 여기서 디스크에 굳힌다. 뒤이어 물 메시를 CreateAsset 하는 순간, 아직 저장되지 않은
            // TerrainData의 디테일이 통째로 날아간다 — 다른 에셋을 만드는 것이 이쪽의 미저장
            // 변경을 되돌린다(스플랫이 CreateAsset에 초기화되던 것과 같은 함정의 반대편).
            AssetDatabase.SaveAssets();
        }

        // ── 디테일(풀·꽃) ───────────────────────────────────────────
        //
        // 지형 텍스처만으로는 에셋 홍보 사진 같은 그림이 나오지 않는다. 그 인상의 대부분은
        // 지면을 덮은 <b>잔디 메시</b>에서 온다 — 바닥이 평평한 그림에서 풀이 선 그림으로 바뀐다.

        // 폴더·간격·패치·거리는 전부 TerrainGenSettings 에셋에 있다.

        /// <summary>
        /// 풀·꽃을 심는다. 심는 자리는 <b>실제 지형이 정한다</b> — 물가 위쪽의 완경사 지면에만
        /// 자란다. 타일로 자르면 안 된다: 워핑·블러를 거친 실제 물가는 타일 경계를 넘나들기
        /// 때문에, 같은 강이라도 한쪽은 물 앞에서 풀이 끊기고 반대쪽은 물속까지 풀이 자란다.
        /// 게다가 잘린 자국이 칸 격자를 그대로 드러낸다.
        ///
        /// 타일을 보는 곳은 절벽 하나뿐이다 — 그 자리는 암벽 프리팹이 덮으므로 비운다.
        /// 절벽에 <b>닿은</b> 칸은 건설 가능한 멀쩡한 지면이므로 비우지 않는다.
        /// 디테일은 콜라이더가 없어 길찾기·건설·사격 판정에 전혀 관여하지 않는다.
        /// </summary>
        static void PaintDetails(TerrainData data, MapDataSO map, float[,] height, float cellSize)
        {
            // 크기는 프리팹 원본에 곱해지는 배율이다. 1인칭 게임이라 무릎 높이여야 시야가 열린다 —
            // Demo 값(0.5~1)을 그대로 쓰면 눈높이를 덮어 앞이 안 보인다.
            var protos = new List<DetailPrototype>();
            foreach (var p in S.grassSet) AddProto(protos, p, S.grassSize.x, S.grassSize.y);
            // 꽃은 잔디보다 조금 커야 보인다 — 작으면 풀숲에 통째로 묻힌다
            int flowerStart = protos.Count;
            foreach (var p in S.flowerSet) AddProto(protos, p, S.flowerSize.x, S.flowerSize.y);
            int grassCount = flowerStart;
            int flowerCount = protos.Count - flowerStart;

            // 풀이 <b>한 종은</b> 있어야 한다 — 아래 칠하기 루프는 칸마다 풀을 먼저 놓고
            // 그 위에 꽃을 얹는 구조라, 풀 종류 수가 0이면 나눗셈에서 터진다.
            // 배열이 이제 인스펙터에서 비워질 수 있으므로 여기서 막는다.
            if (grassCount == 0)
            {
                Debug.LogWarning("[WorldTerrainGenerator] 풀 프리팹이 하나도 없어 디테일을 심지 않았습니다 — " +
                                 $"{TerrainGenSettings.AssetPath} 의 Grass Set 확인.");
                return;
            }

            // 해상도·모드를 먼저 잡고 <b>프로토타입은 마지막에</b> 대입한다.
            // SetDetailResolution은 디테일 데이터를 초기화하면서 프로토타입 목록까지 비운다 —
            // 순서를 반대로 하면 심을 것이 하나도 없는 상태가 된다.
            int DetailRes = Mathf.Min(2048, Mathf.NextPowerOfTwo(
                Mathf.CeilToInt(Mathf.Max(map.width, map.height) * cellSize / S.detailPointM)));
            data.SetDetailResolution(DetailRes, S.detailPatch);

            // 밀도 값을 <b>셀당 개수</b>로 해석하게 한다(Demo와 같은 모드).
            // 기본값인 CoverageMode에서는 같은 값이 0~255 커버리지 비율로 읽혀,
            // 2~3을 넣으면 1%도 안 되는 밀도가 되어 풀이 아예 보이지 않는다.
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

            data.detailPrototypes = protos.ToArray();

            var layers = new int[protos.Count][,];
            for (int i = 0; i < layers.Length; i++) layers[i] = new int[DetailRes, DetailRes];

            for (int j = 0; j < DetailRes; j++)
                for (int i = 0; i < DetailRes; i++)
                {
                    // 디테일 격자 → 타일 좌표. 디테일 맵은 [y, x] 순서다(높이맵과 같다).
                    float tx = (float)i / DetailRes * map.width;
                    float ty = (float)j / DetailRes * map.height;
                    int cx = Mathf.Clamp(Mathf.FloorToInt(tx), 0, map.width - 1);
                    int cy = Mathf.Clamp(Mathf.FloorToInt(ty), 0, map.height - 1);

                    // 절벽 타일은 <b>경계 0.1칸 띠만</b> 심는다 — 벽면 후퇴 노이즈로 암벽이
                    // 물러난 자리는 절벽 타일 앞부분이 드러나므로 띠가 필요하지만, 안쪽은
                    // 바위가 빈틈없이 덮어(관통 구멍 해결 후) 심어도 보이지 않는 낭비다.
                    // 절벽이 맵의 상당분이라 디테일 인스턴스가 그만큼 줄어든다.
                    if (map.TileAt(cx, cy) == MapTile.Cliff)
                    {
                        const float Fringe = 0.1f;   // 칸 단위 — 벽면 후퇴 최대(0.45칸)보다 얕은 앞줄만
                        bool nearOpen = false;
                        for (int oy = -1; oy <= 1 && !nearOpen; oy++)
                            for (int ox = -1; ox <= 1; ox++)
                            {
                                if (ox == 0 && oy == 0) continue;
                                int nx = Mathf.FloorToInt(tx + ox * Fringe);
                                int nz = Mathf.FloorToInt(ty + oy * Fringe);
                                if (map.InBounds(nx, nz) && map.TileAt(nx, nz) != MapTile.Cliff) { nearOpen = true; break; }
                            }
                        if (!nearOpen) continue;
                    }

                    // 물가 아래는 비운다. 판정을 실제 높이로 하므로 풀이 끊기는 선이
                    // 칸 모서리가 아니라 물가 곡선을 그대로 따라간다.
                    if (SampleHeightAt(height, tx, ty, map) < GrassWaterLine) continue;

                    // 급경사에도 두지 않는다 — 비탈에 선 풀은 지면을 뚫고 나온 것처럼 보인다
                    if (SlopeAt(height, tx, ty, map, cellSize) > S.grassMaxSlope) continue;

                    // 풀은 <b>빈 곳 없이</b> 깔되 밀도만 흔든다. 임계값으로 자르면 노이즈 모양 그대로
                    // 구멍이 뚫려, 잔디밭이 아니라 얼룩으로 보인다.
                    float patch = Mathf.PerlinNoise(tx * 0.06f, ty * 0.06f);
                    int amount = patch > 0.7f ? 3 : 2;   // 평균 약 2.3 — 이전(1.45)의 1.5배
                    layers[Hash(i, j, 17) % grassCount][j, i] = amount;

                    // 꽃 — 셀 단위로 흩뿌린다. 칸 좌표로 종류를 고르면 같은 칸이 통째로 한 색이 되고,
                    // 그 칸들이 규칙적으로 늘어서 <b>줄무늬</b>가 생긴다(꽃밭이 아니라 격자무늬가 됐던 이유).
                    if (flowerCount > 0)
                    {
                        float bloom = Mathf.PerlinNoise(tx * 0.04f + 31.7f, ty * 0.04f + 12.9f);
                        if (bloom > 0.58f && Hash(i, j, 91) % 8 == 0)
                            layers[flowerStart + Hash(i, j, 53) % flowerCount][j, i] = 1;
                    }
                }

            for (int i = 0; i < layers.Length; i++) data.SetDetailLayer(0, 0, i, layers[i]);
        }

        /// <summary>위치를 잘 섞인 값으로 바꾼다 — 좌표를 곱해 더하는 식은 대각선 줄무늬를 만든다.</summary>
        static int Hash(int x, int y, int salt)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + salt * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) & 0x7fffffff;
            }
        }

        static void AddProto(List<DetailPrototype> into, GameObject prefab, float min, float max)
        {
            // 빈 슬롯은 그냥 건너뛴다 — 설정 에셋 배열에 빈 칸이 남아 있는 경우다
            if (prefab == null) return;

            into.Add(new DetailPrototype
            {
                prototype = prefab,
                usePrototypeMesh = true,
                renderMode = DetailRenderMode.VertexLit,
                useInstancing = true,        // 수만 개가 깔리므로 인스턴싱이 아니면 감당이 안 된다
                minWidth = min, maxWidth = max,
                minHeight = min, maxHeight = max,
                noiseSpread = 20f,
                density = 1f,
                healthyColor = S.healthyTint,
                dryColor = S.dryTint,
            });
        }

        /// <summary>정규화 높이맵을 타일 좌표에서 읽어 미터로 돌려준다 — 경사면 판정용.</summary>
        static float SampleHeightAt(float[,] height, float tx, float ty, MapDataSO map)
        {
            int res = height.GetLength(0);
            int i = Mathf.Clamp(Mathf.RoundToInt(tx / map.width * (res - 1)), 0, res - 1);
            int j = Mathf.Clamp(Mathf.RoundToInt(ty / map.height * (res - 1)), 0, res - 1);
            return height[j, i] * S.terrainHeightRange - S.riverDepth;
        }

        /// <summary>
        /// 그 지점의 지면 기울기(높이차 ÷ 수평거리). 45°가 1이다.
        /// 높이맵 한 칸 간격의 중앙 차분이라, 실제로 심는 해상도에서 느끼는 경사와 같다.
        /// </summary>
        static float SlopeAt(float[,] height, float tx, float ty, MapDataSO map, float cellSize)
        {
            float d = 1f / SamplesPerCell;   // 높이맵 한 칸(타일 단위)
            float dx = SampleHeightAt(height, tx + d, ty, map) - SampleHeightAt(height, tx - d, ty, map);
            float dy = SampleHeightAt(height, tx, ty + d, map) - SampleHeightAt(height, tx, ty - d, map);
            return Mathf.Sqrt(dx * dx + dy * dy) / (2f * d * cellSize);
        }

        // ── 경계 · 정적 표시 ────────────────────────────────────────

        // ── 절벽 ────────────────────────────────────────────────────

        /// <summary>앞면을 안쪽으로 밀어 볼 수 있는 한계(m). 이보다 들어가면 벽이 아니라 언덕이다.</summary>
        const float InsetLimitM = 8f;

        /// <summary>벽 조각 하나의 실측 — 프리팹 로컬 바운즈.</summary>
        readonly struct WallPiece
        {
            public readonly GameObject Prefab;
            public readonly Vector3 Centre;   // 피벗 기준 바운즈 중심
            public readonly Vector3 Size;

            /// <summary>
            /// <b>실제로 벽이 되는 폭</b> — 높이를 여러 겹으로 잘라 잰 단면 폭의 최솟값.
            ///
            /// 바운딩 박스 폭으로 간격을 재면 안 된다. 이 조각들은 위가 넓고 아래가 좁아서,
            /// 박스 폭은 제일 넓은 한 높이의 값이다. 그걸로 걸으면 꼭대기는 닿는데 밑동이
            /// 벌어진다 — 눈높이에서 보이는 것이 바로 그 밑동이다.
            /// </summary>
            public readonly float Cover;

            public float Width  => Size.x;    // 긴 축 — 경계에 접한다
            public float Depth  => Size.z;    // 앞뒤 — 앞면이 −Z 쪽이다
            public float Height => Size.y;

            /// <summary>피벗에서 <b>앞면</b>까지의 거리. 앞면을 경계에 맞추는 데 쓴다.</summary>
            public float FrontReach => Size.z * 0.5f - Centre.z;

            /// <summary>피벗에서 바닥까지(보통 0에 가깝다 — 이 에셋은 피벗이 밑에 있다).</summary>
            public float Bottom => Centre.y - Size.y * 0.5f;

            public WallPiece(GameObject p, Vector3 c, Vector3 s, float cover)
            { Prefab = p; Centre = c; Size = s; Cover = cover; }
        }

        /// <summary>프리팹 묶음을 실측한다. 렌더러가 없는 것은 버린다.</summary>
        static List<WallPiece> MeasureSet(GameObject[] set)
        {
            var list = new List<WallPiece>();
            if (set == null) return list;

            foreach (var pf in set)
            {
                if (pf == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf);
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

        /// <summary>
        /// 절벽 경계를 따라 벽 조각을 <b>한 줄</b> 세운다.
        ///
        /// 앞선 세 세대는 전부 낱개 바위를 <b>쌓아서</b> 벽을 만들려 했다. 그 에셋의 최대
        /// 반지름이 3m라 10m 벽을 만들려면 그 수밖에 없었고, 쌓기가 모든 문제의 근원이었다
        /// (공중에 뜨고, 서로 파묻고, 사이가 벌어지고).
        ///
        /// 이 에셋에는 쌓을 이유가 없다. Cliff_02~05 는 <b>한 개가 9.6~11.2m</b>다 —
        /// 목표 벽 높이가 통째로 한 조각이다. 그래서 배치가 3차원 패킹에서 <b>1차원 경계
        /// 걷기</b>로 내려온다. 높이·겹침·받침 문제가 전부 사라진다.
        ///
        /// 방향은 거리장의 기울기에서 나온다. 예전에 이 기울기가 문제였던 것은 <b>중심축</b>
        /// (벽 한가운데)에서 방향이 상쇄되기 때문인데, 여기서는 경계 바로 안쪽만 표본으로
        /// 삼으므로 그 지점이 아예 후보에 없다.
        ///
        /// 위와 뒤는 보지 않는다 — 게임 카메라가 눈높이(y≈2.2, 부감 없음)다. 그래서 고원
        /// 위를 꾸미거나 지형을 융기시키지 않는다. 뒷면이 밋밋한 것도 상관없다.
        /// </summary>
        static void PlaceCliffs(MapDataSO map, World world, Transform root, float[,] height)
        {
            var walls = MeasureSet(S.cliffWallSet);
            if (walls.Count == 0)
            {
                Debug.LogWarning("[WorldTerrainGenerator] 절벽 벽 조각이 없어 절벽을 세우지 못했습니다 — " +
                                 "Terrain Gen Settings의 Cliff Wall Set에 프리팹을 넣으세요.");
                return;
            }
            walls.Sort((a, b) => b.Width.CompareTo(a.Width));   // 넓은 것부터 — 먼저 시도한다

            var field = SignedDistance(map, MapTile.Cliff);     // 안이 음수, <b>칸</b> 단위
            int fw = field.GetLength(0), fh = field.GetLength(1);
            float px = Cell / FieldSubDiv;
            Vector3 origin = world.CellToWorld(Vector2Int.zero);

            // 월드 좌표 → 거리장(칸 단위)
            float FieldAt(Vector2 p) =>
                SampleField(field, map, (p.x - origin.x) / Cell, (p.y - origin.z) / Cell);

            // 판정은 <b>실제 타일</b>로 한다. 거리장은 Smooth 를 거쳐 경계가 한 칸쯤 뭉개져
            // 있어서, 그것으로 "타일 안"을 판정하면 그만큼 어긋난다.

            // 판정은 <b>실제 타일</b>로 한다. 거리장은 Smooth 를 거쳐 경계가 한 칸쯤 뭉개져
            // 있어서, 그것으로 "타일 안"을 판정하면 그만큼 어긋난다.
            bool OnCliff(Vector2 w)
            {
                int cx = Mathf.FloorToInt((w.x - origin.x) / Cell);
                int cy = Mathf.FloorToInt((w.y - origin.z) / Cell);
                return map.InBounds(cx, cy) && map.TileAt(cx, cy) == MapTile.Cliff;
            }

            float GroundAt(Vector2 p) =>
                world.Origin.y + SampleHeightAt(height, (p.x - origin.x) / Cell, (p.y - origin.z) / Cell, map);

            // 바깥을 향하는 단위 법선 — 거리장은 밖으로 갈수록 커진다

            // ── 경계선을 따라 이어 붙인다 ──
            //
            // 예전에는 경계 안쪽 띠의 픽셀을 <b>래스터 순서</b>로 훑으면서 "이미 놓인 것과
            // 중심거리가 멀면 놓는다"였다. 화면 스캔 순서라 경계를 따라가는 순서가 아니고,
            // 굽은 곳에서 두 조각이 서로 다른 방향을 보면서 중심거리 검사만 통과해 그 사이가
            // 쐐기꼴로 벌어졌다. 뒤에 가림막을 세워 덮을 문제가 아니라 순서의 문제였다.
            //
            // 경계를 폴리라인으로 뽑아 <b>호길이를 따라</b> 걸으면, 앞 조각이 끝난 자리에서
            // 다음 조각이 시작한다. 틈이 생길 수가 없다.
            var loops = TraceContours(field, fw, fh, px, origin);
            if (loops.Count == 0) return;

            var parent = new GameObject("Cliffs").transform;
            parent.SetParent(root, false);

            float overlap = Mathf.Clamp01(S.cliffWallOverlap);
            int placedCount = 0, rejShort = 0, shoved = 0, spilled = 0, backOut = 0;
            double walked = 0;

            float wSum = 0f;
            foreach (var w in walls) wSum += w.Width;

            foreach (var line in loops)
            {
                // 폴리라인 누적 길이
                int n = line.Count;
                var cum = new float[n];
                for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector2.Distance(line[i - 1], line[i]);
                float total = cum[n - 1];
                if (total < 2f) { rejShort++; continue; }
                walked += total;

                // 호길이 t 에서의 위치와 접선
                Vector2 PosAt(float t)
                {
                    t = Mathf.Clamp(t, 0f, total);
                    int i = 1;
                    while (i < n - 1 && cum[i] < t) i++;
                    float seg = Mathf.Max(1e-4f, cum[i] - cum[i - 1]);
                    return Vector2.Lerp(line[i - 1], line[i], (t - cum[i - 1]) / seg);
                }

                // ── 모서리 감지 ──
                //
                // 꺾이는 곳에서 이동 거리를 직선일 때처럼 계산하면 호길이 5m 앞의 점이
                // 직선거리로는 3.5m 밖에 안 떨어져 있어 조각이 모서리 안쪽에서 겹친다.
                // 게다가 방향을 잡는 현(chord)이 꺾인 두 변을 대각으로 가로질러, 조각이
                // 모서리를 통째로 넘어가 반대편 잔디로 나온다 — 길을 막던 조각의 정체다.
                //
                // 폴리라인의 꼭짓점마다 앞뒤 접선 각도를 재서 임계를 넘으면 모서리로 표시한다.
                // 걷다가 다음 모서리에 닿기 전에 <b>구간을 끊고</b>, 모서리 뒤에서 새로 시작한다.
                var corners = new List<float>();
                {
                    float lookM = Mathf.Max(1f, S.cliffWallCornerLookM);
                    for (int i = 1; i < n - 1; i++)
                    {
                        var a0 = PosAt(cum[i] - lookM) - line[i];
                        var a1 = PosAt(cum[i] + lookM) - line[i];
                        if (a0.sqrMagnitude < 1e-6f || a1.sqrMagnitude < 1e-6f) continue;
                        float ang = Vector2.Angle(-a0, a1);
                        if (ang > S.cliffWallCornerDeg) corners.Add(cum[i]);
                    }
                    // 붙어 있는 모서리 표시를 하나로 합친다
                    var merged = new List<float>();
                    foreach (var c in corners)
                        if (merged.Count == 0 || c - merged[merged.Count - 1] > lookM) merged.Add(c);
                    corners = merged;
                }
                int cornerIdx = 0;

                // 조각의 네 귀퉁이가 전부 절벽 타일 안에 있는가.
                // 예전에는 앞모서리 세 점만 봤다 — 뒤·옆 귀퉁이가 잔디로 나가도 통과했다.
                bool Inside(Vector3 pivot, Quaternion rot, WallPiece w, float sc)
                {
                    float hx = w.Width * 0.5f * sc, hz = w.Depth * 0.5f * sc;
                    var c3 = pivot + rot * new Vector3(w.Centre.x * sc, 0f, w.Centre.z * sc);
                    var tanW3 = rot * Vector3.right; var depW3 = rot * Vector3.forward;
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

                    float scale = Mathf.Lerp(S.cliffWallScale.x, S.cliffWallScale.y,
                                             Hash(seedX, seedY, 71) % 1000 / 1000f);

                    // 뽑기는 폭에 비례해 가중한다. 균등하면 4종 중 2종이 폭 4m 기둥이라
                    // 벽의 절반이 기둥이 되어 빗처럼 보인다.
                    float roll = Hash(seedX, seedY, 131) % 1000 / 1000f * wSum;
                    int pick = walls.Count - 1;
                    for (int i = 0; i < walls.Count; i++) { roll -= walls[i].Width; if (roll <= 0f) { pick = i; break; } }
                    var piece = walls[pick];

                    // ── 이 조각이 덮을 구간 ──
                    // 다음 모서리까지 남은 길이보다 길면 <b>거기서 끊는다.</b> 모서리를 넘어가는
                    // 조각은 만들지 않는다. 남은 구간이 너무 짧으면 그만큼 작은 조각을 쓴다.
                    while (cornerIdx < corners.Count && corners[cornerIdx] <= t + 0.01f) cornerIdx++;
                    float limit = cornerIdx < corners.Count ? corners[cornerIdx] - t : total - t;

                    float span = piece.Cover * scale;
                    if (span > limit)
                    {
                        // 남은 구간에 맞춰 줄인다 — 단, 배율 하한 아래로는 <b>안 줄인다.</b>
                        // 처음엔 하한의 절반까지 줄였더니 높이 6m 미만 조각이 2574개가 되고 보폭이
                        // 잘아져 84% 가 서로 파묻혔다. 하한에 걸리면 그냥 모서리를 조금 넘긴다.
                        scale = Mathf.Max(S.cliffWallScale.x, limit / piece.Cover);
                        span = piece.Cover * scale;
                    }
                    float step = span * (1f - overlap);

                    var pEnd = PosAt(t + span);
                    var chord = pEnd - p;
                    if (chord.sqrMagnitude < 1e-6f) chord = PosAt(t + 1f) - p;
                    var tan = chord.normalized;

                    var nrm = new Vector2(tan.y, -tan.x);
                    var mid = Vector2.Lerp(p, pEnd, 0.5f);
                    if (FieldAt(mid + nrm * Cell * 0.5f) < FieldAt(mid - nrm * Cell * 0.5f)) nrm = -nrm;

                    var look = Quaternion.LookRotation(new Vector3(-nrm.x, 0f, -nrm.y), Vector3.up);
                    float yaw = look.eulerAngles.y
                              + (Hash(seedX, seedY, 89) % 1000 / 1000f - 0.5f) * 2f * S.cliffWallYawJitter;
                    var rot = Quaternion.Euler(0f, yaw, 0f);
                    var dir = rot * Vector3.back;

                    // ── 절벽 타일 안으로 맞춘다 ──
                    //
                    // 절벽 밖은 건설·통행이 되는 칸이다. 벽이 거기 걸치면 길이 막힌다.
                    //
                    // 네 귀퉁이가 전부 타일 안에 들어올 때까지: 먼저 안쪽으로 밀어 보고, 안 되면
                    // 조각을 줄여서 다시 민다. <b>포기하지 않는다</b> — 예전에는 세 단계 줄이고
                    // 안 되면 "가장 작은 조각으로 그냥 놓는다"였고, 그게 잔디로 나온 조각이다.
                    // 배율 하한까지 줄여도 안 들어가는 자리는 띠가 조각 깊이보다 얇은 곳뿐이고,
                    // 그런 곳에서는 뒤쪽 귀퉁이만 봐준다(앞은 여전히 엄격).
                    Vector3 pos = Vector3.zero;
                    float useScale = scale;
                    bool seated = false;
                    float minScale = S.cliffWallScale.x * S.cliffWallMinShrink;

                    for (float sc = scale; sc >= minScale - 1e-4f && !seated; sc *= 0.85f)
                    {
                        for (float inset = S.cliffWallOverhangM; inset > -InsetLimitM; inset -= 0.4f)
                        {
                            Vector3 f3 = new Vector3(mid.x, 0f, mid.y) + dir * inset;
                            var pv = f3 - dir * (piece.FrontReach * sc);
                            if (!Inside(pv, rot, piece, sc)) continue;
                            pos = pv; useScale = sc; seated = true;
                            if (sc < scale - 1e-4f || inset < S.cliffWallOverhangM - 0.01f) shoved++;
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
                            for (float inset = S.cliffWallOverhangM; inset > -InsetLimitM; inset -= 0.4f)
                            {
                                Vector3 f3 = new Vector3(mid.x, 0f, mid.y) + dir * inset;
                                var pv = f3 - dir * (piece.FrontReach * sc);
                                var fc = pv + rot * new Vector3(piece.Centre.x * sc, 0f,
                                                                (piece.Centre.z - piece.Size.z * 0.5f) * sc);
                                var fc2 = new Vector2(fc.x, fc.z);
                                bool all = true;
                                for (int k = -1; k <= 1 && all; k++) if (!OnCliff(fc2 + tanW * (hx * k))) all = false;
                                if (!all) continue;
                                pos = pv; useScale = sc; seated = true; backOut++;
                                break;
                            }
                        }
                    }

                    if (!seated)
                    {
                        // 앞모서리조차 못 넣는 자리 — 경계가 타일 밖으로 튀어나온 곳이다.
                        // 여기 놓으면 길을 막는다. <b>건너뛴다.</b>
                        spilled++;
                        t += Mathf.Max(0.5f, step);
                        continue;
                    }

                    float scaleF = useScale;
                    pos.y = GroundAt(mid) - S.cliffWallSinkM - piece.Bottom * scaleF;

                    var go = (GameObject)PrefabUtility.InstantiatePrefab(piece.Prefab, parent);
                    go.transform.SetPositionAndRotation(pos, rot);
                    go.transform.localScale = Vector3.one * scaleF;
                    AddConvexCollider(go);

                    placedCount++;
                    // 실제로 세운 조각의 폭만큼만 나아간다 — 줄였으면 보폭도 준다
                    t += Mathf.Max(0.5f, piece.Cover * scaleF * (1f - overlap));
                }
            }

            int foot = PlaceCliffFoot(field, fw, fh, px, origin, GroundAt, parent);

            Debug.Log($"[WorldTerrainGenerator] 절벽 조각 {placedCount + foot}개 " +
                      $"(벽 {placedCount} + 발치 {foot}, 칸 {world.CellSize}m)" +
                      System.Environment.NewLine +
                      $"  경계 {loops.Count}가닥 · 총 길이 {walked:F0}m · 너무 짧아 건너뜀 {rejShort}" +
                      System.Environment.NewLine +
                      $"  타일 안으로 밀어 넣은 것 {shoved} · 뒤만 봐준 것 {backOut} · 건너뛴 것 {spilled}" +
                      System.Environment.NewLine +
                      $"  조각 폭 {walls[walls.Count - 1].Width:F1}~{walls[0].Width:F1}m " +
                      $"(실덮임 {walls[walls.Count - 1].Cover:F1}~{walls[0].Cover:F1}m) · " +
                      $"평균 {(walked / Mathf.Max(1, placedCount)):F1}m 마다 하나");
        }

        /// <summary>
        /// 높이를 겹으로 잘라 각 겹의 X 방향 폭을 재고 그 <b>최솟값</b>을 돌려준다.
        ///
        /// 벽으로 쓸 수 있는 폭은 제일 넓은 데가 아니라 제일 좁은 데가 정한다. 꼭대기 근처
        /// 몇 겹은 뺀다 — 거기는 원래 들쭉날쭉해야 하고, 그 몫까지 지키려 들면 조각이
        /// 쓸데없이 촘촘해진다.
        /// </summary>
        static float CoverWidth(GameObject inst, Bounds b)
        {
            const int Slices = 6;
            var lo = new float[Slices];
            var hi = new float[Slices];
            for (int i = 0; i < Slices; i++) { lo[i] = float.MaxValue; hi[i] = float.MinValue; }

            float y0 = b.min.y, h = Mathf.Max(0.01f, b.size.y);
            float top = 0.7f;   // 위 30%는 스카이라인 몫으로 남긴다

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
                if (lo[i] > hi[i]) continue;          // 이 겹에 정점이 없다
                best = Mathf.Min(best, hi[i] - lo[i]);
            }
            return best == float.MaxValue ? b.size.x : best;
        }

        /// <summary>
        /// 절벽 경계를 <b>순서 있는 폴리라인</b>으로 뽑는다 — 마칭 스퀘어.
        ///
        /// 왜 필요한가: 배치를 후보 픽셀의 래스터 순서로 돌리면, 굽은 곳에서 두 조각이 서로
        /// 다른 방향을 보면서 중심거리 검사만 통과해 그 사이가 쐐기꼴로 벌어진다. 경계를
        /// 따라가는 순서가 있어야 "앞 조각이 끝난 자리에서 다음 조각이 시작"할 수 있고,
        /// 그러면 틈이 생길 수가 없다.
        /// </summary>
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

                    // 안(절벽)이 음수다 — 안쪽 corner 에 비트를 세운다
                    int k = (v00 <= 0f ? 1 : 0) | (v10 <= 0f ? 2 : 0) | (v11 <= 0f ? 4 : 0) | (v01 <= 0f ? 8 : 0);
                    if (k == 0 || k == 15) continue;

                    Vector2 p00 = P(x, y), p10 = P(x + 1, y), p11 = P(x + 1, y + 1), p01 = P(x, y + 1);
                    Vector2 eB = Lerp(p00, v00, p10, v10);   // 아래 모서리
                    Vector2 eR = Lerp(p10, v10, p11, v11);   // 오른쪽
                    Vector2 eT = Lerp(p01, v01, p11, v11);   // 위
                    Vector2 eL = Lerp(p00, v00, p01, v01);   // 왼쪽

                    switch (k)
                    {
                        case 1: case 14: segs.Add((eL, eB)); break;
                        case 2: case 13: segs.Add((eB, eR)); break;
                        case 3: case 12: segs.Add((eL, eR)); break;
                        case 4: case 11: segs.Add((eR, eT)); break;
                        case 6: case 9:  segs.Add((eB, eT)); break;
                        case 7: case 8:  segs.Add((eL, eT)); break;
                        // 안장점 — 두 조각으로 나눈다
                        case 5:  segs.Add((eL, eB)); segs.Add((eR, eT)); break;
                        case 10: segs.Add((eL, eT)); segs.Add((eB, eR)); break;
                    }
                }

            // ── 이어 붙이기 ──
            // 끝점을 격자에 반올림해 열쇠로 쓴다. 마칭 스퀘어의 끝점은 이웃 칸과 정확히
            // 같은 자리에서 나오므로 이 정도 해상도면 정확히 맞는다.
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

                // 양쪽으로 뻗는다
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
                        var s = segs[next];
                        Vector2 far = Key(s.a) == Key(tip) ? s.b : s.a;
                        if (side == 0) line.Add(far); else line.Insert(0, far);
                    }
                }

                if (line.Count >= 3) loops.Add(line);
            }

            return loops;
        }

        /// <summary>
        /// 발치 — 반쯤 묻힌 판을 경계 바깥 띠에 흩는다.
        ///
        /// 이 에셋의 RockBuried_* 는 높이가 0.2~0.4m뿐인 <b>납작한 판</b>이다. 애초에 땅에
        /// 반쯤 박힌 모습으로 만들어져 있어서, 예전처럼 반지름의 몇 할을 파묻는 보정이
        /// 필요 없다. 그냥 지면에 놓으면 된다.
        /// </summary>
        static int PlaceCliffFoot(float[,] field, int fw, int fh, float px, Vector3 origin,
                                  System.Func<Vector2, float> GroundAt, Transform parent)
        {
            var rocks = MeasureSet(S.cliffFootSet);
            if (rocks.Count == 0 || S.cliffFootDensity <= 0f) return 0;

            float band = Mathf.Max(0.05f, S.cliffFootBandCells);
            int gate = Mathf.RoundToInt(Mathf.Clamp01(S.cliffFootDensity) * 1000f);
            var placed = new List<(Vector2 c, float r)>();
            int count = 0;

            for (int fy = 0; fy < fh; fy++)
                for (int fx = 0; fx < fw; fx++)
                {
                    float v = field[fx, fy];
                    if (v < -band * 0.4f || v > band) continue;      // 경계 바깥쪽으로 넓게, 안쪽은 조금
                    if (Hash(fx, fy, 307) % 1000 >= gate) continue;

                    var p = new Vector2(origin.x + (fx + 0.5f) * px, origin.z + (fy + 0.5f) * px);
                    float scale = Mathf.Lerp(S.cliffFootScale.x, S.cliffFootScale.y,
                                             Hash(fx, fy, 311) % 1000 / 1000f);

                    int pick = (int)(Hash(fx, fy, 23) % (uint)rocks.Count);
                    var rock = rocks[pick];
                    float r = 0.5f * Mathf.Max(rock.Size.x, rock.Size.z) * scale;

                    bool clash = false;
                    foreach (var o in placed)
                    {
                        float min = (r + o.r) * S.cliffFootSpacing;
                        if ((o.c - p).sqrMagnitude < min * min) { clash = true; break; }
                    }
                    if (clash) continue;
                    placed.Add((p, r));

                    // 판은 눕는 것이 자연스럽다 — 기울이지 않고 yaw 만 돌린다
                    float yaw = Hash(fx, fy, 313) % 3600 / 10f;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(rock.Prefab, parent);
                    go.transform.SetPositionAndRotation(
                        new Vector3(p.x, GroundAt(p) - S.cliffFootSinkM - rock.Bottom * scale, p.y),
                        Quaternion.Euler(0f, yaw, 0f));
                    go.transform.localScale = Vector3.one * scale;
                    count++;
                }

            return count;
        }

        /// <summary>
        /// 볼록 메시 콜라이더를 붙인다. 절벽은 지나갈 수 없어야 한다 —
        /// 길찾기는 타일을 보지만 플레이어는 물리로 움직인다.
        /// </summary>
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

        /// <summary>
        /// 맵 둘레에 보이지 않는 벽을 세운다 — 지형은 맵 크기만큼만 있어서 그 밖은 허공이다.
        ///
        /// <b>총알은 통과해야 한다.</b> 그래서 `Ignore Raycast` 레이어에 둔다:
        /// 사격 판정(<see cref="ProjectileSystem"/>)이 쓰는 `Physics.DefaultRaycastLayers`가
        /// 정확히 이 레이어를 빼고 있어, 총알은 없는 것처럼 지나간다.
        /// 반면 물리 충돌은 레이어 충돌 매트릭스가 따로 정하므로 플레이어는 그대로 막힌다.
        /// </summary>
        static void CreateBounds(MapDataSO map, World world, Transform root)
        {
            int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycast < 0)
            {
                Debug.LogWarning("[WorldTerrainGenerator] 'Ignore Raycast' 레이어가 없어 경계벽을 세우지 못했습니다.");
                return;
            }

            var parent = new GameObject("Bounds").transform;
            parent.SetParent(root, false);

            float w = map.width * world.CellSize, h = map.height * world.CellSize;
            float t = S.boundsThickness, half = S.boundsHeight * 0.5f;

            // 벽 안쪽 면이 맵 경계에 딱 맞도록 두께의 절반만큼 바깥으로 민다
            Wall("Bounds_-X", new Vector3(-t * 0.5f, half, h * 0.5f), new Vector3(t, S.boundsHeight, h + t * 2f));
            Wall("Bounds_+X", new Vector3(w + t * 0.5f, half, h * 0.5f), new Vector3(t, S.boundsHeight, h + t * 2f));
            Wall("Bounds_-Z", new Vector3(w * 0.5f, half, -t * 0.5f), new Vector3(w + t * 2f, S.boundsHeight, t));
            Wall("Bounds_+Z", new Vector3(w * 0.5f, half, h + t * 0.5f), new Vector3(w + t * 2f, S.boundsHeight, t));

            void Wall(string name, Vector3 center, Vector3 size)
            {
                var go = new GameObject(name) { layer = ignoreRaycast };
                go.transform.SetParent(parent, false);
                go.transform.localPosition = center - new Vector3(0f, S.boundsSink, 0f);

                var box = go.AddComponent<BoxCollider>();
                box.size = size;
            }
        }

        /// <summary>
        /// 생성물 전체를 정적으로 표시한다 — 배칭·오클루전·라이트맵이 걸리도록.
        /// 플래그를 나열하는 이유: `(StaticEditorFlags)~0`은 아직 없는 비트까지 켠다.
        /// </summary>
        static void MarkStatic(Transform root)
        {
            const StaticEditorFlags All =
                StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic |
                StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OffMeshLinkGeneration |
                StaticEditorFlags.ReflectionProbeStatic;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, All);
        }

        /// <summary>URP 지형 머티리얼 — 렌더 파이프라인의 기본값을 쓰되, 없으면 셰이더로 직접 만든다.</summary>
        static Material TerrainMaterial()
        {
            string path = $"{S.assetFolder}/Terrain.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && pipeline.defaultTerrainMaterial != null)
                return pipeline.defaultTerrainMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader == null)
            {
                Debug.LogWarning("[WorldTerrainGenerator] URP 지형 셰이더를 찾지 못했습니다 — 지형이 보이지 않을 수 있습니다.");
                return null;
            }

            mat = new Material(shader) { name = "Terrain" };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>
        /// 지면·절벽·강바닥 3종 레이어 — Idyllic Fantasy Nature의 것을 그대로 쓴다.
        /// <b>순서가 곧 스플랫 채널</b>이다(0 지면 / 1 절벽 / 2 강바닥) — <see cref="PaintSplat"/>과 맞물려 있으니
        /// 순서를 바꾸면 칠이 뒤바뀐다.
        ///
        /// 예전에는 단색 텍스처를 구워 썼다. 에셋의 레이어는 노멀맵까지 들어 있어 같은 조명에서
        /// 굴곡이 살고, 무엇보다 나무·풀 프리팹과 같은 팔레트라 지형만 따로 노는 일이 없다.
        /// </summary>
        static TerrainLayer[] BuildLayers()
        {
            // 배열 순서가 곧 채널이다 — 설정 에셋에서 순서를 바꾸면 칠이 뒤바뀐다.
            var layers = S.terrainLayers;
            if (layers == null || layers.Length < 3 || layers[0] == null || layers[1] == null || layers[2] == null)
            {
                Debug.LogError("[WorldTerrainGenerator] 지형 레이어 3종(지면·절벽·강바닥)이 모두 지정돼야 합니다 — " +
                               $"{TerrainGenSettings.AssetPath} 의 Terrain Layers 확인.");
                return System.Array.Empty<TerrainLayer>();
            }

            // 4번째 이후는 무시한다 — PaintSplat이 채널 3개만 칠한다
            return new[] { layers[0], layers[1], layers[2] };
        }

        /// <summary>
        /// 표면 칠하기. 강바닥은 <b>실제 높이</b>로 정한다 — 워핑으로 흐트러진 물가와 텍스처가
        /// 어긋나지 않는다. 절벽은 이제 지형이 아니라 프리팹이라 높이로 알 수 없으므로
        /// <b>타일</b>로 칠한다(프리팹 사이 틈으로 풀밭이 비치지 않게 하는 것이 목적).
        /// </summary>
        static void PaintSplat(TerrainData data, MapDataSO map, float[,] height)
        {
            int res = data.alphamapResolution;
            int hres = height.GetLength(0);
            var alphas = new float[res, res, 3];
            float total = S.terrainHeightRange;

            // 절벽 타일에서 바깥으로 번지는 정도(칸) — 딱 자르면 칸 경계가 그대로 드러난다
            var cliffField = SignedDistance(map, MapTile.Cliff);

            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    // 알파맵 좌표 → 높이맵 좌표
                    int hi = Mathf.Clamp(Mathf.RoundToInt((float)i / (res - 1) * (hres - 1)), 0, hres - 1);
                    int hj = Mathf.Clamp(Mathf.RoundToInt((float)j / (res - 1) * (hres - 1)), 0, hres - 1);

                    float world = height[hj, hi] * total - S.riverDepth;   // 월드 높이(m)

                    float tx = (float)i / (res - 1) * map.width;
                    float ty = (float)j / (res - 1) * map.height;
                    float cliff = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(0.4f, -0.3f, SampleField(cliffField, map, tx, ty)));

                    // 모래는 수면(-0.15m) 아래에서 시작한다. -0.05부터 깔면 물 위의 마른 물가까지
                    // 모래가 덮여, 밝은 모래 텍스처가 웅덩이마다 흰 테두리 원반처럼 보인다.
                    float bed = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, -0.45f, world));
                    float ground = Mathf.Max(0f, 1f - cliff - bed);

                    float sum = cliff + bed + ground;
                    alphas[j, i, 0] = ground / sum;
                    alphas[j, i, 1] = cliff / sum;
                    alphas[j, i, 2] = bed / sum;
                }

            data.SetAlphamaps(0, 0, alphas);
        }

        // ── 물 ──────────────────────────────────────────────────────

        /// <summary>맵을 덮는 사각형 한 장 — 지형이 파인 곳(강)에서만 보이고 가장자리는 저절로 얕아진다.</summary>
        static void CreateWater(MapDataSO map, World world, Transform root, float[,] height)
        {
            float w = map.width * world.CellSize, h = map.height * world.CellSize;

            // 맵 밖으로 한참 더 깔아 <b>바다</b>로 쓴다 — 지형이 물 한가운데 뜬 섬으로 보인다.
            // 맵 크기의 배수라 큰 맵에서도 수평선이 같은 비율로 물러난다.
            float margin = Mathf.Max(w, h) * S.seaMargin;
            float x0 = -margin, z0 = -margin;
            float sizeX = w + margin * 2f, sizeZ = h + margin * 2f;

            // <b>격자로 쪼갠다.</b> 물 셰이더가 정점을 흔들어 파도를 만들기 때문에, 사각형 네 점으로는
            // 흔들 것이 없어 수면이 판판한 판때기로 남는다. 파도 주기가 몇 미터 단위라
            // 정점 간격도 그 정도여야 물결이 산다.
            int cols = Mathf.Clamp(Mathf.CeilToInt(sizeX / S.waterVertexSpacing), 1, 512);
            int rows = Mathf.Clamp(Mathf.CeilToInt(sizeZ / S.waterVertexSpacing), 1, 512);

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
                    // UV는 칸 단위로 — 바다까지 같은 밀도로 이어져야 물결 타일링이 끊기지 않는다
                    uvs[v] = new Vector2(fx * sizeX / world.CellSize, fz * sizeZ / world.CellSize);

                    // <b>정점 컬러가 거품을 정한다</b> — 빨강이 거품, 검정이 맑은 물이다.
                    // 채우지 않으면 Unity 기본값인 흰색이 들어가 수면 전체가 거품 최대치로 렌더돼,
                    // 바다가 통째로 흰 판때기가 된다.
                    //
                    // 거품은 <b>수심</b>으로 정한다. 경계선까지의 거리로 재면 섬 안쪽 수면(강)이
                    // 전부 거품이 되고 띠도 두꺼워진다 — 물가가 어디인지는 결국 물이 얕은 곳이다.
                    float ttx = wx / world.CellSize, ttz = wz / world.CellSize;
                    bool overMap = ttx >= 0f && ttz >= 0f && ttx <= map.width && ttz <= map.height;
                    float bed = overMap ? SampleHeightAt(height, ttx, ttz, map) : -99f;   // 맵 밖은 먼 바다
                    float depth = S.waterLevel - bed;
                    // 얕을수록 거품 — 다만 수심 0 부근에서는 다시 0으로 죽인다. 수면과 거의 같은
                    // 높이의 지형 위에서는 물 한 겹 없이 거품 100%가 그대로 얹혀, 물가의 얕은
                    // 둔덕이 흰색 원반처럼 렌더되던 원인이다. 거품 띠는 물가에서 살짝 떨어져 남는다.
                    float foam = Mathf.Clamp01(1f - depth / S.foamDepth) * Mathf.Clamp01(depth / 0.05f);
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

            var mesh = new Mesh { name = $"{map.Id.Replace(':', '_')}_Water" };
            if (verts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.RecalculateBounds();

            string meshPath = $"{S.assetFolder}/{mesh.name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var go = new GameObject("Water (Sea)");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, S.waterLevel, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = WaterMaterial();
            // 콜라이더 없음 — 물은 건너다니는 것이고, 바닥은 Terrain이 받는다
        }

        /// <summary>
        /// 물 머티리얼 — Bitgem StylisedWater의 것을 <b>복제해서</b> 쓴다.
        ///
        /// 원본을 직접 참조하지 않는 이유는 색·파도를 우리 맵에 맞게 만지게 되는데,
        /// 그러면 서드파티 에셋을 고치는 셈이라 업데이트 때 덮이거나 예제 씬이 함께 바뀐다.
        ///
        /// 에셋의 WaterVolume 컴포넌트는 쓰지 않는다 — 그쪽은 타일 볼륨(최대 100×100)을 굽는
        /// 방식이라 수백 미터짜리 바다를 담지 못한다. 우리는 메시를 직접 굽고 셰이더만 빌린다.
        /// </summary>
        static Material WaterMaterial()
        {
            string path = $"{S.assetFolder}/Water.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            var source = S.waterMaterialSource;
            if (source == null)
            {
                Debug.LogError("[WorldTerrainGenerator] 물 머티리얼 원본이 비어 있습니다 — " +
                               $"{TerrainGenSettings.AssetPath} 의 Water Material Source 확인.");
                return null;
            }

            mat = new Material(source) { name = "Water" };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Data/Maps"))
                AssetDatabase.CreateFolder("Assets/Data", "Maps");
            if (!AssetDatabase.IsValidFolder(S.assetFolder))
                AssetDatabase.CreateFolder("Assets/Data/Maps", "Terrain");
        }
    }
}
