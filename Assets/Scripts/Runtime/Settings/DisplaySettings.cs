using UnityEngine;

/// <summary>
/// 그래픽 설정의 저장·적용 — 설정 화면(SettingsPanelView)의 그래픽 탭이 쓰는 뒷단.
///
/// 소리(AudioSaveSystem)와 저장 방식이 다른 이유: 소리는 값이 셋뿐인 실수라 JSON 파일이
/// 읽기 편하지만, 그래픽은 유니티가 이미 자기 상태로 들고 있는 것(품질 레벨·화면 모드·
/// 수직동기)이라 우리가 저장하는 건 "다음 실행 때 되돌릴 숫자" 세 개뿐이다.
/// 그 정도에 파일 포맷을 하나 더 만들 값이 없어 PlayerPrefs를 쓴다.
///
/// 설정 화면을 한 번도 열지 않아도 적용돼야 하므로 게임 시작 때 <see cref="Apply"/>가 돈다 —
/// 화면을 열 때 적용하는 구조였다면 저장해 둔 값이 창을 열기 전까지 무시된다.
/// </summary>
public static class DisplaySettings
{
    const string KeyQuality = "gfx.quality";
    const string KeyFullscreen = "gfx.fullscreen";
    const string KeyVSync = "gfx.vsync";

    /// <summary>품질 레벨 — QualitySettings.names의 인덱스.</summary>
    public static int QualityLevel
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(KeyQuality, QualitySettings.GetQualityLevel()),
                           0, Mathf.Max(0, QualitySettings.names.Length - 1));
        set
        {
            PlayerPrefs.SetInt(KeyQuality, value);
            PlayerPrefs.Save();
            // applyExpensiveChanges: false — 텍스처 해상도 같은 무거운 변경을 미뤄
            // 슬라이드하듯 레벨을 훑을 때 프레임이 끊기지 않게 한다
            QualitySettings.SetQualityLevel(value, applyExpensiveChanges: false);
        }
    }

    public static bool Fullscreen
    {
        get => PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;
        set
        {
            PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0);
            PlayerPrefs.Save();
            // 에디터의 Game 뷰는 창 모드를 무시한다 — 빌드에서만 눈에 보인다
            Screen.fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }
    }

    public static bool VSync
    {
        get => PlayerPrefs.GetInt(KeyVSync, QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
        set
        {
            PlayerPrefs.SetInt(KeyVSync, value ? 1 : 0);
            PlayerPrefs.Save();
            QualitySettings.vSyncCount = value ? 1 : 0;
        }
    }

    /// <summary>저장된 값을 유니티에 그대로 밀어 넣는다. 게임 시작 때 한 번 돈다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Apply()
    {
        QualitySettings.SetQualityLevel(QualityLevel, applyExpensiveChanges: false);
        QualitySettings.vSyncCount = VSync ? 1 : 0;

        // 화면 모드는 에디터에서 건드리지 않는다 — Game 뷰가 전체화면으로 튀어나오면
        // 에디터 조작이 막힌다. 빌드에서만 적용한다.
#if !UNITY_EDITOR
        Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
#endif
    }

    /// <summary>기본값으로 되돌린다 — 품질은 프로젝트 기본 레벨, 전체화면 켬, 수직동기 켬.</summary>
    public static void ResetToDefaults()
    {
        QualityLevel = Mathf.Clamp(QualitySettings.names.Length / 2, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
        Fullscreen = true;
        VSync = true;
    }
}
