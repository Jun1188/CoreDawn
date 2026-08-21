using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 근접무기 뷰모델 빌더 — 타워/몬스터 리그 빌더와 같은 자리, 같은 성격(언제든 다시 돌릴 수 있는 생성기).
///
/// 칼날은 큐브를 늘린 게 아니라 <b>코드로 로프트한 메시</b>다: 밑동이 넓고 끝으로 갈수록 얇아지는
/// 양날 단면(다이아몬드)이라 실루엣이 칼로 읽힌다. 손잡이·가드·이미터는 큐브로 충분하다.
///
/// <b>제자리 갱신</b>이 핵심이다. 프리팹을 지우고 새로 만들면 자식 오브젝트의 fileID가 갈려서
/// Player.prefab의 <c>Gun.muzzlePoint/sightPoint</c> 참조가 조용히 끊긴다
/// (GeneratedAnimationAssets가 애니메이션 쪽에서 같은 이유로 존재한다).
/// 그래서 이름으로 찾아 있으면 고치고 없을 때만 만든다.
/// </summary>
public static class MeleeWeaponBuilder
{
    const string PrefabPath = "Assets/Prefabs/Weapon/PlasmaCutter_Model.prefab";
    const string MeshPath   = "Assets/Art/Models/Generated/PlasmaCutterBlade.asset";
    const string GripMat    = "Assets/Materials/Weapon/PlasmaCutterGrip.mat";
    const string EdgeMat    = "Assets/Materials/Weapon/PlasmaCutterEdge.mat";

    const float BladeStart  = 0.19f;   // 이미터 끝 = 칼날이 시작되는 z
    const float BladeLength = 0.56f;

    [MenuItem("Tools/Weapons/Rebuild Plasma Cutter Model")]
    public static void Rebuild()
    {
        var mesh = SaveMesh(BuildBladeMesh(), MeshPath);
        var grip = AssetDatabase.LoadAssetAtPath<Material>(GripMat);
        var edge = AssetDatabase.LoadAssetAtPath<Material>(EdgeMat);
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);

        // 손잡이 — 뒤로 짧게, 끝에 폼멜을 달아 손에 쥔 물건처럼 보이게
        Box(root, "Grip",    new Vector3(0f, 0f, 0.02f),   new Vector3(0.042f, 0.052f, 0.20f), cube, grip);
        Box(root, "Pommel",  new Vector3(0f, 0f, -0.10f),  new Vector3(0.052f, 0.062f, 0.03f), cube, grip);
        Box(root, "Guard",   new Vector3(0f, 0f, 0.125f),  new Vector3(0.115f, 0.042f, 0.04f), cube, grip);
        Box(root, "Emitter", new Vector3(0f, 0f, 0.165f),  new Vector3(0.05f, 0.068f, 0.06f),  cube, grip);

        // 칼날 — 생성 메시. 스케일 1 (형상은 메시가 갖는다)
        var blade = Child(root, "Blade");
        blade.localPosition = new Vector3(0f, 0f, BladeStart);
        blade.localRotation = Quaternion.identity;
        blade.localScale = Vector3.one;
        Renderer(blade.gameObject, mesh, edge);

        // 총구(=칼끝)와 가늠자 앵커 — 이름과 위치만 유지하면 Player.prefab의 참조가 그대로 산다
        Child(root, "MuzzlePoint").localPosition = new Vector3(0f, 0f, BladeStart + BladeLength * 0.92f);
        Child(root, "SightPos").localPosition    = new Vector3(0f, 0.05f, 0.14f);

        int weaponLayer = LayerMask.NameToLayer("Weapon");
        if (weaponLayer >= 0)
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = weaponLayer;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();

