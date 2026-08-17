using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 타워 프리팹의 <b>View 서브트리</b>를 표준 계층으로 만들어 넣는 에디터 도구.
///
/// 왜 도구로 만드는가: 클립(.anim)은 트랜스폼 <i>경로 문자열</i>로 바인딩된다. 6개를 손으로
/// 조립하면 이름이나 깊이가 미묘하게 어긋나 공용 클립이 조용히 안 먹는다. 여기서 한 번에
/// 찍어내면 모든 타워가 같은 경로를 갖고, 클립 한 세트를 전부가 공유한다.
///
/// 표준 계층:
///   View                    ← Animator: 등장(Deploy)
///     Base                    받침 메시
///     YawPivot              ← 코드: 좌우 선회
///       PitchPivot          ← 코드: 상하 부앙
///         Droop             ← Animator: 탄약 끊김 처짐
///           Recoil          ← 코드: 반동
///             Turret          포탑 메시
///               Muzzle_0..n   총구 (템플릿의 ProjectilePoint 좌표 그대로)
///
/// 포탑이 없는 타워는 YawPivot 이하를 만들지 않는다 — TowerVisualController가 전부 null 허용이다.
///
/// 새 타워를 추가하려면 <see cref="Specs"/>에 한 줄 넣고 메뉴를 다시 실행하면 된다.
/// </summary>
public static class TowerRigBuilder
{
    private const string TemplateRoot = "Assets/ThirdParty/UnityTechnologies/TowerDefenseTemplate";
    private const string BuildingRoot = "Assets/Prefabs/Buildings";

    /// <summary>타워 하나를 어떤 템플릿 모델로 만들지.</summary>
    public class Spec
    {
        /// <summary>고칠 타워 프리팹 (GUID 보존을 위해 항상 제자리 수정한다).</summary>
        public string TowerPrefab;

        /// <summary>레이아웃을 베껴올 템플릿 프리팹 — 받침/포탑 위치와 총구 좌표의 출처.</summary>
        public string TemplatePrefab;

        /// <summary>받침 메시 GameObject 이름 (템플릿 안에서).</summary>
        public string BaseName;

        /// <summary>
        /// 받침만 다른 템플릿에서 가져올 때. 비우면 <see cref="TemplatePrefab"/>에서 찾는다.
        ///
        /// 레벨을 섞어 쓰기 위한 것이다. 기관총 L03 받침은 좌우로 뻗은 넓은 방호판이 달려 있어
        /// 폭이 2.89까지 나가는데, 그 폭을 칸에 맞추면 타워 전체가 절반으로 줄어든다.
        /// 배율로 키우면 이번엔 방호판이 옆 칸 타워를 파고든다. L01 받침(1.81 정사각, 방호판 없음)에
        /// L03 포탑을 얹는 쪽이 칸도 지키고 키도 산다.
        /// </summary>
        public string BaseTemplatePrefab;

        /// <summary>포탑 메시 GameObject 이름. 비우면 포탑 없는 단일 메시 타워.</summary>
        public string TurretName;

        /// <summary>본체 머티리얼 (URP로 변환해 둔 것).</summary>
        public string Material;

        /// <summary>그리드 점유 칸 수 — 받침이 이 크기에 맞도록 View 전체를 균일 축소한다.</summary>
        public int Cells = 1;

        /// <summary>
        /// 발사음이 든 폴더 — 안의 클립을 전부 후보로 넣고 매 발 랜덤으로 고른다.
        /// 기관총처럼 초당 여러 발 나가는 타워는 후보가 많아야 귀가 덜 피곤하다.
        /// </summary>
        public string FireClipFolder;

        /// <summary>폴더 대신 딱 집어 쓸 발사음.</summary>
        public string[] FireClips;

        /// <summary>
        /// 칸에 맞춘 축척에 곱하는 연출용 보정. 1이면 받침이 칸에 정확히 들어간다.
        ///
        /// 모델마다 높이/가로 비율이 제각각이라 받침만 칸에 맞추면 어떤 타워는 납작해진다
        /// (기관총 0.78 : EMP 2.09 — 2.7배 차이). 1을 넘기면 받침이 칸을 조금 넘지만
        /// 존재감이 산다. 이웃 타워와 겹쳐 보이기 시작하는 한계는 대략 1.4다.
        /// </summary>
        public float ScaleMultiplier = 1f;

