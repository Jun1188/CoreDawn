using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 타워 공용 애니메이션 에셋(.anim + .controller)을 코드로 찍어내는 에디터 도구.
///
/// 클립은 트랜스폼 <b>경로 문자열</b>로 바인딩되므로, <see cref="TowerRigBuilder"/>가 만든
/// 표준 계층을 쓰는 타워라면 종류에 상관없이 같은 클립이 그대로 재생된다.
/// 그래서 타워가 늘어나도 여기서 만들 에셋은 늘지 않는다 — 개성이 필요한 타워만
/// AnimatorOverrideController로 Active 클립을 갈아끼우면 된다.
///
/// 역할 분담(중요): 이 클립들은 <c>View/Anim</c>과 <c>Droop</c>만 건드린다.
/// 조준(YawPivot·PitchPivot)과 반동(Recoil)은 코드가 매 프레임 쓰므로 절대 건드리지 않는다.
/// </summary>
public static class TowerAnimationBuilder
{
    private const string Dir = "Assets/Art/Animation/Towers";

    // 표준 계층에서의 경로 — TowerRigBuilder와 반드시 일치해야 한다
    private const string PathAnim = "View/Anim";
    private const string PathDroop = "View/Anim/YawPivot/PitchPivot/Droop";

    private const float DeployLength = 0.45f;
    private const float DroopDegrees = -18f;

    [MenuItem("Tools/Towers/Rebuild Tower Animations")]
    public static void RebuildMenu() => Debug.Log(BuildAll());

    public static string BuildAll()
    {
        if (!AssetDatabase.IsValidFolder(Dir))
        {
            EnsureFolder("Assets/Art/Animation");
            AssetDatabase.CreateFolder("Assets/Art/Animation", "Towers");
        }

        AnimationClip deploy = BuildDeploy();
        AnimationClip active = BuildActive();
        AnimationClip starved = BuildStarved();

        var controller = BuildController(deploy, active, starved);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return $"OK animations: {AssetDatabase.GetAssetPath(controller)} " +
               $"(deploy {deploy.length:F2}s, active, starved)";
    }

    // ── 클립 ────────────────────────────────────────────────────

    /// <summary>등장 — 땅에서 솟아오르며 살짝 오버슈트한 뒤 자리를 잡는다.</summary>
    private static AnimationClip BuildDeploy()
    {
        var clip = NewClip("Tower_Deploy", loop: false);

        SetCurve(clip, PathAnim, "m_LocalScale", 'x', Key(0f, 0.01f), Key(0.30f, 1.06f), Key(DeployLength, 1f));
        SetCurve(clip, PathAnim, "m_LocalScale", 'y', Key(0f, 0.01f), Key(0.30f, 1.06f), Key(DeployLength, 1f));
        SetCurve(clip, PathAnim, "m_LocalScale", 'z', Key(0f, 0.01f), Key(0.30f, 1.06f), Key(DeployLength, 1f));
        SetCurve(clip, PathAnim, "m_LocalPosition", 'y', Key(0f, -0.6f), Key(0.30f, 0.06f), Key(DeployLength, 0f));

        // 등장 중에도 포신은 곧게 — Starved에서 곧장 재배치될 때 처짐이 남지 않도록
        SetIdentityRotation(clip, PathDroop);
        return Save(clip);
    }

    /// <summary>
    /// 기본 자세. "아무것도 안 하는 클립"이 아니라 <b>되돌리는 클립</b>이다 —
    /// Starved에서 빠져나올 때 처진 포신을 원위치로 돌려놓는 것이 이 클립의 일이다.
    /// 아키타입별 개성(EMP 코일 회전 등)은 이 슬롯을 오버라이드해서 넣는다.
    /// </summary>
    private static AnimationClip BuildActive()
    {
        var clip = NewClip("Tower_Active", loop: true);

        SetCurve(clip, PathAnim, "m_LocalScale", 'x', Key(0f, 1f), Key(0.5f, 1f));
        SetCurve(clip, PathAnim, "m_LocalScale", 'y', Key(0f, 1f), Key(0.5f, 1f));
        SetCurve(clip, PathAnim, "m_LocalScale", 'z', Key(0f, 1f), Key(0.5f, 1f));
        SetCurve(clip, PathAnim, "m_LocalPosition", 'y', Key(0f, 0f), Key(0.5f, 0f));
        SetIdentityRotation(clip, PathDroop);
        return Save(clip);
    }

