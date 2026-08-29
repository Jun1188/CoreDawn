using UnityEngine;

namespace CoreDawn.DayTime
{
    /// <summary>
    /// 낮/밤 시간에 따라 태양(디렉셔널 라이트)을 연출하는 뷰 — 로직 없음, 읽기 전용.
    /// DayCycle.NormalizedTimeOfDay(0=일출, 0.25=정오, 0.5=일몰, 0.75=자정)를 따라
    /// 해 각도·색·강도·앰비언트를 갱신한다.
    ///
    /// 사용법: 씬의 Directional Light를 sun에 연결 (또는 이 컴포넌트를 라이트에 부착).
    /// </summary>
    public class DayNightLightingView : MonoBehaviour
    {
        [Tooltip("태양 역할의 디렉셔널 라이트. 비우면 자기 자신에서 찾는다.")]
        [SerializeField] Light sun;

        [Tooltip("해가 뜨고 지는 방위(Y 회전).")]
        [SerializeField] float sunYaw = 30f;

        [Tooltip("시간(0~1)에 따른 태양 색. 0=일출, 0.25=정오, 0.5=일몰, 0.75=자정.")]
        [SerializeField] Gradient sunColor;

        [Tooltip("시간(0~1)에 따른 태양 강도.")]
        [SerializeField] AnimationCurve sunIntensity;

        [Tooltip("앰비언트 라이트도 함께 조절할지.")]
        [SerializeField] bool driveAmbient = true;

        [Tooltip("시간(0~1)에 따른 앰비언트 색.")]
        [SerializeField] Gradient ambientColor;

        void Awake()
        {
            if (sun == null) sun = GetComponent<Light>();
        }

        void Update()
        {
            var tm = TimeManager.Instance;
            if (tm == null || sun == null) return;

            float t = tm.Cycle.NormalizedTimeOfDay;

            // 해 각도: X = t*360 → 일출(t=0)에 지평선(0°), 정오(0.25)에 머리 위(90°),
            // 일몰(0.5)에 반대 지평선(180°), 자정(0.75)에 지평선 아래
            sun.transform.rotation = Quaternion.Euler(t * 360f, sunYaw, 0f);

            sun.color     = sunColor.Evaluate(t);
            sun.intensity = sunIntensity.Evaluate(t);

            if (driveAmbient)
                RenderSettings.ambientLight = ambientColor.Evaluate(t);
        }

        /// <summary>컴포넌트 추가 시 그럴듯한 기본 연출값 세팅 (인스펙터에서 조정 가능).</summary>
        void Reset()
        {
            sun = GetComponent<Light>();

            sunColor = new Gradient();
            sunColor.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.00f, 0.55f, 0.35f), 0.00f), // 일출 주황
                    new GradientColorKey(new Color(1.00f, 0.96f, 0.88f), 0.25f), // 정오 백색
                    new GradientColorKey(new Color(1.00f, 0.45f, 0.25f), 0.50f), // 일몰 주황
                    new GradientColorKey(new Color(0.30f, 0.38f, 0.60f), 0.60f), // 밤 푸른빛
                    new GradientColorKey(new Color(0.30f, 0.38f, 0.60f), 0.90f),
                    new GradientColorKey(new Color(1.00f, 0.55f, 0.35f), 1.00f), // 다음 일출
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

            sunIntensity = new AnimationCurve(
                new Keyframe(0.00f, 0.3f),   // 일출
                new Keyframe(0.25f, 1.2f),   // 정오
                new Keyframe(0.50f, 0.3f),   // 일몰
                new Keyframe(0.60f, 0.05f),  // 밤
                new Keyframe(0.90f, 0.05f),
                new Keyframe(1.00f, 0.3f));  // 다음 일출

            ambientColor = new Gradient();
            ambientColor.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.55f, 0.50f, 0.45f), 0.00f),
                    new GradientColorKey(new Color(0.60f, 0.60f, 0.60f), 0.25f),
                    new GradientColorKey(new Color(0.40f, 0.35f, 0.35f), 0.50f),
                    new GradientColorKey(new Color(0.10f, 0.12f, 0.20f), 0.60f), // 밤 어둡게
                    new GradientColorKey(new Color(0.10f, 0.12f, 0.20f), 0.90f),
                    new GradientColorKey(new Color(0.55f, 0.50f, 0.45f), 1.00f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        }
    }
}
