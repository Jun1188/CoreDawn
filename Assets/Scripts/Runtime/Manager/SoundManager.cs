using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Audio Sources")]
    [SerializeField] private AudioSource mainSource;
    [SerializeField] private AudioSource battleSource;
    [Header("Interaction SFX")]
    [SerializeField] private AudioClip constructSFX; // 설치 소리
    [SerializeField] private AudioClip destroySFX; // 제거 소리
    [SerializeField] private AudioClip clickSFX; // 클릭 소리
    [SerializeField] private AudioClip footstepSFX; // 발자국 소리
    [Header("BGM")]
    [SerializeField] private AudioClip mainBGM; // 메인 배경음악
    [SerializeField] private AudioClip battleBGM; // 전투 배경음악 소스 

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void PlayConstructSFX() => mainSource.PlayOneShot(constructSFX);
    public void PlayDestroySFX() => mainSource.PlayOneShot(destroySFX);
}