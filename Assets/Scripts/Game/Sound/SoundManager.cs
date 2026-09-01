using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using CoreDawn.Sound;
using CoreDawn.UI;
using CoreDawn.Data;

namespace CoreDawn.Sound
{
    public enum BGMType
    {
        Main,           // 메인 배경음악
        Battle,         // 전투 배경음악
        Boss,           // 보스전
        Factory         // 공장 내부음
    }

    [RequireComponent(typeof(UISoundHandler))]
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
        private readonly Dictionary<BGMType, AudioClip> bgmDict = new Dictionary<BGMType, AudioClip>();

        // 동일 효과음 쿨타임 관리용 딕셔너리 (귀 테러 방지)
        private readonly Dictionary<AudioClip, float> sfxCooldownDict = new Dictionary<AudioClip, float>();
        private const float DEFAULT_COOLDOWN = 0.05f;

        // 저장된 볼륨이 믹서에 자리 잡았다고 볼 조건 — 근거는 Co_ApplySavedVolumes 참고
        private const int VOLUME_SETTLED_FRAMES = 5;      // 연속 몇 프레임 그대로 남아야 하는가
        private const int VOLUME_APPLY_MAX_FRAMES = 300;  // 그때까지 못 잡으면 포기하고 경고 (프레임)

        // 믹서에 노출해 둔 볼륨 파라미터 — 이름이 하나라도 어긋나면 저장된 볼륨이 통째로 무시된다
        private static readonly string[] VolumeParameters = { "MasterVolume", "BGMVolume", "SFXVolume" };

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
            // 저장해 둔 볼륨을 믹서에 넣는다 — 이게 없으면 설정 창(SettingsPanelView)을
            // 한 번 열기 전까지 지난번에 줄여 둔 볼륨이 무시되고 기본값으로 소리가 난다.
            StartCoroutine(Co_ApplySavedVolumes());

