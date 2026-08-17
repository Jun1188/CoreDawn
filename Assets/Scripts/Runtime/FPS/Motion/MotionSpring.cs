using UnityEngine;

/// <summary>
/// 절차적 모션(카메라·무기)이 공유하는 수학 유틸.
///
/// 기존에 WeaponKickback 안에만 있던 "감쇠 스프링 닫힌형 해석해"를 끌어올린 것.
/// 수치 적분이 아니라 상미분방정식 x'' + 2ζωx' + ω²x = 0 의 정확해를 매 프레임 그대로
/// 평가하므로 dt가 크든 진동수가 높든 절대 발산하지 않는다.
/// 카메라 틸트·보브·반동·무기 스웨이가 전부 같은 스프링을 쓰면 모션 톤이 하나로 맞는다.
/// </summary>
public static class MotionSpring
{
    /// <summary>프레임률 독립 지수 감쇠 보간. Lerp(a, b, dt * k)의 안전한 대체.</summary>
    public static float Damp(float current, float target, float sharpness, float dt)
        => Mathf.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * dt));

    public static Vector2 Damp(Vector2 current, Vector2 target, float sharpness, float dt)
        => Vector2.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * dt));

    public static Vector3 Damp(Vector3 current, Vector3 target, float sharpness, float dt)
        => Vector3.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * dt));

    public static Quaternion Damp(Quaternion current, Quaternion target, float sharpness, float dt)
        => Quaternion.Slerp(current, target, 1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * dt));

    /// <summary>진동 한 주기가 최소 몇 프레임에 걸쳐야 곡선으로 보이는가.</summary>
    private const float MinFramesPerCycle = 8f;

    /// <summary>
    /// 화면에서 실제로 진동으로 보이는 최대 진동수로 낮춘다.
    ///
    /// 해석해가 아무리 정확해도 <b>보이는 것은 프레임에 찍힌 표본뿐</b>이다. 13Hz를 60fps로
    /// 그리면 한 주기가 4.6프레임이라 중간이 없는 순간이동처럼 보인다(반동이 '탁탁' 끊기던 원인).
    /// 프레임률이 높은 기기에서는 상한도 함께 올라가므로, 여유가 있으면 원래의 날카로움이 살아난다.
    /// </summary>
    public static float Visible(float frequencyHz, float dt)
        => dt > 0f ? Mathf.Min(frequencyHz, 1f / (dt * MinFramesPerCycle)) : frequencyHz;

    /// <summary>(value, velocity)를 감쇠 스프링 해석해로 dt만큼 전진시킨다.</summary>
    public static void Step(ref Vector3 value, ref Vector3 velocity, float frequencyHz, float dampingRatio, float dt)
    {
        if (dt <= 0f) return;

        float omega = 2f * Mathf.PI * Mathf.Max(frequencyHz, 0.001f);
        float zeta = Mathf.Max(dampingRatio, 0.001f);

        Vector3 x0 = value;
        Vector3 v0 = velocity;

        if (zeta < 0.999f)
        {
            // 언더댐프 — 살짝 튕겼다 정지. 반동/충격 특유의 손맛
            float wd = omega * Mathf.Sqrt(1f - zeta * zeta);
            float et = Mathf.Exp(-zeta * omega * dt);
            float c = Mathf.Cos(wd * dt);
            float s = Mathf.Sin(wd * dt);

            Vector3 c2 = (v0 + zeta * omega * x0) / wd;
            value = et * (x0 * c + c2 * s);
            velocity = et * (v0 * c - (omega * (omega * x0 + zeta * v0) / wd) * s);
        }
        else if (zeta < 1.001f)
        {
            // 임계댐프 — 오버슈트 없이 최단 정지
            float et = Mathf.Exp(-omega * dt);
            value = et * (x0 + (v0 + omega * x0) * dt);
            velocity = et * (v0 - omega * dt * (v0 + omega * x0));
        }
        else
        {
            // 오버댐프 — 진동 없이 느긋하게 정지
            float wd = omega * Mathf.Sqrt(zeta * zeta - 1f);
            float et = Mathf.Exp(-zeta * omega * dt);
            float c = (float)System.Math.Cosh(wd * dt);
            float s = (float)System.Math.Sinh(wd * dt);

            Vector3 c2 = (v0 + zeta * omega * x0) / wd;
            value = et * (x0 * c + c2 * s);
            velocity = et * (v0 * c - (omega * (omega * x0 + zeta * v0) / wd) * s);
        }
    }

    /// <summary>스칼라 오버로드.</summary>
    public static void Step(ref float value, ref float velocity, float frequencyHz, float dampingRatio, float dt)
    {
        Vector3 v = new Vector3(value, 0f, 0f);
        Vector3 vel = new Vector3(velocity, 0f, 0f);
        Step(ref v, ref vel, frequencyHz, dampingRatio, dt);
        value = v.x;
        velocity = vel.x;
    }

    /// <summary>
    /// "정지 상태에서 임펄스를 줬을 때 스프링의 최대 변위(peak)가 정확히 desiredPeak가 되도록"
    /// 필요한 초기 속도를 역산한다. 기획 수치(반동 세기 등)가 스프링 세팅과 무관하게
    /// "딱 그만큼 튄다"는 직관적 의미를 유지하게 해준다.
    /// </summary>
    public static float SolveImpulseVelocity(float desiredPeak, float frequencyHz, float dampingRatio)
    {
        if (Mathf.Approximately(desiredPeak, 0f)) return 0f;

        float omega = 2f * Mathf.PI * Mathf.Max(frequencyHz, 0.001f);
        float zeta = Mathf.Max(dampingRatio, 0.001f);
        float k; // 단위 임펄스(V=1) 당 피크 변위

        if (zeta < 0.999f)
        {
            float wd = omega * Mathf.Sqrt(1f - zeta * zeta);
            float tPeak = Mathf.Atan2(wd, zeta * omega) / wd;
            k = Mathf.Exp(-zeta * omega * tPeak) * Mathf.Sin(wd * tPeak) / wd;
        }
        else if (zeta < 1.001f)
        {
            k = Mathf.Exp(-1f) / omega;
        }
        else
        {
            float wd = omega * Mathf.Sqrt(zeta * zeta - 1f);
            float ratio = wd / (zeta * omega);
            float tPeak = (0.5f * Mathf.Log((1f + ratio) / (1f - ratio))) / wd;
            k = Mathf.Exp(-zeta * omega * tPeak) * (float)System.Math.Sinh(wd * tPeak) / wd;
        }

        return Mathf.Abs(k) > 1e-6f ? desiredPeak / k : 0f;
    }
}
