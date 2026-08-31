using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 각도 계산기 — 발사기(포탑·총)는 각도만 정한다는 문법의 수식 모음. 탄속·중력은 탄약의 성질이라 인자로 받는다.
    /// 직사탄 리드는 이차식 한 번(<see cref="LinearLead"/>), 곡사탄은 발사각이 비행시간을 바꾸어 닫힌 해 대신 반복(<see cref="BallisticLead"/>).
    /// 순수 수식이라 심(포탑 두뇌)이 쓴다 — 뷰(총)는 지금 리드가 없다(사람이 리드한다).
    /// </summary>
    public static class Ballistics
    {
        // ── 리드·탄도해 ───────────────────────────────────────────────

        /// <summary>
        /// 고정 초속·중력으로 표적을 맞추는 발사 방향(탄도해). 발사기는 각도만 정한다는
        /// 문법의 "각도 계산기" — 탄속·중력은 탄약의 성질이라 인자로 받는다.
        /// 같은 사거리에 해가 둘(저각·고각) 있으면 highArc로 고른다 — 박격포는 고각으로
        /// 장애물을 넘기고, 직사 발사기는 저각으로 빨리 닿는다.
        /// 초속이 모자라 닿지 않으면 최대 사거리인 45°로 최선을 다한다.
        /// </summary>
        public static Vector3 BallisticAim(Vector3 origin, Vector3 target, float speed, float gravity, bool highArc = false)
        {
            Vector3 delta = target - origin;
            var flat = new Vector3(delta.x, 0f, delta.z);
            float d = flat.magnitude;
            if (d < 0.001f || gravity <= 0f || speed <= 0f)
                return delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.up;

            // 포물선 공식: tanθ = (v² ± √(v⁴ − g(gd² + 2yv²))) / (gd)  (y = 높이차)
            float v2 = speed * speed;
            float disc = v2 * v2 - gravity * (gravity * d * d + 2f * delta.y * v2);
            float tan = disc <= 0f
                ? 1f // 도달 불가 — 45°
                : (v2 + (highArc ? Mathf.Sqrt(disc) : -Mathf.Sqrt(disc))) / (gravity * d);

            return (flat / d + Vector3.up * tan).normalized;
        }

        /// <summary>
        /// 직사탄(중력 0)의 리드 — 등속 직선탄은 반복이 필요 없다. 탄이 목표를 만나는 시각 t는
        /// |p + v·t| = s·t  (p = 목표 오프셋, v = 목표 속도, s = 탄속)  →  (v·v − s²)t² + 2(p·v)t + p·p = 0
        /// 의 가장 이른 양의 근이다. 근이 없으면(목표가 탄보다 빠르거나 멀어지는데 못 따라잡음) 현재 위치를 겨눈다.
        /// 곡사탄은 <see cref="BallisticLead"/> — 발사각이 비행시간을 바꾸므로 닫힌 해 대신 반복으로 푼다.
        /// </summary>
        /// <param name="impact">탄이 목표를 만나는 지점 — 발사기가 사거리 여유를 잡는 데 쓴다.</param>
        public static Vector3 LinearLead(Vector3 origin, Vector3 target, Vector3 targetVelocity, float speed, out Vector3 impact)
        {
            impact = target;
            Vector3 p = target - origin;
            if (speed <= 0f || targetVelocity.sqrMagnitude < 0.0001f)
                return p.sqrMagnitude > 0.0001f ? p.normalized : Vector3.forward;

            float a = Vector3.Dot(targetVelocity, targetVelocity) - speed * speed;
            float b = 2f * Vector3.Dot(p, targetVelocity);
            float c = Vector3.Dot(p, p);
            float t = -1f;
            if (Mathf.Abs(a) < 0.0001f)
            {
                // 탄속 == 목표 속도 — 일차식. b > 0이면 멀어지는 목표라 못 따라잡는다
                if (b < -0.0001f) t = -c / b;
            }
            else
            {
                float disc = b * b - 4f * a * c;
                if (disc >= 0f)
                {
                    float sq = Mathf.Sqrt(disc);
                    float t1 = (-b - sq) / (2f * a), t2 = (-b + sq) / (2f * a);
                    // 양의 근 중 가장 이른 것 (a < 0이 보통이라 t1 > t2 — 순서를 믿지 말고 둘 다 본다)
                    float lo = Mathf.Min(t1, t2), hi = Mathf.Max(t1, t2);
                    t = lo > 0f ? lo : (hi > 0f ? hi : -1f);
                }
            }
            if (t <= 0f)
                return p.sqrMagnitude > 0.0001f ? p.normalized : Vector3.forward;

            impact = target + targetVelocity * t;
            Vector3 dir = impact - origin;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        }

        /// <summary>탄착점을 다시 푸는 횟수 — 1회면 대개 수십 cm 안으로 수렴한다.</summary>
        const int LeadRefineSteps = 2;

        /// <summary>
        /// 곡사탄의 <b>탄착점</b> 조준 — 목표의 현재 위치가 아니라, 포탄이 날아가 떨어질 때
        /// 목표가 서 있을 자리를 겨눈다.
        ///
        /// 왜 필요한가: 중력탄은 궤적이 굽는 만큼 비행 시간이 길다(박격포 최대 사거리에서
        /// 0.7초 남짓, 고각이면 몇 배). 그동안 몬스터는 걸어서 자리를 뜨므로, 현재 위치로
        /// 탄도해를 풀면 포탄은 <b>몬스터가 있던 자리</b>에 정확히 떨어진다. 직사탄은 탄속이
        /// 빨라 이 오차가 눈에 띄지 않지만, 곡사탄은 한 발이 통째로 빗나간다.
        ///
        /// 닭과 달걀 문제라 반복해서 푼다: 비행 시간을 알아야 탄착점을 알고, 탄착점을 알아야
        /// 비행 시간을 안다. 현재 위치의 해에서 시간을 얻어 목표를 그만큼 앞질러 놓고 다시
        /// 푸는 것을 <see cref="LeadRefineSteps"/>번 반복하면 충분히 수렴한다.
        ///
        /// <b>중력 0이면 부르지 말 것</b> — 직사탄은 예측도 탄도해도 필요 없다. 이 함수는
        /// 중력탄 전용이며(gravity ≤ 0이면 곧장 직선 조준으로 빠진다), 발사기는 탄약의
        /// gravity가 0이 아닐 때만 이 경로를 타면 된다.
        /// </summary>
        /// <param name="targetVelocity">목표의 수평 속도(m/s). 0이면 현재 위치를 그대로 겨눈다.</param>
        /// <param name="impact">푼 탄착점 — 발사기가 사거리 여유를 잡는 데 쓴다.</param>
        public static Vector3 BallisticLead(Vector3 origin, Vector3 target, Vector3 targetVelocity,
                                            float speed, float gravity, bool highArc, out Vector3 impact)
        {
            impact = target;
            Vector3 direction = BallisticAim(origin, target, speed, gravity, highArc);
            if (gravity <= 0f || targetVelocity.sqrMagnitude < 0.0001f) return direction;

            for (int i = 0; i < LeadRefineSteps; i++)
            {
                float flight = FlightTime(origin, impact, direction, speed);
                if (flight <= 0f) break;

                impact = target + targetVelocity * flight;
                direction = BallisticAim(origin, impact, speed, gravity, highArc);
            }

            return direction;
        }

        /// <summary>
        /// 주어진 발사 방향으로 목표의 수평 거리를 지나는 데 걸리는 시간.
        /// 수평 속도는 중력의 영향을 받지 않으므로(가속이 아래로만 걸린다) 수평 성분만으로 나온다 —
        /// 포물선을 적분할 필요가 없다.
        /// </summary>
        static float FlightTime(Vector3 origin, Vector3 target, Vector3 direction, float speed)
        {
            Vector3 delta = target - origin;
            float distance = new Vector2(delta.x, delta.z).magnitude;
            float horizontalSpeed = new Vector2(direction.x, direction.z).magnitude * speed;

            // 수평 속도가 0이면 수직 발사 — 거리를 지나는 시간이 정의되지 않으니 예측을 포기한다
            return horizontalSpeed > 0.001f ? distance / horizontalSpeed : 0f;
        }
    }
}
