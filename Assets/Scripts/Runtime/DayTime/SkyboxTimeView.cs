using UnityEngine;

/// <summary>
/// Dynamic Stylized Skybox(Evets)를 우리 낮/밤 주기에 물리는 뷰 — 로직 없음, 읽기 전용.
///
/// 역할 분담:
///   이 컴포넌트           — 해·달 트랜스폼 회전 + 디렉셔널 라이트(회전·색·강도) + 앰비언트.
///                           시간은 전부 TimeManager.Cycle.NormalizedTimeOfDay에서 온다
///                           (0=일출, 0.25=정오, 0.5=일몰, 0.75=자정).
///   에셋 SkyboxController — 해·달 트랜스폼을 셰이더 전역값으로 동기화(그 일만 남긴다).
///                           자체 시계(isRotatingSun)와 라이트 제어(isMatchingDirectionalLight...)는
///                           꺼야 한다 — 켜두면 이쪽과 서로 회전을 덮어쓴다.
///
/// 에셋을 고치지 않는 이유: 스토어 에셋은 업데이트로 통째로 교체될 수 있다.
/// 접점을 "트랜스폼 돌려주기"로 좁히면 에셋 갱신에도 우리 쪽은 깨질 일이 없다.
/// </summary>
[ExecuteAlways]
public class SkyboxTimeView : MonoBehaviour
{
    [Header("하늘 (Sky 프리팹의 자식)")]
    [Tooltip("해 트랜스폼 — -forward가 해의 방향이 된다(에셋 규약).")]
    [SerializeField] Transform sun;
    [Tooltip("달 트랜스폼. 해의 정반대에 둔다 — 해가 지면 달이 뜬다.")]
    [SerializeField] Transform moon;

    [Header("조명 — 해·달 라이트 두 개")]
    [Tooltip("햇빛 디렉셔널 라이트. 항상 해 방향을 따르고, 해가 지면 강도가 0으로 죽는다. " +
             "라이트 하나를 180° 돌려 겸용하면 교대 순간에 조명이 홱 도는 문제가 있어 둘로 나눈다. " +
             "URP는 어느 순간이든 더 밝은 쪽을 주광(그림자 담당)으로 골라 준다.")]
    [SerializeField] Light sunLight;

    [Tooltip("달빛 디렉셔널 라이트. 항상 달 방향을 따르고, 달이 지면 강도가 0으로 죽는다.")]
    [SerializeField] Light moonLight;

    [Tooltip("해가 뜨고 지는 방위(Y 회전).")]
    [SerializeField] float sunYaw = 30f;

    [Tooltip("달 궤도면을 해와 어긋나게 하는 방위 오프셋(도). 0이면 달이 항상 해의 정반대라 " +
             "에셋의 개기월식 판정(정렬 1.1° 이내)이 걸려 달이 지기 직전 시커멓게 먹힌다. " +
             "반대로 크게 어긋내면 달에 위상(이지러짐)이 생긴다 — 달은 해와 정반대일 때만 보름달이다. " +
             "월식 원뿔(1.1°) 바로 밖, 위상은 눈에 안 띄는 4°가 적정.")]
    [SerializeField] float moonOrbitYawOffset = 4f;

    [Tooltip("물량제 밤 달 궤적의 보간 강도(초당). 2단 지수 감쇠라 처치로 목표가 튀어도 " +
             "속도가 0에서 천천히 올라갔다가, 목표에 가까워지면 다시 천천히 줄며 멎는다. " +
             "낮출수록 더 느긋하게 움직인다.")]
    [SerializeField] float moonEase = 0.35f;

    [Tooltip("시간(0~1)에 따른 햇빛 색. 0=일출, 0.25=정오, 0.5=일몰.")]
    [SerializeField] Gradient lightColor;

    [Tooltip("시간(0~1)에 따른 햇빛 강도 — 밤 구간 값은 해 고도 게이트에 어차피 깎이므로 무의미.")]
    [SerializeField] AnimationCurve lightIntensity;

    [Tooltip("달빛 색.")]
    [SerializeField] Color moonColor = new(0.55f, 0.65f, 0.90f);

