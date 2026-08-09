using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 이동 상태 하나. 입력 파이프라인 설계문서 §6의 IPlayerState를 실제로 구현한 형태다.
/// (문서에서는 "상호배타적 상태가 없어 보류"였지만, 앉기/슬라이딩/체공이 들어오면서
///  상태별로 입력 해석과 물리 규칙이 실제로 갈리게 되었다.)
/// </summary>
public interface IPlayerLocomotionState
{
    PlayerLocomotion Id { get; }
    void Enter(PlayerController c, PlayerLocomotion from);
    void Exit(PlayerController c, PlayerLocomotion to);
    /// <summary>Update 주기 — 표현/타이머용. 물리를 만지지 말 것.</summary>
    void Tick(PlayerController c, float dt);
    /// <summary>FixedUpdate 주기 — 물리와 상태 전이는 전부 여기서.</summary>
    void FixedTick(PlayerController c, float dt);
    /// <summary>상태 고유 입력 해석. true면 소비.</summary>
    bool HandleInput(PlayerController c, in InputEvent e);
}

// ═══════════════════════════════════════════════════════════════════════════
//  지상 — 걷기 / 달리기
// ═══════════════════════════════════════════════════════════════════════════
public sealed class GroundedState : IPlayerLocomotionState
{
    public PlayerLocomotion Id => PlayerLocomotion.Grounded;

    public void Enter(PlayerController c, PlayerLocomotion from) => c.SetStance(PlayerStance.Stand);
    public void Exit(PlayerController c, PlayerLocomotion to) { }
    public void Tick(PlayerController c, float dt) { }
    public bool HandleInput(PlayerController c, in InputEvent e) => false;