            // 게임 시작 시 메인 BGM을 1.5초 동안 은은하게 재생
            PlayBGM(BGMType.Main, 1.5f);
        }

        /// <summary>
        /// 저장된 볼륨(audio_settings.json)을 믹서에 반영한다. 값이 실제로 자리 잡을 때까지 붙잡는다.
        ///
        /// 왜 한 번 넣고 끝내지 않는가 — 갓 로드된 AudioMixer는 SetFloat을 흘린다. 이 매니저는
        /// <see cref="EnsureInstance"/>가 씬 로드 직후에 띄우므로 Start가 곧 앱의 첫 프레임이고,
        /// 그 시점의 믹서는 아직 자기 시작 스냅샷(MainMixer의 Normal)을 밀어 넣는 중이라
        /// 방금 넣은 값을 도로 덮어쓸 수 있다. 그러면 "설정 값은 남아 있는데 소리는 기본값"이 되고,
        /// 설정 창에서 슬라이더를 한 번 건드려야(=한참 뒤의 SetFloat) 비로소 반영된다.
        /// 에디터에서는 믹서가 이미 메모리에 떠 있어 재현되지 않지만, 빌드에서는 이때 로드된다.
        ///
        /// SetFloat의 반환값은 믿을 수 없다 — 덮어쓰기는 그 뒤에 오므로 true를 받아도 남지 않는다.
        /// 그래서 GetFloat으로 되읽어, 넣은 값이 <see cref="VOLUME_SETTLED_FRAMES"/>프레임 연속으로
        /// 그대로 남아 있을 때만 자리 잡은 것으로 본다.
        ///
        /// 포기 기준은 초가 아니라 프레임이다 — 예전엔 2초를 재다가, 값은 첫 프레임부터 멀쩡히
        /// 들어가 있는데도 "믹서에 반영되지 않았습니다" 경고가 매 실행마다 떴다. 맵·플로우필드를
        /// 굽는 초반 몇 초는 한 프레임이 0.5초를 넘어서, 2초 안에 셀 프레임이 5개가 안 됐기 때문이다.
        /// 세는 단위가 프레임이니 재는 단위도 프레임이어야 한다.
        /// </summary>
        private IEnumerator Co_ApplySavedVolumes()
        {
            AudioSettingsData settings = AudioSaveSystem.LoadSettings();
            int settled = 0;
            int frames = 0;

            while (settled < VOLUME_SETTLED_FRAMES && frames < VOLUME_APPLY_MAX_FRAMES)
            {
                SetMasterVolume(settings.masterVolume);
                SetBGMVolume(settings.bgmVolume);
                SetSFXVolume(settings.sfxVolume);

                settled = IsVolumeApplied("MasterVolume", settings.masterVolume)
                       && IsVolumeApplied("BGMVolume", settings.bgmVolume)
                       && IsVolumeApplied("SFXVolume", settings.sfxVolume)
                        ? settled + 1
                        : 0;   // 한 번이라도 어긋나면 처음부터 다시 센다

                frames++;
                yield return null;   // 프레임 대기 — timeScale과 무관해 일시정지 중에도 진행된다
            }

            if (settled >= VOLUME_SETTLED_FRAMES) yield break;

            // 여기까지 왔으면 정말 이상한 것이니 무엇이 문제인지까지 짚어 준다.
            // "이름을 확인하라"는 노출 파라미터가 아예 없을 때만 맞는 말이다.
            string missing = FindMissingParameter();
            if (missing != null)
            {
                Debug.LogWarning($"[SoundManager] 저장된 볼륨을 넣을 수 없습니다 — MainMixer에 노출 파라미터 " +
                                 $"'{missing}'이(가) 없습니다. 믹서에서 해당 볼륨을 Expose 했는지 확인하세요.");
            }
            else
            {
                Debug.LogWarning($"[SoundManager] 저장된 볼륨이 {VOLUME_APPLY_MAX_FRAMES}프레임 안에 자리 잡지 못했습니다. " +
                                 "다른 코드가 매 프레임 볼륨을 덮어쓰고 있는지 확인하세요.");
            }
        }

        /// <summary>믹서에 없는 노출 파라미터 이름 — 전부 있으면 null. 믹서 자체가 없으면 그 사실을 돌려준다.</summary>
        private string FindMissingParameter()
        {
            if (audioMixer == null) return "(AudioMixer 미할당)";

            foreach (string parameterName in VolumeParameters)
            {
                if (!audioMixer.GetFloat(parameterName, out _)) return parameterName;
            }
            return null;
        }

        /// <summary>믹서에 실제로 그 볼륨이 들어가 있는가 — SetFloat의 반환값만으로는 알 수 없다.</summary>
        private bool IsVolumeApplied(string parameterName, float linearValue)
        {
            if (audioMixer == null) return false;
            if (!audioMixer.GetFloat(parameterName, out float dB)) return false;
            return Mathf.Abs(dB - ToDecibel(linearValue)) < 0.01f;
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
                // 소스마다 전용 오브젝트 — 한 오브젝트에 모으면 Transform을 공유해서,
                // 새 소리를 놓을 때마다 재생 중인 다른 소리까지 전부 그 좌표로 끌려간다.
                var holder = new GameObject($"3D_SFX_{i}");
                holder.transform.SetParent(poolParent.transform);
                AudioSource src = holder.AddComponent<AudioSource>();
                Setup3DSource(src);
                sfx3DPool.Add(src);
            }
        }

        /// <summary>
        /// 3D 효과음 소스의 공통 세팅 — 풀 밖의 개별 소스(총 재장전 등)도 이걸 거쳐야
        /// 감쇠 곡선과 SFX 믹서 그룹(볼륨 슬라이더)이 풀과 같아진다.
        /// </summary>
        public void Setup3DSource(AudioSource src)
        {
            src.spatialBlend = 1.0f; // 100% 3D 사운드 (거리감/방향감 적용)
            src.minDistance = 2f;    // 소리가 최고로 크게 들리는 거리
            src.maxDistance = 25f;   // 소리가 완전히 안 들리는 거리
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.outputAudioMixerGroup = sfxMixerGroup; // SFX 믹서 그룹 연결
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
        // 팩 소리 — 자리(SoundUse)를 재생한다. 클립은 카탈로그(sounds 섹션)에서 변형 하나를 고른다
        // ========================================================================

        /// <summary>
        /// 소리 자리 하나를 재생 — <paramref name="at"/>이 있고 자리가 spatial이면 3D, 아니면 2D.
        /// 자리가 null이면(정의에 그 자리가 없음) 조용히 지나간다 — 자리 이름 검증은 ViewSchema가 로드 때 한다.
        /// </summary>
        public void Play(SoundUse use, Vector3? at = null, float volumeScale = 1f)
        {
            if (use == null) return;
            var clip = ClipOf(use.Sound);
            if (clip == null) return;
            if (use.Spatial && at.HasValue) Play3DSFX(clip, at.Value, use.Volume * volumeScale);
            else PlaySFX(clip, use.Volume * volumeScale, false);
        }

        /// <summary>팩 최상위 sfx의 공용 자리(ui_click·construct·destroy·warning·item_pickup·mine…) — 구 CommonSFX enum.</summary>
        public void PlayCommon(string name, float volumeScale = 1f) => Play(ViewSchema.Common(name), null, volumeScale);

        static readonly HashSet<string> warnedSounds = new HashSet<string>();

        /// <summary>소리 id의 변형 클립 하나(무작위). 카탈로그에 없으면 한 번 경고하고 null.</summary>
        public static AudioClip ClipOf(string soundId)
        {
            if (string.IsNullOrEmpty(soundId)) return null;
            var clips = ViewCatalogSO.ClipsOf(soundId);
            if (clips == null || clips.Length == 0)
            {
                if (warnedSounds.Add(soundId)) Debug.LogWarning($"[SoundManager] 소리 '{soundId}'의 클립이 카탈로그에 없습니다 — 팩 sounds와 ViewCatalog 베이크를 확인하세요.");
                return null;
            }
            return clips[UnityEngine.Random.Range(0, clips.Length)];
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
                // 일시정지(timeScale 0) 중에 씬을 떠나도 페이드가 얼어붙지 않게 실시간으로 잰다
                timer += Time.unscaledDeltaTime;
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
            audioMixer.SetFloat(parameterName, ToDecibel(linearValue));
        }

        /// <summary>슬라이더의 0~1을 믹서가 쓰는 dB로. 0은 믹서 최저치인 -80dB로 눌러 붙인다.</summary>
        private static float ToDecibel(float linearValue)
            => Mathf.Log10(Mathf.Clamp(linearValue, 0.0001f, 1f)) * 20f;

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
}
