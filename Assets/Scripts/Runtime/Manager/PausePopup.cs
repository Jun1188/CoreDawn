using UnityEngine;
using UnityEngine.UIElements;
/// <summary>
/// 일시정지 패널(pausePanel)에 부착하는 파이프라인 어댑터.
/// timeScale·커서 처리를 패널의 Enter/Exit(활성/비활성)에 집중시킨다 —
/// 어떤 경로로 열리든(ESC, UI 버튼) 부수 상태가 항상 일관된다.
/// 열기는 SystemUIManager(fallback 리시버), 닫기는 UIPopup 기본 Cancel 처리.
/// </summary>
public class PausePopup : UIPopup
{
    protected override void OnEnable()
    {
        base.OnEnable();   // 리시버 등록 + UI 맵 Push
        Time.timeScale = 0f;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

    }

    protected override void OnDisable()
    {
        Time.timeScale = 1f;
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;
        base.OnDisable();  // 리시버 해제 + UI 맵 Pop
    }
}