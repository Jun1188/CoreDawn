using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 캐릭터 팩 원본을 이 프로젝트에서 쓸 수 있는 상태로 만드는 <b>1회성 준비 도구</b>.
/// 결과는 전부 <c>.meta</c>와 새 <c>.mat</c>으로 남으므로 커밋하면 다시 돌릴 일이 없다.
///
/// 세 가지를 한다:
///   1. 클립 분할 — Chomper의 Hit1~4·CutsceneTOIdle은 <c>clipAnimations: []</c>라
///      이름 없는 기본 테이크로 들어온다. 이름을 붙여야 오버라이드 컨트롤러가 집을 수 있다.
///   2. 임포터 최적화 — 본 GameObject 계층 제거, 애니메이션 압축, 안 쓰는 채널 차단.
///      몬스터가 대량으로 깔릴 때 가장 싸게 먹히는 최적화다(코드 변경 0).
///   3. URP 머티리얼 생성 — 팩 셰이더는 전부 Built-in RP 서피스 셰이더
///      (<c>CGPROGRAM</c> + <c>#pragma surface … Standard</c>)라 이 프로젝트(URP 17.3)에서 깨진다.
///      원본은 그대로 두고 URP/Lit 사본을 따로 만든다 — 서드파티 원본은 건드리지 않는다.
/// </summary>
public static class MonsterAssetSetup
{
    private const string UrpLitShader = "Universal Render Pipeline/Lit";

    [MenuItem("Tools/Monsters/1. Prepare Source Assets")]
    public static void PrepareMenu() => Debug.Log(PrepareAll());

    public static string PrepareAll()
    {
        var log = new System.Text.StringBuilder();

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (var species in MonsterCatalog.All)
            {
                log.AppendLine(PrepareClips(species));
                log.AppendLine(SetupModelImporter(species));
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        // 머티리얼 생성은 임포트가 끝난 뒤여야 텍스처를 확실히 집는다
        foreach (var species in MonsterCatalog.All)
            log.AppendLine(BuildUrpMaterials(species));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return log.ToString();
    }

    // ── 1. 클립 분할 ────────────────────────────────────────────

    /// <summary>
    /// 클립 FBX를 두 가지로 손본다.
    ///
    /// 1. <b>이름 붙이기</b> — 파일명 <c>@ChomperHit1.fbx</c> → 클립 <c>ChomperHit1</c>.
    ///    이미 분할된 FBX(Grenadier 전부, Chomper 4개)의 구간은 건드리지 않는다.
    ///    원본 저작자가 정한 프레임 구간이 우리 추측보다 정확하다.
    ///
    /// 2. <b>애니메이션 이벤트 제거</b> — 3D Game Kit의 클립에는 원래 킷의
    ///    <c>PlayStep</c>·<c>Grunt</c> 같은 이벤트가 박혀 있다. 받는 컴포넌트가 없으면
    ///    Unity가 <i>이벤트가 발화할 때마다</i> 에러를 찍는다. 발소리는 걸을 때마다 나므로
    ///    몬스터 한 마리가 초당 몇 줄, 수십 마리면 로그가 폭주하고 그만큼 느려진다.
    ///    나중에 발소리를 쓰고 싶어지면, 이벤트를 남기는 대신 같은 이름의 메서드를 가진
    ///    수신 컴포넌트를 모델 루트에 붙이는 쪽이 낫다(그때 이 제거를 빼면 된다).
    /// </summary>
    private static string PrepareClips(MonsterCatalog.Species species)
    {
        int named = 0, kept = 0, eventsStripped = 0, rootHeightFixed = 0;

        foreach (string folder in species.ClipFolders)
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                bool dirty = false;

                if (clips == null || clips.Length == 0)
                {
                    var takes = importer.importedTakeInfos;
                    if (takes == null || takes.Length == 0) continue;

                    var take = takes[0];
                    clips = new[]
                    {
                        new ModelImporterClipAnimation
                        {
                            name = System.IO.Path.GetFileNameWithoutExtension(path).TrimStart('@'),
                            takeName = take.name,
                            firstFrame = take.bakeStartTime * take.sampleRate,
                            lastFrame = take.bakeStopTime * take.sampleRate,
                            loopTime = false,   // 피격·컷신 모션 — 반복하면 안 된다
                            lockRootRotation = false,
                            keepOriginalOrientation = true,
                            keepOriginalPositionY = true,
                            keepOriginalPositionXZ = true,
                        }
                    };
                    named++;
                    dirty = true;
                }
                else kept++;

                foreach (var clip in clips)
                {
                    if (clip.events != null && clip.events.Length > 0)
                    {
                        eventsStripped += clip.events.Length;
                        clip.events = new AnimationEvent[0];
                        dirty = true;
                    }

                    // 루트 높이를 포즈에 굽는다 — 이게 없으면 몬스터가 땅에 파묻힌다.
                    //
                    // 팩의 클립은 전부 lockRootHeightY=false(= "Bake Into Pose" 꺼짐)라 루트의 Y가
                    // 루트 모션으로 '추출'된다. 우리는 위치를 MovementComponent가 정하므로
                    // applyRootMotion=false이고, 그러면 추출된 Y는 그냥 <b>버려진다</b>.
                    // 그 결과 클립마다 제멋대로인 기준 높이로 그려진다 — 실측으로 Chomper의
                    // Idle과 Walk가 0.25m나 어긋났다(키가 1m인 놈에게).
                    // 켜 두면 작가가 의도한 높이가 포즈에 남아 모든 클립이 같은 지면을 공유한다.
                    // XZ(lockRootPositionXZ)는 건드리지 않는다 — 그걸 구우면 걷기 루프가
                    // 앞으로 미끄러지다 루프마다 튄다.
                    if (!clip.lockRootHeightY)
                    {
                        clip.lockRootHeightY = true;
                        rootHeightFixed++;
                        dirty = true;
                    }
                }

                if (!dirty) continue;

                importer.clipAnimations = clips;
                ApplyClipImporterDefaults(importer);
                importer.SaveAndReimport();
            }
        }

