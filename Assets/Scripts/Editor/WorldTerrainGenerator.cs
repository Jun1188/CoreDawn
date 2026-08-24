using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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
    static float CliffHeightLow  => S.cliffHeightCells.x * Cell;
    static float CliffHeightHigh => S.cliffHeightCells.y * Cell;
    static float CliffBaseSink   => S.cliffBaseSinkCells * Cell;

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
        PlaceCliffs(map, world, root.transform);
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

                // 절벽은 지형이 아니라 프리팹이 맡는다(PlaceCliffs) — 높이맵은 평평하게 둔다.
                // 높이맵으로 세운 벽은 결국 늘어난 텍스처라 가까이서 암벽으로 보이지 않는다.
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

    // ── 절벽 프리팹 배치 ────────────────────────────────────────
    // 칸마다 바위 하나. 제약은 "절벽이 아닌 타일 침범 금지" 하나뿐이고, 절벽끼리는
    // 마음껏 겹친다(데모 씬이 그렇게 조립돼 있다 — 이웃 중심거리가 반폭 합의 18%).
    //
    // 높이는 <b>쌓지 않고 배치 고도로</b> 만든다: 가장자리 줄만 땅에 서고, 안쪽 바위는
    // 공중에 띄운다. 바닥은 앞줄이 가리므로 보이지 않는다. 쌓으면 이음매가 오레오처럼
    // 줄무늬가 되고, 세로로 늘리면 옆으로 넓은 바위가 콜라캔 판자가 된다 — 둘 다 겪었다.
    // 능선고·변주 주파수·세로 배율, 그리고 프리팹 세트와 디테일 틴트·풀 조건은
    // 전부 TerrainGenSettings 에셋에 있다. 칸 비례 파생값(CliffHeightLow/High,
    // CliffBaseSink)과 GrassWaterLine 은 이 파일 머리의 환산 구역에 있다.

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

    // ── 절벽 ────────────────────────────────────────────────────

    /// <summary>암벽 프리팹 하나의 실측.</summary>
    readonly struct CliffRock
    {
        public readonly GameObject Prefab;
        /// <summary>원본 발자국(x, z) — 축별로 다르게 눌러 정사각형에 맞추는 데 쓴다.</summary>
        public readonly Vector2 Foot;
        /// <summary>
        /// 발자국의 장단축 비(≥1). 슬롯이 이방성이 된 뒤로는 이것이 프리팹 선택의 기준이다 —
        /// 요청 비율에 가까운 프리팹을 고르면 눌러 일그러뜨릴 이유가 없어진다.
        ///
        /// 예전에는 "정육면체로 눌렀을 때 덜 일그러지는가"(Distortion)로 골랐는데, 그 기준은
        /// 곧 "제일 둥근 프리팹"이라 세로로 긴 것이 통째로 배제됐다(실측 3개가 한 번도
        /// 쓰이지 않았다). 절벽면으로 읽힐 형태가 죽은 코드였다.
        /// </summary>
        public readonly float FootAspect;
        /// <summary>정렬·선택용 대표 크기 — 짧은 축 반폭.</summary>
        public float Radius => 0.5f * Mathf.Min(Foot.x, Foot.y);
        public readonly float Height;   // m
        /// <summary>원점 대비 바닥(m). 바닥 피벗이면 0, 중심 피벗이면 음수.</summary>
        public readonly float Bottom;
        public CliffRock(GameObject p, Vector2 foot, float h, float b)
        {
            Prefab = p; Foot = foot; Height = h; Bottom = b;
            FootAspect = Mathf.Max(foot.x, foot.y) / Mathf.Max(0.01f, Mathf.Min(foot.x, foot.y));
        }
    }

    /// <summary>배치가 확정된 바위 하나 — 겹침 검사와 쌓기의 단위.</summary>
    struct PlacedRock
    {
        public int Rock;        // CliffRock 목록의 인덱스
        public Vector2 Center;  // 월드 XZ
        public float Radius;    // 겹침·쌓기용 등가 원 반지름(m) — 두 반폭의 기하평균
        public float HalfAcross; // 벽을 가로지르는 반폭(m) — 클리어런스에 묶인 하드 제약
        public float HalfAlong;  // 벽 접선 방향 반폭(m) — 여기가 길어져야 벽으로 읽힌다
        public float ScaleX, ScaleZ, ScaleY;   // 축별 배율 — 정사각형에 맞추느라 갈린다
        public float TiltX, TiltZ;             // 기울기(도) — 크기를 정할 때 이미 반영했다
        // yaw·strike 를 여기서 들고 가는 이유: 기울기 방위가 yaw 에 상대적이라(Euler ZXY)
        // 둘을 같은 자리에서 정해야 어긋나지 않는다. 예전에는 yaw 를 인스턴스화 때 따로
        // 뽑았고, 그래서 기울기 방위가 벽과 무관한 방향을 향했다.
        public float Yaw;       // 도 — 실제 회전. 긴 축이 X인 프리팹은 여기에 90도가 더 붙는다
        // 장축이 겨눈 방향(도). Yaw 와 나누는 이유: 프리팹에 따라 Yaw 에 90도가 붙어서,
        // Yaw 를 비교하면 장축은 나란한데도 90도 어긋난 것으로 읽힌다.
        // "이웃끼리 절리 방향을 공유하는가"를 재려면 이쪽을 봐야 한다.
        public float AxisDeg;
        public float Strike;    // 도 — 이 자리의 벽 접선. 쌓기가 물려받는다
        public float BaseY;     // 월드 y — 바위 바닥
        public float Top;       // 월드 y — 바위 꼭대기
        public int Layer;       // 0 = 지면 층
        public Vector2Int Seed; // 노이즈·해시의 기준 좌표
    }

    /// <summary>
    /// 절벽 타일에 암벽 프리팹을 세운다. <b>지형을 솟구치게 하지 않는 이유</b>는 높이맵으로
    /// 만든 벽이 결국 늘어난 텍스처 덩어리이기 때문이다 — 가까이서 보면 풀이 벽면에
    /// 발려 있고, 실루엣도 매끄러운 언덕에 가깝다. 프리팹은 제 형태와 노멀을 갖고 있다.
    ///
    /// <b>칸에 맞추지 않는다 — 수평 바운딩 원으로 채운다.</b>
    /// 예전에는 칸마다 하나씩 놓고 축별로 예산을 역산해 비균등 스케일로 칸에 우겨넣었다.
    /// 그 방식은 두 가지를 못 견딘다: (1) 프리팹 가로세로비가 제각각이면 x·z 배율이 갈라져
    /// 저작된 실루엣이 뭉개지고, (2) 회전하면 축정렬 점유 폭이 커져 자유 회전을 못 쓴다 —
    /// 그 보정(회전 점유 폭 역산·기울임 역산·90° 제한·로컬축 되돌리기)이 복잡도의 절반이었다.
    ///
    /// 원은 <b>회전에 불변</b>이라 그 전부가 사라진다. 남는 규칙은 하나다:
    ///   바위의 수평 반지름 ≤ 그 자리의 클리어런스(비절벽 타일까지의 거리)
    /// 이것만 지키면 어떤 각도로 돌리든 지면 타일을 침범하지 않는다. 배율이 균등이라
    /// 바위 모양이 그대로 살고, 크기는 벽 두께가 정한다 — 두꺼운 곳엔 큰 바위가
    /// 듬성듬성, 얇은 능선엔 작은 바위가 촘촘히. 크기 노이즈를 인위로 곱할 필요가 없다.
    ///
    /// 콜라이더는 <b>여기서 붙인다</b> — 프리팹에 없다. 이제 이것이 플레이어를 막고
    /// 총알을 받는 실체다(예전에는 Terrain 경사가 그 일을 했다). 길찾기는 여전히 타일이 정한다.
    /// </summary>
    static void PlaceCliffs(MapDataSO map, World world, Transform root)
    {
        var rocks = new List<CliffRock>();
        foreach (var p in S.cliffSet)
        {
            if (p == null) continue;
            if (!PrefabBounds(p, out Vector2 foot, out float bottom, out float tallness)) continue;
            if (Mathf.Min(foot.x, foot.y) < 0.02f || tallness < 0.01f) continue;
            rocks.Add(new CliffRock(p, foot, tallness, bottom));
        }
        if (rocks.Count == 0)
        {
            Debug.LogWarning("[WorldTerrainGenerator] 쓸 수 있는 절벽 프리팹이 없어 절벽을 세우지 못했습니다 — " +
                             $"{TerrainGenSettings.AssetPath} 의 Cliff Set 확인.");
            return;
        }
        // 발자국 비 순 — 순서 자체에 의미는 없고(PickRock이 비율로 찾는다) 에셋 배열 순서가
        // 바뀌어도 결과가 흔들리지 않게 하는 정규화다.
        rocks.Sort((a, b) => a.FootAspect.CompareTo(b.FootAspect));

        // 프리팹이 낼 수 있는 발자국 비의 상한 — cliffSlotAspect 를 이보다 크게 잡으면
        // 그 차이는 눌러서(roundness) 메울 수밖에 없다. 둘이 상충하므로 값을 함께 봐야 한다.
        float footAspectMax = 1f;
        foreach (var rk in rocks) footAspectMax = Mathf.Max(footAspectMax, rk.FootAspect);
        if (S.cliffSlotAspect > footAspectMax * 1.15f && S.cliffRoundness < 0.6f)
            Debug.LogWarning($"[WorldTerrainGenerator] Cliff Slot Aspect({S.cliffSlotAspect:F2})가 " +
                $"프리팹 발자국 비의 최대({footAspectMax:F2})를 넘습니다. Cliff Roundness가 " +
                $"{S.cliffRoundness:F2}로 낮아 눌러 메우지 않으므로 슬롯이 그만큼 비어 갑니다 — " +
                "상한을 낮추거나 발자국이 길쭉한 프리팹을 넣으세요.");

        var clear = CliffClearance(map, out float maxClear);
        if (maxClear <= 0f) return;   // 절벽 타일이 없다

        int sub = FieldSubDiv;
        int fw = map.width * sub, fh = map.height * sub;
        float px = Cell / sub;                       // 픽셀 한 변(m)
        Vector3 fieldOrigin = world.CellToWorld(Vector2Int.zero);
        float groundY = world.Origin.y - CliffBaseSink;

        // 클리어런스는 "여기까지 들어갈 수 있다"이지 "여기까지 커도 좋다"가 아니다 —
        // 두꺼운 구간에서 한 덩어리가 산봉우리가 되는 것은 따로 죄야 한다.
        float minR = Mathf.Max(0.1f, S.cliffMinRadius);
        float maxR = Mathf.Max(minR, S.cliffMaxRadius);
        // 반지름은 <b>절대</b> 클리어런스를 넘지 않는다 — 절벽 영역 밖으로 나가지 않는 것이
        // 이 알고리즘의 유일한 하드 제약이다. minR은 "이보다 작으면 놓지 않는다"는 하한이지
        // 크기를 끌어올리는 값이 아니다: Max(minR, ...)로 쓰면 얇은 자리에서 클리어런스를
        // 넘겨 튀어나간다(실제로 그렇게 나왔다).

        var placedList = new List<PlacedRock>();

        // 이웃 검사용 공간 해시 — 격자 한 칸이 최대 지름이라 인접 3×3만 보면 된다
        // 버킷 한 변은 있을 수 있는 최대 지름 — 클리어런스 최댓값이 그 상한이다
        float bucket = Mathf.Max(maxClear * 2f, 1f);
        CliffBucketProbe = bucket;   // 품질 계측이 같은 버킷으로 이웃을 찾는다
        var grid = new Dictionary<Vector2Int, List<int>>();

        Vector2 PixelToWorld(int fx, int fy)
            => new Vector2(fieldOrigin.x + (fx + 0.5f) * px, fieldOrigin.z + (fy + 0.5f) * px);

        float ClearAt(Vector2 p)
        {
            int fx = Mathf.FloorToInt((p.x - fieldOrigin.x) / px);
            int fy = Mathf.FloorToInt((p.y - fieldOrigin.z) / px);
            if (fx < 0 || fy < 0 || fx >= fw || fy >= fh) return 0f;
            return clear[fx, fy];
        }

        Vector2Int BucketOf(Vector2 p)
            => new Vector2Int(Mathf.FloorToInt(p.x / bucket), Mathf.FloorToInt(p.y / bucket));

        // 접선 방향으로 얼마나 뻗을 수 있는가 — 벽이 그쪽으로 이어지는 동안만.
        //
        // 장축을 그냥 늘리면 벽이 꺾이거나 끝나는 자리에서 바위가 절벽 밖으로 튀어나간다.
        // "절벽 영역을 벗어나지 않는다"는 이 알고리즘의 유일한 하드 제약이므로, 늘리려는
        // 만큼 양쪽으로 걸어가 보고 벽이 끊기는 지점에서 자른다. 양쪽 대칭으로 보는 이유는
        // 바위가 중심 기준으로 퍼지기 때문이다.
        /// <summary>
        /// 접선 방향 반폭(장축)을 정하고, 그 구간에서 <b>가장 얇은 곳</b>에 맞춰 단축을 줄인다.
        ///
        /// 판정을 이 방향으로 세워야 하는 이유: 클리어런스는 국소 최대(벽 중앙선)에서 어느
        /// 쪽으로 가도 줄어든다. 그래서 "봉우리 두께를 유지하라"고 요구하면 <b>어떤 길이도
        /// 통과하지 못한다</b> — 실측으로 슬롯 장/단축이 1.02에 붙어 이방성이 통째로 죽었다.
        /// 슬랙을 20%로 줘도, 타원 테이퍼를 얹어도 마찬가지였다. 첫 걸음에서 이미 막히기 때문이다.
        ///
        /// 실제 슬래브는 <b>길어지는 만큼 얇아진다</b>. 그래서 길이를 먼저 늘리고, 그 길이
        /// 전체가 들어갈 만큼 두께를 깎는다. 침범 금지는 그대로다 — 오히려 구간 최솟값을
        /// 쓰므로 예전보다 보수적이다.
        /// </summary>
        float SlotAlong(Vector2 p, Vector2 tan, float acrossPeak, float aspect, out float acrossFit)
        {
            const int Steps = 6;
            float want = acrossPeak * aspect;
            float fit = acrossPeak;
            float best = 0f;

            for (int i = 1; i <= Steps; i++)
            {
                float t = (float)i / Steps;
                float d = want * t;
                float room = Mathf.Min(ClearAt(p + tan * d), ClearAt(p - tan * d));

                // 끝으로 갈수록 바위 폭이 줄어드니(타원) 그만큼은 좁아도 된다 —
                // 끝에서 필요한 폭은 중앙 폭의 √(1−t²) 배다.
                float taper = Mathf.Sqrt(Mathf.Max(0.04f, 1f - t * t));
                float allow = room / taper;

                float next = Mathf.Min(fit, allow);
                if (next < minR) break;       // 이만큼 늘리면 너무 얇아진다 — 여기서 멈춘다
                fit = next;
                best = d;
            }

            acrossFit = fit;
            return Mathf.Max(fit, best);      // 최소한 등방(정사각)은 보장
        }

        static Vector2 TangentOf(float strikeDeg)
        {
            float a = strikeDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a));   // yaw 의 방향 벡터와 같은 규약
        }

        /// <summary>
        /// 윤곽의 이 자리에서 안쪽으로 놓을 수 있는 <b>최대 반두께</b>.
        ///
        /// 윤곽에서 법선을 따라 a 만큼 들어간 점의 클리어런스는 직선 벽이라면 정확히 a 다
        /// (클리어런스 = 가장 가까운 비절벽까지의 거리). 그래서 "a 만큼 들어갔을 때
        /// 클리어런스가 a 를 따라오는가"만 보면 된다 — 벽 중앙선을 넘는 순간 따라오지
        /// 못하고, 거기가 곧 이 자리의 최대 두께다.
        ///
        /// 반두께 a 인 바위를 윤곽에서 a 만큼 안쪽에 놓으면 윤곽에 정확히 접한다.
        /// 그래서 "절벽 밖으로 나가지 않는다"가 계산이 아니라 <b>배치 방식 자체로</b> 보장된다.
        /// </summary>
        float MaxHalfThickness(Vector2 edge, Vector2 nrm, float lo, float hi)
        {
            // <b>lo 를 포함해</b> 로그 등간격으로 훑는다.
            //
            // 예전에는 Lerp(lo, hi, i/Steps) 를 i=1 부터 돌렸다. lo=1.05·hi=10 이면 첫 표본이
            // 이미 1.945m 라, 이 함수는 <b>0 아니면 1.945 이상</b>만 돌려줬다. 그래서 반두께
            // 1.945m 를 못 내는 벽 — 폭 3.9m 미만 — 에는 지면 층이 통째로 놓이지 않았다.
            // 칸이 4m 이므로 폭 1칸짜리 절벽 띠 전체가 여기 걸렸다(면 구멍의 84%).
            //
            // 로그 간격인 이유: 여기서 중요한 것은 절대 오차가 아니라 비율이다. 0.4m 와
            // 0.6m 의 차이가 8m 와 8.2m 의 차이보다 훨씬 크게 보인다.
            lo = Mathf.Max(0.05f, lo);
            hi = Mathf.Max(lo, hi);
            const int Steps = 14;
            float best = 0f;
            for (int i = 0; i <= Steps; i++)
            {
                float a = lo * Mathf.Pow(hi / lo, (float)i / Steps);
                if (ClearAt(edge + nrm * a) < a * 0.9f) break;
                best = a;
            }
            return best;
        }

        // ── 절리 방향(strike) — 이 알고리즘에서 <b>방향을 만드는 유일한 곳</b> ──
        //
        // 노두가 노두로 읽히는 이유는 조각들이 공통 절리면을 공유하기 때문이다. 방향이
        // 제각각이면 지질이 아니라 쏟아놓은 자갈로 보인다. 그래서 각도를 난수에서 뽑지 않고
        // 벽의 형상에서 끌어낸다: 클리어런스가 가장 빨리 변하는 방향이 벽을 가로지르는
        // 방향이고, 그 <b>수직</b>이 벽의 접선이다.
        //
        // 스텐실을 넓혀 가며 보는 이유: 거리장은 벽 중앙선에서 그래디언트가 0에 가깝다
        // (양쪽 벽이 같은 거리라 상쇄된다). 한 픽셀 차분으로는 그 자리에서 노이즈만 읽어
        // 두꺼운 벽 안쪽 바위들이 방향을 잃는다.
        float StrikeAt(int fx, int fy)
        {
            // <b>구조 텐서</b>로 뽑는다. 그래디언트를 벡터로 더하면 안 되는 이유가 있다:
            // 벽 중앙선에서는 양쪽 벽의 그래디언트가 서로 <b>반대</b>를 향해 상쇄되고,
            // 남는 것은 노이즈뿐이다. 그런데 지면 층은 클리어런스가 큰 곳(=중앙선)부터
            // 채우므로 <b>제일 큰 바위들이 전부 그 자리에서 방향을 잃는다</b>.
            // 실측: 접선을 따라 늘리려던 장축이 실제로는 벽을 가로질러 뻗다가 잘려
            // 슬롯 장/단축 중앙값이 1.02(= 정사각)로 주저앉았다.
            //
            // 텐서는 곱(gx²·gxgy·gy²)을 쌓아서 <b>부호에 둔감하다</b> — 반대 방향 그래디언트가
            // 상쇄되지 않고 서로를 보강한다. 그래서 직선 벽이면 중앙선에서도 벽 방향이 나온다.
            const int Rad = 8, Step = 4, G = 4;
            double jxx = 0, jxy = 0, jyy = 0;
            for (int oy = -Rad; oy <= Rad; oy += Step)
                for (int ox = -Rad; ox <= Rad; ox += Step)
                {
                    int x = Mathf.Clamp(fx + ox, 0, fw - 1);
                    int y = Mathf.Clamp(fy + oy, 0, fh - 1);
                    float gx = clear[Mathf.Min(x + G, fw - 1), y] - clear[Mathf.Max(x - G, 0), y];
                    float gy = clear[x, Mathf.Min(y + G, fh - 1)] - clear[x, Mathf.Max(y - G, 0)];
                    jxx += gx * gx; jxy += gx * gy; jyy += gy * gy;
                }

            // 사방이 평평한 자리(넓은 고원) — 저주파 노이즈로 완만하게 돌린다.
            // 픽셀별 난수와 달리 이웃끼리 이어져서 최소한 모자이크는 되지 않는다.
            if (jxx + jyy < 1e-5)
                return Mathf.PerlinNoise(fx * 0.004f + 3.1f, fy * 0.004f + 7.7f) * 180f;

            // 텐서의 주축 = 변화가 가장 큰 방향 = 벽을 가로지르는 방향.
            float th = 0.5f * Mathf.Atan2((float)(2.0 * jxy), (float)(jxx - jyy));
            // 그 수직이 벽 접선. Unity 의 yaw 는 +Z 가 0 이라 (x, z) 순서로 atan2 한다.
            return Mathf.Atan2(-Mathf.Sin(th), Mathf.Cos(th)) * Mathf.Rad2Deg;
        }

        // 클리어런스만 쓰면 벽 두께가 일정한 구간에서 바위가 전부 같은 크기가 된다.
        // 큰 흐름(펄린)에 낱개 차이(해시)를 섞는다 — 칸별 독립 난수만 쓰면 "쌓인 지형"이
        // 아니라 "흩뿌린 에셋"으로 읽힌다. 줄이는 방향이라 침범은 여전히 불가능하다.
        float SizeNoise(int fx, int fy)
            => Mathf.Lerp(S.cliffSizeNoise.x, S.cliffSizeNoise.y,
                   0.65f * Mathf.PerlinNoise(fx * 0.018f + 61.7f, fy * 0.018f + 8.9f)
                 + 0.35f * (Hash(fx, fy, 47) % 1000 / 1000f));

        bool TooClose(Vector2 p, float r, float spacing)
        {
            var b0 = BucketOf(p);
            for (int by = -1; by <= 1; by++)
                for (int bx = -1; bx <= 1; bx++)
                    if (grid.TryGetValue(new Vector2Int(b0.x + bx, b0.y + by), out var list))
                        foreach (int i in list)
                        {
                            var o = placedList[i];
                            if (o.Layer != 0) continue;
                            float min = (r + o.Radius) * spacing;
                            if ((o.Center - p).sqrMagnitude < min * min) return true;
                        }
            return false;
        }

        /// <summary>
        /// 지면 층의 간격 검사 — <b>방향별로 다르게</b> 잰다.
        ///
        /// 등가 원으로 재던 것이 이방성 바위와 맞지 않았다. 반지름 √(단축×장축)은
        /// 단축보다 뚱뚱하고 장축보다 홀쭉해서, 벽을 <b>가로질러</b>서는 필요 이상으로
        /// 밀어내고(줄이 안 들어간다) 벽을 <b>따라</b>서는 덜 밀어냈다.
        ///
        /// 그런데 두 방향은 원하는 바가 애초에 다르다:
        ///   · 벽을 따라서는 <b>겹쳐야</b> 한다 — 겹치지 않으면 조각 사이가 그대로 관통이다.
        ///     절리 방향을 공유하는 조각들이라 겹침이 뭉개짐이 아니라 쌓인 벽으로 읽힌다.
        ///   · 벽을 가로질러서는 <b>떨어져야</b> 한다 — 붙으면 줄이 하나로 녹아 두께가 안 생긴다.
        /// 그래서 축마다 다른 계수를 쓰고, 타원 합으로 판정한다.
        /// </summary>
        bool TooCloseSlot(Vector2 p, float across, float along, Vector2 tan)
        {
            Vector2 nrm = new Vector2(-tan.y, tan.x);
            // 계수는 <b>벽을 따라</b>가 작아야 한다 — 작을수록 겹쳐도 통과다.
            // 처음에 반대로 걸었더니(장축 합에 큰 계수) 벽을 따라 더 밀어내서
            // 덮임이 51.5% → 44.6% 로 떨어졌다. 겹치라고 만든 장치가 떼어놓고 있었다.
            float sAlongF = Mathf.Max(0.05f, S.cliffPackSpacing / Mathf.Max(0.2f, S.cliffRowSeparation));
            float sAcrossF = Mathf.Max(0.05f, S.cliffPackSpacing);

            var b0 = BucketOf(p);
            for (int by = -1; by <= 1; by++)
                for (int bx = -1; bx <= 1; bx++)
                    if (grid.TryGetValue(new Vector2Int(b0.x + bx, b0.y + by), out var list))
                        foreach (int i in list)
                        {
                            var o = placedList[i];
                            if (o.Layer != 0) continue;
                            Vector2 d = o.Center - p;
                            float dl = Vector2.Dot(d, tan) / Mathf.Max(0.01f, (along + o.HalfAlong) * sAlongF);
                            float dc = Vector2.Dot(d, nrm) / Mathf.Max(0.01f, (across + o.HalfAcross) * sAcrossF);
                            if (dl * dl + dc * dc < 1f) return true;
                        }
            return false;
        }

        // 애추끼리의 간격 — 지면 층과는 따로 본다. 애추는 벽의 일부가 아니라 그 앞에
        // 흩어진 퇴적물이라, 큰 바위와 겹쳐도(오히려 겹쳐야) 자연스럽다.
        bool TooCloseTalus(Vector2 p, float r)
        {
            var b0 = BucketOf(p);
            for (int by = -1; by <= 1; by++)
                for (int bx = -1; bx <= 1; bx++)
                    if (grid.TryGetValue(new Vector2Int(b0.x + bx, b0.y + by), out var list))
                        foreach (int i in list)
                        {
                            var o = placedList[i];
                            if (o.Layer >= 0) continue;
                            float min = (r + o.Radius) * S.cliffTalusSpacing;
                            if ((o.Center - p).sqrMagnitude < min * min) return true;
                        }
            return false;
        }

        // 덮임 판정 — 실제 반지름보다 <b>작게</b> 본다. 바위는 원이 아니라 그 원 안에 든
        // 덩어리라, 외접원으로 덮였다고 실제로 막혔다는 보장이 없다. 양쪽 다 보수적으로:
        // 침범 판정은 외접원(크게), 커버 판정은 그보다 작게.
        bool Covered(Vector2 p)
        {
            float f = Mathf.Clamp01(S.cliffCoverFactor);
            var b0 = BucketOf(p);
            for (int by = -1; by <= 1; by++)
                for (int bx = -1; bx <= 1; bx++)
                    if (grid.TryGetValue(new Vector2Int(b0.x + bx, b0.y + by), out var list))
                        foreach (int i in list)
                        {
                            var o = placedList[i];
                            if (o.Layer != 0) continue;
                            float rr = o.Radius * f;
                            if ((o.Center - p).sqrMagnitude < rr * rr) return true;
                        }
            return false;
        }

        void Place(Vector2Int seed, Vector2 p, float across, float along, float baseY,
                   int layer, float strike)
        {
            float aspectWant = along / Mathf.Max(0.01f, across);
            int pick = PickRock(rocks, aspectWant, Hash(seed.x, seed.y, 23 + layer * 7));
            var rock = rocks[pick];

            // yaw — 벽 접선을 따르고 지터만 얹는다. 예전에는 PerlinNoise * 720 이었는데,
            // 픽셀당 노이즈가 0.02 씩 가고 이웃 바위는 10~17픽셀 떨어져 있어서 이웃 간
            // 각도차가 100도를 넘었다. 흐름장을 두려던 장치가 균등난수가 돼 있었다.
            float yaw = strike + (Hash(seed.x, seed.y, 89) % 1000 / 1000f - 0.5f) * S.cliffYawJitter;

            // 프리팹의 <b>긴 발자국 축을 접선에 맞춘다.</b> 로컬 +Z 가 yaw 방향이므로,
            // 긴 축이 X 인 프리팹은 yaw 를 90도 돌려 X 를 접선으로 보낸다.
            // 이 한 줄이 납작한 프리팹을 벽을 따라 눕게 만든다 — 예전에는 어느 축이 어디를
            // 향하든 정육면체로 눌러 버려서 프리팹의 비율이 아무 의미가 없었다.
            bool longIsX = rock.Foot.x > rock.Foot.y;
            float axisDeg = yaw;             // 장축이 겨눈 방향 — 90도 보정 전의 값
            if (longIsX) yaw += 90f;
            float footAlong = longIsX ? rock.Foot.x : rock.Foot.y;
            float footAcross = longIsX ? rock.Foot.y : rock.Foot.x;

            // <b>먼저 둥글게, 그 다음 내접으로 배치.</b>
            //
            // 외접원으로 재면 정사각 발자국조차 √2배 손해다 — 두께 2r 벽에 폭 1.41r짜리만
            // 들어가 70%밖에 못 채운다. 그래서 축별로 눌러 발자국을 정사각(D×D)에 맞춘 뒤
            // 내접(D/2 = r)으로 잰다. 그러면 바위가 벽 두께를 100% 채운다.
            //
            // 정사각이면 회전해도 내접원이 그대로라 자유 회전도 유지된다(모서리는 √2까지
            // 나가지만, 둥글게 만든 메시라 그 자리에 실제 형체가 거의 없다).
            // <b>기울기를 먼저 정한다.</b> 기울이면 수평 점유가 늘어나므로(누운 만큼 옆으로
            // 퍼진다) 그 증가분을 크기에서 미리 빼야 절벽 밖으로 안 나간다. 순서를 반대로
            // 하면 기울이는 순간 침범이라, 예전에는 기울기를 ±4°로 죄어 놨었다 —
            // 그래서 바위가 전부 위로만 섰다.
            // dip 은 <b>두 세트만</b> 쓴다. 조각들이 저마다 다른 각도로 기울면 무질서지만
            // 두 각도로만 기울면 층리로 읽힌다 — 지질 느낌의 대부분이 여기서 나온다.
            // 예전에는 tiltX·tiltZ 를 독립 해시로 뽑아서 기울기 방위가 완전 랜덤이었다.
            bool steep = Hash(seed.x, seed.y, 101) % 1000 < Mathf.RoundToInt(S.cliffDipSteepShare * 1000f);
            float dip = S.cliffTilt * (steep ? 0.85f : 0.25f)
                      + (Hash(seed.x, seed.y, 103) % 1000 / 1000f - 0.5f) * S.cliffTilt * 0.25f;
            dip = Mathf.Max(0f, dip);

            // 기울기 방위는 벽을 가로지르는 방향(strike 의 수직) — 층리가 언덕 안쪽으로
            // 파고드는 모습이다. Euler(x, yaw, z) 는 ZXY 순서라 기울기가 yaw 보다 먼저
            // 적용된다: 월드 방위를 로컬로 환산해 넣어야 벽 기준으로 눕는다.
            float leanLocal = (strike + 90f - yaw) * Mathf.Deg2Rad;
            float tiltX = dip * Mathf.Sin(leanLocal);
            float tiltZ = -dip * Mathf.Cos(leanLocal);
            float tilt = dip * Mathf.Deg2Rad;

            // 눕히면 수평 점유가 얼마나 커지는가 — <b>상자가 아니라 타원체로</b> 잰다.
            // 바위엔 모서리가 없다. 직육면체로 보면 cos+sin·(H/D)라 22°에서 1.30(30% 손해)이
            // 나오는데, 그건 앞서 외접원을 쓴 것과 똑같은 과잉 보수다.
            //
            // 반축 (D/2, H/2, D/2)인 타원체를 θ만큼 눕혔을 때의 수평 반경:
            //     √( (D/2)²·cos²θ + (H/2)²·sin²θ )
            // D == H면 각도와 무관하다 — <b>구는 굴려도 같다</b>. stretch로 늘린 만큼만 는다.
            // stretch 1.1 · 22°면 1.015라 손해가 1.5%뿐이다.
            float stretch = Mathf.Lerp(S.cliffStretch.x, S.cliffStretch.y,
                                       Hash(seed.x, seed.y, 37 + layer) % 1000 / 1000f);
            float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);
            float grow = Mathf.Sqrt(ct * ct + stretch * stretch * st * st);

            // <b>grow 는 단축에만 적용한다.</b> 기울기 방위가 벽을 가로지르는 방향이므로
            // 눕는 만큼 늘어나는 점유는 단축뿐이다 — 예전에는 정사각 D 전체에 걸었고,
            // 그만큼 접선 방향을 공짜로 깎아먹고 있었다.
            float acrossFit = across / Mathf.Max(0.5f, grow);

            float sAlong = 2f * along / footAlong;
            float sAcross = 2f * acrossFit / footAcross;
            // 원본 비율을 지킬 때 슬롯 안에 들어가는 균등 배율. 슬롯 비율에 맞는 프리팹을
            // 골랐으므로 이 값이 두 축 배율 모두에 가깝고, 남는 여유가 작다.
            float u = Mathf.Min(sAlong, sAcross);

            // <b>필요한 곳에서만 누른다.</b> roundness 를 전역 상수로 두면 두 극단뿐이다 —
            // 높으면 11종의 실루엣이 전부 정육면체가 되고, 낮으면 프리팹 비율(최대 1.56)을
            // 넘는 슬롯을 채우지 못해 얇은 벽이 빈다.
            //
            // 그래서 프리팹 비율이 슬롯 비율을 못 따라가는 <b>그만큼만</b> 누른다.
            // 두꺼운 벽에서는 둘이 비슷해 round 가 설정값 그대로 낮게 유지되고(실루엣 보존),
            // 얇은 벽에서만 눌러 길게 눕는다 — 거기서는 구멍이 나느니 눌리는 편이 낫다.
            float need = 1f - rock.FootAspect / Mathf.Max(1f, aspectWant);
            float round = Mathf.Clamp01(Mathf.Max(S.cliffRoundness, need));

            float scaleAlong = Mathf.Min(Mathf.Lerp(u, sAlong, round), S.cliffMaxScale);
            float scaleAcross = Mathf.Min(Mathf.Lerp(u, sAcross, round), S.cliffMaxScale);
            // 높이는 <b>단축</b> 기준이다. 장축에 맞추면 벽을 따라 길게 뻗은 바위가 그만큼
            // 높아져 얇은 벽 위에 탑이 선다.
            float scaleY = Mathf.Min(Mathf.Lerp(u, 2f * acrossFit / rock.Height, round),
                                     S.cliffMaxScale) * stretch;

            float scaleX = longIsX ? scaleAlong : scaleAcross;
            float scaleZ = longIsX ? scaleAcross : scaleAlong;

            // 실제 점유 반폭. 겹침·쌓기는 원으로 재므로 두 반폭의 기하평균(같은 면적의 원)을 쓴다 —
            // 단축으로 재면 벽을 따라 겹쳐 붙고, 장축으로 재면 벽을 가로질러 헐거워진다.
            float halfAcross = 0.5f * footAcross * scaleAcross * grow;
            float halfAlong = 0.5f * footAlong * scaleAlong;
            float radius = Mathf.Sqrt(Mathf.Max(0.01f, halfAcross * halfAlong));

            // 세장비 안전 상한. 바위 하나에 최소 높이를 보장하던 코드는 걷어냈다 —
            // 수평이 클리어런스에 묶인 상태에서 높이를 요구하면 늘어나는 건 세로뿐이라
            // 벽이 통째로 길쭉해진다(실측: 폭 2.7m에 높이 8.2m). 높이는 쌓기가 만든다.
            // 세장비는 <b>단축</b> 기준 — 등가 반지름으로 재면 벽을 따라 긴 바위가
            // 그 길이만큼 높아질 수 있어 바늘이 된다.
            float maxY = halfAcross * S.cliffMaxAspect / rock.Height;
            if (scaleY > maxY) scaleY = maxY;

            // <b>절대 높이 상한.</b> 크기를 클리어런스가 정하게 두면 두꺼운 구간에서
            // 반지름이 7m까지 가고, 둥글게 눌러 놨으니 높이도 같이 따라 올라가 산봉우리가
            // 된다. 절벽은 벽이지 봉우리가 아니다 — 여기서 잘라 스카이라인을 잡는다.
            //
            // 세로만 자른다(수평은 그대로) — 폭까지 줄이면 두꺼운 구간이 도로 헐거워진다.
            // 대신 그만큼 납작해지므로 상한을 너무 낮게 잡으면 팬케이크가 된다.
            float capY = S.cliffMaxHeightCells * Cell / rock.Height;
            if (scaleY > capY) scaleY = capY;

            placedList.Add(new PlacedRock
            {
                Rock = pick,
                Center = p,
                Radius = radius,
                HalfAcross = halfAcross,
                HalfAlong = halfAlong,
                ScaleX = scaleX,
                ScaleZ = scaleZ,
                ScaleY = scaleY,
                TiltX = tiltX,
                TiltZ = tiltZ,
                Yaw = yaw,
                AxisDeg = axisDeg,
                Strike = strike,
                BaseY = baseY,
                Top = baseY + rock.Height * scaleY,
                Layer = layer,
                Seed = seed,
            });

            var b0 = BucketOf(p);
            if (!grid.TryGetValue(b0, out var bl)) grid[b0] = bl = new List<int>();
            bl.Add(placedList.Count - 1);
        }

        // ── 지면 층 — <b>윤곽을 따라 걷는다</b> ──
        //
        // 예전에는 절벽 영역의 픽셀을 클리어런스 큰 순으로 훑으며 놓았다(그리디). 그런데
        // 절벽 영역이 맵의 26%나 되는데 플레이어에게 보이는 것은 그 <b>테두리</b>뿐이다 —
        // 실측으로 바위 하나가 50㎡를 맡아 넓은 면적에 한 겹을 얇게 펴 바르고 있었고,
        // 안쪽 바위는 서로 가려 아무 일도 하지 않으면서 예산만 썼다. 벽이 낮아 보이고
        // 덮임이 73%에 그친 것이 그 결과다.
        //
        // <b>면적을 샘플링해 선을 만들고 있었다.</b> 그래서 선을 직접 걷는다:
        //   · 루프를 끝까지 걸으므로 구멍이 구조적으로 생기지 않는다
        //   · strike 가 공짜다 — 폴리라인의 접선이 곧 절리 방향
        //   · 얇은 벽이 자연히 처리된다. 얇으면 얇은 바위가 놓일 뿐, 안 놓이지 않는다
        //   · 크기가 벽 두께에서 <b>풀려난다</b> — 두께는 안쪽으로 얼마나 물러나 놓느냐로
        //     정해지므로, 크기는 원하는 분포대로 뽑을 수 있다
        var loops = CliffContours(clear, fw, fh, fieldOrigin, px);
        // 루프마다, <b>층 단위로 한 바퀴씩</b> 돈다.
        //
        // 예전에는 한 자리에서 기둥을 끝까지 쌓고 옆으로 갔다. 그러면 기둥 하나가 실패했을 때
        // 바닥부터 꼭대기까지 통째로 비고, 이웃 기둥은 그 사실을 모른다 — 스크린샷에 계속
        // 나오던 세로 슬롯이 그것이다. 게다가 그 구조는 <b>이음매가 세로로 정렬되도록
        // 보장</b>한다. 벽돌을 그렇게 쌓는 사람은 없다.
        //
        // 층 단위로 돌면 층마다 시작 위상이 어긋나 아래 층의 이음매 위에 위 층의 블록이
        // 온다(엇쌓기). 그러면 세로로 뚫리려면 <b>모든 층이 같은 자리에 이음매를 가져야</b>
        // 하는데, 위상을 어긋내면 그럴 수가 없다.
        //
        // 층이 어긋나므로 "내 아래가 어디까지 찼는가"를 기둥 변수로는 알 수 없다. 대신
        // 루프의 <b>호길이별 높이 프로파일</b>을 들고 다닌다 — 벽돌공이 아래 켜를 보고
        // 다음 켜를 얹는 것과 같다.
        int stations = 0;
        // 걷기 계측 — 어느 관문에서 얼마나 걸러지는지. 추측 대신 이걸 본다.
        int stTried = 0, stThin = 0, stPebble = 0, stNoRoom = 0, stHigh = 0;
        double contourM = 0;
        foreach (var lp in loops)
            for (int k = 0; k < lp.Count; k++)
                contourM += (lp[(k + 1) % lp.Count] - lp[k]).magnitude;

        foreach (var loop in loops)
        {
            if (loop.Count < 3) continue;

            // 호길이 색인 — 임의의 호 위치에서 점과 접선을 뽑는다
            var cum = new float[loop.Count + 1];
            for (int i = 0; i < loop.Count; i++)
                cum[i + 1] = cum[i] + (loop[(i + 1) % loop.Count] - loop[i]).magnitude;
            float total = cum[loop.Count];
            if (total < minR) continue;

            int SegAt(float arc)
            {
                int lo = 0, hi = loop.Count;
                while (lo + 1 < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (cum[mid] <= arc) lo = mid; else hi = mid;
                }
                return lo;
            }

            // 높이 프로파일 — 해상도는 0.5m. 층이 어긋나도 이 배열이 아래 켜를 알려준다.
            const float Res = 0.5f;
            int slots = Mathf.Max(1, Mathf.CeilToInt(total / Res));
            var topAt = new float[slots];
            for (int i = 0; i < slots; i++) topAt[i] = groundY;

            int SlotOf(float arc) => ((Mathf.FloorToInt(arc / Res) % slots) + slots) % slots;

            // 발자국 구간에서 <b>가장 낮은</b> 곳에 앉힌다.
            //
            // 최댓값에 앉히면 안 된다 — 구간 안에 높은 바위가 하나라도 있으면 그 높이로
            // 올라가는데, sway·setback 이 바위를 옆·안쪽으로 옮겨 놓으므로 <b>그 높이를 만든
            // 바위 위가 아니라 허공에 앉는다</b>. 실제로 바위가 공중에 뜬 채로 흩어졌다.
            //
            // 최솟값이면 높은 이웃을 파고들지만 그건 공짜다(불투명한 덩어리끼리 겹치는 것은
            // 보이지 않는다). 뜨는 것은 공짜가 아니다.
            float TopOver(float arc, float half)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(half * 2f / Res));
                float low = float.MaxValue;
                for (int i = 0; i <= n; i++)
                    low = Mathf.Min(low, topAt[SlotOf(arc - half + i * (half * 2f / n))]);
                return low == float.MaxValue ? groundY : low;
            }
            void WriteTop(float arc, float half, float top)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(half * 2f / Res));
                for (int i = 0; i <= n; i++)
                {
                    int k = SlotOf(arc - half + i * (half * 2f / n));
                    if (top > topAt[k]) topAt[k] = top;
                }
            }

            int courses = Mathf.Max(1, S.cliffCourses);
            for (int course = 0; course < courses; course++)
            {
                // 층마다 시작 위상을 어긋낸다. 0.37은 무리수에 가까운 비율이라 층이 몇이든
                // 위상이 겹치지 않는다 — 0.5로 두면 짝수 층끼리 도로 정렬된다.
                float arc = total * (course * 0.37f % 1f);
                float walked = 0f;

                while (walked < total)
                {
                    int seg = SegAt(Mathf.Repeat(arc, total));
                    Vector2 a0v = loop[seg], b0v = loop[(seg + 1) % loop.Count];
                    float segLen = Mathf.Max(1e-4f, cum[seg + 1] - cum[seg]);
                    float f = Mathf.Clamp01((Mathf.Repeat(arc, total) - cum[seg]) / segLen);
                    Vector2 onEdge = Vector2.Lerp(a0v, b0v, f);

                    Vector2 tan = LoopTangent(loop, seg, 3);
                    if (tan.sqrMagnitude < 1e-6f) tan = (b0v - a0v).normalized;

                    Vector2 nrm = new Vector2(-tan.y, tan.x);
                    if (ClearAt(onEdge + nrm) < ClearAt(onEdge - nrm)) nrm = -nrm;

                    // 마칭스퀘어 정점은 실제 경계에서 최대 1m 어긋난다 — 안으로 밀어 넣는다
                    int push = 0;
                    while (ClearAt(onEdge) <= 0f && push < 6) { onEdge += nrm * (px * 2f); push++; }
                    if (ClearAt(onEdge) <= 0f) { arc += minR; walked += minR; continue; }

                    stTried++;
                    int hx = Mathf.RoundToInt(onEdge.x * 4f), hy = Mathf.RoundToInt(onEdge.y * 4f);

                    float thinFloor = Mathf.Max(0.05f, S.cliffMinThicknessM);
                    float roomMax = MaxHalfThickness(onEdge, nrm, thinFloor, maxR);
                    if (roomMax < thinFloor) { stThin++; arc += minR; walked += minR; continue; }

                    // 크기 — 층마다 다른 해시라 이음매가 저절로 어긋난다
                    // 크기 — <b>범위를 넓게</b> 잡아야 위계가 생긴다.
                    //
                    // 예전에는 roomMax 의 0.45~0.8 로 묶어 놓고 멱법칙을 씌웠다. 분포를 아무리
                    // 비틀어도 1.8배 범위 안에서만 노니 전부 중간 크기가 되고, 벽이 "비슷한
                    // 돌을 여러 겹 쌓은 것"으로 보였다. 자연의 멱법칙은 <b>범위가 넓다</b> —
                    // 압도적인 몇 개와 잔해가 같은 벽에 있다.
                    //
                    // 상한이 1을 넘는 것은 일부러다. 큰 덩어리가 벽 두께를 조금 넘어 튀어나오는
                    // 것이 절벽에서는 오히려 자연스럽다(하드 제약은 아래 ClearAt 검사가 지킨다).
                    float u01 = Hash(hx, hy, 71 + course * 13) % 1000 / 1000f;
                    float big = Mathf.Pow(u01, 2.6f);
                    float ar = roomMax * Mathf.Lerp(0.22f, 1.25f, big)
                             * Mathf.Pow(0.96f, course);       // 위 층은 아주 조금씩만 작게

                    float thinBoost = Mathf.Clamp(minR / Mathf.Max(0.05f, ar),
                                                  1f, Mathf.Max(1f, S.cliffThinAspectMax));
                    float aspect = Mathf.Lerp(1f, Mathf.Max(1f, S.cliffSlotAspect),
                                              Hash(hx, hy, 53 + course * 13) % 1000 / 1000f) * thinBoost;
                    float br = ar * aspect;

                    float advance = Mathf.Max(minR * 0.4f, br * 2f * S.cliffPackSpacing);

                    if (ar * Mathf.Sqrt(aspect) < minR * 0.45f)
                    { stPebble++; arc += advance; walked += advance; continue; }

                    // 안쪽으로 물러남(안식각) + 접선 방향 흔들림
                    float setback = course * ar * S.cliffCourseSetback;
                    float sway = (Hash(hx, hy, 271 + course) % 1000 / 1000f - 0.5f)
                               * 2f * ar * S.cliffCourseSway;
                    Vector2 c = onEdge + nrm * (setback + ar) + tan * sway;

                    if (ClearAt(c) < ar * 0.75f)
                    { stNoRoom++; arc += advance; walked += advance; continue; }

                    // <b>아래 켜를 보고 얹는다.</b> 자기 발자국 구간에서 가장 높은 곳이 앉을 자리다.
                    float seat = TopOver(arc, br);

                    float ridge = Mathf.PerlinNoise(onEdge.x * S.cliffRidgeFrequency / Cell + 5.3f,
                                                    onEdge.y * S.cliffRidgeFrequency / Cell + 9.1f);
                    float wantTop = world.Origin.y + Mathf.Lerp(CliffHeightLow, CliffHeightHigh, ridge);
                    if (seat >= wantTop) { stHigh++; arc += advance; walked += advance; continue; }

                    // 이음매를 파묻는다 — 아래 켜 꼭대기보다 조금 내려 앉힌다
                    if (course > 0) seat -= ar * S.cliffCourseSink;

                    // <b>간격 검사는 하지 않는다.</b> 걸음이 이미 간격을 정한다 —
                    // 겹치도록 전진해 놓고 겹쳤다고 거부하면 서로를 상쇄할 뿐이었다
                    // (실측: 그 검사가 자리 1420회를 막고 있었다).
                    float strike = Mathf.Atan2(tan.x, tan.y) * Mathf.Rad2Deg;
                    Place(new Vector2Int(hx, hy + course * 7919), c, ar, br, seat, course, strike);
                    stations++;

                    WriteTop(arc, br, placedList[placedList.Count - 1].Top);

                    arc += advance;
                    walked += advance;
                }
            }
        }

        int ground = placedList.Count;
        // ── 발치 애추 ──
        // 벽의 바깥 테두리 띠에만 작은 바위를 흩는다. 반지름이 클리어런스를 넘어도 좋다 —
        // <b>일부러 지면 쪽으로 삐져나오게</b> 두는 것이 이 띠의 목적이다. 바위와 잔디가
        // 선으로 만나는 것을 깨는 것 말고는 하는 일이 없다.
        //
        // 그래서 콜라이더를 붙이지 않는다(아래 인스턴스화에서 Layer로 가른다). 통행 가능한
        // 땅에 콜라이더를 두면 플레이어가 안 보이는 것에 걸리고, 길찾기 격자는 맵 타일을
        // 보므로 몬스터와 플레이어의 통행 판정이 어긋난다.
        int talus = 0;
        if (S.cliffTalusDensity > 0f && S.cliffTalusRadius.y > 0f)
        {
            float band = Mathf.Max(0.05f, S.cliffTalusBandM);
            int gate = Mathf.RoundToInt(Mathf.Clamp01(S.cliffTalusDensity) * 1000f);
            float tLo = minR * Mathf.Max(0.02f, S.cliffTalusRadius.x);
            float tHi = minR * Mathf.Max(S.cliffTalusRadius.x, S.cliffTalusRadius.y);

            for (int fy = 0; fy < fh; fy++)
                for (int fx = 0; fx < fw; fx++)
                {
                    float c = clear[fx, fy];
                    if (c <= 0f || c > band) continue;
                    if (Hash(fx, fy, 307) % 1000 >= gate) continue;

                    Vector2 p = PixelToWorld(fx, fy);
                    float r = Mathf.Lerp(tLo, tHi, Hash(fx, fy, 311) % 1000 / 1000f);
                    if (TooCloseTalus(p, r)) continue;

                    // 애추는 절리 세트를 따르지 않는다 — 무너져 내린 조각에는 방향이 없다.
                    // 정렬된 벽 아래에 방향 없는 잔해가 깔리는 것이 "벽이 부서져 쌓였다"로 읽힌다.
                    float strikeT = (Hash(fx, fy, 313) % 1000 / 1000f) * 360f;
                    // 애추는 등방 슬롯이다 — 무너져 내린 잔해에는 벽의 접선이라는 개념이 없다
                    Place(new Vector2Int(fx, fy), p, r, r,
                          groundY - r * S.cliffTalusSink, -1, strikeT);
                    talus++;
                }
        }

        // ── 인스턴스별 명도 단계 ──
        var shade = BuildShadeVariants(rocks);
        int shadeSteps = Mathf.Clamp(S.cliffShadeSteps, 1, 16);
        float shadeLo = 1f - S.cliffShadeRange - S.cliffShadeDepthDarken;
        float shadeHi = 1f + S.cliffShadeRange;

        // ── 인스턴스화 ──
        var parent = new GameObject("Cliffs").transform;
        parent.SetParent(root, false);

        foreach (var pr in placedList)
        {
            // 각도는 전부 Place에서 확정했다 — yaw는 벽 접선, 기울기는 그 수직으로 눕힌
            // 두 절리 세트다. 여기서 다시 뽑으면 크기 보정(grow)과 어긋나 절벽 밖으로
            // 나가고, 기울기 방위가 yaw와 어긋나 벽과 무관한 방향으로 눕는다.
            var rockDef = rocks[pr.Rock];
            float yaw = pr.Yaw;
            float tiltX = pr.TiltX, tiltZ = pr.TiltZ;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(rockDef.Prefab, parent);
            go.transform.SetPositionAndRotation(
                new Vector3(pr.Center.x, pr.BaseY - rockDef.Bottom * pr.ScaleY, pr.Center.y),
                Quaternion.Euler(tiltX, yaw, tiltZ));
            go.transform.localScale = new Vector3(pr.ScaleX, pr.ScaleY, pr.ScaleZ);

            // 명도 — 낱개 흔들림 + "낮은 것이 어둡다". 높이는 바위 <b>중심</b>으로 잰다:
            // 바닥으로 재면 지면 층이 전부 같은 값이 되고, 꼭대기로 재면 납작한 것이 억울하다.
            if (shade.Count > 0)
            {
                float mid = (pr.BaseY + pr.Top) * 0.5f - world.Origin.y;
                float hFrac = Mathf.Clamp01(mid / Mathf.Max(0.5f, CliffHeightHigh));
                float v = 1f
                    + (Hash(pr.Seed.x, pr.Seed.y, 401) % 1000 / 1000f - 0.5f) * 2f * S.cliffShadeRange
                    - S.cliffShadeDepthDarken * (1f - hFrac);
                int k = Mathf.Clamp(
                    Mathf.FloorToInt((v - shadeLo) / Mathf.Max(1e-4f, shadeHi - shadeLo) * shadeSteps),
                    0, shadeSteps - 1);

                foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = rend.sharedMaterials;
                    bool any = false;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && shade.TryGetValue(mats[i], out var arr))
                        { mats[i] = arr[k]; any = true; }
                    if (any) rend.sharedMaterials = mats;
                }
            }

            // 애추(Layer < 0)에는 콜라이더를 붙이지 않는다 — 통행 가능한 땅으로 삐져나오는
            // 장식이라, 콜라이더를 두면 플레이어가 안 보이는 것에 걸린다.
            if (pr.Layer >= 0) AddConvexCollider(go);
        }

        // ── 뒷벽(커튼) ──
        //
        // 볼록한 덩어리를 쌓는 한 조각 사이의 오목한 틈은 <b>원리적으로</b> 남는다.
        // 밀도·간격·층수를 아무리 조여도 없어지지 않는다(이번 작업에서 여러 번 확인했다).
        //
        // 그래서 틈을 없애는 대신 <b>틈 뒤를 막는다</b>. 윤곽을 따라 불투명한 면을 세우면
        // 같은 틈이 '관통'이 아니라 '그늘'로 읽힌다 — 깎아낸 덩어리에 난 균열처럼 보인다.
        // 팀의 바위 프리팹은 그대로 절벽의 얼굴로 남는다.
        //
        // 높이는 그 자리에 실제로 선 바위의 꼭대기에서 끌어온다. 능선 목표치를 쓰면
        // 바위가 낮은 구간에서 커튼이 위로 삐져나온다.
        int backingTris = BuildCliffBacking(loops, placedList, grid, bucket, parent, groundY);

        // 실제로 다 덮였는지 재서 보고한다 — "구멍 없음"은 눈으로 못 믿을 종류의 주장이라
        // 숫자로 남긴다. 여기가 0이 아니면 벽에 사람이 지나갈 틈이 있다는 뜻이다.
        // 덮임은 <b>벽면 띠</b>에서만 잰다.
        //
        // 예전에는 절벽 영역 전체를 쟀는데, 윤곽 걷기로 바꾼 뒤로는 그게 틀린 질문이 됐다 —
        // 안쪽을 비우는 것이 이 방식의 <b>요점</b>이기 때문이다(보이지도 않는 곳에 예산을
        // 쓰지 않는다). 영역 전체로 재면 잘 되고 있을 때도 30%가 나와 경보만 울린다.
        //
        // 물어야 할 것은 "플레이어가 보는 면에 구멍이 있는가"이고, 그 면은 윤곽에서
        // 한 칸 들어간 띠다. 통행은 이 값과 무관하다 — 절벽 타일은 TileRules 가 이미
        // 통째로 막고 있어서 바위가 없어도 몬스터는 지나가지 못한다.
        // 구멍을 클리어런스로 분류하던 것은 걷어냈다 — 띠 자체를 "클리어런스 4m 이하"로
        // 정의해 놓고 그 안에서 "3m 미만인가"를 물었으니 무엇을 넣어도 80~90%가 나오는
        // 동어반복이었다. 원인은 <b>자리 단위 계측</b>(아래 줄)이 말해 준다: 두께가 모자라
        // 버려진 자리가 몇 개인지가 거기 그대로 찍힌다.
        float faceBand = Cell;
        int cliffPx = 0, holePx = 0, innerPx = 0;
        for (int fy = 0; fy < fh; fy++)
            for (int fx = 0; fx < fw; fx++)
            {
                float c = clear[fx, fy];
                if (c <= 0f) continue;
                if (c > faceBand) { innerPx++; continue; }   // 안쪽 — 일부러 비운다
                cliffPx++;
                if (!Covered(PixelToWorld(fx, fy))) holePx++;
            }

        Debug.Log($"[WorldTerrainGenerator] 절벽 바위 {placedList.Count}개 " +
                  $"(벽 {ground} + 애추 {talus}, 칸 {world.CellSize}m) — " +
                  $"벽면 덮임 {100f * (cliffPx - holePx) / Mathf.Max(1, cliffPx):F1}%" +
                  $" (안쪽 {innerPx} 픽셀은 일부러 비움 · 뒷벽 {backingTris}삼각형)" +
                  System.Environment.NewLine +
                  $"  윤곽 {loops.Count}루프 {contourM:F0}m · 자리 {stTried}회 시도 → " +
                  $"얇아서 {stThin} · 조약돌 {stPebble} · 공간부족 {stNoRoom} · 목표높이도달 {stHigh} " +
                  $"→ 놓임 {stations}" +
                  System.Environment.NewLine +
                  CliffQualityReport(placedList, grid, world.Origin.y));
        // 5%까지는 큰 바위들이 겹치며 남긴 그늘이라 정상이다. 그보다 크게 비면 벽에
        // 사람이 지나갈 틈이 있다는 뜻이므로, 원인을 짚어 알린다.
        if (holePx > cliffPx / 20)
            Debug.LogWarning($"[WorldTerrainGenerator] 벽면의 {100f * holePx / Mathf.Max(1, cliffPx):F1}%에 " +
                             $"바위가 서지 않았습니다({holePx}픽셀) — 시야가 뚫려 뒤가 보일 수 있습니다. " +
                             "(통행은 무관합니다 — 절벽 타일은 길찾기가 이미 막고 있습니다.)" +
                             System.Environment.NewLine +
                             "위의 자리 단위 계측을 보세요. '얇아서'가 크면 벽이 실제로 얇은 것이고, " +
                             "'줄 겹침'이 크면 Cliff Pack Spacing을, '줄 공간부족'이 크면 " +
                             "Cliff Wall Rows나 바위 크기 상한을 낮춰야 합니다.");
        if (placedList.Count > 12000)
            Debug.LogWarning($"[WorldTerrainGenerator] 절벽 바위가 {placedList.Count}개입니다 — " +
                             "드로우콜·씬 용량이 부담됩니다. cliffRadiusRange 최소값을 올리거나 " +
                             "cliffCoverFactor를 올려(구멍 판정을 느슨하게) 줄일 수 있습니다.");
    }

    /// <summary>
    /// 원하는 반지름에 가장 가까운 프리팹을 고른다(목록은 반지름 오름차순).
    /// 가까운 것을 고르는 이유는 균등 배율이 1 근처에 머물러야 노멀·텍스처 밀도가
    /// 유지되기 때문이다 — 1.7m 바위를 6m로 늘리면 표면이 뭉개져 보인다.
    /// 후보 셋 중 해시로 하나 — 같은 크기 자리마다 같은 바위가 오는 것을 깬다.
    /// </summary>
    /// <summary>
    /// 절벽 영역의 경계를 닫힌 폴리라인(월드 XZ)으로 뽑는다 — 마칭스퀘어.
    ///
    /// 픽셀 경계를 그대로 따라가는 무어 추적 대신 마칭스퀘어를 쓰는 이유: 얇은 구조나
    /// 한 픽셀짜리 돌기에서 무한 루프에 빠지지 않고, 구멍이 있는 영역·여러 덩어리를
    /// 따로 다룰 필요 없이 한 번에 처리된다.
    ///
    /// <paramref name="stride"/>로 성기게 본다. 어차피 바위 간격(수 미터)으로 다시
    /// 샘플링하므로 픽셀 단위 계단은 의미가 없고, 성기게 볼수록 계단이 저절로 뭉개진다.
    /// </summary>
    static List<List<Vector2>> CliffContours(float[,] clear, int fw, int fh,
                                             Vector3 fieldOrigin, float px)
    {
        const int Stride = 4;                       // 0.25m 픽셀 기준 1m 격자
        int gw = (fw - 1) / Stride, gh = (fh - 1) / Stride;
        float cell = px * Stride;

        bool In(int gx, int gy)
        {
            int fx = Mathf.Clamp(gx * Stride, 0, fw - 1);
            int fy = Mathf.Clamp(gy * Stride, 0, fh - 1);
            return clear[fx, fy] > 0f;
        }

        // 격자 좌표(반정수) → 월드. 키는 2배해 정수로 만든다
        Vector2 W(float gx, float gy)
            => new Vector2(fieldOrigin.x + gx * cell, fieldOrigin.z + gy * cell);
        long Key(float gx, float gy)
            => ((long)Mathf.RoundToInt(gx * 2f) << 32) ^ (uint)Mathf.RoundToInt(gy * 2f);

        var segA = new List<Vector2>();
        var segB = new List<Vector2>();
        var segKeyA = new List<long>();
        var segKeyB = new List<long>();

        void Emit(float ax, float ay, float bx, float by)
        {
            segA.Add(W(ax, ay)); segB.Add(W(bx, by));
            segKeyA.Add(Key(ax, ay)); segKeyB.Add(Key(bx, by));
        }

        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                int code = (In(gx, gy) ? 1 : 0) | (In(gx + 1, gy) ? 2 : 0)
                         | (In(gx + 1, gy + 1) ? 4 : 0) | (In(gx, gy + 1) ? 8 : 0);
                if (code == 0 || code == 15) continue;

                float L = gx, R = gx + 1, B = gy, T = gy + 1, MX = gx + 0.5f, MY = gy + 0.5f;
                switch (code)
                {
                    case 1: case 14: Emit(L, MY, MX, B); break;
                    case 2: case 13: Emit(MX, B, R, MY); break;
                    case 3: case 12: Emit(L, MY, R, MY); break;
                    case 4: case 11: Emit(R, MY, MX, T); break;
                    case 6: case 9:  Emit(MX, B, MX, T); break;
                    case 7: case 8:  Emit(MX, T, L, MY); break;
                    // 대각으로 마주 본 두 칸 — 어느 쪽으로 이어도 되지만 둘을 따로 끊는다
                    case 5:  Emit(L, MY, MX, B); Emit(R, MY, MX, T); break;
                    case 10: Emit(MX, B, R, MY); Emit(MX, T, L, MY); break;
                }
            }

        // 끝점 키로 이어 붙여 폴리라인을 만든다
        var byKey = new Dictionary<long, List<int>>();
        void Reg(long k, int i)
        {
            if (!byKey.TryGetValue(k, out var l)) byKey[k] = l = new List<int>();
            l.Add(i);
        }
        for (int i = 0; i < segA.Count; i++) { Reg(segKeyA[i], i); Reg(segKeyB[i], i); }

        var used = new bool[segA.Count];
        var loops = new List<List<Vector2>>();

        for (int start = 0; start < segA.Count; start++)
        {
            if (used[start]) continue;
            used[start] = true;

            var line = new List<Vector2> { segA[start], segB[start] };
            long tailKey = segKeyB[start];
            Vector2 tail = segB[start];

            while (true)
            {
                int next = -1;
                if (byKey.TryGetValue(tailKey, out var cands))
                    foreach (int j in cands)
                        if (!used[j]) { next = j; break; }
                if (next < 0) break;

                used[next] = true;
                bool fromA = segKeyA[next] == tailKey;
                tail = fromA ? segB[next] : segA[next];
                tailKey = fromA ? segKeyB[next] : segKeyA[next];
                line.Add(tail);
            }

            if (line.Count >= 3) loops.Add(line);
        }
        return loops;
    }

    /// <summary>
    /// 폴리라인의 국소 접선 — 앞뒤 <paramref name="span"/>개를 아울러 본다.
    /// 마칭스퀘어의 결과를 한 구간만 보고 쓰면 45도 단위로 꺾인 방향이 나온다.
    /// </summary>
    static Vector2 LoopTangent(List<Vector2> loop, int i, int span)
    {
        int n = loop.Count;
        Vector2 a = loop[((i - span) % n + n) % n];
        Vector2 b = loop[(i + span) % n];
        var d = b - a;
        return d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.zero;
    }

    /// <summary>
    /// 윤곽을 따라 <b>불투명한 커튼</b>을 세운다 — 조각 사이 틈으로 뒤가 비치는 것을 막는다.
    ///
    /// 바위 뒤로 조금 물러나 세우고, 높이는 그 자리 바위들의 실제 꼭대기에서 끌어온다.
    /// 메시는 리전 하나당 한 장이라 오브젝트 수에 사실상 영향이 없다.
    /// </summary>
    static int BuildCliffBacking(List<List<Vector2>> loops, List<PlacedRock> placed,
                                 Dictionary<Vector2Int, List<int>> grid, float bucket,
                                 Transform parent, float groundY)
    {
        if (S.cliffBackingInset < 0f) return 0;

        var verts = new List<Vector3>();
        var tris = new List<int>();

        // 이 지점 근처 바위들의 꼭대기 — 커튼이 그보다 낮아야 삐져나오지 않는다
        float TopNear(Vector2 p)
        {
            var b0 = new Vector2Int(Mathf.FloorToInt(p.x / bucket), Mathf.FloorToInt(p.y / bucket));
            float best = groundY;
            for (int by = -1; by <= 1; by++)
                for (int bx = -1; bx <= 1; bx++)
                    if (grid.TryGetValue(new Vector2Int(b0.x + bx, b0.y + by), out var list))
                        foreach (int i in list)
                        {
                            var o = placed[i];
                            if (o.Layer < 0) continue;                 // 애추는 벽이 아니다
                            float reach = o.Radius * 1.6f;
                            if ((o.Center - p).sqrMagnitude > reach * reach) continue;
                            if (o.Top > best) best = o.Top;
                        }
            return best;
        }

        foreach (var loop in loops)
        {
            if (loop.Count < 3) continue;

            // <b>열린 폴리라인을 닫힌 것처럼 이으면 안 된다.</b> CliffContours 는 이어붙이다
            // 끊기면 열린 선을 내놓는데, 마지막 정점을 첫 정점에 연결하면 맵을 가로지르는
            // 거대한 삼각형이 생긴다(실제로 검은 스파이크가 화면을 덮었다).
            bool closed = (loop[0] - loop[loop.Count - 1]).sqrMagnitude < 4f;

            int start = verts.Count;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 a = loop[i];
                Vector2 t = LoopTangent(loop, i, 3);
                Vector2 n = new Vector2(-t.y, t.x);

                // 안쪽은 <b>바위가 선 쪽</b>이다. 루프 무게중심으로 가늠하면 안 된다 —
                // 맵 경계 링의 무게중심은 맵 한가운데, 즉 잔디 쪽이라 정반대로 민다.
                float plus = TopNear(a + n * S.cliffBackingInset);
                float minus = TopNear(a - n * S.cliffBackingInset);
                Vector2 inward = plus >= minus ? n : -n;

                Vector2 q = a + inward * S.cliffBackingInset;
                float top = TopNear(a) - S.cliffBackingDrop;
                if (top < groundY) top = groundY;

                verts.Add(new Vector3(q.x, groundY - 2f, q.y));
                verts.Add(new Vector3(q.x, top, q.y));
            }

            int segs = closed ? loop.Count : loop.Count - 1;
            for (int i = 0; i < segs; i++)
            {
                int i0 = start + i * 2, i1 = start + ((i + 1) % loop.Count) * 2;
                // 양면 — 안팎 어디서 봐도 막힌다(컬링 때문에 한쪽만 뚫려 보이지 않게)
                tris.Add(i0); tris.Add(i0 + 1); tris.Add(i1);
                tris.Add(i1); tris.Add(i0 + 1); tris.Add(i1 + 1);
                tris.Add(i1); tris.Add(i0 + 1); tris.Add(i0);
                tris.Add(i1 + 1); tris.Add(i0 + 1); tris.Add(i1);
            }
        }

        if (tris.Count == 0) return 0;

        var mesh = new Mesh { name = "CliffBacking", indexFormat = verts.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        EnsureFolder();
        string meshPath = $"{S.assetFolder}/CliffBacking.asset";
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (old != null) AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        var go = new GameObject("Cliff Backing");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = CliffBackingMaterial();
        // 콜라이더는 붙이지 않는다 — 바위 콜라이더가 이미 벽을 막고, 절벽 타일은
        // 길찾기가 통째로 막는다. 여기 콜라이더를 두면 틈에 낀 플레이어를 밀어낼 뿐이다.

        return tris.Count / 3;
    }

    /// <summary>커튼용 어두운 머티리얼 — 바위와 같은 셰이더라 배칭이 유지된다.</summary>
    static Material CliffBackingMaterial()
    {
        EnsureFolder();
        string path = $"{S.assetFolder}/CliffBacking.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(shader) { name = "CliffBacking" };
            AssetDatabase.CreateAsset(m, path);
        }
        int id = m.HasProperty(BaseColorId) ? BaseColorId : LegacyColorId;
        var want = new Color(0.16f, 0.16f, 0.17f, 1f);   // 그늘로 읽힐 만큼 어둡게
        if (m.HasProperty(id) && (Vector4)m.GetColor(id) != (Vector4)want)
        {
            m.SetColor(id, want);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(m);
        }
        return m;
    }

    /// <summary>구워낸 명도 변종이 사는 폴더. 지우고 다시 구우면 원본 머티리얼에서 새로 만든다.</summary>
    const string CliffShadeFolder = "Assets/Data/Rendering/CliffShades";

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int LegacyColorId = Shader.PropertyToID("_Color");

    /// <summary>
    /// 명도만 다른 머티리얼 변종을 원본별로 구워 돌려준다 (원본 → 단계 배열).
    ///
    /// <b>MaterialPropertyBlock을 쓰지 않는 이유</b>가 둘이다. 하나는 그것이 런타임 상태라
    /// <b>직렬화되지 않는다</b>는 것 — 에디터가 구운 씬을 다시 열면 색이 통째로 사라진다.
    /// 다른 하나는 프로퍼티 블록이 SRP Batcher 배칭을 깨서, 바위 수천 개에는 오히려 더 비싸다.
    /// 머티리얼을 나누면 둘 다 피한다: 에셋이라 저장되고, 셰이더가 같으니 계속 묶인다.
    ///
    /// 값이 이미 맞으면 에셋을 건드리지 않는다 — 매번 다시 구우면 .mat 파일이 매 생성마다
    /// 변경으로 잡혀 커밋이 지저분해진다. 대신 원본의 <b>색 아닌</b> 속성(텍스처·거칠기)을
    /// 나중에 고쳤다면 이 폴더를 지우고 다시 구워야 반영된다.
    /// </summary>
    static Dictionary<Material, Material[]> BuildShadeVariants(List<CliffRock> rocks)
    {
        var map = new Dictionary<Material, Material[]>();
        int steps = Mathf.Clamp(S.cliffShadeSteps, 1, 16);
        if (steps <= 1 || (S.cliffShadeRange <= 0f && S.cliffShadeDepthDarken <= 0f)) return map;

        var sources = new List<Material>();
        foreach (var rk in rocks)
            foreach (var rend in rk.Prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var m in rend.sharedMaterials)
                    if (m != null && !sources.Contains(m)) sources.Add(m);
        if (sources.Count == 0) return map;

        System.IO.Directory.CreateDirectory(CliffShadeFolder);
        float lo = 1f - S.cliffShadeRange - S.cliffShadeDepthDarken;
        float hi = 1f + S.cliffShadeRange;
        bool wrote = false;

        foreach (var src in sources)
        {
            int id = src.HasProperty(BaseColorId) ? BaseColorId
                   : src.HasProperty(LegacyColorId) ? LegacyColorId : -1;
            if (id < 0) continue;   // 색을 흔들 프로퍼티가 없는 셰이더는 건너뛴다

            var arr = new Material[steps];
            for (int k = 0; k < steps; k++)
            {
                string path = $"{CliffShadeFolder}/{src.name}_Shade{k:00}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader != src.shader)
                {
                    m = new Material(src) { name = $"{src.name}_Shade{k:00}" };
                    if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                        AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(m, path);
                    wrote = true;
                }

                var b = src.GetColor(id);
                float f = Mathf.Lerp(lo, hi, (k + 0.5f) / steps);
                var want = new Color(b.r * f, b.g * f, b.b * f, b.a);
                if ((Vector4)m.GetColor(id) != (Vector4)want)
                {
                    m.CopyPropertiesFromMaterial(src);
                    m.SetColor(id, want);
                    EditorUtility.SetDirty(m);
                    wrote = true;
                }
                arr[k] = m;
            }
            map[src] = arr;
        }

        if (wrote) AssetDatabase.SaveAssets();
        return map;
    }

    /// <summary>
    /// 배치 결과의 <b>품질</b> 지표. 덮임%와 목적이 다르다 — 그쪽은 "뚫렸는가"를 재는
    /// 정합성 지표고(그것도 필요하다), 이쪽은 "자연스러운가"를 잡으려는 값들이다.
    ///
    /// 덮임%만 보면 코드가 자꾸 채우는 방향으로 끌려간다. 사람 눈은 벽 두께를 몇 % 채웠는지
    /// 못 보고, 아래 셋은 바로 본다.
    ///   반지름 사분위 — 25~75% 구간이 좁으면 크기가 균질하다(자연은 멱법칙: 압도적인 몇 개
    ///                   + 중간 + 잔해). 사분위 폭을 중앙값으로 나눈 값을 함께 찍는다.
    ///   이웃 yaw 차이 중앙값 — 방향이 랜덤이면 45도 근처(축 데이터를 0~90도로 접은 기준),
    ///                          절리 세트가 살아 있으면 20도 이하로 떨어진다.
    ///   스카이라인 편차/평균 — 0에 가까우면 높이가 고른 담벼락이다.
    /// </summary>
    static string CliffQualityReport(List<PlacedRock> placed,
                                     Dictionary<Vector2Int, List<int>> grid, float baseY)
    {
        var radii = new List<float>();
        var groundIdx = new List<int>();
        for (int i = 0; i < placed.Count; i++)
            if (placed[i].Layer == 0) { radii.Add(placed[i].Radius); groundIdx.Add(i); }
        if (radii.Count < 4) return "  (품질 지표: 표본이 너무 적다)";

        radii.Sort();
        float q1 = radii[radii.Count / 4], q2 = radii[radii.Count / 2], q3 = radii[radii.Count * 3 / 4];

        // 이웃 간 yaw 차이 — 가장 가까운 다른 지면 바위 하나와만 비교한다.
        // 축 데이터라 0~90도로 접는다(180도 돌린 바위는 같은 방향으로 읽힌다).
        var deltas = new List<float>();
        foreach (int i in groundIdx)
        {
            var a = placed[i];
            float best = float.MaxValue; int bestJ = -1;
            var b0 = new Vector2Int(Mathf.FloorToInt(a.Center.x / CliffBucketProbe),
                                    Mathf.FloorToInt(a.Center.y / CliffBucketProbe));
            foreach (var kv in grid)
            {
                if (Mathf.Abs(kv.Key.x - b0.x) > 1 || Mathf.Abs(kv.Key.y - b0.y) > 1) continue;
                foreach (int j in kv.Value)
                {
                    if (j == i || placed[j].Layer != 0) continue;
                    float d = (placed[j].Center - a.Center).sqrMagnitude;
                    if (d < best) { best = d; bestJ = j; }
                }
            }
            if (bestJ < 0) continue;
            float dy = Mathf.Abs(Mathf.DeltaAngle(a.AxisDeg, placed[bestJ].AxisDeg));
            deltas.Add(dy > 90f ? 180f - dy : dy);
        }
        deltas.Sort();
        float medYaw = deltas.Count > 0 ? deltas[deltas.Count / 2] : -1f;

        // 실제 슬롯 비율 — 이방성이 정말 일어났는지 재는 값. 1에 가까우면 여전히 정사각이다.
        var asp = new List<float>();
        foreach (int i in groundIdx)
            asp.Add(placed[i].HalfAlong / Mathf.Max(0.01f, placed[i].HalfAcross));
        asp.Sort();
        float medAsp = asp.Count > 0 ? asp[asp.Count / 2] : 1f;

        // 스카이라인 — 모든 층의 꼭대기 높이
        double sum = 0, sum2 = 0;
        int n = 0;
        foreach (var pr in placed)
        {
            if (pr.Layer < 0) continue;   // 애추는 벽이 아니다 — 스카이라인에서 뺀다
            double h = pr.Top - baseY; sum += h; sum2 += h * h; n++;
        }
        n = System.Math.Max(1, n);
        double mean = sum / n;
        double sd = System.Math.Sqrt(System.Math.Max(0, sum2 / n - mean * mean));

        // <b>떠 있는 바위</b> — 바닥이 지면보다 높은데 그 아래를 받쳐 주는 것이 없는 것.
        //
        // 이 지표가 없어서 배치가 완전히 무너진 회차를 "스카이라인 편차가 가장 고름"이라고
        // 보고했다. 덮임(바닥 평면)도 스카이라인(평균·편차)도 공중에 뜬 바위를 잡지 못한다.
        // 눈에 제일 먼저 걸리는 것이 계측에 안 잡히면 계측이 판단을 오도한다.
        int floating = 0;
        foreach (var pr in placed)
        {
            if (pr.Layer <= 0 || pr.BaseY < baseY + 0.5f) continue;
            bool supported = false;
            foreach (var o in placed)
            {
                if (o.Layer > pr.Layer) continue;
                float reach = pr.Radius + o.Radius;
                if ((o.Center - pr.Center).sqrMagnitude > reach * reach) continue;
                if (o.Top >= pr.BaseY - 0.3f && o.BaseY <= pr.BaseY) { supported = true; break; }
            }
            if (!supported) floating++;
        }

        return string.Join(System.Environment.NewLine, new[]
        {
            $"  반지름 사분위 {q1:F2} / {q2:F2} / {q3:F2}m " +
            $"(IQR/중앙값 {(q3 - q1) / Mathf.Max(0.01f, q2):F2} — 낮으면 균질)",
            $"  이웃 yaw 차이 중앙값 {medYaw:F1}도 (랜덤이면 45도 · 절리 세트면 20도 이하)",
            $"  슬롯 장/단축 중앙값 {medAsp:F2} (1.0이면 정사각 — 이방성이 안 걸린 것)",
            $"  스카이라인 {mean:F2}m ± {sd:F2} (편차/평균 {sd / System.Math.Max(0.01, mean):F2})",
            $"  떠 있는 바위 {floating}개 (0이어야 한다 — 받침 없이 공중에 앉은 것)",
        });
    }

    /// <summary>품질 계측이 이웃을 찾을 때 쓰는 버킷 한 변 — 배치 때 쓴 값과 같아야 한다.</summary>
    static float CliffBucketProbe = 1f;

    /// <summary>
    /// 요청 비율(<paramref name="aspectWant"/>)에 가장 가까운 후보 셋 중에서 해시로 고른다.
    ///
    /// 슬롯이 이방성이 된 뒤로 이것이 핵심이 됐다. 납작한 프리팹은 납작한 슬롯으로, 길쭉한
    /// 것은 길쭉한 슬롯으로 자연히 가므로 <b>눌러 일그러뜨릴 이유가 사라진다</b>. 그래서
    /// cliffRoundness 를 낮게 둘 수 있고, 그러면 11종의 실루엣이 살아난다.
    ///
    /// 하나만 고르지 않고 셋 중에서 뽑는 이유: 최적 하나만 쓰면 비슷한 슬롯이 이어지는
    /// 구간에서 같은 프리팹이 줄줄이 나와 격자가 드러난다.
    /// </summary>
    static int PickRock(List<CliffRock> rocks, float aspectWant, int hash)
    {
        int n = rocks.Count;
        if (n <= 1) return 0;
        int cand = Mathf.Min(3, n);

        // n 이 열 남짓이라 정렬보다 3칸 삽입이 싸다
        var idx = new int[3];
        var err = new float[3];
        for (int i = 0; i < 3; i++) { idx[i] = -1; err[i] = float.MaxValue; }

        for (int i = 0; i < n; i++)
        {
            // 로그 비율 오차 — 2배 크고 2배 작은 것을 같은 거리로 본다
            float e = Mathf.Abs(Mathf.Log(rocks[i].FootAspect / Mathf.Max(0.01f, aspectWant)));
            for (int k = 0; k < cand; k++)
                if (e < err[k])
                {
                    for (int j = cand - 1; j > k; j--) { err[j] = err[j - 1]; idx[j] = idx[j - 1]; }
                    err[k] = e; idx[k] = i;
                    break;
                }
        }

        int at = idx[hash % cand];
        return at >= 0 ? at : 0;
    }

    /// <summary>
    /// 절벽 영역 각 점에서 <b>비절벽 타일까지의 거리(m)</b>. 절벽 밖은 0.
    /// 이 값이 곧 "여기 놓을 수 있는 바위의 최대 수평 반지름"이다.
    ///
    /// <see cref="SignedDistance"/>를 쓰지 않는 이유: 그쪽은 등고선을 둥글게 펴려고 블러가
    /// 들어가 있어 실제 거리보다 크게 나오는 구간이 생긴다. 침범 금지는 보수적이어야 한다.
    /// 픽셀 반 칸을 빼는 것도 같은 이유 — 경계는 픽셀 중심이 아니라 픽셀 사이에 있다.
    /// </summary>
    static float[,] CliffClearance(MapDataSO map, out float maxClear)
    {
        int sub = FieldSubDiv;
        int w = map.width * sub, h = map.height * sub;
        var d = new float[w, h];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int cx = x / sub, cy = y / sub;
                // 맵 밖은 TileAt이 절벽으로 돌려주므로(경계 검사를 없애려는 규약) 반드시 거른다
                bool cliff = map.InBounds(cx, cy) && map.TileAt(cx, cy) == MapTile.Cliff;
                d[x, y] = cliff ? float.MaxValue : 0f;
            }

        Chamfer(d, w, h);   // 픽셀 단위

        float px = Cell / sub;
        maxClear = 0f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                d[x, y] = Mathf.Max(0f, d[x, y] * px - px * 0.5f);
                if (d[x, y] > maxClear) maxClear = d[x, y];
            }
        return d;
    }

    /// <summary>
    /// 암벽에 콜라이더를 붙인다 — 프리팹에 없어서 여기서 만든다.
    ///
    /// convex인 이유: 오목한 틈이 메워져 플레이어가 바위 crevice에 끼지 않고 총알이 새지
    /// 않는다. 벽으로 쓰는 물건이라 그쪽이 낫다. 면 수가 255를 넘으면 유니티가 알아서
    /// 줄인 헐을 굽는다.
    /// </summary>
    static void AddConvexCollider(GameObject go)
    {
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.GetComponent<Collider>() != null) continue;   // 프리팹에 이미 있으면 존중한다
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = true;
        }
    }

    // ── 경계 · 정적 표시 ────────────────────────────────────────

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

    /// <summary>
    /// 프리팹의 폭(가로·세로 중 큰 쪽)과 <b>바닥이 원점보다 얼마나 아래인지</b>를 잰다.
    ///
    /// 바닥을 따로 재는 이유: 이 암벽들은 중심이 원점에 있어서 그냥 지면에 놓으면
    /// 절반이 땅에 묻힌다. 스케일을 키울수록 더 묻히므로, 바닥을 기준으로 올려줘야
    /// "키운 만큼 높아진다"가 성립한다.
    /// </summary>
    static bool PrefabBounds(GameObject prefab, out Vector2 footprint, out float bottom, out float height)
    {
        footprint = Vector2.zero; bottom = 0f; height = 0f;

        var rends = prefab.GetComponentsInChildren<MeshRenderer>(true);
        if (rends.Length == 0) return false;

        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        footprint = new Vector2(b.size.x, b.size.z);   // 두 축을 따로 — 회전각별 점유 폭 계산용
        bottom = b.min.y;              // 보통 음수 — 원점 아래로 내려간 깊이
        height = b.size.y;
        return footprint.x > 0.01f && footprint.y > 0.01f && height > 0.01f;
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
