using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
public enum CommonSFX
{
    Click,          // UI 버튼 클릭
    Hover,          // UI 마우스 올림
    Construct,      // 건물 설치
    Destroy,        // 건물 파괴
    Warning,        // 경고/에러음
    LevelUp         // 해금/승급
}
public enum BGMType
{
    Main,           // 메인 배경음악
    Battle,         // 전투 배경음악
    Boss,           // 보스전
    Factory         // 공장 내부음
}

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    /// <summary>
    /// 씬에 SoundManager가 없으면 Resources의 프리팹으로 하나 띄운다.
    ///
    /// 이게 없으면 SoundTest 씬 밖에서는 소리가 전혀 나지 않는다 — 지금 SoundManager를
    /// 들고 있는 씬은 그 하나뿐이다. 씬마다 수동으로 넣게 하면 새 씬을 만들 때마다 잊어버리고,
    /// 조용한 이유를 찾느라 시간을 쓴다. 씬에 이미 있으면 Awake의 중복 검사가 이 사본을
    /// 정리하므로 기존 씬의 설정이 항상 우선한다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (Instance != null) return;
        if (FindAnyObjectByType<SoundManager>() != null) return;

        var prefab = Resources.Load<GameObject>("SoundManager");
        if (prefab == null)
        {
            Debug.LogWarning("[SoundManager] Resources/SoundManager 프리팹이 없어 소리가 재생되지 않습니다.");
            return;
        }
        Instantiate(prefab).name = prefab.name;
    }

    [Serializable]
    public struct CommonSoundData
    {
        public CommonSFX sfxType;
        public AudioClip clip;
    }
    [Serializable]
    public struct BGMData
    {
        public BGMType bgmType;
        public AudioClip clip;
    }
    [Header("=== Audio Mixer & Groups ===")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private AudioMixerGroup bgmMixerGroup;
    [SerializeField] private AudioMixerGroup sfxMixerGroup;

    [Header("=== Audio Sources ===")]
    [SerializeField] private AudioSource bgmSourceA;
    [SerializeField] private AudioSource bgmSourceB;
    [SerializeField] private AudioSource sfxSource;

    [Header("=== Common Sound Library (Enum/Dictionary) ===")]
    [SerializeField] private List<CommonSoundData> commonSoundList;
    [Header("=== BGM Library (Enum/Dictionary) ===")]
    [SerializeField] private List<BGMData> bgmList;

    [Header("=== 3D Audio Pooling ===")]
    [SerializeField] private int poolSize = 10;
    private readonly List<AudioSource> sfx3DPool = new();
    private int currentPoolIndex = 0;
    [Header("=== AudioMixer Snapshots ===")]
    [SerializeField] private AudioMixerSnapshot normalSnapshot;
    [SerializeField] private AudioMixerSnapshot pausedSnapshot;

    // 런타임에 $O(1)$ 속도로 찾기 위한 딕셔너리
    private readonly Dictionary<CommonSFX, AudioClip> commonSFXDict = new Dictionary<CommonSFX, AudioClip>();
    private readonly Dictionary<BGMType, AudioClip> bgmDict = new Dictionary<BGMType, AudioClip>();

    // 동일 효과음 쿨타임 관리용 딕셔너리 (귀 테러 방지)
    private readonly Dictionary<AudioClip, float> sfxCooldownDict = new Dictionary<AudioClip, float>();
    private const float DEFAULT_COOLDOWN = 0.05f;

    // BGM 크로스페이드용
    private AudioSource activeBgmSource;
    private Coroutine bgmCrossfadeCoroutine;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitAudioSources();
            InitCommonSoundDictionary(); // Inspector 리스트를 Dictionary로 변환
            InitBGMDictionary(); // BGM 리스트를 Dictionary로 변환
            Init3DPool(); // 3D 사운드 풀링 초기화
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // 저장해 둔 볼륨을 먼저 믹서에 넣는다 — 이게 없으면 설정 창(SettingsPanelView)을
        // 한 번 열기 전까지 지난번에 줄여 둔 볼륨이 무시되고 기본값으로 소리가 난다.
        // 믹서 파라미터는 Awake 시점엔 아직 안 잡힐 수 있어 Start에서 민다.
        ApplySavedVolumes();

        // 게임 시작 시 메인 BGM을 1.5초 동안 은은하게 재생
        PlayBGM(BGMType.Main, 1.5f);
    }

    /// <summary>저장된 볼륨(audio_settings.json)을 믹서에 반영한다.</summary>
    private void ApplySavedVolumes()
    {
        AudioSettingsData settings = AudioSaveSystem.LoadSettings();
        SetMasterVolume(settings.masterVolume);
        SetBGMVolume(settings.bgmVolume);
        SetSFXVolume(settings.sfxVolume);
    }

    private void InitAudioSources()
    {
        if (bgmSourceA != null && bgmMixerGroup != null) bgmSourceA.outputAudioMixerGroup = bgmMixerGroup;
        if (bgmSourceB != null && bgmMixerGroup != null) bgmSourceB.outputAudioMixerGroup = bgmMixerGroup;
        if (sfxSource != null && sfxMixerGroup != null) sfxSource.outputAudioMixerGroup = sfxMixerGroup;

        bgmSourceA.loop = true;
        bgmSourceB.loop = true;
        activeBgmSource = bgmSourceA;
    }

    /// <summary> Inspector에 등록한 CommonSoundData 리스트를 딕셔너리로 바인딩 </summary>
    private void InitCommonSoundDictionary()
    {
        commonSFXDict.Clear();
        foreach (var data in commonSoundList)
        {
            if (data.clip != null && !commonSFXDict.ContainsKey(data.sfxType))
            {
                commonSFXDict.Add(data.sfxType, data.clip);
            }
        }
    }
    private void InitBGMDictionary()
    {
        bgmDict.Clear();
        foreach (var data in bgmList)
        {
            if (data.clip != null && !bgmDict.ContainsKey(data.bgmType))
            {
                bgmDict.Add(data.bgmType, data.clip);
            }
        }
    }
    private void Init3DPool()
    {
        GameObject poolParent = new GameObject("3D_SFX_Pool");
        poolParent.transform.SetParent(transform);

        for (int i = 0; i < poolSize; i++)
        {
            AudioSource src = poolParent.AddComponent<AudioSource>();
            src.spatialBlend = 1.0f; // 100% 3D 사운드 (거리감/방향감 적용)
            src.minDistance = 2f;    // 소리가 최고로 크게 들리는 거리
            src.maxDistance = 25f;   // 소리가 완전히 안 들리는 거리
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.outputAudioMixerGroup = sfxMixerGroup; // SFX 믹서 그룹 연결
            sfx3DPool.Add(src);
        }
    }
    // ========================================================================
    // 객체 직접 호출용
    // ========================================================================

    /// <summary> 원본 AudioClip을 직접 받아서 재생 </summary>
    public void PlaySFX(AudioClip clip, float volume = 1.0f, bool randomizePitch = false)
    {
        if (clip == null || sfxSource == null) return;

        // 쿨타임 검사 (연타로 소리 찢어짐 방지)
        if (sfxCooldownDict.TryGetValue(clip, out float lastPlayTime))
        {
            if (Time.unscaledTime - lastPlayTime < DEFAULT_COOLDOWN) return;
        }
        sfxCooldownDict[clip] = Time.unscaledTime;

        // 미세 피치 조절 (발소리/총소리 피로감 완화)
        sfxSource.pitch = randomizePitch ? UnityEngine.Random.Range(0.9f, 1.1f) : 1.0f;
        sfxSource.PlayOneShot(clip, volume);
    }
    /// <summary> 지정한 3D 좌표 위치에서 효과음 재생 </summary>
    public void Play3DSFX(AudioClip clip, Vector3 position, float volume = 1.0f)
    {
        if (clip == null || sfx3DPool.Count == 0) return;

        // 순환형 풀링 (가장 오래된 AudioSource 재사용)
        AudioSource source = sfx3DPool[currentPoolIndex];
        currentPoolIndex = (currentPoolIndex + 1) % poolSize;

        source.transform.position = position;
        source.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
        source.PlayOneShot(clip, volume);
    }

    // ========================================================================
    // 공통 UI/시스템 소리 딕셔너리 호출용
    // ========================================================================

    /// <summary> Enum 키값으로 딕셔너리에서 찾아 재생 </summary>
    public void PlayCommonSFX(CommonSFX sfxType, float volume = 1.0f)
    {
        if (commonSFXDict.TryGetValue(sfxType, out AudioClip clip))
        {
            PlaySFX(clip, volume, false);
        }
        else
        {
            Debug.LogWarning($"[SoundManager] {sfxType}에 해당하는 공통 사운드가 등록되지 않았습니다.");
        }
    }
    public void PlayBGM(BGMType bgmType, float fadeDuration = 1.0f)
    {
        if (bgmDict.TryGetValue(bgmType, out AudioClip clip))
        {
            // 기존에 만들어둔 크로스페이드 재생 함수 재활용!
            PlayBGM(clip, fadeDuration); 
        }
        else
        {
            Debug.LogWarning($"[SoundManager] {bgmType}에 해당하는 BGM이 등록되지 않았습니다.");
        }
    }
    // ========================================================================
    // BGM 크로스페이드 및 Mixer 볼륨 조절
    // ========================================================================
    
    public void PlayBGM(AudioClip newBgm, float fadeDuration = 1.0f)
    {
        if (newBgm == null || activeBgmSource.clip == newBgm) return;

        if (bgmCrossfadeCoroutine != null) StopCoroutine(bgmCrossfadeCoroutine);
        bgmCrossfadeCoroutine = StartCoroutine(Co_CrossfadeBGM(newBgm, fadeDuration));
    }

    private IEnumerator Co_CrossfadeBGM(AudioClip newBgm, float duration)
    {
        AudioSource fadeOutSource = activeBgmSource;
        AudioSource fadeInSource = (activeBgmSource == bgmSourceA) ? bgmSourceB : bgmSourceA;

        fadeInSource.clip = newBgm;
        fadeInSource.volume = 0f;
        fadeInSource.Play();

        float timer = 0f;
        float startVolume = fadeOutSource.volume;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float progress = timer / duration;

            fadeOutSource.volume = Mathf.Lerp(startVolume, 0f, progress);
            fadeInSource.volume = Mathf.Lerp(0f, 1f, progress);

            yield return null;
        }

        fadeOutSource.Stop();
        activeBgmSource = fadeInSource;
    }

    public void SetVolume(string parameterName, float linearValue)
    {
        if (audioMixer == null) return;
        float dB = Mathf.Log10(Mathf.Clamp(linearValue, 0.0001f, 1f)) * 20f;
        audioMixer.SetFloat(parameterName, dB);
    }
    /// <summary> 게임 일시정지 / 해제 시 사운드 연출 전환 </summary>
    public void SetPauseEffect(bool isPaused, float transitionTime = 0.2f)
    {
        if (isPaused)
            pausedSnapshot.TransitionTo(transitionTime);
        else
            normalSnapshot.TransitionTo(transitionTime);
    }

    public void SetMasterVolume(float val) => SetVolume("MasterVolume", val);
    public void SetBGMVolume(float val) => SetVolume("BGMVolume", val);
    public void SetSFXVolume(float val) => SetVolume("SFXVolume", val);

    private void OnApplicationFocus(bool hasFocus) => AudioListener.pause = !hasFocus;
}