    [Tooltip("달빛 강도(달이 높이 떴을 때).")]
    [SerializeField] float moonIntensity = 0.18f;

    [Tooltip("앰비언트도 함께 조절할지. 켜면 앰비언트 모드를 Flat으로 바꾼다 — " +
             "Skybox 모드는 GI 환경을 다시 굽기 전에는 하늘을 따라오지 않아서, " +
             "하늘만 밤이 되고 지면은 낮처럼 밝은 어색함이 생긴다.")]
    [SerializeField] bool driveAmbient = true;

    [Tooltip("시간(0~1)에 따른 앰비언트 색.")]
    [SerializeField] Gradient ambientColor;

    [Header("식생 틴트")]
    [Tooltip("잔디·꽃(Idyllic Vegetation 셰이더)은 색을 Emission으로 내는 풀브라이트라 " +
             "라이트도 앰비언트도 안 받는다 — 밤에도 대낮처럼 빛난다. 그래서 머티리얼의 " +
             "색 프로퍼티에 시간 틴트를 직접 곱한다. 비워두면 터레인 디테일에서 자동 수집.")]
    [SerializeField] Material[] vegetationMaterials;

    [Tooltip("시간(0~1)에 따른 식생 색 배율. 낮=백색(원색 그대로), 밤=어둡게.")]
    [SerializeField] Gradient vegetationTint;

    [Header("에디터 미리보기")]
    [Tooltip("에디트 모드에서 이 시간으로 하늘을 미리 본다(플레이 중에는 무시). " +
             "식생 틴트는 에셋을 더럽히지 않도록 플레이 중에만 적용된다.")]
    [Range(0f, 1f)] [SerializeField] float previewTime = 0.25f;

    // 식생 머티리얼 원색 캐시 — 플레이가 끝나면 되돌린다. 터레인 디테일은 sharedMaterial로
    // 그려서 에셋 자체를 만지는 수밖에 없는데, 복원 없이는 에디터에서 에셋이 영구히 얼룩진다.
    static readonly string[] VegColorProps = { "_Top_Color", "_Bottom_Color", "_Color" };
    readonly System.Collections.Generic.List<(Material mat, int prop, Color original)> _vegCache = new();

    void OnEnable()
    {
        if (!Application.isPlaying) return;

        // 명시 목록이 비어 있으면 터레인 디테일(풀·꽃)의 머티리얼을 자동 수집한다 —
        // 지형은 생성기가 굽는 물건이라 프로토타입 구성이 바뀌어도 따라간다.
        var mats = new System.Collections.Generic.HashSet<Material>();
        if (vegetationMaterials != null)
            foreach (var m in vegetationMaterials) if (m != null) mats.Add(m);
        if (mats.Count == 0)
            foreach (var terrain in Terrain.activeTerrains)
                foreach (var proto in terrain.terrainData.detailPrototypes)
                {
                    if (proto.prototype == null) continue;
                    foreach (var r in proto.prototype.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);
                }

        _vegCache.Clear();
        foreach (var m in mats)
            foreach (var name in VegColorProps)
            {
                int prop = Shader.PropertyToID(name);
                if (m.HasProperty(prop)) _vegCache.Add((m, prop, m.GetColor(prop)));
            }
    }

    void OnDisable()
    {
        // 원색 복원 — sharedMaterial을 만졌으므로 안 되돌리면 에셋이 얼룩진 채 남는다
        foreach (var (mat, prop, original) in _vegCache)
            if (mat != null) mat.SetColor(prop, original);
        _vegCache.Clear();
    }