        Debug.Log($"[MeleeWeaponBuilder] {PrefabPath} 갱신 — 칼날 {mesh.vertexCount}버텍스");
    }

    // ── 칼날 메시 ───────────────────────────────────────────────

    /// <summary>
    /// +Z로 뻗는 양날. 단면은 다이아몬드(±X가 넓은 면, ±Y가 날) — 밑동에서 끝으로 가며
    /// 높이·두께가 함께 줄고, 마지막은 살짝 기운 칼끝 한 점으로 모인다.
    /// </summary>
    static Mesh BuildBladeMesh()
    {
        const int rings = 7;
        var verts = new List<Vector3>();
        var tris = new List<int>();

        for (int i = 0; i < rings; i++)
        {
            float t = i / (float)(rings - 1);
            float z = BladeLength * t * 0.92f;
            float h = Mathf.Lerp(0.072f, 0.030f, Mathf.Pow(t, 0.85f));   // 날 높이(폭)
            float w = Mathf.Lerp(0.013f, 0.004f, t);                     // 두께

            verts.Add(new Vector3(+w, 0f, z));
            verts.Add(new Vector3(0f, +h, z));
            verts.Add(new Vector3(-w, 0f, z));
            verts.Add(new Vector3(0f, -h, z));
        }

        // 링 사이를 잇는다
        for (int i = 0; i < rings - 1; i++)
        {
            int a = i * 4, b = (i + 1) * 4;
            for (int j = 0; j < 4; j++)
            {
                int j2 = (j + 1) % 4;
                tris.Add(a + j); tris.Add(b + j); tris.Add(b + j2);
                tris.Add(a + j); tris.Add(b + j2); tris.Add(a + j2);
            }
        }

        // 칼끝 — 마지막 링에서 한 점으로. 살짝 아래로 기울여 사선 컷처럼 보이게
        int tip = verts.Count;
        verts.Add(new Vector3(0f, -0.008f, BladeLength));
        int last = (rings - 1) * 4;
        for (int j = 0; j < 4; j++)
        {
            int j2 = (j + 1) % 4;
            tris.Add(last + j); tris.Add(tip); tris.Add(last + j2);
        }

        // 밑동 마감 (이미터에 묻히지만 뒤에서 보면 뚫려 보인다)
        int baseCenter = verts.Count;
        verts.Add(new Vector3(0f, 0f, 0f));
        for (int j = 0; j < 4; j++)
        {
            int j2 = (j + 1) % 4;
            tris.Add(j); tris.Add(j2); tris.Add(baseCenter);
        }

        FixWinding(verts, tris);

        var mesh = new Mesh { name = "PlasmaCutterBlade" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// 면이 안쪽을 보고 있으면(뒤집힌 메시) 전체 인덱스 순서를 뒤집는다 —
    /// 로프트 방향을 손으로 맞히는 대신 계산으로 확정한다.
    /// </summary>
    static void FixWinding(List<Vector3> verts, List<int> tris)
    {
        var center = Vector3.zero;
        foreach (var v in verts) center += v;
        center /= verts.Count;

        int outward = 0;
        for (int i = 0; i < tris.Count; i += 3)
        {
            Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            outward += Vector3.Dot(n, (a + b + c) / 3f - center) > 0f ? 1 : -1;
        }
        if (outward >= 0) return;

        for (int i = 0; i < tris.Count; i += 3)
            (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
    }

    /// <summary>경로의 기존 메시에 내용을 덮어써 <b>같은 에셋</b>으로 유지한다 (참조 보존).</summary>
    static Mesh SaveMesh(Mesh fresh, string path)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(fresh, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }
        EditorUtility.CopySerialized(fresh, existing);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    // ── 계층 헬퍼 (있으면 고치고, 없을 때만 만든다) ──────────────

    static Transform Child(GameObject root, string name)
    {
        var t = root.transform.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            t = go.transform;
            t.SetParent(root.transform, false);
        }
        return t;
    }

    static void Box(GameObject root, string name, Vector3 pos, Vector3 scale, Mesh cube, Material mat)
    {
        var t = Child(root, name);
        t.localPosition = pos;
        t.localRotation = Quaternion.identity;
        t.localScale = scale;
        Renderer(t.gameObject, cube, mat);
    }

    static void Renderer(GameObject go, Mesh mesh, Material mat)
    {
        // ?? 는 못 쓴다 — 유니티의 '가짜 null'은 C# null이 아니라서 없는 컴포넌트가 그대로 반환된다
        var mf = go.GetComponent<MeshFilter>();
        if (mf == null) mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;  // 뷰모델 — 그림자 불필요
        mr.receiveShadows = false;
    }
}
