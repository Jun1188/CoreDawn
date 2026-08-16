using UnityEngine;

/// <summary>
/// 카메라 흔들림(Camera Shake) 매니저.
/// [구조 분리 방식 적용] 
/// Main Camera의 부모(Pivot)는 FPSController가 제어하고,
/// 이 스크립트는 Main Camera 자신의 localPosition/Rotation을 (0,0,0) 기준으로 제어합니다.
/// </summary>
public class CameraShakeManager : MonoBehaviour
{
    public static CameraShakeManager Instance { get; private set; }

    [Header("대상 (Main Camera)")]
    public Transform cameraTransform;

    [Header("글로벌 제한")]
    public float maxPositionOffset = 0.06f;
    public float maxRotationOffset = 1.2f;
    [Range(0f, 1f)] public float globalIntensityScale = 1f;

    public struct ImpulseRequest
    {
        public float positionAmplitude;
        public float rotationAmplitude;
        public float duration;
        public float frequency;
    }

    private struct ActiveImpulse
    {
        public float posAmplitude;
        public float rotAmplitude;
        public float remainingTime;
        public float totalDuration;
        public float frequency;
        public float seed;
    }

    private readonly ActiveImpulse[] impulsePool = new ActiveImpulse[8];
    private int impulseCount = 0;
    private float perlinTime = 0f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(this); // 컴포넌트만 제거 — 카메라 리그에 붙어 있어 오브젝트째 지우면 위험
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void LateUpdate()
    {
        if (cameraTransform == null) return;

        // 흔들림이 꺼져있거나 진행 중인 임펄스가 없으면, 카메라를 완벽한 원점(0,0,0)으로 고정
        if (globalIntensityScale <= 0f || impulseCount == 0)
        {
            cameraTransform.localPosition = Vector3.zero;
            cameraTransform.localRotation = Quaternion.identity;
            return;
        }

        perlinTime += Time.deltaTime;

        Vector3 totalPos = Vector3.zero;
        Vector3 totalRot = Vector3.zero;

        int alive = 0;
        for (int i = 0; i < impulseCount; i++)
        {
            ref ActiveImpulse imp = ref impulsePool[i];
            imp.remainingTime -= Time.deltaTime;
            if (imp.remainingTime <= 0f) continue;

            float t = imp.remainingTime / imp.totalDuration;
            float freq = imp.frequency * perlinTime + imp.seed;

            float px = (Mathf.PerlinNoise(freq, imp.seed + 13.7f) - 0.5f) * 2f;
            float py = (Mathf.PerlinNoise(freq + 31.3f, imp.seed + 47.1f) - 0.5f) * 2f;
            float pz = (Mathf.PerlinNoise(freq + 59.9f, imp.seed + 83.3f) - 0.5f) * 2f;

            totalPos += new Vector3(px, py, 0f) * (imp.posAmplitude * t);
            totalRot += new Vector3(px, py, pz) * (imp.rotAmplitude * t);

            impulsePool[alive++] = imp;
        }
        impulseCount = alive;

        totalPos = Vector3.ClampMagnitude(totalPos * globalIntensityScale, maxPositionOffset);
        totalRot = Vector3.ClampMagnitude(totalRot * globalIntensityScale, maxRotationOffset);

        // 카메라는 오직 "흔들림"만을 위해 움직이므로 항상 0,0,0을 기준으로 동작합니다.
        cameraTransform.localPosition = totalPos;
        cameraTransform.localRotation = Quaternion.Euler(totalRot);
    }

    public void Impulse(ImpulseRequest req)
    {
        if (globalIntensityScale <= 0f) return;
        if (impulseCount >= impulsePool.Length) return;

        impulsePool[impulseCount++] = new ActiveImpulse
        {
            posAmplitude = req.positionAmplitude,
            rotAmplitude = req.rotationAmplitude,
            remainingTime = req.duration,
            totalDuration = req.duration,
            frequency = req.frequency,
            seed = Random.Range(0f, 100f)
        };
    }

    public void ShakeOnPlayerShoot(float scale)
    {
        float n = Mathf.Clamp01(scale / 10f);
        Impulse(new ImpulseRequest
        {
            positionAmplitude = Mathf.Lerp(0.01f, 0.045f, n),
            rotationAmplitude = Mathf.Lerp(0.3f, 0.9f, n),
            duration = Mathf.Lerp(0.15f, 0.35f, n),
            frequency = 12f
        });
    }

}