    void Update()
    {
        float t;
        if (Application.isPlaying)
        {
            var tm = TimeManager.Instance;
            if (tm == null || tm.Cycle == null) return;
            t = tm.Cycle.NormalizedTimeOfDay;
        }
        else t = previewTime;   // 에디트 모드 — 인스펙터 슬라이더로 스크럽

        // 해는 t가 한 바퀴(0~1) 도는 동안 X축으로 한 바퀴 돈다. 달은 정확히 반대편.
        //
        // 단, 밤(0.5~1)의 해 궤적은 선형이 아니다 — 스카이박스가 하늘색을 <b>해의 깊이</b>로
        // 정하기 때문에, 선형으로 두면 자정에 바닥을 찍자마자 되올라오며 밤의 후반부가
        // 통째로 여명이 된다(밤 중반부터 하늘이 밝아지던 원인). 해는 밤에 안 보이므로
        // 궤적을 마음대로 다듬어도 된다: 초·말 15%에 빠르게 꽂히고/솟고, 가운데 70%는
        // 바닥 근처에 머문다 → 하늘은 밤 대부분 어둡고 여명은 끝자락에만 온다.
        // 달은 눈에 보이는 천체라 선형 그대로 부드럽게 돈다.
        float sunT = t;
        if (t > 0.5f)
        {
            float p = (t - 0.5f) * 2f;   // 밤 진행도 0~1
            // 구간 안에서도 smoothstep으로 눕힌다 — 선형이면 새벽에 해가 등속으로
            // 솟구쳐 하늘이 스위치 켜듯 밝아진다(밤→낮 급변의 원인 중 하나).
            float g = p < 0.15f ? SmoothStep01(p / 0.15f) * 0.45f
                    : p > 0.80f ? 0.55f + SmoothStep01((p - 0.80f) / 0.20f) * 0.45f
                    : 0.45f + (p - 0.15f) / 0.65f * 0.10f;
            sunT = 0.5f + g * 0.5f;
        }
        float moonAngle = MoonAngle(t);

        // 물량제 밤에는 해가 달의 정반대를 따른다 — 스카이박스의 달 위상과 하늘 밝기가
        // 해·달 각도차에서 나오므로, 해가 자기 궤적을 따로 돌면 밤새 보름달이 이지러졌다
        // 돌아온다(반달 버그). 이러면 하늘 어둠도 시계가 아니라 밤의 실제 진행(달)을 따르고,
        // 새벽(달 180° = 해 360°)에서 낮 궤적과 정확히 이어진다. 궤도면 4° 오프셋이
        // 개기월식 판정(정렬 1.1°)은 계속 피해준다.
        float sunAngle = _moonQuantityNight ? moonAngle + 180f : sunT * 360f;
        var sunRot  = Quaternion.Euler(sunAngle, sunYaw, 0f);
        var moonRot = Quaternion.Euler(moonAngle, sunYaw + moonOrbitYawOffset, 0f);
        if (sun != null) sun.rotation = sunRot;
        if (moon != null) moon.rotation = moonRot;

        // 라이트는 <b>둘</b>이다 — 해 라이트는 항상 해를, 달 라이트는 항상 달을 따르고,
        // 각자 자기 천체의 고도로 강도가 죽는다. 하나를 돌려 겸용하면 교대 순간에
        // 회전 스냅/슬럽이 필요해지고 그게 지평선 깜빡임과 급변을 만들었다.
        // URP는 더 밝은 디렉셔널을 주광(그림자)으로 자동 선택하므로 교대도 저절로 된다.
        if (sunLight != null)
        {
            sunLight.transform.rotation = sunRot;
            float elev = Mathf.Sin(sunAngle * Mathf.Deg2Rad);               // 실제 해 각도의 고도
            float gate = SmoothStep01(elev / Mathf.Sin(10f * Mathf.Deg2Rad)); // 고도 10°까지 페이드인
            if (lightColor != null) sunLight.color = lightColor.Evaluate(t);
            if (lightIntensity != null) sunLight.intensity = lightIntensity.Evaluate(t) * gate;
            sunLight.enabled = sunLight.intensity > 0.005f;
        }
        if (moonLight != null)
        {
            moonLight.transform.rotation = moonRot;
            float elev = Mathf.Sin(moonAngle * Mathf.Deg2Rad);              // 실제 달 각도의 고도
            float gate = SmoothStep01(elev / Mathf.Sin(10f * Mathf.Deg2Rad));
            moonLight.color = moonColor;
            moonLight.intensity = moonIntensity * gate;
            moonLight.enabled = moonLight.intensity > 0.005f;
        }

        if (driveAmbient && ambientColor != null)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor.Evaluate(t);
        }

