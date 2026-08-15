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
    const float CliffHeight = 3.6f;    // 절벽 정상 높이(m) — 사람 키를 훌쩍 넘겨 시야를 끊는다
    // 강바닥 깊이(m). 물 평면보다 넉넉히 깊어야 한다 — 다듬기가 폭 1칸짜리 물길의 바닥을
    // 들어올리기 때문에, 여유가 없으면 그런 구간에서 물이 끊겨 보인다.
    const float RiverDepth = 0.85f;
    const float WaterLevel = -0.15f;   // 물 표면 높이(m)

    // 형상이 완성되는 거리 — 타일 <b>경계에서 안쪽으로</b> 몇 칸 들어가야 제 높이가 되는가.
    // 짧아야 한다. 경사를 칸 하나에 걸쳐 눕히면 폭 1~2칸짜리 절벽·물길은 제 높이에 닿기도
    // 전에 반대쪽 경계를 만나 밋밋한 둔덕이 된다(0.6칸일 때 절벽 43%가 절반 높이도 못 됐다).
    // 절벽·강 타일은 어차피 건설 불가라 칸 안에서는 마음껏 깎아도 된다. 경계선 자체는 이미
    // 블러로 매끈한 곡선이므로, 짧게 세울수록 그 곡선이 또렷한 벼랑·물길이 된다.
    const float CliffFalloff = 0.12f;
    const float RiverFalloff = 0.08f;

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
    // 타일 경계에서 이만큼 밖으로 나가면 원래 높이를 그대로 지킨다 — 건물 놓을 땅은 평평해야 한다.
    // 인접 칸 중심이 경계에서 0.5칸이므로 그보다 짧아야 옆 칸이 온전히 지켜진다.
    const float GroundGuard = 0.3f;

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
        CreateWater(map, world, root.transform);

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
        float total = CliffHeight + RiverDepth;   // 정규화 기준 (0 = 강바닥, 1 = 절벽 정상)

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

                // 경계(0)에서 지면 높이, 안쪽으로 falloff만큼 들어가면 제 높이.
                // 밖(양수)에서는 아예 0이라 건설 가능한 땅은 평평하게 남는다.
                float h = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, -CliffFalloff, cliff)) * CliffHeight;

                // 강: 안쪽으로 들어갈수록 파이고, 절벽과 겹치면 절벽이 이긴다
                float dig = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, -RiverFalloff, river)) * RiverDepth;
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
    /// 다만 블러는 절벽·강을 옆 지면으로 흘려보내므로, 지면에서는 원래 높이로 되돌린다.
    /// 되돌리는 세기는 <b>타일 경계로부터의 거리</b>로 잰다 — 칸 중심/경계로 재면 그 되돌림이
    /// 칸 주기로 반복돼 물가에 가리비 같은 물결이 새로 생긴다(격자를 지우려다 다시 그리는 꼴).
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

                // 절벽·강 밖으로 얼마나 나왔는가(거리장은 안이 음수) — 안쪽은 자유롭게 뭉갠다
                float outside = Mathf.Min(SampleField(cliffField, map, tx, ty),
                                          SampleField(riverField, map, tx, ty));
                if (outside <= 0f) continue;

                float hold = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, GroundGuard, outside));
                height[j, i] = Mathf.Lerp(height[j, i], original[j, i], hold);
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
    /// 뜬 뒤에는 블러로 그 직각을 뭉개되(<see cref="Smooth"/>), 뭉개진 형상이 타일 <b>밖으로</b>
    /// 번지지는 못하게 잘라낸다 — 지면은 건물을 짓는 땅이라 깎이면 안 되기 때문이다.
    /// 결과적으로 다듬기는 언제나 자기 쪽을 깎는 방향으로만 일어난다.
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

        // 타일 밖은 음수로 내려가지 못한다 = 어떤 다듬기도 남의 땅을 파지 않는다
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (map.TileAt(x / FieldSubDiv, y / FieldSubDiv) != tile)
                    signed[x, y] = Mathf.Max(signed[x, y], 0f);

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
        data.size = new Vector3(map.width * world.CellSize, CliffHeight + RiverDepth, map.height * world.CellSize);
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

        // 스플랫은 반드시 에셋으로 굳힌 뒤에 칠한다. TerrainData를 CreateAsset 하는 순간
        // 알파맵 텍스처가 새로 만들어지면서 그 전에 칠한 내용이 전부 첫 레이어로 초기화된다.
        PaintSplat(data, map, height);
        EditorUtility.SetDirty(data);
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

    /// <summary>지면·절벽·강바닥 3종 레이어. 텍스처가 준비되기 전이라 단색으로 굽는다.</summary>
    static TerrainLayer[] BuildLayers() => new[]
    {
        MakeLayer("Ground", new Color(0.42f, 0.55f, 0.33f)),
        MakeLayer("Cliff",  new Color(0.45f, 0.42f, 0.38f)),
        MakeLayer("Riverbed", new Color(0.38f, 0.36f, 0.28f)),
    };

    static TerrainLayer MakeLayer(string name, Color color)
    {
        string path = $"{AssetFolder}/Layer_{name}.terrainlayer";
        var existing = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (existing != null) return existing;

        var tex = new Texture2D(8, 8);
        var pixels = new Color[64];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();

        string texPath = $"{AssetFolder}/Tex_{name}.png";
        System.IO.File.WriteAllBytes(texPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(texPath);

        var layer = new TerrainLayer
        {
            diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath),
            tileSize = new Vector2(4f, 4f),
        };
        AssetDatabase.CreateAsset(layer, path);
        return layer;
    }

    /// <summary>
    /// 표면 칠하기 — 타일이 아니라 <b>실제 높이·경사</b>로 정한다.
    /// 그래야 워핑으로 흐트러진 경계와 텍스처가 어긋나지 않는다.
    /// </summary>
    static void PaintSplat(TerrainData data, MapDataSO map, float[,] height)
    {
        int res = data.alphamapResolution;
        int hres = height.GetLength(0);
        var alphas = new float[res, res, 3];
        float total = CliffHeight + RiverDepth;

        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                // 알파맵 좌표 → 높이맵 좌표
                int hi = Mathf.Clamp(Mathf.RoundToInt((float)i / (res - 1) * (hres - 1)), 0, hres - 1);
                int hj = Mathf.Clamp(Mathf.RoundToInt((float)j / (res - 1) * (hres - 1)), 0, hres - 1);

                float world = height[hj, hi] * total - RiverDepth;   // 월드 높이(m)

                // 절벽 표면은 "지면보다 확실히 솟은 곳" — 임계값을 높이에 비례시켜
                // CliffHeight를 조절해도 칠이 따라오게 한다
                float cliff = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(CliffHeight * 0.15f, CliffHeight * 0.6f, world));
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
    static void CreateWater(MapDataSO map, World world, Transform root)
    {
        float w = map.width * world.CellSize, h = map.height * world.CellSize;

        var mesh = new Mesh { name = $"{map.Id.Replace(':', '_')}_Water" };
        mesh.vertices = new[]
        {
            new Vector3(0, 0, 0), new Vector3(w, 0, 0),
            new Vector3(0, 0, h), new Vector3(w, 0, h),
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.uv = new[] { Vector2.zero, new Vector2(map.width, 0), new Vector2(0, map.height), new Vector2(map.width, map.height) };
        mesh.RecalculateBounds();

        string meshPath = $"{AssetFolder}/{mesh.name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        var go = new GameObject("Water");
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0f, WaterLevel, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = WaterMaterial();
        // 콜라이더 없음 — 물은 건너다니는 것이고, 바닥은 Terrain이 받는다
    }

    static Material WaterMaterial()
    {
        string path = $"{AssetFolder}/Water.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mat = new Material(shader) { name = "Water" };

        // URP Lit 반투명 설정 — 얕은 물에서 바닥이 비친다
        var color = new Color(0.18f, 0.45f, 0.62f, 0.72f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.9f);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);          // Transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);              // Alpha
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

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
