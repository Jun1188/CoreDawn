using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 맵 데이터(MapDataSO)로 지형 메시를 굽는다 — `Tools > Factory > Build World Terrain`.
///
/// 왜 Unity Terrain이 아니라 메시인가: Terrain은 heightmap 기반이라 절벽 같은 수직면이
/// 계단처럼 뭉개지고, 타일 경계를 정확히 맞추기 어렵다. 타일 격자는 메시가 정직하다.
///
/// 왜 런타임 생성이 아니라 에디터 도구인가: 라이트 베이킹·수동 장식·검수가 가능해야 하고,
/// 맵이 바뀔 때만 다시 구우면 되기 때문이다(매 실행 비용 0).
///
/// <b>절벽을 Obstacle 레이어에 두는 것이 핵심</b> — GridManager가 이미 그 레이어를 물리
/// 장애물로 검사하므로, 타일 판정(TileRules)과 실제 물리가 저절로 일치한다.
/// 플레이어(FPS)도 같은 콜라이더에 막힌다.
/// </summary>
public static class WorldTerrainBuilder
{
    const string TerrainRootName = "Terrain (Generated)";
    const string MeshFolder = "Assets/Data/Maps/Meshes";

    const float RiverDepth = 0.25f;   // 강은 살짝 파여 물이 고인 것처럼
    const float CliffHeight = 2f;     // 절벽 높이 — 시야를 가리되 넘겨다볼 수는 있게

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
        var root = PrepareRoot(world);

        // 타일 종류별로 메시를 나눈다 — 머티리얼·레이어·콜라이더 설정이 각각 다르다
        BuildGround(map, world.CellSize, root);
        BuildRiver(map, world.CellSize, root);
        BuildCliffs(map, world.CellSize, root);

        EditorUtility.SetDirty(world.gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        Debug.Log($"[WorldTerrainBuilder] '{map.Id}' 지형 생성 완료 ({map.width}×{map.height})", world);
    }

    /// <summary>기존 생성물을 지우고 새 루트를 만든다 — 다시 구울 때 겹치지 않게.</summary>
    static Transform PrepareRoot(World world)
    {
        var old = world.transform.Find(TerrainRootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var go = new GameObject(TerrainRootName);
        go.transform.SetParent(world.transform, false);
        go.transform.localPosition = Vector3.zero;
        return go.transform;
    }

    // ── 지면 ────────────────────────────────────────────────────

    /// <summary>
    /// 강이 아닌 칸의 바닥. 맵 전체를 덮는 평면 하나로 만들면 그 아래 파인 강이 가려지므로
    /// 칸 단위로 굽고 강 자리는 비운다 — 물길이 실제로 파여 보인다.
    /// (칸 수만큼 정점이 늘지만 121²는 5만 정점 수준으로 충분히 가볍다. 더 큰 맵이 필요해지면
    ///  같은 높이의 사각 영역을 합치는 그리디 메싱으로 줄이면 된다.)
    /// </summary>
    static void BuildGround(MapDataSO map, float cell, Transform root)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        for (int y = 0; y < map.height; y++)
            for (int x = 0; x < map.width; x++)
            {
                if (map.TileAt(x, y) == MapTile.River) continue;   // 강 자리는 비운다
                AddQuad(verts, tris, x, y, cell, 0f);
            }

        var mesh = BuildMesh($"{map.Id}_Ground", verts, tris);
        var go = CreatePiece("Ground", mesh, root, new Color(0.42f, 0.55f, 0.33f));
        int layer = LayerMask.NameToLayer("Ground");
        if (layer >= 0) go.layer = layer;
    }

    // ── 강 ──────────────────────────────────────────────────────

