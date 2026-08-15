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
    const float RiverDepth = 0.55f;    // 강바닥 깊이(m) — 물 평면보다 깊어야 물이 보인다
    const float WaterLevel = -0.15f;   // 물 표면 높이(m)

    // 잦아드는 거리(타일). 이 값이 곧 경사다 — 높이/거리가 기울기다.
    // 절벽은 1.0이면 약 74°로, 걸어 오를 수 없는 벼랑이 된다(2.2면 47°라 언덕처럼 보였다).
    const float CliffFalloff = 1.0f;
    // 강둑은 1.2 — 더 넓히면 폭 1~2칸짜리 물길이 바닥까지 파이지 못해 물 표면과 지형이
    // 어중간하게 만나고, 그 교선이 칸 모양 톱니로 드러난다.
    const float RiverFalloff = 1.2f;

    // ── 불규칙성 ────────────────────────────────────────────────
    const float WarpStrength = 2.4f;   // 경계를 흔드는 세기(타일). 직각을 깨는 주역
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

                float cliff = SampleField(cliffField, map, wx, wy);
                float river = SampleField(riverField, map, wx, wy);

                // 절벽: 안쪽으로 들어갈수록 솟고, 밖으로 falloff만큼 가면 지면
                float h = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CliffFalloff, -CliffFalloff, cliff)) * CliffHeight;

                // 강: 안쪽으로 들어갈수록 파이고, 절벽과 겹치면 절벽이 이긴다
                float dig = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RiverFalloff, -RiverFalloff, river)) * RiverDepth;
                h -= dig;

                // 미세 굴곡 — 평평한 지면을 깬다. 물 밑은 잔잔하게(파임을 흐리지 않도록 약하게)
                float detail = (Mathf.PerlinNoise(tx * DetailFrequency, ty * DetailFrequency) - 0.5f) * 2f * DetailAmplitude;
                h += detail * Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(-1f, 1f, river));

                height[j, i] = Mathf.Clamp01((h + RiverDepth) / total);
            }

        return height;
    }

    static int HeightmapResolution(MapDataSO map)
    {
        // Terrain 높이맵은 2ⁿ+1이어야 한다. 맵보다 촘촘하게 잡아 곡선이 뭉개지지 않게.
        int target = Mathf.Max(map.width, map.height) * 2;
        int res = 33;
        while (res - 1 < target && res < 2049) res = (res - 1) * 2 + 1;
        return res;
    }

    // ── 거리장 ──────────────────────────────────────────────────

    /// <summary>
    /// 부호 있는 거리장(타일 단위) — 해당 타일 안이면 음수, 밖이면 양수.
    /// 두 번 훑는 체임퍼 변환이라 맵 크기에 선형이다(브루트포스 O(n²)를 피한다).
    /// </summary>
    static float[,] SignedDistance(MapDataSO map, MapTile tile)
    {
        int w = map.width, h = map.height;
        var inside = new float[w, h];
        var outside = new float[w, h];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool isTile = map.TileAt(x, y) == tile;
                outside[x, y] = isTile ? 0f : float.MaxValue;   // 타일까지의 거리
                inside[x, y] = isTile ? float.MaxValue : 0f;    // 타일 밖까지의 거리
            }

        Chamfer(outside, w, h);
        Chamfer(inside, w, h);

        var signed = new float[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                signed[x, y] = outside[x, y] - inside[x, y];   // 안이면 음수
        return signed;
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

    /// <summary>거리장을 실수 좌표에서 이중선형 보간으로 읽는다 — 워핑된 좌표를 부드럽게 조회하기 위해.</summary>
    static float SampleField(float[,] field, MapDataSO map, float x, float y)
    {
        x = Mathf.Clamp(x - 0.5f, 0f, map.width - 1.001f);
        y = Mathf.Clamp(y - 0.5f, 0f, map.height - 1.001f);

        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, map.width - 1), y1 = Mathf.Min(y0 + 1, map.height - 1);
        float fx = x - x0, fy = y - y0;

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
            alphamapResolution = Mathf.Min(1024, Mathf.NextPowerOfTwo(Mathf.Max(map.width, map.height) * 4)),
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
