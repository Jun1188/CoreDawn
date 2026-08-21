using UnityEngine;

/// <summary>
/// 카메라 흔들림(Camera Shake) 매니저.
/// 대상은 <b>ShakeHolder</b> — Main Camera 와 Weapon_Holder 의 공통 부모다.
/// 카메라만 흔들면 무기는 제자리라 총이 조준점에 대해 덜덜거려 보인다.
/// 공통 부모를 흔들어야 총은 화면에 붙어 있고 세상이 흔들린다.
/// 이 스크립트는 그 노드의 localPosition/Rotation 을 (0,0,0) 기준으로 단독 소유한다.
/// </summary>
public class CameraShakeManager : MonoBehaviour
{
    public static CameraShakeManager Instance { get; private set; }

    [Header("대상 (ShakeHolder — 카메라·무기의 공통 부모)")]
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
        /// <summary>수직 성분을 위쪽으로 모는 비율(0=대칭, 1=전부 위). 반동성 임펄스용.</summary>
        public float upBias;
    }

    private struct ActiveImpulse
    {
        public float posAmplitude;
        public float rotAmplitude;
        public float remainingTime;
        public float totalDuration;
        public float frequency;
        public float seed;
        public float upBias;
    }

    [Header("추종")]
    [Tooltip("계산된 흔들림을 따라가는 속도. 프레임 사이의 점프를 메워 '탁탁' 끊기는 것을 막는다.")]
    public float followSharpness = 28f;

    private readonly ActiveImpulse[] impulsePool = new ActiveImpulse[8];
    private int impulseCount = 0;
    private float perlinTime = 0f;

    // 화면에 실제로 적용 중인 값 — 목표(totalPos/Rot)를 스프링으로 뒤쫓는다
    private Vector3 shownPos;
    private Vector3 shownRot;

    /// <summary>현재 적용 중인 셰이크 — 조준 중 무기를 셰이크에 잠그는 <see cref="WeaponShakeFollow"/>가 읽는다.</summary>
    public Vector3 CurrentPositionOffset => shownPos;
    public Quaternion CurrentRotationOffset => Quaternion.Euler(shownRot);

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

        float dt = Time.deltaTime;

        // 흔들림이 꺼져있거나 진행 중인 임펄스가 없으면 원점으로 — 다만 <b>끌어당겨서</b> 돌아온다.
        // 즉시 0으로 스냅하면 마지막 임펄스가 끝나는 프레임에 화면이 한 번 튄다.
        if (globalIntensityScale <= 0f || impulseCount == 0)
        {
            Settle(Vector3.zero, Vector3.zero, dt);
            return;
        }

        perlinTime += dt;

        Vector3 totalPos = Vector3.zero;
        Vector3 totalRot = Vector3.zero;

        int alive = 0;
        for (int i = 0; i < impulseCount; i++)
        {
            ref ActiveImpulse imp = ref impulsePool[i];
            imp.remainingTime -= Time.deltaTime;
            if (imp.remainingTime <= 0f) continue;

            // 감쇠를 곡선으로 — 선형(t)이면 끝날 때까지 진폭이 남아 있다가 뚝 끊긴다
            float t = imp.remainingTime / imp.totalDuration;
            t = t * t * (3f - 2f * t);

            // 프레임률이 감당하는 진동수로 제한 — 넘어서면 진동이 아니라 덜컹거림이 된다
            float freq = MotionSpring.Visible(imp.frequency, dt) * perlinTime + imp.seed;

            float px = (Mathf.PerlinNoise(freq, imp.seed + 13.7f) - 0.5f) * 2f;
            float py = (Mathf.PerlinNoise(freq + 31.3f, imp.seed + 47.1f) - 0.5f) * 2f;
            float pz = (Mathf.PerlinNoise(freq + 59.9f, imp.seed + 83.3f) - 0.5f) * 2f;

            // 위 편향 — 발사 반동은 위로 쏠려야 한다. 대칭 노이즈면 위로 차는 반동을
            // 무작위로 상쇄해 전방향 떨림처럼 읽힌다. 위치 y는 +가 위, 회전 x는 −가 위.
            float upY = Mathf.Lerp(py, Mathf.Abs(py), imp.upBias);
            float upPitch = Mathf.Lerp(px, -Mathf.Abs(px), imp.upBias);

            totalPos += new Vector3(px, upY, 0f) * (imp.posAmplitude * t);
            totalRot += new Vector3(upPitch, py, pz) * (imp.rotAmplitude * t);

            impulsePool[alive++] = imp;
        }
        impulseCount = alive;

        totalPos = Vector3.ClampMagnitude(totalPos * globalIntensityScale, maxPositionOffset);
        totalRot = Vector3.ClampMagnitude(totalRot * globalIntensityScale, maxRotationOffset);

        Settle(totalPos, totalRot, dt);
    }

    /// <summary>
    /// 목표 흔들림을 스프링으로 뒤쫓아 적용한다.
    ///
    /// 펄린 노이즈를 그대로 트랜스폼에 꽂으면 카메라가 순간이동하듯 튄다. 12Hz 진동을
    /// 60fps로 샘플링하면 한 주기가 <b>다섯 프레임</b>이라, 값 자체는 연속이어도 화면에는
    /// 계단으로 찍히기 때문이다. 여기서 한 겹 걸러야 사이가 메워진다.
    /// </summary>
    private void Settle(Vector3 targetPos, Vector3 targetRot, float dt)
    {
        shownPos = MotionSpring.Damp(shownPos, targetPos, followSharpness, dt);
        shownRot = MotionSpring.Damp(shownRot, targetRot, followSharpness, dt);

        // 카메라는 오직 "흔들림"만을 위해 움직이므로 항상 0,0,0을 기준으로 동작합니다.
        cameraTransform.localPosition = shownPos;
        cameraTransform.localRotation = Quaternion.Euler(shownRot);
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
            seed = Random.Range(0f, 100f),
            upBias = Mathf.Clamp01(req.upBias)
        };
    }

    [Header("발사 셰이크")]
    [Tooltip("발사 셰이크의 수직 성분을 위로 모는 비율. 0=전방향 대칭, 1=수직은 전부 위.")]
    [Range(0f, 1f)] public float shootUpBias = 0.75f;

    public void ShakeOnPlayerShoot(float scale)
    {
        float n = Mathf.Clamp01(scale * 0.1f);
        Impulse(new ImpulseRequest
        {
            positionAmplitude = Mathf.Lerp(0.01f, 0.045f, n),
            rotationAmplitude = Mathf.Lerp(0.3f, 0.9f, n),
            duration = Mathf.Lerp(0.15f, 0.35f, n),
            frequency = 10f,
            upBias = shootUpBias
        });
    }

}