    public void FixedTick(PlayerController c, float dt)
    {
        if (!c.Motion.IsGrounded) { c.ChangeState(PlayerLocomotion.Airborne); return; }

        if (c.TryConsumeJump()) { c.PerformJump(); c.ChangeState(PlayerLocomotion.Airborne); return; }

        // 앉기 입력: 충분히 빠르면 슬라이딩, 아니면 그냥 앉기
        if (c.CrouchHeld)
        {
            c.ChangeState(c.CanEnterSlide ? PlayerLocomotion.Sliding : PlayerLocomotion.Crouching);
            return;
        }

        bool sprinting = c.WantsSprint;
        c.Motion.IsSprinting = sprinting;

        float target = sprinting ? c.SprintSpeed : c.WalkSpeed;
        c.ApplyGroundMove(target, c.GroundAccel, c.GroundFriction, dt);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  앉기
// ═══════════════════════════════════════════════════════════════════════════
public sealed class CrouchingState : IPlayerLocomotionState
{
    public PlayerLocomotion Id => PlayerLocomotion.Crouching;

    public void Enter(PlayerController c, PlayerLocomotion from) => c.SetStance(PlayerStance.Crouch);
    public void Exit(PlayerController c, PlayerLocomotion to) { }
    public void Tick(PlayerController c, float dt) { }
    public bool HandleInput(PlayerController c, in InputEvent e) => false;

    public void FixedTick(PlayerController c, float dt)
    {
        c.Motion.IsSprinting = false;

        if (!c.Motion.IsGrounded) { c.ChangeState(PlayerLocomotion.Airborne); return; }

        // 앉은 채 점프 = 일어서면서 점프 (머리 위가 막혔으면 선입력을 소비하지 않고 보류)
        if (c.HasHeadroom() && c.TryConsumeJump())
        {
            c.PerformJump();
            c.ChangeState(PlayerLocomotion.Airborne);
            return;
        }

        // 앉기 해제는 머리 위 공간이 확보돼야 한다
        if (!c.CrouchHeld && c.HasHeadroom()) { c.ChangeState(PlayerLocomotion.Grounded); return; }

        c.ApplyGroundMove(c.CrouchSpeed, c.GroundAccel * 0.7f, c.GroundFriction, dt);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  슬라이딩 — 운동량 보존이 핵심. 가속은 오직 내리막에서만 얻는다.
// ═══════════════════════════════════════════════════════════════════════════
public sealed class SlidingState : IPlayerLocomotionState
{
    public PlayerLocomotion Id => PlayerLocomotion.Sliding;

    private float _elapsed;

    public void Enter(PlayerController c, PlayerLocomotion from)
    {
        _elapsed = 0f;
        c.SetStance(PlayerStance.Slide);
        c.Motion.IsSprinting = false;

        // 진입 임펄스: 현재 진행 방향으로 한 번 밀어준다 (상한 있음).
        // 게시된 Motion 값이 아니라 강체를 직접 읽는다 — Enter가 어느 시점에 불려도 최신 속도를 쓴다.
        Vector3 raw = c.Rb.linearVelocity;
        Vector3 planar = new Vector3(raw.x, 0f, raw.z);
        Vector3 dir = planar.sqrMagnitude > 0.01f ? planar.normalized : c.transform.forward;
        float entry = Mathf.Min(planar.magnitude + c.SlideImpulse, c.SlideMaxSpeed);

        Vector3 v = dir * entry;
        v.y = raw.y;
        c.Rb.linearVelocity = v;

        c.Motion.SlideProgress = 0f;
        c.Motion.RaiseSlideStarted(entry);
    }

    public void Exit(PlayerController c, PlayerLocomotion to)
    {
        c.Motion.SlideProgress = 0f;
        c.MarkSlideCooldown();
        c.Motion.RaiseSlideEnded();
    }

    public void Tick(PlayerController c, float dt) { }
    public bool HandleInput(PlayerController c, in InputEvent e) => false;

    public void FixedTick(PlayerController c, float dt)
    {
        _elapsed += dt;
        c.Motion.SlideProgress = Mathf.Clamp01(_elapsed / c.SlideMaxDuration);

        if (!c.Motion.IsGrounded) { c.ChangeState(PlayerLocomotion.Airborne); return; }

        // 슬라이드 점프 — 슬라이드로 번 속도를 그대로 들고 뜬다 (연계 이동의 핵심)
        if (c.HasHeadroom() && c.TryConsumeJump())
        {
            c.PerformJump(c.SlideJumpBoost);
            c.ChangeState(PlayerLocomotion.Airborne);
            return;
        }

        Vector3 v = c.Rb.linearVelocity;
        Vector3 planar = new Vector3(v.x, 0f, v.z);
        float speed = planar.magnitude;
        Vector3 dir = speed > 0.01f ? planar / speed : c.transform.forward;

        // ① 경사 가속: 내리막 성분만큼 가속, 오르막이면 감속
        Vector3 slopeDown = Vector3.ProjectOnPlane(Vector3.down, c.Motion.GroundNormal);
        float slopeAlign = Vector3.Dot(dir, slopeDown);          // 내리막 +, 오르막 -
        speed += slopeAlign * c.SlideSlopeAccel * dt;

        // ② 마찰: 빠를수록 덜 깎인다(속도 제곱 항 없이 선형 + 하한)
        speed -= c.SlideFriction * dt;
        speed = Mathf.Clamp(speed, 0f, c.SlideMaxSpeed);

        // ③ 조향: 방향만 제한적으로 틀 수 있다. 속도를 "새로 만드는" 것은 불가
        Vector3 wish = c.WishDirWorld;
        if (wish.sqrMagnitude > 0.01f)
            dir = Vector3.RotateTowards(dir, wish, c.SlideSteerRate * Mathf.Deg2Rad * dt, 0f).normalized;

        Vector3 newPlanar = dir * speed;
        c.SetPlanarVelocityOnSlope(newPlanar);

        // ④ 종료 조건 — 느려졌거나, 시간이 다 됐거나, 앉기를 뗐거나
        bool tooSlow = speed < c.SlideMinSpeed;
        bool expired = _elapsed >= c.SlideMaxDuration;
        bool released = !c.CrouchHeld;

        if (tooSlow || expired || released)
        {
            bool stayDown = c.CrouchHeld || !c.HasHeadroom();
            c.ChangeState(stayDown ? PlayerLocomotion.Crouching : PlayerLocomotion.Grounded);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  체공 — 운동량은 유지하고, 공중 가속은 "이미 가진 속도를 넘지 않는 선"에서만
// ═══════════════════════════════════════════════════════════════════════════
public sealed class AirborneState : IPlayerLocomotionState
{
    public PlayerLocomotion Id => PlayerLocomotion.Airborne;

    public void Enter(PlayerController c, PlayerLocomotion from) { }
    public void Exit(PlayerController c, PlayerLocomotion to) { }
    public void Tick(PlayerController c, float dt) { }
    public bool HandleInput(PlayerController c, in InputEvent e) => false;

    public void FixedTick(PlayerController c, float dt)
    {
        c.Motion.IsSprinting = false;

        if (c.Motion.IsGrounded)
        {
            // 착지 순간의 분기: 앉기를 누른 채 빠르게 착지하면 곧바로 슬라이딩으로 이어진다
            if (c.CrouchHeld)
                c.ChangeState(c.CanEnterSlide ? PlayerLocomotion.Sliding : PlayerLocomotion.Crouching);
            else
                c.ChangeState(PlayerLocomotion.Grounded);
            return;
        }

        // 코요테 타임 안이면 공중에서도 점프를 허용 (밖이면 선입력을 남겨 착지 순간에 쓴다)
        if (c.CanCoyoteJump && c.TryConsumeJump())
        {
            c.PerformJump();
            return;
        }

        c.ApplyAirMove(dt);
        c.ApplyAirGravity(dt);
    }
}
