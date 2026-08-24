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
        /// 얼마나 이미 둥근가(0에 가까울수록 둥글다). 세 축의 로그 비율 차이다 —
        /// 21×16×8.9처럼 납작한 프리팹은 정육면체로 눌렀을 때 왜곡이 크므로 덜 쓴다.
        /// </summary>
        public readonly float Distortion;
        /// <summary>정렬·선택용 대표 크기 — 짧은 축 반폭.</summary>
        public float Radius => 0.5f * Mathf.Min(Foot.x, Foot.y);
        public readonly float Height;   // m
        /// <summary>원점 대비 바닥(m). 바닥 피벗이면 0, 중심 피벗이면 음수.</summary>
        public readonly float Bottom;
        public CliffRock(GameObject p, Vector2 foot, float h, float b)
        {
            Prefab = p; Foot = foot; Height = h; Bottom = b;
            float m = Mathf.Max(0.01f, (foot.x + foot.y + h) / 3f);
            Distortion = Mathf.Abs(Mathf.Log(foot.x / m)) + Mathf.Abs(Mathf.Log(foot.y / m))
                       + Mathf.Abs(Mathf.Log(h / m));
        }
    }

    /// <summary>배치가 확정된 바위 하나 — 겹침 검사와 쌓기의 단위.</summary>
    struct PlacedRock
    {
        public int Rock;        // CliffRock 목록의 인덱스
        public Vector2 Center;  // 월드 XZ
        public float Radius;    // 실제 수평 반지름(m)
        public float ScaleX, ScaleZ, ScaleY;   // 축별 배율 — 정사각형에 맞추느라 갈린다
        public float TiltX, TiltZ;             // 기울기(도) — 크기를 정할 때 이미 반영했다
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
        // 왜곡이 적은 순 — PickRock이 앞쪽 70%에서만 고른다. 축별로 눌러 크기를 맞추는
        // 방식이라 "크기가 비슷한 프리팹"을 찾을 필요가 없고, 대신 <b>정육면체로 눌렀을 때
        // 덜 일그러지는 것</b>이 중요해진다.
        rocks.Sort((a, b) => a.Distortion.CompareTo(b.Distortion));

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

        void Place(Vector2Int seed, Vector2 p, float r, float baseY, int layer)
        {
            int pick = PickRock(rocks, r, Hash(seed.x, seed.y, 23 + layer * 7));
            var rock = rocks[pick];

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
            float tiltX = (Hash(seed.x, seed.y, 79) % 1000 / 1000f - 0.5f) * S.cliffTilt;
            float tiltZ = (Hash(seed.x, seed.y, 83) % 1000 / 1000f - 0.5f) * S.cliffTilt;
            float tilt = Mathf.Sqrt(tiltX * tiltX + tiltZ * tiltZ) * Mathf.Deg2Rad;

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

            float D = 2f * r / Mathf.Max(0.5f, grow);
            float sx = D / rock.Foot.x;
            float sz = D / rock.Foot.y;
            float u = Mathf.Min(sx, sz);      // 원본 비율을 지킬 때 D 안에 들어가는 균등 배율
            float round = Mathf.Clamp01(S.cliffRoundness);

            float scaleX = Mathf.Min(Mathf.Lerp(u, sx, round), S.cliffMaxScale);
            float scaleZ = Mathf.Min(Mathf.Lerp(u, sz, round), S.cliffMaxScale);
            float scaleY = Mathf.Min(Mathf.Lerp(u, D / rock.Height, round), S.cliffMaxScale) * stretch;

            // 실제로 차지하는 내접 반지름(기울임 포함) — 겹침·쌓기 계산은 이 값으로 한다
            float radius = 0.5f * Mathf.Min(rock.Foot.x * scaleX, rock.Foot.y * scaleZ) * grow;

            // 세장비 안전 상한. 바위 하나에 최소 높이를 보장하던 코드는 걷어냈다 —
            // 수평이 클리어런스에 묶인 상태에서 높이를 요구하면 늘어나는 건 세로뿐이라
            // 벽이 통째로 길쭉해진다(실측: 폭 2.7m에 높이 8.2m). 높이는 쌓기가 만든다.
            float maxY = radius * S.cliffMaxAspect / rock.Height;
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
                ScaleX = scaleX,
                ScaleZ = scaleZ,
                ScaleY = scaleY,
                TiltX = tiltX,
                TiltZ = tiltZ,
                BaseY = baseY,
                Top = baseY + rock.Height * scaleY,
                Layer = layer,
                Seed = seed,
            });

            var b0 = BucketOf(p);
            if (!grid.TryGetValue(b0, out var bl)) grid[b0] = bl = new List<int>();
            bl.Add(placedList.Count - 1);
        }

        // ── 지면 층 ──
        // 클리어런스가 큰 곳부터 — 두꺼운 곳에 큰 바위가 먼저 자리를 잡는다.
        // 픽셀이 수십만이라 전체 정렬은 비싸다. 버킷 정렬로 대신한다(순서만 필요하다).
        const int Bins = 48;
        var bins = new List<int>[Bins];
        for (int i = 0; i < Bins; i++) bins[i] = new List<int>();
        for (int fy = 0; fy < fh; fy++)
            for (int fx = 0; fx < fw; fx++)
            {
                float c = clear[fx, fy];
                if (c < minR) continue;
                bins[Mathf.Clamp(Mathf.FloorToInt(c / maxClear * (Bins - 1)), 0, Bins - 1)].Add(fy * fw + fx);
            }

        for (int bin = Bins - 1; bin >= 0; bin--)
            foreach (int flat in bins[bin])
            {
                int fx = flat % fw, fy = flat / fw;
                float c = clear[fx, fy];
                if (c < minR) continue;
                Vector2 p = PixelToWorld(fx, fy);
                float r = Mathf.Min(c, maxR) * SizeNoise(fx, fy);
                if (r < minR) continue;
                if (TooClose(p, r, S.cliffPackSpacing)) continue;
                Place(new Vector2Int(fx, fy), p, r, groundY, 0);
            }

        // ── 구멍 메움은 두지 않는다 ──
        // 예전에는 덮이지 않은 픽셀마다 작은 바위를 박아 넣었다. 그 결과가 <b>큰 바위 사이에
        // 낀 조약돌</b>이다 — 반지름 0.7m짜리가 틈마다 박혀 절벽이 자갈 무더기로 보였다.
        // 큰 바위끼리 깊이 겹쳐 놓은 사이의 틈은 원래 절벽에도 있는 그늘이지 메울 구멍이
        // 아니다. 벽이 얇아 아무것도 못 서는 자리는 그냥 비워 둔다.
        //
        // 대신 아래에서 <b>덮임률을 재서 로그로 남긴다</b> — 비었다는 사실을 숨기지 않되,
        // 메우는 판단은 사람이 한다.
        int filled = 0;

        // ── 쌓기 ──
        // 능선고까지 위층을 얹는다. 아래 바위의 꼭대기보다 내려 잡아 이음매를 파묻고,
        // 위로 갈수록 작아지며 안쪽으로 물러난다 — 버섯처럼 걸쳐지지 않게 하는 장치다.
        int ground = placedList.Count;
        int stacked = 0;
        // 작은 바위 위에 또 얹으면 자갈탑이 된다 — 층을 이고 설 만한 것에만 쌓는다
        float stackMin = minR * S.cliffStackMinRadius;
        for (int i = 0; i < ground; i++)
        {
            var below = placedList[i];
            if (below.Radius < stackMin) continue;
            var seed = below.Seed;

            // 능선 노이즈가 이 자리의 목표 높이를 정한다 — 이웃끼리 높이가 이어져
            // 몇 칸에 걸쳐 완만하게 솟았다 가라앉는다.
            float ridge = Mathf.PerlinNoise(below.Center.x * S.cliffRidgeFrequency / Cell + 5.3f,
                                            below.Center.y * S.cliffRidgeFrequency / Cell + 9.1f);
            float wantTop = world.Origin.y + Mathf.Lerp(CliffHeightLow, CliffHeightHigh, ridge);

            float curTop = below.Top, curR = below.Radius;
            Vector2 curC = below.Center;

            for (int layer = 1; layer < S.cliffStackLayers && curTop < wantTop; layer++)
            {
                float r2 = curR * Mathf.Lerp(S.cliffStackShrink.x, S.cliffStackShrink.y,
                                             Hash(seed.x, seed.y, 211 + layer) % 1000 / 1000f);
                if (r2 < minR) break;

                // <b>옆으로도 민다.</b> 정확히 위로만 얹으면 탑이 되는데, 실제 너덜은 옆으로
                // 기대고 흘러내린다. 방향은 층마다 도는 흐름 노이즈 + 해시 지터.
                float ang = (Mathf.PerlinNoise(seed.x * 0.05f + 12.3f, seed.y * 0.05f + 71.9f)
                           + layer * 0.37f) * Mathf.PI * 2f
                          + (Hash(seed.x, seed.y, 193 + layer) % 1000 / 1000f - 0.5f) * 1.4f;
                float off = (curR + r2) * S.cliffStackOffset
                          * Mathf.Lerp(0.4f, 1f, Hash(seed.x, seed.y, 197 + layer) % 1000 / 1000f);
                Vector2 c2 = curC + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * off;

                // 옮긴 자리에서도 침범 금지는 그대로 — 클리어런스가 모자라면 거기 맞춰 줄인다
                float room = ClearAt(c2);
                if (room < minR) break;
                r2 = Mathf.Min(r2, room);

                // 옆으로 민 만큼 <b>접점이 내려온다</b> — 아래 바위는 둥글어서 중심에서 멀수록
                // 어깨가 낮다. 이 한 줄이 층 높이를 들쭉날쭉하게 만들어 계단식 탑을 깬다.
                float lean = Mathf.Clamp01(off / Mathf.Max(0.01f, curR + r2));
                float rest = curTop - (curTop - below.BaseY) * S.cliffStackSink;
                float baseY2 = Mathf.Lerp(rest, below.BaseY + (curTop - below.BaseY) * 0.3f, lean);
                Place(new Vector2Int(seed.x, seed.y + layer * 977), c2, r2, baseY2, layer);
                stacked++;

                var top = placedList[placedList.Count - 1];
                curTop = top.Top; curR = top.Radius; curC = top.Center;
            }
        }

        // ── 인스턴스화 ──
        var parent = new GameObject("Cliffs").transform;
        parent.SetParent(root, false);

        foreach (var pr in placedList)
        {
            // yaw는 완전 자유다 — 발자국을 정사각으로 눌러 놔서 각도가 내접원을 안 바꾼다.
            // 기울기(tilt)는 Place에서 이미 정했고 그만큼 크기를 줄여 뒀다 — 여기서 다시
            // 뽑으면 그 보정과 어긋나 절벽 밖으로 나간다.
            // 흐름 필드로 이웃끼리 비슷한 방향을 향하게 하고 낱개 지터를 얹는다: 칸별 독립
            // 각도는 벽면을 모자이크로 만들고, 고정 각도는 격자를 드러낸다.
            var rockDef = rocks[pr.Rock];
            float flow = Mathf.PerlinNoise(pr.Seed.x * 0.02f + 12.3f, pr.Seed.y * 0.02f + 71.9f);
            float yaw = flow * 720f + (Hash(pr.Seed.x, pr.Seed.y, 89) % 1000 / 1000f - 0.5f) * 90f;
            float tiltX = pr.TiltX, tiltZ = pr.TiltZ;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(rockDef.Prefab, parent);
            go.transform.SetPositionAndRotation(
                new Vector3(pr.Center.x, pr.BaseY - rockDef.Bottom * pr.ScaleY, pr.Center.y),
                Quaternion.Euler(tiltX, yaw, tiltZ));
            go.transform.localScale = new Vector3(pr.ScaleX, pr.ScaleY, pr.ScaleZ);

            AddConvexCollider(go);
        }

        // 실제로 다 덮였는지 재서 보고한다 — "구멍 없음"은 눈으로 못 믿을 종류의 주장이라
        // 숫자로 남긴다. 여기가 0이 아니면 벽에 사람이 지나갈 틈이 있다는 뜻이다.
        int cliffPx = 0, holePx = 0;
        for (int fy = 0; fy < fh; fy++)
            for (int fx = 0; fx < fw; fx++)
            {
                if (clear[fx, fy] <= 0f) continue;
                cliffPx++;
                if (!Covered(PixelToWorld(fx, fy))) holePx++;
            }

        Debug.Log($"[WorldTerrainGenerator] 절벽 바위 {placedList.Count}개 " +
                  $"(지면 {ground - filled} + 구멍 메움 {filled} + 쌓기 {stacked}, 칸 {world.CellSize}m) — " +
                  $"덮임 {100f * (cliffPx - holePx) / Mathf.Max(1, cliffPx):F1}%");
        // 5%까지는 큰 바위들이 겹치며 남긴 그늘이라 정상이다. 그보다 크게 비면 벽에
        // 사람이 지나갈 틈이 있다는 뜻이므로, 원인을 짚어 알린다.
        if (holePx > cliffPx / 20)
            Debug.LogWarning($"[WorldTerrainGenerator] 절벽의 {100f * holePx / Mathf.Max(1, cliffPx):F1}%에 " +
                             $"바위가 서지 않았습니다({holePx}픽셀) — 벽이 뚫렸을 수 있습니다.\n" +
                             $"Cliff Min Radius({S.cliffMinRadius}m)보다 얇은 구간에는 아무것도 놓지 않습니다. " +
                             "그 값을 낮추면 채워지지만 그만큼 작은 바위가 나오고, 크고 안 뚫리는 벽을 " +
                             "원하면 맵의 절벽 타일 폭을 넓혀야 합니다(그래야 클리어런스가 커집니다).");
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
    static int PickRock(List<CliffRock> rocks, float radius, int hash)
    {
        // 목록은 <b>왜곡이 적은 순</b>이다. 어차피 축별로 눌러 목표 크기에 맞추므로
        // "크기가 비슷한 것"을 고를 이유가 없다 — 대신 정육면체로 눌렀을 때 덜 일그러지는
        // 것을 자주 쓴다. 납작한 프리팹(21×16×8.9)을 정육면체로 만들면 2.4배 늘어난다.
        int pool = Mathf.Max(1, Mathf.CeilToInt(rocks.Count * 0.7f));
        return hash % pool;
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