        /// <summary>
        /// 포탑 회전축의 높이 보정(모델 로컬 단위). 받침을 다른 레벨에서 가져왔을 때
        /// 받침 높이 차이만큼 포탑이 뜨거나 잠기는 것을 메운다. 같은 템플릿을 쓰면 0.
        /// </summary>
        public float TurretYOffset;
    }

    // 모든 타워가 공유하는 소리·연출
    private const string AudioRoot = TemplateRoot + "/Audio/SFX";
    private const string DestroyClipFolder = AudioRoot + "/Tower Destruction";
    private const string StarvedClip = "Assets/Art/Audio/Casual Game Sounds U6/CasualGameSounds/DM-CGS-04.wav";
    private const string DeployVfx = TemplateRoot + "/Particles/Prefabs/BuildPfx.prefab";
    private const string DestroyVfx = TemplateRoot + "/Particles/Prefabs/TowerDeathExplosion.prefab";

    /// <summary>칸 하나당 월드 크기. PlacementSystem.cellSize와 맞춘다.</summary>
    private const float CellSize = 1f;

    /// <summary>칸을 꽉 채우지 않고 남기는 여유 — 타워끼리 딱 붙어 보이지 않게.</summary>
    private const float FootprintFill = 0.9f;

    private static readonly Spec[] Specs =
    {
        new Spec {
            TowerPrefab = BuildingRoot + "/BasicTurret.prefab",
            TemplatePrefab = TemplateRoot + "/Prefabs/Towers/MachineGun/MachineGunTower_2.prefab",
            // 받침은 L01(1.81 정사각, 방호판 없음), 포탑은 L03. 레벨을 섞는 이유는 BaseTemplatePrefab 주석 참고.
            BaseTemplatePrefab = TemplateRoot + "/Prefabs/Towers/MachineGun/MachineGunTower_0.prefab",
            BaseName = "Base_MachineGun_L01", TurretName = "Turret_MachineGun_L03",
            Material = TemplateRoot + "/Materials/Towers/MachineGun/MachineGun_Level3_tex_v003.mat",
            Cells = 1,
            ScaleMultiplier = 1.3f,
            TurretYOffset = -0.167f,   // L03 받침(0.97) → L01 받침(0.80) 높이차만큼 포탑을 내린다
            FireClipFolder = AudioRoot + "/Towers/MachineGun",
        },
        new Spec {
            TowerPrefab = BuildingRoot + "/HeavyTurret.prefab",
            TemplatePrefab = TemplateRoot + "/Prefabs/Towers/Rocket/RocketTower_2.prefab",
            BaseName = "Base_RocketTower_L03", TurretName = "Turret_RocketTower_L03",
            Material = TemplateRoot + "/Materials/Towers/Rocket/Rocket_Tower_L03_Texture_V003.mat",
            Cells = 2,
            FireClipFolder = AudioRoot + "/Towers/RocketLauncher",
        },
        new Spec {
            TowerPrefab = BuildingRoot + "/MortarTower.prefab",
            TemplatePrefab = TemplateRoot + "/Prefabs/Towers/Supertower/SuperTower_0.prefab",
            BaseName = "SuperTower_Base", TurretName = "SuperTower_Turret",
            Material = TemplateRoot + "/Materials/Towers/SuperTower/SuperTower_Albedo_V001.mat",
            Cells = 2,
            FireClipFolder = AudioRoot + "/Towers/SuperTower",
        },
        new Spec {
            TowerPrefab = BuildingRoot + "/LaserTower.prefab",
            TemplatePrefab = TemplateRoot + "/Prefabs/Towers/Laser/LaserTower_2.prefab",
            BaseName = "LaserTower_BASE_L03", TurretName = "LaserTower_TURRET_L03",
            Material = TemplateRoot + "/Materials/Towers/Laser/LaserTower_L03_Albedo_V001.mat",
            Cells = 2,
            FireClipFolder = AudioRoot + "/Towers/Laser",
        },
        // 포탑 없는 타워 — TurretName을 비워 둔다
        new Spec {
            TowerPrefab = BuildingRoot + "/SlowFieldTower.prefab",
            TemplatePrefab = TemplateRoot + "/Prefabs/Towers/EMP/EMP_2.prefab",
            BaseName = "EMP_Tower_level_3", TurretName = null,
            Material = TemplateRoot + "/Materials/Towers/EMP Tower/EMP_Tower_level_3_tex_v001.mat",
            Cells = 1,
            FireClips = new[] { "Assets/Art/Audio/Casual Game Sounds U6/CasualGameSounds/DM-CGS-22.wav" },
        },
        new Spec {
            TowerPrefab = BuildingRoot + "/Fence.prefab",
            TemplatePrefab = TemplateRoot + "/Prefabs/Towers/Pylon/EnergyPylon_2.prefab",
            BaseName = "pylon_level_3", TurretName = null,
            Material = TemplateRoot + "/Materials/Towers/EnergyPylon/EnergyPylon_L03_Albedo_V001.mat",
            Cells = 1,
        },
    };