    /// <summary>탄약 끊김 — 동력이 빠진 듯 포신이 앞으로 처진다.</summary>
    private static AnimationClip BuildStarved()
    {
        var clip = NewClip("Tower_Starved", loop: true);

        SetCurve(clip, PathAnim, "m_LocalScale", 'x', Key(0f, 1f), Key(1f, 1f));
        SetCurve(clip, PathAnim, "m_LocalScale", 'y', Key(0f, 1f), Key(1f, 1f));
        SetCurve(clip, PathAnim, "m_LocalScale", 'z', Key(0f, 1f), Key(1f, 1f));
        SetCurve(clip, PathAnim, "m_LocalPosition", 'y', Key(0f, 0f), Key(1f, 0f));

        // 쿼터니언으로 직접 넣는다 — 오일러 곡선(localEulerAnglesRaw)은 클립을 만든
        // 방식에 따라 바인딩 이름이 갈려서 조용히 안 먹는 일이 있다.
        float half = DroopDegrees * 0.5f * Mathf.Deg2Rad;
        float sx = Mathf.Sin(half), w = Mathf.Cos(half);

        SetCurve(clip, PathDroop, "m_LocalRotation", 'x', Key(0f, 0f), Key(0.6f, sx), Key(1f, sx));
        SetCurve(clip, PathDroop, "m_LocalRotation", 'y', Key(0f, 0f), Key(1f, 0f));
        SetCurve(clip, PathDroop, "m_LocalRotation", 'z', Key(0f, 0f), Key(1f, 0f));
        SetCurve(clip, PathDroop, "m_LocalRotation", 'w', Key(0f, 1f), Key(0.6f, w), Key(1f, w));
        return Save(clip);
    }

    // ── 컨트롤러 ────────────────────────────────────────────────

    private static AnimatorController BuildController(AnimationClip deploy, AnimationClip active, AnimationClip starved)
    {
        string path = Dir + "/TowerCommon.controller";
        AssetDatabase.DeleteAsset(path);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Starved", AnimatorControllerParameterType.Bool);

        var sm = controller.layers[0].stateMachine;

        var sDeploy = sm.AddState("Deploy");
        sDeploy.motion = deploy;
        var sActive = sm.AddState("Active");
        sActive.motion = active;
        var sStarved = sm.AddState("Starved");
        sStarved.motion = starved;

        sm.defaultState = sDeploy;

        // 등장이 끝나면 기본 자세로 — 파라미터 없이 클립 길이로 넘긴다
        var toActive = sDeploy.AddTransition(sActive);
        toActive.hasExitTime = true;
        toActive.exitTime = 1f;
        toActive.duration = 0.05f;

        var toStarved = sActive.AddTransition(sStarved);
        toStarved.hasExitTime = false;
        toStarved.duration = 0.35f;
        toStarved.AddCondition(AnimatorConditionMode.If, 0f, "Starved");

        var backToActive = sStarved.AddTransition(sActive);
        backToActive.hasExitTime = false;
        backToActive.duration = 0.25f;
        backToActive.AddCondition(AnimatorConditionMode.IfNot, 0f, "Starved");

        // 배치 도중에 보급이 끊긴 경우에도 처짐으로 넘어갈 수 있어야 한다
        var deployToStarved = sDeploy.AddTransition(sStarved);
        deployToStarved.hasExitTime = true;
        deployToStarved.exitTime = 1f;
        deployToStarved.duration = 0.2f;
        deployToStarved.AddCondition(AnimatorConditionMode.If, 0f, "Starved");

        EditorUtility.SetDirty(controller);
        return controller;
    }

    // ── 도우미 ──────────────────────────────────────────────────

    private static AnimationClip NewClip(string name, bool loop)
    {
        var clip = new AnimationClip { name = name, frameRate = 60f };
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    private static Keyframe Key(float t, float v) => new Keyframe(t, v);

    private static void SetCurve(AnimationClip clip, string path, string property, char component,
                                 params Keyframe[] keys)
    {
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < curve.length; i++)
            curve.SmoothTangents(i, 0f); // 부드럽게 — 기본 선형은 등장 연출이 뚝뚝 끊겨 보인다

        var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), $"{property}.{component}");
        AnimationUtility.SetEditorCurve(clip, binding, curve);
    }

    private static void SetIdentityRotation(AnimationClip clip, string path)
    {
        SetCurve(clip, path, "m_LocalRotation", 'x', Key(0f, 0f), Key(0.5f, 0f));
        SetCurve(clip, path, "m_LocalRotation", 'y', Key(0f, 0f), Key(0.5f, 0f));
        SetCurve(clip, path, "m_LocalRotation", 'z', Key(0f, 0f), Key(0.5f, 0f));
        SetCurve(clip, path, "m_LocalRotation", 'w', Key(0f, 1f), Key(0.5f, 1f));
    }

    private static AnimationClip Save(AnimationClip clip)
    {
        string path = $"{Dir}/{clip.name}.anim";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(clip, path);
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