    /// <summary>
    /// 파인 강바닥과 둑. 걸어서 건널 수 있어야 하므로 콜라이더를 둔다 —
    /// 지면이 이 자리를 비워 두었기 때문에 여기가 곧 바닥이다.
    /// </summary>
    static void BuildRiver(MapDataSO map, float cell, Transform root)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        for (int y = 0; y < map.height; y++)
            for (int x = 0; x < map.width; x++)
            {
                if (map.TileAt(x, y) != MapTile.River) continue;

                AddQuad(verts, tris, x, y, cell, -RiverDepth);   // 강바닥

                // 둑 — 이웃이 강이 아닌 쪽만 (지면 높이에서 강바닥까지 내려가는 벽)
                if (map.TileAt(x - 1, y) != MapTile.River) AddBank(verts, tris, x, y, cell, Vector2Int.left);
                if (map.TileAt(x + 1, y) != MapTile.River) AddBank(verts, tris, x, y, cell, Vector2Int.right);
                if (map.TileAt(x, y - 1) != MapTile.River) AddBank(verts, tris, x, y, cell, Vector2Int.down);
                if (map.TileAt(x, y + 1) != MapTile.River) AddBank(verts, tris, x, y, cell, Vector2Int.up);
            }

        if (verts.Count == 0) return;

