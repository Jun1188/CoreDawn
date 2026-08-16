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
    const string AssetFolder = "Assets/Data/Maps/Terrain";

    // ── 지형 프로파일 ───────────────────────────────────────────
    // 지형이 담는 높이 폭(m). 절벽은 프리팹이 맡으므로 지형이 표현할 것은 강 깊이뿐이다 —
    // 여유를 조금 둬야 미세 굴곡이 잘리지 않는다.
    const float TerrainHeightRange = 2f;
    // 강바닥 깊이(m). 물 평면보다 넉넉히 깊어야 한다 — 다듬기가 폭 1칸짜리 물길의 바닥을
    // 들어올리기 때문에, 여유가 없으면 그런 구간에서 물이 끊겨 보인다.
    const float RiverDepth = 0.85f;
    const float WaterLevel = -0.15f;   // 물 표면 높이(m)

    // ── 섬 ──────────────────────────────────────────────────────
    const float ShoreWidth = 1f;    // 맵 가장자리에서 물에 잠기는 폭(칸)
    const float SeaMargin = 1.5f;   // 물을 맵 밖으로 얼마나 더 깔지 (맵 한 변의 배수)

    /// <summary>수면 정점 간격(m). 물 셰이더가 정점을 흔들므로 파도 주기보다 촘촘해야 한다.</summary>
    const float WaterVertexSpacing = 2f;

    /// <summary>이 수심(m)보다 얕으면 거품이 낀다. 정점 컬러의 빨강 채널이 곧 거품 세기다.</summary>
    const float FoamDepth = 0.35f;

    // 형상이 완성되는 거리 — 타일 <b>경계에서 안쪽으로</b> 몇 칸 들어가야 제 높이가 되는가.
    // 짧아야 한다. 경사를 칸 하나에 걸쳐 눕히면 폭 1~2칸짜리 절벽·물길은 제 높이에 닿기도
    // 전에 반대쪽 경계를 만나 밋밋한 둔덕이 된다(0.6칸일 때 절벽 43%가 절반 높이도 못 됐다).
    // 절벽·강 타일은 어차피 건설 불가라 칸 안에서는 마음껏 깎아도 된다. 경계선 자체는 이미
    // 블러로 매끈한 곡선이므로, 짧게 세울수록 그 곡선이 또렷한 벼랑·물길이 된다.
    const float RiverFalloff = 0.16f;   // 크면 완만하다 — 경사를 절반으로 눕히려면 거리를 두 배로

    // 형상을 타일 경계에서 이만큼 <b>안쪽으로</b> 물려 시작한다. 경사면이 남의 땅이 아니라
    // 제 타일을 깎으며 생기게 하는 장치다 — 경계에 딱 맞춰 세우면 다듬는 과정에서 높이가
    // 옆 지면으로 흘러넘쳐, 절벽이 깎이는 게 아니라 땅이 차오르는 모양이 된다.
    // 덤으로 경계선이 칸 격자를 벗어나 거리장의 매끈한 등고선 위에 놓인다.
    const float ShapeInset = 0.15f;

    // ── 해상도 ──────────────────────────────────────────────────
    const int FieldSubDiv = 4;      // 거리장을 칸의 몇 배 격자로 뜨는가 — 계단이 1/4로 잘게 쪼개진다
    const int SamplesPerCell = 4;   // 높이맵 샘플 밀도(칸당). 2ⁿ+1로 올림된다

    // 계단 다듬기 — 타일은 네모라 거리장 등고선이 90°·45°로 꺾인다. 그 각을 뭉갠다.
    // 반경 0.5칸 × 2겹이면 칸 단위 계단은 뭉개지면서, 폭 1~2칸짜리 형상의 속살까지
    // 밀어버리지는 않는다(반경 1칸으로 뭉개면 좁은 물길이 통째로 평탄해진다).
    const int SmoothRadius = FieldSubDiv / 2;
    const int SmoothPasses = 2;             // 박스 블러를 겹쳐 가우시안에 가깝게

    // 높이맵 단계의 2차 다듬기(높이맵 샘플 단위). 거리장을 아무리 뭉개도 "타일 밖은 0" 클램프가
    // 칸 격자에서 일어나 모서리가 되살아나므로, 격자를 떠난 뒤 한 번 더 편다.
    const int HeightSmoothRadius = SamplesPerCell / 2;   // 0.5칸
    const int HeightSmoothPasses = 2;

    // ── 불규칙성 ────────────────────────────────────────────────
    const float WarpStrength = 3.2f;   // 경계를 흔드는 세기(타일). 직각을 깨는 주역
    const float WarpFrequency = 0.055f;
    const float DetailAmplitude = 0.14f;  // 지면 미세 굴곡(m)
    const float DetailFrequency = 0.09f;

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
        float total = TerrainHeightRange;   // 정규화 기준 (0 = 바닥, 1 = 천장)

        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                // 높이맵 픽셀 → 타일 좌표 (Terrain은 [y, x] 순서로 저장한다)
                float tx = (float)i / (res - 1) * map.width;
                float ty = (float)j / (res - 1) * map.height;

                // 도메인 워핑 — 조회 좌표를 흔들어 경계의 직선을 깬다
                float wx = tx + (Mathf.PerlinNoise(tx * WarpFrequency, ty * WarpFrequency) - 0.5f) * 2f * WarpStrength;
                float wy = ty + (Mathf.PerlinNoise(tx * WarpFrequency + 37.7f, ty * WarpFrequency + 12.3f) - 0.5f) * 2f * WarpStrength;

                // 워핑된 좌표와 원래 좌표 중 <b>바깥쪽</b>을 택한다(거리장은 안이 음수).
                // 워핑이 형상을 밖으로 밀어 지면을 파는 일을 막는 장치 —
                // 흔들림은 자기 영역을 깎는 방향으로만 남는다.
                float cliff = Mathf.Max(SampleField(cliffField, map, wx, wy),
                                        SampleField(cliffField, map, tx, ty));
                float river = Mathf.Max(SampleField(riverField, map, wx, wy),
                                        SampleField(riverField, map, tx, ty));

                // 절벽은 지형이 아니라 프리팹이 맡는다(PlaceCliffs) — 높이맵은 평평하게 둔다.
                // 높이맵으로 세운 벽은 결국 늘어난 텍스처라 가까이서 암벽으로 보이지 않는다.
                float h = 0f;

                // 강만 판다. 경계에서 ShapeInset만큼 안쪽에 물가가 있고, 거기서 falloff만큼
                // 더 들어가면 제 깊이 — 발치가 타일 안이라 지면은 평평하게 남는다.
                float dig = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(-ShapeInset, -ShapeInset - RiverFalloff, river)) * RiverDepth;

                // 맵 가장자리도 같은 깊이로 깎아 물에 잠근다 — 바다에 뜬 섬으로 보이게.
                // 타일은 건드리지 않으므로 길찾기·건설 판정은 그대로다(외형만 바뀐다).
                float edge = Mathf.Min(Mathf.Min(tx, ty), Mathf.Min(map.width - tx, map.height - ty));
                dig = Mathf.Max(dig, Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(ShoreWidth, ShoreWidth - RiverFalloff, edge)) * RiverDepth);

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
                float detail = (Mathf.PerlinNoise(tx * DetailFrequency, ty * DetailFrequency) - 0.5f) * 2f * DetailAmplitude;
                float h = height[j, i] + detail * (height[j, i] < -0.05f ? 0.35f : 1f);

                height[j, i] = Mathf.Clamp01((h + RiverDepth) / total);
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

        for (int pass = 0; pass < HeightSmoothPasses; pass++)
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

        for (int pass = 0; pass < SmoothPasses; pass++)
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
        data.size = new Vector3(map.width * world.CellSize, TerrainHeightRange, map.height * world.CellSize);
        data.SetHeights(0, 0, height);
        data.terrainLayers = BuildLayers();

        AssetDatabase.CreateAsset(data, $"{AssetFolder}/{data.name}.asset");

        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.SetParent(root, false);
        // 지면(타일 높이 0)이 월드 y=0에 오도록 강 깊이만큼 내린다
        go.transform.localPosition = new Vector3(0f, -RiverDepth, 0f);

        int ground = LayerMask.NameToLayer("Ground");
        if (ground >= 0) go.layer = ground;

        var terrain = go.GetComponent<Terrain>();
        // URP에서는 전용 지형 셰이더를 명시해야 한다 — null로 두면 내장 파이프라인 머티리얼이라
        // 아무것도 그려지지 않는다(물만 보이는 증상).
        terrain.materialTemplate = TerrainMaterial();
        terrain.heightmapPixelError = 3f;
        terrain.drawInstanced = true;

        // 풀은 멀리서 보이지 않아도 된다 — 가까이서 발밑을 덮는 것이 목적이다
        terrain.detailObjectDistance = DetailDistance;
        terrain.detailObjectDensity = 1f;

        // 스플랫은 반드시 에셋으로 굳힌 뒤에 칠한다. TerrainData를 CreateAsset 하는 순간
        // 알파맵 텍스처가 새로 만들어지면서 그 전에 칠한 내용이 전부 첫 레이어로 초기화된다.
        PaintSplat(data, map, height);
        PaintDetails(data, map, height);
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

    const string PrefabFolder = "Assets/ThirdParty/Idyllic Fantasy Nature/Prefabs";
    const int DetailRes = 512;          // 디테일 격자(맵 전체). 121칸 맵이면 칸당 약 4점
    const int DetailPatch = 32;         // 패치 단위 — Demo와 같은 값
    const float DetailDistance = 120f;  // 이 거리 밖에서는 그리지 않는다

    // ── 절벽 프리팹 배치 ────────────────────────────────────────
    // 칸마다 하나씩 세운다. 크기는 프리팹 실측 폭에서 역산하므로 여기 값은 전부 <b>칸에 대한 비율</b>이다.
    // 칸 대비 폭. 가장자리는 칸을 살짝만 채워 풀밭을 침범하지 않고,
    // 안쪽은 크게 키워 이웃과 겹치게 한다 — 겹쳐야 덩어리가 이어져 벽으로 보인다.
    const float CliffFillEdge = 1.2f;
    const float CliffFillInner = 2.6f;
    const float CliffSink = 0.4f;          // 바닥을 지면에 맞춘 뒤 더 묻는 깊이(m) — 밑동이 떠 보이지 않을 만큼만
    // 절벽의 실제 높이(m). <b>배율이 아니라 목표 높이</b>다 — 프리팹 원본이 3~18m로
    // 제각각이라 배율로 다루면 그 차이가 증폭돼 기둥 숲이 된다.
    // 최저값은 플레이어 점프(1.3m)로 올라설 수 없는 선, 폭은 능선이 단조롭지 않을 만큼만.
    const float CliffHeightLow = 5f;
    const float CliffHeightHigh = 10f;
    const float CliffJitter = 0.18f;       // 칸 안에서 흔드는 폭(칸 대비). 자로 잰 듯한 배치를 깬다

    /// <summary>지면을 덮는 풀.</summary>
    static readonly string[] GrassSet = { "Grass_01", "Grass_02", "Grass_03" };

    /// <summary>드문드문 섞이는 꽃 — 단조로운 초원을 깬다.</summary>
    static readonly string[] FlowerSet =
    {
        "Flower_White", "Flower_Yellow", "Flower_Blue_01", "Flower_Red", "Flower_Purple",
    };

    /// <summary>절벽 타일에 세우는 암벽 프리팹. 지형을 솟구치게 하는 대신 이것들이 벽이 된다.</summary>
    static readonly string[] CliffSet =
    {
        "Cliff_01", "Cliff_02", "Cliff_03", "Cliff_05",
    };

    // 디테일은 지형 텍스처가 아니라 <b>정점 색</b>으로 물드므로 색을 여기서 준다 (Demo와 같은 값)
    static readonly Color HealthyTint = new Color(0.263f, 0.976f, 0.165f);
    static readonly Color DryTint = new Color(0.804f, 0.737f, 0.102f);

    /// <summary>
    /// 풀·꽃·갈대를 심는다. 심는 자리는 <b>타일이 정한다</b> — 지면에만 풀이 자라고,
    /// 갈대는 물가(강에 닿은 지면)에만 선다. 절벽과 강 위에는 아무것도 두지 않는다.
    /// 디테일은 콜라이더가 없어 길찾기·건설·사격 판정에 전혀 관여하지 않는다.
    /// </summary>
    static void PaintDetails(TerrainData data, MapDataSO map, float[,] height)
    {
        // 크기는 프리팹 원본에 곱해지는 배율이다. 1인칭 게임이라 무릎 높이여야 시야가 열린다 —
        // Demo 값(0.5~1)을 그대로 쓰면 눈높이를 덮어 앞이 안 보인다.
        var protos = new List<DetailPrototype>();
        foreach (var n in GrassSet) AddProto(protos, n, 0.56f, 1.0f);
        // 꽃은 잔디보다 조금 커야 보인다 — 작으면 풀숲에 통째로 묻힌다
        int flowerStart = protos.Count;
        foreach (var n in FlowerSet) AddProto(protos, n, 0.7f, 1.2f);
        int grassCount = flowerStart;
        int flowerCount = protos.Count - flowerStart;

        if (protos.Count == 0)
        {
            Debug.LogWarning("[WorldTerrainGenerator] 디테일 프리팹을 하나도 찾지 못해 풀을 심지 않았습니다.");
            return;
        }

        // 해상도·모드를 먼저 잡고 <b>프로토타입은 마지막에</b> 대입한다.
        // SetDetailResolution은 디테일 데이터를 초기화하면서 프로토타입 목록까지 비운다 —
        // 순서를 반대로 하면 심을 것이 하나도 없는 상태가 된다.
        data.SetDetailResolution(DetailRes, DetailPatch);

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

                if (map.TileAt(cx, cy) != MapTile.Ground) continue;

                // 절벽에 닿은 칸은 통째로 비운다. 벽 바로 앞은 풀이 벽면에 파고든 것처럼 보이고,
                // 그 자리는 절벽 프리팹(<see cref="PlaceCliffs"/>)이 채운다.
                if (NextTo(map, cx, cy, MapTile.Cliff)) continue;

                // 지형이 솟은 곳도 제외 — 미세 굴곡을 넘어서면 이미 경사다
                if (SampleHeightAt(height, tx, ty, map) > 0.3f) continue;

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

    static void AddProto(List<DetailPrototype> into, string prefabName, float min, float max)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{prefabName}.prefab");
        if (prefab == null)
        {
            Debug.LogWarning($"[WorldTerrainGenerator] 디테일 프리팹 없음: {prefabName}");
            return;
        }

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
            healthyColor = HealthyTint,
            dryColor = DryTint,
        });
    }

    /// <summary>정규화 높이맵을 타일 좌표에서 읽어 미터로 돌려준다 — 경사면 판정용.</summary>
    static float SampleHeightAt(float[,] height, float tx, float ty, MapDataSO map)
    {
        int res = height.GetLength(0);
        int i = Mathf.Clamp(Mathf.RoundToInt(tx / map.width * (res - 1)), 0, res - 1);
        int j = Mathf.Clamp(Mathf.RoundToInt(ty / map.height * (res - 1)), 0, res - 1);
        return height[j, i] * TerrainHeightRange - RiverDepth;
    }

    /// <summary>
    /// 이웃 8칸에 해당 타일이 있는가. <b>맵 밖은 세지 않는다</b> — TileAt이 맵 밖을 절벽으로
    /// 돌려주는 탓에, 그대로 쓰면 맵 가장자리 지면이 전부 "절벽 옆"으로 판정돼 풀이 빠진다.
    /// </summary>
    static bool NextTo(MapDataSO map, int x, int y, MapTile tile)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!map.InBounds(x + dx, y + dy)) continue;
                if (map.TileAt(x + dx, y + dy) == tile) return true;
            }
        return false;
    }

    // ── 절벽 ────────────────────────────────────────────────────

    /// <summary>
    /// 절벽 타일에 암벽 프리팹을 세운다. <b>지형을 솟구치게 하지 않는 이유</b>는
    /// 높이맵으로 만든 벽이 결국 늘어난 텍스처 덩어리이기 때문이다 — 가까이서 보면
    /// 풀이 벽면에 발려 있고, 실루엣도 매끄러운 언덕에 가깝다.
    /// 프리팹은 제 형태와 노멀을 갖고 있어 그 자리에서 바로 암벽으로 읽힌다.
    ///
    /// 콜라이더는 <b>남긴다</b> — 이제 이것이 플레이어를 막고 총알을 받는 실체다
    /// (예전에는 Terrain 경사가 그 일을 했다). 길찾기는 여전히 타일이 정한다.
    ///
    /// <b>칸마다 하나씩, 칸 크기에 맞춰</b> 세운다. 프리팹 원본이 5~18m로 제각각이라
    /// 고정 배율을 곱하면 어떤 것은 칸을 넘고 어떤 것은 못 채운다 —
    /// 실제 크기를 재서 필요한 배율을 역산해야 격자에 맞는다.
    /// 회전은 90° 단위다. 임의 각도로 돌리면 축정렬 바운드가 커져 다시 칸을 넘는다.
    /// </summary>
    static void PlaceCliffs(MapDataSO map, World world, Transform root)
    {
        // 프리팹과 그 실측 크기를 함께 들고 다닌다
        var prefabs = new List<GameObject>();
        var widths = new List<float>();
        var bottoms = new List<float>();
        var heights = new List<float>();
        foreach (var n in CliffSet)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{n}.prefab");
            if (p == null) continue;
            if (!PrefabBounds(p, out float wide, out float bottom, out float tallness)) continue;

            prefabs.Add(p);
            widths.Add(wide);
            bottoms.Add(bottom);
            heights.Add(tallness);
        }
        if (prefabs.Count == 0)
        {
            Debug.LogWarning("[WorldTerrainGenerator] 절벽 프리팹을 찾지 못해 절벽을 세우지 못했습니다.");
            return;
        }

        var parent = new GameObject("Cliffs").transform;
        parent.SetParent(root, false);

        int placed = 0;
        for (int y = 0; y < map.height; y++)
            for (int x = 0; x < map.width; x++)
            {
                // 맵 밖은 TileAt이 절벽으로 돌려주므로(경계 검사를 없애려는 규약) 반드시 거른다
                if (!map.InBounds(x, y) || map.TileAt(x, y) != MapTile.Cliff) continue;

                int pick = Hash(x, y, 23) % prefabs.Count;
                var cliff = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[pick], parent);

                // 이웃이 절벽인 만큼 크게 키운다.
                // <b>넘치면 안 되는 것은 지면 쪽이지 절벽끼리가 아니다</b> — 안쪽 칸은 사방이
                // 절벽이니 이웃을 덮어도 무방하고, 오히려 겹쳐야 덩어리가 이어져 벽이 된다.
                // 가장자리 칸만 칸 안에 얌전히 들어가면 풀밭을 침범하지 않는다.
                int neighbours = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        if (map.InBounds(x + dx, y + dy) && map.TileAt(x + dx, y + dy) == MapTile.Cliff) neighbours++;
                    }

                float fill = Mathf.Lerp(CliffFillEdge, CliffFillInner, neighbours / 8f);
                float scale = world.CellSize * fill / widths[pick];

                // 가로는 ±15%로 흔든다
                scale *= Mathf.Lerp(0.85f, 1.15f, Hash(x, y, 13) % 1000 / 1000f);

                // 세로는 <b>목표 높이(m)를 정하고 배율을 역산</b>한다.
                // 배율을 곱하는 방식으로는 안 된다 — 프리팹 원본 높이가 3~18m로 6배 차이라
                // 같은 배율을 줘도 결과가 6배 벌어져, 삐죽삐죽한 기둥 숲이 된다.
                // 최저값은 플레이어 점프(1.3m)로 올라설 수 없는 선이다: 통행 차단은 타일이
                // 정하는데 넘어갈 수 있게 생겼으면 규칙과 보이는 것이 어긋난다.
                float wantHeight = Mathf.Lerp(CliffHeightLow, CliffHeightHigh, Hash(x, y, 31) % 1000 / 1000f);
                float tall = wantHeight / heights[pick];

                // 다만 가로와 너무 벌어지면 원래 형태가 뭉개진다 — 늘이고 줄이는 데 한계를 둔다.
                // 그 한계가 최소 높이를 깎는 것은 허용하지 않는다: 폭이 좁은 가장자리 칸이
                // 클램프에 걸려 낮아지면 거기만 넘어갈 수 있는 구멍이 된다.
                tall = Mathf.Clamp(tall, scale * 0.7f, scale * 3.4f);
                tall = Mathf.Max(tall, CliffHeightLow / heights[pick]);

                // 바닥을 지면에 맞춘 뒤 CliffSink만큼만 묻는다.
                // 그냥 원점에 놓으면 중심이 지면에 오므로 절반이 땅에 잠긴다 — 키울수록 더 잠겨서
                // "높이 배율을 올렸는데 여전히 납작한" 상태가 된다.
                Vector3 pos = world.CellToWorldCenter(new Vector2Int(x, y));
                pos.y = world.Origin.y - bottoms[pick] * tall - CliffSink;
                // 칸 안에서만 살짝 흔든다 — 격자에 자로 잰 듯 놓이면 인공물로 보인다
                pos.x += (Hash(x, y, 41) % 1000 / 1000f - 0.5f) * world.CellSize * CliffJitter;
                pos.z += (Hash(x, y, 67) % 1000 / 1000f - 0.5f) * world.CellSize * CliffJitter;

                cliff.transform.SetPositionAndRotation(
                    pos, Quaternion.Euler(0f, 90f * (Hash(x, y, 89) % 4), 0f));
                cliff.transform.localScale = new Vector3(scale, tall, scale);
                placed++;
            }

        Debug.Log($"[WorldTerrainGenerator] 절벽 프리팹 {placed}개 (칸당 1개, 칸 {world.CellSize}m)");
    }

    /// <summary>
    /// 프리팹의 폭(가로·세로 중 큰 쪽)과 <b>바닥이 원점보다 얼마나 아래인지</b>를 잰다.
    ///
    /// 바닥을 따로 재는 이유: 이 암벽들은 중심이 원점에 있어서 그냥 지면에 놓으면
    /// 절반이 땅에 묻힌다. 스케일을 키울수록 더 묻히므로, 바닥을 기준으로 올려줘야
    /// "키운 만큼 높아진다"가 성립한다.
    /// </summary>
    static bool PrefabBounds(GameObject prefab, out float width, out float bottom, out float height)
    {
        width = 0f; bottom = 0f; height = 0f;

        var rends = prefab.GetComponentsInChildren<MeshRenderer>(true);
        if (rends.Length == 0) return false;

        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        width = Mathf.Max(b.size.x, b.size.z);
        bottom = b.min.y;              // 보통 음수 — 원점 아래로 내려간 깊이
        height = b.size.y;
        return width > 0.01f && height > 0.01f;
    }

    /// <summary>URP 지형 머티리얼 — 렌더 파이프라인의 기본값을 쓰되, 없으면 셰이더로 직접 만든다.</summary>
    static Material TerrainMaterial()
    {
        string path = $"{AssetFolder}/Terrain.mat";
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
        const string LayerFolder = "Assets/ThirdParty/Idyllic Fantasy Nature/Terrain Layer";

        var layers = new[]
        {
            Load($"{LayerFolder}/Grass_Layer.terrainlayer"),   // 지면
            Load($"{LayerFolder}/Rock_Layer.terrainlayer"),    // 절벽
            Load($"{LayerFolder}/Sand_Layer.terrainlayer"),    // 강바닥
        };

        foreach (var l in layers)
            if (l == null) return System.Array.Empty<TerrainLayer>();

        return layers;

        static TerrainLayer Load(string path)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
                Debug.LogError($"[WorldTerrainGenerator] 지형 레이어를 찾지 못했습니다: {path}\n" +
                               "Idyllic Fantasy Nature 에셋이 지워졌거나 옮겨졌습니다.");
            return layer;
        }
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
        float total = TerrainHeightRange;

        // 절벽 타일에서 바깥으로 번지는 정도(칸) — 딱 자르면 칸 경계가 그대로 드러난다
        var cliffField = SignedDistance(map, MapTile.Cliff);

        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                // 알파맵 좌표 → 높이맵 좌표
                int hi = Mathf.Clamp(Mathf.RoundToInt((float)i / (res - 1) * (hres - 1)), 0, hres - 1);
                int hj = Mathf.Clamp(Mathf.RoundToInt((float)j / (res - 1) * (hres - 1)), 0, hres - 1);

                float world = height[hj, hi] * total - RiverDepth;   // 월드 높이(m)

                float tx = (float)i / (res - 1) * map.width;
                float ty = (float)j / (res - 1) * map.height;
                float cliff = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.4f, -0.3f, SampleField(cliffField, map, tx, ty)));

                float bed = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, -0.4f, world));
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
        float margin = Mathf.Max(w, h) * SeaMargin;
        float x0 = -margin, z0 = -margin;
        float sizeX = w + margin * 2f, sizeZ = h + margin * 2f;

        // <b>격자로 쪼갠다.</b> 물 셰이더가 정점을 흔들어 파도를 만들기 때문에, 사각형 네 점으로는
        // 흔들 것이 없어 수면이 판판한 판때기로 남는다. 파도 주기가 몇 미터 단위라
        // 정점 간격도 그 정도여야 물결이 산다.
        int cols = Mathf.Clamp(Mathf.CeilToInt(sizeX / WaterVertexSpacing), 1, 512);
        int rows = Mathf.Clamp(Mathf.CeilToInt(sizeZ / WaterVertexSpacing), 1, 512);

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
                float depth = WaterLevel - bed;
                colors[v] = new Color(Mathf.Clamp01(1f - depth / FoamDepth), 0f, 0f, 1f);
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

        string meshPath = $"{AssetFolder}/{mesh.name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        var go = new GameObject("Water (Sea)");
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0f, WaterLevel, 0f);
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
        string path = $"{AssetFolder}/Water.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        const string SourcePath = "Assets/ThirdParty/Bitgem/StylisedWater/URP/Materials/example-water-01.mat";
        var source = AssetDatabase.LoadAssetAtPath<Material>(SourcePath);
        if (source == null)
        {
            Debug.LogError($"[WorldTerrainGenerator] 물 머티리얼 원본을 찾지 못했습니다: {SourcePath}");
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
        if (!AssetDatabase.IsValidFolder(AssetFolder))
            AssetDatabase.CreateFolder("Assets/Data/Maps", "Terrain");
    }
}
