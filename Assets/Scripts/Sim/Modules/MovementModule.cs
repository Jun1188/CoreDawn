using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 이동 — 심 모듈. 소유 엔티티의 Position/Facing을 매 틱 옮긴다(MonsterSystem이 구동).
    /// 두 가지 모드: 경로 추종(런타임 A*, StartMoving)과 방향 이동(플로우필드, SetDirection).
    ///
    /// 구 MovementComponent(뷰, transform 직접 적분)의 이식. Rigidbody가 아니라 transform을 적분하던 코드라
    /// 심에 통째로 들어온다 — 뷰는 결과 위치를 그릴 뿐이다. 격자 통행·지형 배율·지면 높이는 <see cref="INavigation"/>에 묻는다.
    /// </summary>
    public sealed class MovementModule : EntityModule
    {
        readonly float moveSpeed;
        readonly float rotateSpeed;     // 도/초 — 이동 방향으로 몸 돌리는 속도
        readonly float crowdRadius;     // 개체 반지름. 0이면 군중 시스템에서 제외
        readonly float knockbackDamping; // 넉백 감쇠율(초당). 총 밀림 거리는 감쇠율과 무관하게 효과가 정한다
        readonly bool stickToGround;    // 매 틱 발을 지면 높이에 맞춘다. 끄면 스폰 당시 높이를 유지(비행 유닛)

        INavigation nav;

        /// <summary>루트 피벗에서 콜라이더 바닥까지의 거리 — 발을 지면에 놓을 때 더해 줄 값. 뷰가 콜라이더 치수로 잰다.</summary>
        public float PivotToBottom { get; set; }

        List<Vector3> currentPath;
        int targetIndex;
        Vector3 flowDirection;   // 방향 이동 모드 (플로우필드)

        public bool IsMoving => (currentPath != null && currentPath.Count > 0) || flowDirection != Vector3.zero;

        /// <summary>지금 따라갈 경로를 들고 있는가 — 경로가 끝났는지 밖에서 판단할 때 쓴다.</summary>
        public bool HasPath => currentPath != null && currentPath.Count > 0;

        public float MoveSpeed => moveSpeed;

        /// <summary>
        /// 실측 수평 이동 속도(m/s) — 곡사탄의 탄착점 예측이 읽는다.
        /// 계산값(방향 × EffectiveSpeed)이 아니라 <b>실제 이동량</b>을 재는 이유: 경로 추종·플로우필드·넉백이 각자 위치를
        /// 옮기고 감속 효과·지형 배율까지 곱해지므로 어느 한 경로의 의도만 봐서는 몸이 어디로 가는지 알 수 없다.
        /// 지수 평활을 거치는 이유는 웨이포인트 코너다 — 꺾이는 한 프레임의 생속도로 몇 초 뒤를 내다보면 엉뚱한 곳을 찍는다.
        /// </summary>
        public Vector3 Velocity => velocity;

        /// <summary>군중 겹침 해소용 개체 반지름 — MonsterSystem의 군중 패스가 읽는다.</summary>
        public float CrowdRadius => crowdRadius;

        EffectsModule effects;   // 같은 엔티티의 효과 모듈 — 감속·가속 배율의 정본 (없으면 1)

        /// <summary>효과 시스템의 이동 속도 배율(감속 등). 심 Effects 모듈에서 읽는다 — 뷰가 밀어 넣지 않는다.</summary>
        public float SpeedMultiplier
        {
            get
            {
                if (effects == null && Owner != null) effects = Owner.Get<EffectsModule>();   // 부착 순서와 무관하게 늦게 찾는다
                return effects != null ? effects.MoveSpeedMultiplier : 1f;
            }
        }

        /// <summary>
        /// 넉백을 받지 않는가. 교전을 포기하고 자기 자리로 돌아가는 개체가 켠다 — 그 복귀는 되돌릴 수 없다고 정해 놓고
        /// 몸만 밀리면 총알에 떠밀려 집에 영영 못 간다(총기 넉백은 데미지당 0.2m라 25 데미지 한 발이 5m를 민다).
        /// </summary>
        public bool IgnoreKnockback { get; set; }

        public event Action OnDestinationReached;
        public event Action OnPathBlocked;

        public MovementModule(in MonsterSpec spec, INavigation navigation)
        {
            moveSpeed = spec.MoveSpeed;
            rotateSpeed = spec.RotateSpeed;
            crowdRadius = spec.CrowdRadius;
            knockbackDamping = Mathf.Max(0.01f, spec.KnockbackDamping);
            stickToGround = spec.StickToGround;
            nav = navigation;
        }

        internal void SetNavigation(INavigation navigation) => nav = navigation;

        // 지금 밟고 있는 칸의 지형 배율(강 0.5 등) — 효과와 달리 위치의 성질이라 칸을 벗어나는 즉시 원복된다.
        // 그래서 효과 시스템(시간 기반)에 얹지 않고 따로 곱한다. 감속탄과 강이 겹치면 자연스럽게 함께 곱해진다.
        float TerrainMultiplier => nav != null ? nav.TerrainSpeedAt(Owner.Position) : 1f;

        // 실제 이동에 쓰는 속도 — 기본 속도 × 효과 배율 × 지형 배율
        float EffectiveSpeed => moveSpeed * SpeedMultiplier * TerrainMultiplier;

        public void StartMoving(List<Vector3> path)
        {
            flowDirection = Vector3.zero;
            if (path == null || path.Count == 0)
            {
                currentPath = null;
                return;
            }
            currentPath = path;
            targetIndex = 0;

            // 등 뒤의 웨이포인트는 건너뛴다. A*는 현재 칸 중심에서 경로를 시작하는데, 추적이 주기적으로 경로를 다시 깔 때마다
            // 그 중심으로 한 걸음 되돌아가는 진동이 생긴다 — 칸이 클수록 뒷걸음이 길어져 순간이동처럼 보인다.
            // 다음 구간의 진행 방향과 반대쪽에 있는 동안만 건너뛰므로 경로 이탈은 없다.
            Vector3 pos = Owner != null ? Owner.Position : Vector3.zero;
            while (targetIndex < currentPath.Count - 1)
            {
                Vector3 toCurrent = currentPath[targetIndex] - pos;
                Vector3 segment = currentPath[targetIndex + 1] - currentPath[targetIndex];
                toCurrent.y = 0f;
                segment.y = 0f;
                if (Vector3.Dot(toCurrent, segment) > 0f) break;
                targetIndex++;
            }
        }

        /// <summary>플로우필드 방향 이동. 매 틱 갱신 호출을 전제로 한다 (zero면 정지).</summary>
        public void SetDirection(Vector3 direction)
        {
            currentPath = null;
            direction.y = 0f;
            flowDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
        }

        public void StopMoving()
        {
            currentPath = null;
            flowDirection = Vector3.zero;
        }

        /// <summary>속도 평활 계수(1/초) — 클수록 실측을 빨리 따라가고 코너에서 더 튄다.</summary>
        const float VelocitySmoothing = 8f;

        Vector3 velocity;

        public void Tick(float deltaTime)
        {
            if (Owner == null || deltaTime <= 0f) return;

            Vector3 before = Owner.Position;

            if (currentPath != null) TickPath(deltaTime);
            else if (flowDirection != Vector3.zero) TickDirection(deltaTime);

            // 개체 간 겹침 해소는 여기서 하지 않는다 — 모든 이동이 끝난 뒤 MonsterSystem이 중앙 한 패스로 처리한다. 넉백만 개체 소관.
            TickKnockback(deltaTime);

            // 접지는 XZ가 전부 정해진 뒤 마지막에 한다
            if (stickToGround) StickToGround();

            TrackVelocity(before, deltaTime);
        }

        /// <summary>이번 틱 실제로 움직인 거리에서 수평 속도를 갱신한다.</summary>
        void TrackVelocity(Vector3 before, float deltaTime)
        {
            Vector3 delta = Owner.Position - before;
            delta.y = 0f;
            // 프레임 독립 지수 평활 — dt가 흔들려도 따라가는 속도가 같다
            velocity = Vector3.Lerp(velocity, delta / deltaTime, 1f - Mathf.Exp(-VelocitySmoothing * deltaTime));
        }

        /// <summary>
        /// 발을 지면에 붙인다. 이게 없으면 개체는 스폰 당시의 Y에 영원히 고정된다(경로 추종·플로우필드 모두 waypoint.y를
        /// 현재 y로 덮어쓰기 때문). 지형은 1m 가까이 오르내리므로 키 1m짜리 일반몹은 다리가 통째로 사라진다.
        /// </summary>
        void StickToGround()
        {
            if (nav == null) return;
            Vector3 position = Owner.Position;
            float target = nav.GroundHeightAt(position) + PivotToBottom;
            if (Mathf.Approximately(position.y, target)) return;

            position.y = target;
            Owner.Position = position;
        }

        // ── 넉백 — 효과 시스템(KnockbackEffectSO)이 주입하는 외부 충격 ──
        // 이동과 별개 레이어라 이동 속도 제한·감속의 영향을 받지 않는다. 밀린 결과로 생긴 겹침은 같은 틱 군중 패스가 풀어준다.

        Vector3 knockback; // 현재 넉백 속도 (지수 감쇠)

        /// <summary>지정 방향으로 총 distance만큼 밀려나게 한다 (감쇠 적분값이 distance가 되도록 초기 속도를 잡는다).</summary>
        public void AddKnockback(Vector3 direction, float distance)
        {
            if (IgnoreKnockback) return;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f || distance <= 0f) return;
            // 지수 감쇠의 총 이동량 = v0 / 감쇠율 → v0 = 거리 × 감쇠율
            knockback += direction.normalized * (distance * knockbackDamping);
        }

        void TickKnockback(float deltaTime)
        {
            if (knockback.sqrMagnitude < 0.01f)
            {
                knockback = Vector3.zero;
                return;
            }

            // v·dt(explicit Euler)가 아니라 지수 감쇠의 정확한 적분값 — 총 밀림 거리가 프레임레이트와 무관하게 정확히 지정 거리가 된다
            float decay = Mathf.Exp(-knockbackDamping * deltaTime);
            Vector3 next = Owner.Position + knockback * ((1f - decay) / knockbackDamping);

            // 건물/장애물 셀로는 밀려 들어가지 않는다 — 벽에 닿으면 넉백 소멸
            if (nav != null && !nav.IsWalkable(next))
            {
                knockback = Vector3.zero;
                return;
            }

            Owner.Position = next;
            knockback *= decay;
        }

        void TickPath(float deltaTime)
        {
            // 이동 도중 다음 웨이포인트에 건물이 설치되면 즉시 멈추고 재탐색 요청
            if (nav != null && !nav.IsWalkable(currentPath[targetIndex]))
            {
                StopMoving();
                OnPathBlocked?.Invoke();
                return;
            }

            Vector3 position = Owner.Position;
            Vector3 waypoint = currentPath[targetIndex];
            waypoint.y = position.y; // Y축 높이 보정

            Vector3 flatPosition = new Vector3(position.x, 0, position.z);
            Vector3 flatWaypoint = new Vector3(waypoint.x, 0, waypoint.z);

            if (Vector3.Distance(flatPosition, flatWaypoint) < 0.1f)
            {
                targetIndex++;
                if (targetIndex >= currentPath.Count)
                {
                    // 목적지 도착 완료
                    StopMoving();
                    OnDestinationReached?.Invoke();
                    return;
                }
                waypoint = currentPath[targetIndex];
                waypoint.y = position.y;
            }

            Vector3 moveDir = waypoint - position;
            Owner.Position = Vector3.MoveTowards(position, waypoint, EffectiveSpeed * deltaTime);
            Face(moveDir, deltaTime);
        }

        void TickDirection(float deltaTime)
        {
            Vector3 next = Owner.Position + flowDirection * (EffectiveSpeed * deltaTime);

            // 건물/장애물 셀로는 진입하지 않는다 (플로우필드가 목표 건물 셀을 가리킬 수 있음 —
            // 그 앞에서 멈추면 FlowFieldState의 사거리 판정이 공격으로 전환시킨다)
            if (nav != null && !nav.IsWalkable(next))
            {
                Face(flowDirection, deltaTime);
                return;
            }

            Owner.Position = next;
            Face(flowDirection, deltaTime);
        }

        void Face(Vector3 direction, float deltaTime)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            Quaternion look = Quaternion.LookRotation(direction);
            Quaternion current = Quaternion.LookRotation(Owner.Facing);
            Owner.Facing = Quaternion.RotateTowards(current, look, rotateSpeed * deltaTime) * Vector3.forward;
        }

        /// <summary>즉시 바라보기(공격 시) — 회전 속도를 기다리지 않는다.</summary>
        public void FaceImmediately(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            Owner.Facing = direction.normalized;
        }
    }
}