        // 식생(풀브라이트 Emission) 어둡히기 — 플레이 중에만 (에셋 오염 방지, OnDisable에서 복원)
        if (Application.isPlaying && vegetationTint != null && _vegCache.Count > 0)
        {
            var tint = vegetationTint.Evaluate(t);
            foreach (var (mat, prop, original) in _vegCache)
                if (mat != null) mat.SetColor(prop, original * tint);
        }
    }

    // ── 달 궤적 ─────────────────────────────────────────────────

    float _moonShown;         // 물량제 밤 궤적의 표시 각도(0~180)
    float _moonMid;           // 2단 감쇠의 중간 단 — 이게 있어야 표시 속도가 0에서 부드럽게 출발한다
    float _moonSetStart;      // 전멸 순간의 달 각도 — 지는 구간은 여기서 180°까지 남은 밤 시간에 편다
    bool _moonCleared;        // 전멸을 감지했는가 (setStart를 한 번만 찍기 위함)
    bool _moonNightActive;    // 밤 궤적이 켜져 있는가 — 밤 진입 순간 0°(지평선)에서 시작
    bool _moonQuantityNight;  // 이번 프레임이 물량제 밤인가 — 해가 달의 정반대를 따르게 한다

    // 시간 구동 구간(뜸·짐)의 추종 강도 — 시계는 등속이라 바짝 따라가도 튀지 않는다.
    // 처치 구동 구간만 moonEase(느긋한 S자)를 쓴다.
    const float TimeEase = 2f;

    /// <summary>
    /// 달의 X축 각도. 낮·에디터 프리뷰·시간제 밤은 해의 정반대(t·360+180) 그대로.
    /// 물량제 밤은 0~180°(뜸→짐)를 세 구간으로 나눠 흐른다:
    ///   0→30°       자정까지의 밤 시간 진행 (시계)
    ///   30→처치각    처치 비율 = 잡은 수 / 전체 물량 (최대 150°)
    ///   전멸각→180°  전멸 순간의 각도에서 새벽까지의 밤 시간에 펴서 진다 (시계)
    /// 지는 구간을 고정 150° 기준으로 두면 빨리 전멸했을 때 목표가 한참 앞으로 튀어
    /// 달이 훅 진다 — 전멸 순간의 자리에서 시작하면 어디서 전멸하든 새벽에 정확히
    /// 다 지고 속도도 일정하다. 표시 각도는 목표를 2단 지수 감쇠로 뒤쫓는다 —
    /// 처치 구간은 느긋한 S자(moonEase), 시계 구간은 등속이라 바짝(TimeEase) 따른다.
    /// </summary>
    float MoonAngle(float t)
    {
        var tm = Application.isPlaying ? TimeManager.Instance : null;
        int remain = 0, total = 0;
        _moonQuantityNight = tm != null && tm.TryGetNightWaveStatus(out remain, out total);
        if (!_moonQuantityNight)
        {
            float raw = t * 360f + 180f;
            if (!_moonNightActive) return raw;

            // 밤 궤적이 남긴 잔차를 낮 궤적에 부드럽게 합류시킨다 — 새벽 순간
            // 몇 도가 스냅으로 튀지 않게. 합류가 끝나면 원래 공식으로 되돌아간다.
            _moonShown = Mathf.Lerp(_moonShown, raw, 1f - Mathf.Exp(-2f * Time.deltaTime));
            if (Mathf.Abs(_moonShown - raw) < 0.5f) { _moonNightActive = false; return raw; }
            return _moonShown;
        }

        float nightP = Mathf.Clamp01(tm.PhaseProgress);
        bool cleared = total > 0 && remain == 0;

        if (!_moonNightActive)
        {
            _moonNightActive = true;
            _moonShown = 0f;   // 밤은 항상 지평선에서 시작한다
            _moonMid = 0f;
            _moonCleared = false;
        }

        if (cleared && !_moonCleared)
        {
            _moonCleared = true;
            _moonSetStart = _moonShown;   // 전멸 순간의 그 자리에서 지기 시작한다
        }

        float target, ease;
        if (cleared)
        {
            target = Mathf.Lerp(_moonSetStart, 180f, Mathf.Clamp01((nightP - 0.5f) / 0.5f));
            ease = TimeEase;
        }
        else if (nightP < 0.5f)
        {
            target = nightP / 0.5f * 30f;
            ease = TimeEase;
        }
        else
        {
            target = 30f + (total > 0 ? 120f * (total - remain) / total : 0f);
            ease = moonEase;
        }

        // 2단 감쇠 — 중간 단이 목표를 쫓고, 표시 단이 중간 단을 쫓는다.
        // 1단이면 목표가 튀는 순간 속도도 튄다(처치 때 확 올라가던 원인). 2단은 표시 속도가
        // 0에서 부드럽게 올라갔다가 목표 근처에서 다시 부드럽게 죽는 S자 프로파일이 된다.
        // 구간별 ease 전환은 목표와 표시가 거의 붙은 순간(경계는 값이 이어진다)에 일어나
        // 속도 점프를 만들지 않는다.
        float a = 1f - Mathf.Exp(-ease * Time.deltaTime);
        _moonMid = Mathf.Lerp(_moonMid, target, a);
        _moonShown = Mathf.Lerp(_moonShown, _moonMid, a);
        return _moonShown;
    }

    static float SmoothStep01(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

    /// <summary>컴포넌트 추가 시 그럴듯한 기본 연출값 (인스펙터에서 조정 가능).</summary>
    void Reset()
    {
        lightColor = new Gradient();
        lightColor.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.00f, 0.55f, 0.35f), 0.00f), // 일출 주황
                new GradientColorKey(new Color(1.00f, 0.96f, 0.88f), 0.25f), // 정오 백색
                new GradientColorKey(new Color(1.00f, 0.45f, 0.25f), 0.50f), // 일몰 주황
                new GradientColorKey(new Color(0.55f, 0.65f, 0.90f), 0.56f), // 달빛 푸른빛
                new GradientColorKey(new Color(0.55f, 0.65f, 0.90f), 0.94f),
                new GradientColorKey(new Color(1.00f, 0.55f, 0.35f), 1.00f), // 다음 일출
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

        // 밤 강도는 0이 아니다 — 라이트가 달 방향에서 내려오므로 달빛 역할을 한다
        lightIntensity = new AnimationCurve(
            new Keyframe(0.00f, 0.35f),   // 일출
            new Keyframe(0.25f, 1.15f),   // 정오
            new Keyframe(0.50f, 0.35f),   // 일몰
            new Keyframe(0.56f, 0.18f),   // 달빛
            new Keyframe(0.94f, 0.18f),
            new Keyframe(1.00f, 0.35f));  // 다음 일출

        vegetationTint = new Gradient();
        vegetationTint.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.85f, 0.75f, 0.68f), 0.00f), // 일출 — 살짝 웜
                new GradientColorKey(Color.white,                    0.10f), // 낮 — 원색 그대로
                new GradientColorKey(Color.white,                    0.42f),
                new GradientColorKey(new Color(0.80f, 0.62f, 0.52f), 0.50f), // 일몰 — 노을빛
                new GradientColorKey(new Color(0.26f, 0.30f, 0.44f), 0.58f), // 밤 — 어둡고 푸르게
                new GradientColorKey(new Color(0.26f, 0.30f, 0.44f), 0.92f),
                new GradientColorKey(new Color(0.85f, 0.75f, 0.68f), 1.00f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

        ambientColor = new Gradient();
        ambientColor.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.72f, 0.62f, 0.55f), 0.00f), // 일출
                new GradientColorKey(new Color(0.82f, 0.84f, 0.88f), 0.25f), // 정오
                new GradientColorKey(new Color(0.66f, 0.52f, 0.46f), 0.50f), // 일몰
                new GradientColorKey(new Color(0.16f, 0.19f, 0.30f), 0.58f), // 밤
                new GradientColorKey(new Color(0.16f, 0.19f, 0.30f), 0.92f),
                new GradientColorKey(new Color(0.72f, 0.62f, 0.55f), 1.00f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
    }
}
