using UnityEngine;

namespace CoreDawn.FPS
{
    /// <summary>
    /// 카메라 절차적 모션 모듈. <see cref="IWeaponMotionModule"/>과 같은 계약을 카메라 쪽에 세운 것.
    ///
    /// 모듈은 transform을 직접 만지지 않고 "원점 기준 순수 오프셋"만 계산해 내놓는다.
    /// 합성은 <see cref="CameraMotionManager"/>가 전담 — 두 모듈이 같은 transform을
    /// 번갈아 덮어써서 서로를 지우던 사고(구 ProceduralRecoil ↔ CameraShakeManager)를 구조적으로 막는다.
    /// </summary>
    public interface ICameraMotionModule
    {
        /// <summary>합성 순서 힌트. 낮을수록 먼저(안쪽). 합산이라 대개 무의미하지만 로깅/디버그에 쓴다.</summary>
        int MotionOrder { get; }

        /// <summary>부모 로컬 공간의 위치 오프셋(m).</summary>
        Vector3 PositionOffset { get; }

        /// <summary>부모 로컬 공간의 회전 오프셋.</summary>
        Quaternion RotationOffset { get; }
    }
}
