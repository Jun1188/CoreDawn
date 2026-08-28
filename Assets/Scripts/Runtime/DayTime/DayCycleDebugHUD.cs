using UnityEngine;

/// <summary>
/// 낮/밤 주기 수동 테스트용 HUD.
/// TimeSystemTest 씬에서 빈 GameObject에 부착 — 현재 페이즈/남은 시간 표시,
/// 배속·밤 조기 종료 버튼 제공. TimeManager가 씬에 있어야 한다.
/// </summary>
public class DayCycleDebugHUD : MonoBehaviour
{
    void OnGUI()
    {
        var tm = TimeManager.Instance;
        if (tm == null)
        {
            GUI.TextArea(new Rect(20, 20, 300, 40), "TimeManager가 씬에 없습니다.");
            return;
        }

        var c = tm.Cycle;
        string phase = c.Phase == DayPhase.Day ? "☀️ 낮 (건설)" : "🌙 밤 (디펜스)";
        GUI.TextArea(new Rect(20, 20, 300, 80),
            $"Day {c.DayNumber}  {phase}\n" +
            $"남은 시간: {c.PhaseRemaining:F1}s / {c.PhaseDuration:F0}s\n" +
            $"진행도: {c.PhaseProgress01:P0}   시각(0~1): {c.NormalizedTimeOfDay:F2}\n" +
            $"배속: x{Time.timeScale:F0}");

        if (GUI.Button(new Rect(20, 110, 145, 30), "배속 x1 / x5"))
            Time.timeScale = Time.timeScale >= 5f ? 1f : 5f;

        if (GUI.Button(new Rect(175, 110, 145, 30), "밤 조기 종료"))
            tm.EndNightEarly();
    }
}