        return $"[{species.Id}] 클립 이름 부여 {named}개 / 기존 분할 유지 {kept}개 / " +
               $"애니메이션 이벤트 제거 {eventsStripped}개 / 루트 높이 포즈에 굽기 {rootHeightFixed}개";
    }

    /// <summary>클립 전용 FBX의 공통 임포터 설정 — 메시·머티리얼이 없으니 전부 끈다.</summary>
    private static void ApplyClipImporterDefaults(ModelImporter importer)
    {
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importVisibility = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;

        // 키프레임 리덕션 — 곡선당 키가 줄어 클립 메모리와 평가 비용이 함께 내려간다.
        // 기본 허용오차(0.5)는 눈으로 차이를 못 느끼는 수준이다.
        importer.animationCompression = ModelImporterAnimationCompression.Optimal;
        importer.animationRotationError = 0.5f;
        importer.animationPositionError = 0.5f;
        importer.animationScaleError = 0.5f;
    }

    // ── 2. 모델 임포터 ──────────────────────────────────────────

    private static string SetupModelImporter(MonsterCatalog.Species species)
    {
        string modelPath = FindModelPath(species);
        if (modelPath == null) return $"[{species.Id}] FAIL 모델 FBX를 찾지 못함";

        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null) return $"[{species.Id}] FAIL ModelImporter 아님: {modelPath}";

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importVisibility = false;

        // 본 GameObject 계층을 통째로 걷어낸다. 본 하나가 곧 Transform 하나라
        // Chomper·Grenadier급(본 50~70개)에서는 개체당 수십 개의 트랜스폼이 사라진다.
        // 지금 본에 매달린 소켓(총구 등)이 하나도 없어 안전하다 —
        // 나중에 필요해지면 extraExposedTransformPaths로 필요한 본만 노출하면 된다.
        importer.optimizeGameObjects = true;

        importer.meshCompression = ModelImporterMeshCompression.Medium;
        importer.isReadable = false;   // 런타임에 메시를 읽지 않는다 → CPU 사본을 들고 있을 이유가 없다

        importer.SaveAndReimport();
        return $"[{species.Id}] 모델 임포터 최적화: {modelPath}";
    }

    private static string FindModelPath(MonsterCatalog.Species species)
    {
        string folder = species.SourceFolder + "/Models";
        if (!AssetDatabase.IsValidFolder(folder)) return null;

        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { folder });
        return guids.Length > 0 ? AssetDatabase.GUIDToAssetPath(guids[0]) : null;
    }

    // ── 3. URP 머티리얼 ────────────────────────────────────────

    /// <summary>
    /// 팩 머티리얼 하나당 URP/Lit 사본 하나. 원본 머티리얼들이 (커스텀 셰이더로 바뀐 뒤에도)
    /// Standard 시절의 프로퍼티 이름을 그대로 들고 있어서, 텍스처를 그대로 옮겨 담을 수 있다.
    /// 이름 규칙이 아니라 프로퍼티 존재 여부로 판단하므로 새 종에도 그대로 먹는다.
    /// </summary>
    private static string BuildUrpMaterials(MonsterCatalog.Species species)
    {
        string sourceFolder = species.SourceFolder + "/Materials";
        if (!AssetDatabase.IsValidFolder(sourceFolder))
            return $"[{species.Id}] 머티리얼 폴더 없음: {sourceFolder}";

        var shader = Shader.Find(UrpLitShader);
        if (shader == null) return $"[{species.Id}] FAIL '{UrpLitShader}' 셰이더를 찾지 못함";

        MonsterCatalog.EnsureFolder(species.MaterialFolder);

        int made = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { sourceFolder }))
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (source == null) continue;

            string path = $"{species.MaterialFolder}/{source.name}_URP.mat";
            var target = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (target == null)
            {
                target = new Material(shader);
                AssetDatabase.CreateAsset(target, path);
            }
            target.shader = shader;

            CopyColor(source, "_Color", target, "_BaseColor");
            CopyTexture(source, "_MainTex", target, "_BaseMap", null);
            CopyTexture(source, "_BumpMap", target, "_BumpMap", "_NORMALMAP", markAsNormal: true);
            CopyTexture(source, "_MetallicGlossMap", target, "_MetallicGlossMap", "_METALLICSPECGLOSSMAP");
            CopyTexture(source, "_OcclusionMap", target, "_OcclusionMap", "_OCCLUSIONMAP");

            if (CopyTexture(source, "_EmissionMap", target, "_EmissionMap", "_EMISSION"))
            {
                target.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (target.HasProperty("_EmissionColor") &&
                    target.GetColor("_EmissionColor") == Color.black)
                    target.SetColor("_EmissionColor", Color.white);
            }

            // MetallicGlossMap의 알파가 smoothness를 정한다 — 스칼라는 그 상한이므로 1로 열어 둔다
            if (target.HasProperty("_Smoothness")) target.SetFloat("_Smoothness", 1f);
            if (target.HasProperty("_Metallic")) target.SetFloat("_Metallic", 1f);

            // 같은 종 몬스터가 수십 마리 깔리므로 인스턴싱을 켜 둔다
            target.enableInstancing = true;

            EditorUtility.SetDirty(target);
            made++;
        }

        return $"[{species.Id}] URP 머티리얼 {made}개 → {species.MaterialFolder}";
    }

    private static bool CopyTexture(Material source, string from, Material target, string to,
                                    string keyword, bool markAsNormal = false)
    {
        if (!source.HasProperty(from) || !target.HasProperty(to)) return false;

        var tex = source.GetTexture(from);
        if (tex == null) return false;

        if (markAsNormal) EnsureNormalMapImport(tex);

        target.SetTexture(to, tex);
        if (!string.IsNullOrEmpty(keyword)) target.EnableKeyword(keyword);
        return true;
    }

    private static void CopyColor(Material source, string from, Material target, string to)
    {
        if (source.HasProperty(from) && target.HasProperty(to))
            target.SetColor(to, source.GetColor(from));
    }

    /// <summary>
    /// 노멀맵으로 쓰이는 텍스처는 임포터 타입도 노멀맵이어야 한다.
    /// 팩 원본은 Default로 들어와 있어서, 그대로 두면 URP/Lit이 "노멀맵이 아니다" 경고를 내고
    /// 굴곡이 이상하게 나온다.
    /// </summary>
    private static void EnsureNormalMapImport(Texture texture)
    {
        string path = AssetDatabase.GetAssetPath(texture);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || importer.textureType == TextureImporterType.NormalMap) return;

        importer.textureType = TextureImporterType.NormalMap;
        importer.SaveAndReimport();
    }

    /// <summary>
    /// 리그 빌더가 쓰는 "원본 머티리얼 이름 → URP 사본" 사전.
    /// 서브메시 순서를 모른 채 통째로 덮어쓰지 않고 <b>이름으로 짝을 지어</b> 바꾸므로,
    /// 머티리얼이 여러 개인 모델(Grenadier: 본체·코어·눈)도 제자리를 지킨다.
    /// </summary>
    public static Dictionary<string, Material> LoadUrpMaterials(MonsterCatalog.Species species)
    {
        var map = new Dictionary<string, Material>();
        if (!AssetDatabase.IsValidFolder(species.MaterialFolder)) return map;

        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { species.MaterialFolder }))
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat == null || !mat.name.EndsWith("_URP")) continue;
            map[mat.name.Substring(0, mat.name.Length - 4)] = mat;
        }
        return map;
    }
}