    [MenuItem("Tools/Towers/Rebuild Tower Rigs")]
    public static void RebuildAll()
    {
        Debug.Log(BuildAll());
    }

    /// <summary>전부 다시 짓고 결과를 한 덩어리 문자열로 돌려준다 (CLI에서 읽기 좋게).</summary>
    public static string BuildAll()
    {
        var log = new System.Text.StringBuilder();
        foreach (var spec in Specs)
        {
            try { log.AppendLine(Build(spec)); }
            catch (System.Exception e) { log.AppendLine($"FAIL {spec.TowerPrefab}: {e.Message}"); }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return log.ToString();
    }

    public static string Build(Spec spec)
    {
        var template = AssetDatabase.LoadAssetAtPath<GameObject>(spec.TemplatePrefab);
        if (template == null) return $"FAIL {spec.TowerPrefab}: 템플릿 없음 {spec.TemplatePrefab}";

        // 받침은 다른 레벨 템플릿에서 가져올 수 있다 (넓은 방호판을 피하려고 레벨을 섞는 경우)
        GameObject baseTemplate = template;
        if (!string.IsNullOrEmpty(spec.BaseTemplatePrefab))
        {
            baseTemplate = AssetDatabase.LoadAssetAtPath<GameObject>(spec.BaseTemplatePrefab);
            if (baseTemplate == null)
                return $"FAIL {spec.TowerPrefab}: 받침 템플릿 없음 {spec.BaseTemplatePrefab}";
        }

        Transform tBase = FindDeep(baseTemplate.transform, spec.BaseName);
        if (tBase == null) return $"FAIL {spec.TowerPrefab}: 받침 '{spec.BaseName}' 없음";

        Transform tTurret = string.IsNullOrEmpty(spec.TurretName)
            ? null : FindDeep(template.transform, spec.TurretName);

        var material = AssetDatabase.LoadAssetAtPath<Material>(spec.Material);

        // 받침의 가로 크기로 전체 축척을 정한다 — 그리드 칸에 맞추는 유일한 기준
        float target = spec.Cells * CellSize * FootprintFill;
        Bounds baseBounds = MeshBounds(tBase);
        float widest = Mathf.Max(baseBounds.size.x, baseBounds.size.z);
        float scale = (widest > 0.0001f ? target / widest : 1f) * spec.ScaleMultiplier;

        GameObject root = PrefabUtility.LoadPrefabContents(spec.TowerPrefab);
        try
        {
            // 기존 시각물 제거 — 자리표시 큐브(Mesh)와 이전 실행의 View
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var c = root.transform.GetChild(i);
                if (c.name == "Mesh" || c.name == "View") Object.DestroyImmediate(c.gameObject);
            }

            // View는 빌더의 것 — 칸에 맞춘 축척과 접지 보정이 여기 실린다.
            // Anim은 애니메이터의 것 — 항상 항등 자세에서 출발하므로 클립이 절대값을 써도
            // 타워마다 다른 축척·접지값을 덮어쓰지 않는다. 둘을 한 트랜스폼에 두면
            // 등장 연출이 재생되는 순간 타워가 원래 크기와 높이를 잃는다.
            var view = new GameObject("View").transform;
            view.SetParent(root.transform, false);
            view.localScale = Vector3.one * scale;

            var anim = new GameObject("Anim").transform;
            anim.SetParent(view, false);

            // ── 받침 ──
            GameObject baseGo = CopySubtree(tBase, anim, "Base");
            baseGo.transform.localPosition = tBase.localPosition;
            baseGo.transform.localRotation = tBase.localRotation;
            baseGo.transform.localScale = tBase.localScale;

            Transform recoil = null;
            var muzzles = new List<Transform>();

            if (tTurret != null)
            {
                // ── 포탑 체인 ── 회전축은 템플릿의 포탑 위치에 둔다
                var yaw = new GameObject("YawPivot").transform;
                yaw.SetParent(anim, false);
                yaw.localPosition = new Vector3(0f, tTurret.localPosition.y + spec.TurretYOffset, 0f);

                var pitch = new GameObject("PitchPivot").transform;
                pitch.SetParent(yaw, false);

                var droop = new GameObject("Droop").transform;
                droop.SetParent(pitch, false);

                recoil = new GameObject("Recoil").transform;
                recoil.SetParent(droop, false);

                GameObject turretGo = CopySubtree(tTurret, recoil, "Turret");
                // 회전축이 이미 포탑 높이에 있으므로 세로 성분만 뺀다
                turretGo.transform.localPosition = new Vector3(
                    tTurret.localPosition.x, 0f, tTurret.localPosition.z);
                turretGo.transform.localRotation = tTurret.localRotation;
                turretGo.transform.localScale = tTurret.localScale;

                // ── 총구 ── 템플릿의 ProjectilePoint를 포탑 자식으로 그대로 옮긴다.
                // 포탑 아래 두어야 템플릿과 같은 로컬 공간이라 좌표를 그대로 쓸 수 있고,
                // 반동·조준을 자동으로 따라간다.
                foreach (Transform src in tTurret)
                {
                    if (!src.name.StartsWith("ProjectilePoint")) continue;
                    var m = new GameObject("Muzzle_" + muzzles.Count).transform;
                    m.SetParent(turretGo.transform, false);
                    m.localPosition = src.localPosition;
                    m.localRotation = src.localRotation;
                    muzzles.Add(m);
                }
            }

            CleanUpVisuals(view, material);
            GroundAlign(view);

            // ── 컴포넌트 배선 ──
            var visual = root.GetComponent<TowerVisualController>();
            if (visual == null) visual = root.AddComponent<TowerVisualController>();

            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.applyRootMotion = false;
            // 공용 컨트롤러 — 표준 계층을 쓰는 한 모든 타워가 이 하나를 공유한다
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/Art/Animation/Towers/TowerCommon.controller");
            if (controller != null) animator.runtimeAnimatorController = controller;
            // CullCompletely는 화면 밖에서 상태 진행 자체를 멈춰 한 번뿐인 등장 연출을 건너뛸 수 있다.
            // 트랜스폼 쓰기만 생략하는 쪽이 안전하다.
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            WireVisual(visual, view, recoil, muzzles, spec);

            PrefabUtility.SaveAsPrefabAsset(root, spec.TowerPrefab);

            float muzzleY = muzzles.Count > 0
                ? muzzles[0].position.y
                : baseBounds.max.y * scale;

            return $"OK {System.IO.Path.GetFileNameWithoutExtension(spec.TowerPrefab)} " +
                   $"scale={scale:F3} muzzles={muzzles.Count} muzzleY={muzzleY:F3}";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 그림자 대역 메시를 버리고(참조 머티리얼이 프로젝트에 없어 마젠타로 뜬다) 진짜 그림자를 켠다.
    /// 템플릿은 가짜 그림자 판을 깔고 본체의 그림자를 꺼 둔 구조였다 — URP에서는 불필요하다.
    /// </summary>
    private static void CleanUpVisuals(Transform view, Material material)
    {
        var doomed = new List<GameObject>();
        foreach (var t in view.GetComponentsInChildren<Transform>(true))
            if (t.name.EndsWith("_Shadow")) doomed.Add(t.gameObject);

        // 템플릿의 총구 화염 파티클 뭉치도 뗀다. 이 프로젝트에서 발사 연출의 주인은
        // 탄약(AmmoModuleSO.muzzleFlashPrefab)이지 타워가 아니다 — 같은 탄을 쓰면
        // 총과 타워가 같은 연출을 쓴다는 규칙을 지키려면 여기 붙어 있으면 안 된다.
        // (원본은 템플릿 프리팹에 그대로 남아 있으니 나중에 따로 뽑아 쓰면 된다.)
        foreach (var ps in view.GetComponentsInChildren<ParticleSystem>(true))
        {
            Transform node = ps.transform;
            // 메시를 품지 않은 가장 바깥 조상까지 올라가 통째로 버린다
            while (node.parent != null && node.parent != view &&
                   node.parent.GetComponentInChildren<MeshFilter>(true) == null)
                node = node.parent;
            if (!doomed.Contains(node.gameObject)) doomed.Add(node.gameObject);
        }

        foreach (var go in doomed) if (go != null) Object.DestroyImmediate(go);

        foreach (var mr in view.GetComponentsInChildren<MeshRenderer>(true))
        {
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            if (material == null) continue;

            var mats = new Material[mr.sharedMaterials.Length == 0 ? 1 : mr.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            mr.sharedMaterials = mats;
        }
    }

    /// <summary>
    /// View를 통째로 올려 가장 낮은 메시 점이 바닥(y=0)에 닿게 한다.
    /// 모델마다 피벗이 제각각이다 — EMP 타워는 피벗이 메시 <i>중심</i>이라 그대로 두면 절반이 땅에 묻힌다
    /// (템플릿은 프리팹 루트를 y=1.935로 올려서 때웠는데, 배치 시스템이 루트 위치를 정하는
    /// 이 프로젝트에서는 그 수법을 쓸 수 없다).
    /// </summary>
    private static void GroundAlign(Transform view)
    {
        var renderers = view.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length == 0) return;

        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);

        // 프리팹 컨텐츠 루트는 원점에 있으므로 월드 y가 그대로 바닥 기준 높이다
        view.localPosition += new Vector3(0f, -b.min.y, 0f);
    }

    private static void WireVisual(TowerVisualController visual, Transform view,
                                   Transform recoil, List<Transform> muzzles, Spec spec)
    {
        var so = new SerializedObject(visual);

        // ── 소리·연출 ──
        AudioClip[] fire = spec.FireClips != null
            ? LoadClips(spec.FireClips)
            : LoadClipFolder(spec.FireClipFolder);

        SetObjectArray(so, "fireClips", fire);
        SetObjectArray(so, "destroyClips", LoadClipFolder(DestroyClipFolder));
        so.FindProperty("starvedClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<AudioClip>(StarvedClip);
        so.FindProperty("deployVfx").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(DeployVfx);
        so.FindProperty("destroyVfx").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(DestroyVfx);

        so.FindProperty("view").objectReferenceValue = view;
        so.FindProperty("yawPivot").objectReferenceValue = view.Find("Anim/YawPivot");
        so.FindProperty("pitchPivot").objectReferenceValue =
            recoil != null ? view.Find("Anim/YawPivot/PitchPivot") : null;
        so.FindProperty("recoil").objectReferenceValue = recoil;

        var arr = so.FindProperty("muzzles");
        arr.arraySize = muzzles.Count;
        for (int i = 0; i < muzzles.Count; i++)
            arr.GetArrayElementAtIndex(i).objectReferenceValue = muzzles[i];

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>폴더 안의 오디오 클립을 전부 이름순으로 — 후보가 많을수록 반복이 덜 지겹다.</summary>
    private static AudioClip[] LoadClipFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            return new AudioClip[0];

        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
        var clips = new List<AudioClip>(guids.Length);
        foreach (var g in guids)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g));
            if (c != null) clips.Add(c);
        }
        clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return clips.ToArray();
    }

    private static AudioClip[] LoadClips(string[] paths)
    {
        var clips = new List<AudioClip>();
        foreach (var p in paths)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
            if (c != null) clips.Add(c);
        }
        return clips.ToArray();
    }

    private static void SetObjectArray(SerializedObject so, string field, Object[] values)
    {
        var arr = so.FindProperty(field);
        arr.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            arr.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    /// <summary>메시가 딸린 서브트리를 씬 사본으로 복제해 붙인다 (모델 프리팹 중첩을 피한다).</summary>
    private static GameObject CopySubtree(Transform source, Transform parent, string name)
    {
        var copy = Object.Instantiate(source.gameObject);
        copy.name = name;
        copy.transform.SetParent(parent, false);

        // 템플릿 프리팹은 이 프로젝트에 없는 스크립트를 참조한다(아트만 임포트했다).
        // 깨진 스크립트가 하나라도 남으면 Unity가 프리팹 저장 자체를 거부한다.
        foreach (var t in copy.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

        return copy;
    }

    /// <summary>서브트리 전체의 로컬 기준 메시 크기 — 축척 계산용.</summary>
    private static Bounds MeshBounds(Transform t)
    {
        var filters = t.GetComponentsInChildren<MeshFilter>(true);
        var b = new Bounds();
        bool first = true;
        foreach (var f in filters)
        {
            if (f.sharedMesh == null) continue;
            // 대역 그림자 판은 크기 기준에서 뺀다
            if (f.name.EndsWith("_Shadow")) continue;

            Bounds mb = f.sharedMesh.bounds;
            Vector3 s = f.transform.lossyScale;
            var scaled = new Bounds(Vector3.Scale(mb.center, s), Vector3.Scale(mb.size, s));
            if (first) { b = scaled; first = false; } else b.Encapsulate(scaled);
        }
        return b;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
