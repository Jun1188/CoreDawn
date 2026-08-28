using UnityEngine;
using CoreDawn.Placement;

namespace CoreDawn.Inputs
{
    /// <summary>
    /// 창이 떠 있는 동안의 커서 소유권 — 참조 계수 한 곳으로 모은다.
    ///
    /// 왜 계수인가: 창은 겹쳐서 열린다(일시정지 위에 설정·불러오기, 인벤토리 위에 상자).
    /// 창마다 닫을 때 무조건 <c>Cursor.lockState = Locked</c>를 쓰면 <b>아래에 아직 떠 있는
    /// 창의 커서까지 같이 잠긴다</b> — 일시정지에서 설정을 열었다 닫으면 마우스가 사라지던
    /// 증상이 정확히 그것이었다. 마지막 하나가 닫힐 때만 잠근다.
    ///
    /// <see cref="PlacementSystem"/>이 <c>Cursor.lockState</c>로 "화면 중앙 조준 / 마우스 조준"을
    /// 가르므로 계수가 어긋나면 건설 조준까지 틀어진다. 그래서 커서를 만지는 자리를 여기 하나로
    /// 모았다 — 새 코드에서 <c>Cursor.lockState</c>를 직접 쓰지 말고 이 표면을 쓸 것.
    ///
    /// 씬 경계에서는 계수를 이어받지 않는다: 게임플레이 진입은 <see cref="ResetLocked"/>,
    /// 타이틀 같은 마우스 화면 진입은 <see cref="ResetFree"/>가 0으로 되돌린다. 창이 열린 채
    /// 씬이 바뀌어도 계수가 영영 남지 않게 하는 것이 이 두 표면의 존재 이유다.
    /// </summary>
    public static class UICursor
    {
        static int held;

        /// <summary>커서를 쓰는 창이 하나라도 열려 있는가.</summary>
        public static bool IsHeld => held > 0;

        /// <summary>창이 열렸다 — 커서를 푼다. 이미 풀려 있으면 계수만 올라간다.</summary>
        public static void Release()
        {
            held++;
            Apply(false);
        }

        /// <summary>창이 닫혔다 — <b>마지막 하나였을 때만</b> 다시 잠근다.</summary>
        public static void Restore()
        {
            if (held > 0) held--;
            if (held > 0) return;   // 아래에 아직 창이 있다 — 커서는 그쪽 것이다
            Apply(true);
        }

        /// <summary>게임플레이 진입 기준 상태 — 계수를 버리고 잠근다.</summary>
        public static void ResetLocked()
        {
            held = 0;
            Apply(true);
        }

        /// <summary>마우스로 조작하는 화면(타이틀 등) 진입 기준 상태 — 계수를 버리고 푼다.</summary>
        public static void ResetFree()
        {
            held = 0;
            Apply(false);
        }

        // UnityEngine.UIElements.Cursor와 이름이 겹쳐 정규화가 필요하다 — 호출부가 아니라
        // 여기서 한 번만 감당한다(UITKPopup이 매번 풀네임을 쓰던 것을 걷어낸 자리).
        static void Apply(bool locked)
        {
            UnityEngine.Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !locked;
        }

        // 도메인 리로드를 끈 플레이 모드에서는 static이 이전 실행의 값을 그대로 물고 온다 —
        // 계수가 1로 남은 채 시작하면 창을 다 닫아도 커서가 영영 잠기지 않는다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => held = 0;
    }
}