        var mesh = BuildMesh($"{map.Id}_River", verts, tris);
        var go = CreatePiece("River", mesh, root, new Color(0.25f, 0.5f, 0.75f, 1f));
        int layer = LayerMask.NameToLayer("Ground");
        if (layer >= 0) go.layer = layer;
    }

    /// <summary>강둑 — 지면(0)에서 강바닥까지 내려가는 벽. 안쪽을 향한다.</summary>
    static void AddBank(List<Vector3> verts, List<int> tris, int x, int y, float cell, Vector2Int dir)
    {
        float x0 = x * cell, x1 = (x + 1) * cell;
        float z0 = y * cell, z1 = (y + 1) * cell;

        Vector3 a, b;
        if (dir == Vector2Int.left)       { a = new Vector3(x0, 0, z0); b = new Vector3(x0, 0, z1); }
        else if (dir == Vector2Int.right) { a = new Vector3(x1, 0, z1); b = new Vector3(x1, 0, z0); }
        else if (dir == Vector2Int.down)  { a = new Vector3(x1, 0, z0); b = new Vector3(x0, 0, z0); }
        else                              { a = new Vector3(x0, 0, z1); b = new Vector3(x1, 0, z1); }

        int i = verts.Count;
        verts.Add(a);
        verts.Add(b);
        verts.Add(a + Vector3.down * RiverDepth);
        verts.Add(b + Vector3.down * RiverDepth);

        tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
        tris.Add(i + 2); tris.Add(i + 3); tris.Add(i + 1);
    }

    // ── 절벽 ────────────────────────────────────────────────────

    /// <summary>
    /// 절벽 칸을 박스로 세운다. 옆면은 <b>이웃이 절벽이 아닐 때만</b> 만든다 —
    /// 절벽 덩어리 내부의 보이지 않는 면을 빼면 정점이 크게 줄어든다.
    /// </summary>
    static void BuildCliffs(MapDataSO map, float cell, Transform root)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        for (int y = 0; y < map.height; y++)
            for (int x = 0; x < map.width; x++)
            {
                if (map.TileAt(x, y) != MapTile.Cliff) continue;

                AddQuad(verts, tris, x, y, cell, CliffHeight);   // 윗면

                // 옆면 — 노출된 방향만
                if (map.TileAt(x - 1, y) != MapTile.Cliff) AddSide(verts, tris, x, y, cell, Vector2Int.left);
                if (map.TileAt(x + 1, y) != MapTile.Cliff) AddSide(verts, tris, x, y, cell, Vector2Int.right);
                if (map.TileAt(x, y - 1) != MapTile.Cliff) AddSide(verts, tris, x, y, cell, Vector2Int.down);
                if (map.TileAt(x, y + 1) != MapTile.Cliff) AddSide(verts, tris, x, y, cell, Vector2Int.up);
            }

        if (verts.Count == 0) return;

        var mesh = BuildMesh($"{map.Id}_Cliff", verts, tris);
        var go = CreatePiece("Cliff", mesh, root, new Color(0.45f, 0.42f, 0.38f));

        // Obstacle 레이어 — GridManager가 이 레이어를 물리 장애물로 굽는다.
        // 타일 판정(절벽=통행 불가)과 실제 물리가 여기서 일치한다.
        int layer = LayerMask.NameToLayer("Obstacle");
        if (layer >= 0) go.layer = layer;
        else Debug.LogWarning("[WorldTerrainBuilder] 'Obstacle' 레이어가 없어 절벽이 물리 장애물로 잡히지 않습니다.");
    }

    // ── 메시 조립 헬퍼 ──────────────────────────────────────────

    /// <summary>칸 (x,y)의 수평면 하나 — 위에서 내려다보는 면.</summary>
    static void AddQuad(List<Vector3> verts, List<int> tris, int x, int y, float cell, float height)
    {
        int b = verts.Count;
        float x0 = x * cell, x1 = (x + 1) * cell;
        float z0 = y * cell, z1 = (y + 1) * cell;

        verts.Add(new Vector3(x0, height, z0));
        verts.Add(new Vector3(x1, height, z0));
        verts.Add(new Vector3(x0, height, z1));
        verts.Add(new Vector3(x1, height, z1));

        tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
        tris.Add(b + 2); tris.Add(b + 3); tris.Add(b + 1);
    }

    /// <summary>절벽 옆면 — 지면(0)에서 꼭대기까지 세우는 벽.</summary>
    static void AddSide(List<Vector3> verts, List<int> tris, int x, int y, float cell, Vector2Int dir)
    {
        float x0 = x * cell, x1 = (x + 1) * cell;
        float z0 = y * cell, z1 = (y + 1) * cell;

        Vector3 a, bb;
        if (dir == Vector2Int.left)       { a = new Vector3(x0, 0, z1); bb = new Vector3(x0, 0, z0); }
        else if (dir == Vector2Int.right) { a = new Vector3(x1, 0, z0); bb = new Vector3(x1, 0, z1); }
        else if (dir == Vector2Int.down)  { a = new Vector3(x0, 0, z0); bb = new Vector3(x1, 0, z0); }
        else                              { a = new Vector3(x1, 0, z1); bb = new Vector3(x0, 0, z1); }

        int b = verts.Count;
        verts.Add(a);
        verts.Add(bb);
        verts.Add(a + Vector3.up * CliffHeight);
        verts.Add(bb + Vector3.up * CliffHeight);

        tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
        tris.Add(b + 2); tris.Add(b + 3); tris.Add(b + 1);
    }

    static Mesh BuildMesh(string name, List<Vector3> verts, List<int> tris)
    {
        var mesh = new Mesh { name = name };
        // 큰 맵은 정점이 65535를 넘는다 (121² 절벽이면 30만 이상)
        if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>메시 조각 하나를 씬에 놓는다 — 메시는 에셋으로 저장해 씬 파일이 부풀지 않게 한다.</summary>
    static GameObject CreatePiece(string name, Mesh mesh, Transform root, Color color, bool collider = true)
    {
        SaveMesh(mesh);

        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial(name, color);
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    static void SaveMesh(Mesh mesh)
    {
        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder("Assets/Data/Maps", "Meshes");

        // id의 ':'(Map:New1)는 파일명에 쓸 수 없다
        string path = $"{MeshFolder}/{mesh.name.Replace(':', '_')}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
    }

    /// <summary>단색 머티리얼 — 임시 룩. 아트가 준비되면 이 에셋만 교체하면 된다.</summary>
    static Material GetMaterial(string name, Color color)
    {
        string path = $"{MeshFolder}/Terrain_{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mat = new Material(shader) { name = $"Terrain_{name}" };

        // URP Lit의 색은 _BaseColor다 — Material.color(_Color)만 넣으면 흰색으로 남는다
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", name == "River" ? 0.75f : 0.1f);

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
