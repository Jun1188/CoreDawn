using UnityEngine;
namespace CoreDawn.Sound
{
    public class SoundTester : MonoBehaviour
    {
        [Header("=== 3D Sound Test Setup ===")]
        [SerializeField] private Transform target3DObject; // 3D 소리가 들릴 대상 위치
        [SerializeField] private AudioClip customClip;     // 직접 전달 방식 테스트용 클립

        private bool isPaused = false;

        private void Update()
        {
            // 1. BGM 전환 테스트
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                Debug.Log("[Test] Main BGM 재생");
                SoundManager.Instance.PlayBGM(BGMType.Main, 1.0f);
            }
            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                Debug.Log("[Test] Battle BGM 재생 (크로스페이드)");
                SoundManager.Instance.PlayBGM(BGMType.Battle, 1.0f);
            }

            // 2. Enum 공통 SFX 테스트
            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                Debug.Log("[Test] 공통 효과음 (Construct)");
                SoundManager.Instance.PlayCommon("construct");
            }

            // 3. 클립 직접 전달 SFX 테스트 (피치 랜더마이징)
            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                if (customClip != null)
                {
                    Debug.Log("[Test] 직접 전달 SFX");
                    SoundManager.Instance.PlaySFX(customClip, 1.0f, randomizePitch: true);
                }
                else
                {
                    Debug.LogWarning("Custom Clip에 오디오 클립을 넣어주세요!");
                }
            }

            // 4. 3D 공간 사운드 + 풀링 테스트
            if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                Vector3 spawnPos = target3DObject != null ? target3DObject.position : transform.position;
                Debug.Log($"[Test] 3D 사운드 재생 위치: {spawnPos}");
                SoundManager.Instance.Play3DSFX(customClip, spawnPos);
            }

            // 5. Pause 스냅샷 (Lowpass 먹먹함) 연출 테스트
            if (Input.GetKeyDown(KeyCode.Space))
            {
                isPaused = !isPaused;
                Debug.Log($"[Test] Pause Effect: {isPaused}");
                SoundManager.Instance.SetPauseEffect(isPaused, 0.3f);
            }
        }

        // 화면 왼쪽 상단에 가이드 표시 (OnGUI)
        private void OnGUI()
        {
            GUI.Box(new Rect(10, 10, 300, 180), "Sound System Test Controls");
            GUI.Label(new Rect(20, 35, 280, 20), "[1] Key : Play Main BGM");
            GUI.Label(new Rect(20, 60, 280, 20), "[2] Key : Play Battle BGM (Crossfade)");
            GUI.Label(new Rect(20, 85, 280, 20), "[3] Key : Play Common SFX (Enum)");
            GUI.Label(new Rect(20, 110, 280, 20), "[4] Key : Play Direct SFX (Random Pitch)");
            GUI.Label(new Rect(20, 135, 280, 20), "[5] Key : Play 3D SFX (Position)");
            GUI.Label(new Rect(20, 160, 280, 20), "[Space] : Toggle Pause Sound Effect");
        }
    }
}
