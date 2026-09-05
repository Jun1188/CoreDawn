using UnityEngine;

namespace CoreDawn.FPS
{
    /// <summary>
    /// 무기 모션 모듈 — 스웨이·킥백·ADS처럼 무기 홀더를 움직이고 싶은 연출이 구현한다.
    /// transform을 직접 만지지 않고 오프셋만 노출하면, WeaponMotionManager가 전부 합산해
    /// 한 번만 쓴다 — 모듈끼리 서로 덮어쓰는 문제가 구조적으로 없다.
    /// </summary>
    public interface IWeaponMotionModule
    {
        Vector3 PositionOffset { get; }
        Quaternion RotationOffset { get; }
    }
}
