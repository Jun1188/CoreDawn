#if UNITY_WEBGL
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the WebGL player on a conservative URP path.
/// The desktop Forward+ renderer, renderer features, and post-processing shaders
/// are not reliable on all WebGL 2 implementations.
/// </summary>
static class WebGLRenderCompatibility
{
    const string PipelineResource = "WebGLRenderPipeline";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure()
    {
        if (Application.isEditor)
            return;

        pipeline = Resources.Load<UniversalRenderPipelineAsset>(PipelineResource);
        if (pipeline == null)
        {
            Debug.LogError("[WebGL] Compatibility render pipeline was not found.");
            return;
        }

        QualitySettings.renderPipeline = pipeline;
        ApplyCameraSettings();
        SceneManager.sceneLoaded += OnSceneLoaded;

        // QualitySettings.renderPipeline은 "현재 품질 레벨"의 오버라이드일 뿐이다.
        // 설정 UI(DisplaySettings.QualityLevel)나 부팅 시 저장값 복원이 SetQualityLevel을
        // 부르면 그 레벨의 Forward+ customRenderPipeline이 되살아나므로, 레벨이 바뀔
        // 때마다 다시 덮어써야 한다.
        QualitySettings.activeQualityLevelChanged += OnQualityLevelChanged;
    }

    static UniversalRenderPipelineAsset pipeline;

    static void OnQualityLevelChanged(int _, int __)
    {
        QualitySettings.renderPipeline = pipeline;
        ApplyCameraSettings();
    }

    static void OnSceneLoaded(Scene _, LoadSceneMode __) => ApplyCameraSettings();

    static void ApplyCameraSettings()
    {
        foreach (var camera in Object.FindObjectsByType<Camera>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            camera.allowHDR = false;
            camera.allowMSAA = false;

            if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                continue;

            data.renderPostProcessing = false;
            data.SetRenderer(0);
        }
    }
}
#endif
