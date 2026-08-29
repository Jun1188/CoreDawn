using System;
using UnityEngine;

namespace CoreDawn.FPS
{
    public enum PlayerStance { Stand, Crouch, Slide }

    public enum PlayerLocomotion { Grounded, Crouching, Sliding, Airborne }

    /// <summary>
    /// 플레이어 운동 상태의 <b>단일 게시판</b>.
    ///
    /// PlayerController가 매 프레임 여기에 쓰고, 카메라 리그·카메라 모션 모듈·무기 모션 모듈이
    /// 여기서만 읽는다. 이전 구조에서 각 모듈이 제각각 <c>Input.GetAxisRaw</c>(구 입력 시스템)를
    /// 읽고 <c>Physics.Raycast</c>로 접지를 재판정하던 "억지 고리"를 이 한 지점으로 대체한다.
    ///
    /// 규칙
    ///  - 쓰기는 PlayerController(및 명시적으로 위임받은 컴포넌트)만 한다.
    ///  - 읽는 쪽은 Update/LateUpdate 어디서 읽어도 같은 값을 본다 (컨트롤러가 Update 초반에 갱신).
    ///  - 이산 사건(점프/착지/슬라이드 시작)은 이벤트로 알린다 — 폴링으로 엣지를 추측하지 말 것.
    /// </summary>
    public sealed class PlayerMotionState
    {
        // ── 운동학 ───────────────────────────────────────────────────────────
        /// <summary>월드 속도 (rb.linearVelocity 사본).</summary>
        public Vector3 Velocity;
        /// <summary>수평 성분만.</summary>
        public Vector3 PlanarVelocity;
        /// <summary>플레이어 요(yaw) 기준 로컬 속도. x=우측, y=상하, z=전방.</summary>
        public Vector3 LocalVelocity;
        /// <summary>로컬 가속도(스무딩됨). 카메라/무기 관성 기울기의 입력.</summary>
        public Vector3 LocalAcceleration;
        /// <summary>수평 속력(m/s).</summary>
        public float Speed;
        /// <summary>Speed / ReferenceSpeed — 0~1 정규화(스프린트 시 1 초과 가능).</summary>
        public float SpeedRatio;
        /// <summary>정규화 기준 속도(= 보행 속도). 모듈이 자기 상수를 갖지 않게 하기 위함.</summary>
        public float ReferenceSpeed = 5f;

        // ── 의도(입력) ───────────────────────────────────────────────────────
        public Vector2 MoveInput;
        /// <summary>로컬 기준 이동 의도 방향(정규화). 입력이 없으면 zero.</summary>
        public Vector3 WishDirLocal;
        /// <summary>이번 프레임 실제로 적용된 시점 회전량(도). x=요, y=피치(위가 +).</summary>
        public Vector2 LookDelta;
        /// <summary>LookDelta의 EMA — 스웨이/틸트용. 프레임 스파이크를 먹지 않는다.</summary>
        public Vector2 LookDeltaSmooth;

        // ── 접지 ─────────────────────────────────────────────────────────────
        public bool IsGrounded;
        public Vector3 GroundNormal = Vector3.up;
        public float GroundSlopeAngle;
        /// <summary>체공 누적 시간(접지 중엔 0).</summary>
        public float TimeInAir;
        /// <summary>착지 직후 경과 시간.</summary>
        public float TimeSinceLanded;

        // ── 자세 ─────────────────────────────────────────────────────────────
        public PlayerStance Stance = PlayerStance.Stand;
        public PlayerLocomotion Locomotion = PlayerLocomotion.Grounded;
        /// <summary>0 = 완전 기립, 1 = 완전 앉음. 콜라이더 높이에서 유도된 연속값.</summary>
        public float CrouchWeight;
        /// <summary>0~1로 이징된 슬라이딩 가중치. 카메라 롤/무기 포즈가 여기 비례.</summary>
        public float SlideWeight;
        /// <summary>슬라이드 경과 비율 0~1.</summary>
        public float SlideProgress;
        public bool IsSprinting;
        public bool IsSliding => Stance == PlayerStance.Slide;
        /// <summary>ADS 가중치 0~1. WeaponADS가 게시한다 — 카메라 FOV/스웨이 억제의 단일 소스.</summary>
        public float AimWeight;

        // ── 보행 위상 (카메라 보브 ↔ 무기 보브 동기화) ────────────────────────
        /// <summary>발걸음 위상(라디안). 카메라와 무기가 같은 값을 써야 서로 어긋나지 않는다.</summary>
        public float StrideCycle;
        /// <summary>보브 진폭 스케일 0~1 (속도·자세·접지 여부 반영).</summary>
        public float StrideAmplitude;

        // ── 이산 사건 ────────────────────────────────────────────────────────
        /// <summary>점프 발생. 인자 = 도약 속도(m/s).</summary>
        public event Action<float> Jumped;
        /// <summary>착지. 인자 = 충격 강도 0~1.</summary>
        public event Action<float> Landed;
        /// <summary>자세 전환. (이전, 이후)</summary>
        public event Action<PlayerStance, PlayerStance> StanceChanged;
        /// <summary>슬라이드 시작. 인자 = 진입 속력.</summary>
        public event Action<float> SlideStarted;
        public event Action SlideEnded;
        /// <summary>발이 땅에 닿는 순간. 인자 = 세기 0~1. 발소리/미세 흔들림용.</summary>
        public event Action<float> Stepped;

        internal void RaiseJumped(float launchSpeed) => Jumped?.Invoke(launchSpeed);
        internal void RaiseLanded(float impact) => Landed?.Invoke(impact);
        internal void RaiseStanceChanged(PlayerStance from, PlayerStance to) => StanceChanged?.Invoke(from, to);
        internal void RaiseSlideStarted(float entrySpeed) => SlideStarted?.Invoke(entrySpeed);
        internal void RaiseSlideEnded() => SlideEnded?.Invoke();
        internal void RaiseStepped(float strength) => Stepped?.Invoke(strength);

        /// <summary>플레이어가 파괴/비활성될 때 구독을 끊는다 (모듈이 유령 참조를 붙들지 않게).</summary>
        internal void ClearSubscribers()
        {
            Jumped = null; Landed = null; StanceChanged = null;
            SlideStarted = null; SlideEnded = null; Stepped = null;
        }
    }

    /// <summary>
    /// 모션 상태를 제공하는 쪽(=PlayerController)의 계약.
    /// 카메라·무기 모듈은 <c>GetComponentInParent&lt;IPlayerMotionProvider&gt;()</c>로 찾는다 —
    /// 구체 타입 대신 이 인터페이스에 의존하므로 나중에 탈것/관전 카메라가 끼어들어도 모듈은 그대로다.
    /// </summary>
    public interface IPlayerMotionProvider
    {
        PlayerMotionState Motion { get; }
        Transform MotionRoot { get; }
    }
}
