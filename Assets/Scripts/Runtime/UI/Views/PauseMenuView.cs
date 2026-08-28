using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.DayTime;
using CoreDawn.Save;

namespace CoreDawn.UI
{
    /// <summary>
    /// 일시정지 메뉴 (UITK). 구 uGUI 사이드 패널(PauseMenu_Panel)을 대신한다.
    ///
    /// 입력·커서·중첩 창 우선순위는 <see cref="UITKPopup"/>이 이미 갖고 있으므로 여기서는
    /// 시간 정지와 버튼 배선만 맡는다. 구 <c>PausePopup</c>이 하던 일과 같은 범위다.
    ///
    /// 열리는 경로: <see cref="PauseMenuHotkey"/>(Fallback 리시버)가 ESC를 받아 연다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class PauseMenuView : UITKPopup
    {
        static PauseMenuView cached;

        Label statusDay, statusPlay;
        Button btnClose, btnResume, btnSave, btnLoad, btnSettings, btnTitle;

        /// <summary>씬에 이 패널이 있으면 열고 true. 없으면 false — 호출부가 구 uGUI로 넘어간다.</summary>
        public static bool TryOpen()
        {
            if (cached == null)
                cached = FindFirstObjectByType<PauseMenuView>(FindObjectsInactive.Include);
            if (cached == null) return false;

            if (cached.isActiveAndEnabled) { cached.Refresh(); return true; }

            cached.gameObject.SetActive(true);
            return true;
        }

        /// <summary>지금 열려 있는가 — 구 uGUI 패널을 함께 닫아야 하는지 판단하는 데 쓴다.</summary>
        public static bool IsOpen => cached != null && cached.isActiveAndEnabled;

        /// <summary>열려 있으면 닫는다. ESC는 UIPopup이 직접 처리하므로 이 경로는 버튼·외부 호출용이다.</summary>
        public static void CloseIfOpen()
        {
            if (IsOpen) cached.Close();
        }

        /// <summary>씬에 이 패널이 존재하는가 (닫혀 있어도 true).</summary>
        public static bool ExistsInScene()
        {
            if (cached == null) cached = FindFirstObjectByType<PauseMenuView>(FindObjectsInactive.Include);
            return cached != null;
        }

        // ───────────────────── UITKPopup 계약 ─────────────────────

        protected override void OnEnable()
        {
            base.OnEnable();
            // 구 PausePopup과 같은 계약 — 창이 떠 있는 동안 세계가 멈춘다
            Time.timeScale = 0f;
        }

        protected override void OnDisable()
        {
            Time.timeScale = 1f;
            base.OnDisable();
        }

        protected override void Bind()
        {
            var r = Root;

            statusDay = r.Q<Label>("status-day");
            statusPlay = r.Q<Label>("status-play");

            btnClose = r.Q<Button>("btn-close");
            btnResume = r.Q<Button>("btn-resume");
            btnSave = r.Q<Button>("btn-save");
            btnLoad = r.Q<Button>("btn-load");
            btnSettings = r.Q<Button>("btn-settings");
            btnTitle = r.Q<Button>("btn-title");

            btnClose.clicked += Close;
            btnResume.clicked += Close;
            btnSave.clicked += OpenSave;
            btnLoad.clicked += OpenLoad;
            btnSettings.clicked += OpenSettings;
            btnTitle.clicked += ReturnToTitle;

            Refresh();
        }

        protected override void Unbind()
        {
            if (btnClose != null) btnClose.clicked -= Close;
            if (btnResume != null) btnResume.clicked -= Close;
            if (btnSave != null) btnSave.clicked -= OpenSave;
            if (btnLoad != null) btnLoad.clicked -= OpenLoad;
            if (btnSettings != null) btnSettings.clicked -= OpenSettings;
            if (btnTitle != null) btnTitle.clicked -= ReturnToTitle;
        }

        // ───────────────────────── 동작 ─────────────────────────

        void Refresh()
        {
            var cycle = TimeManager.Instance != null ? TimeManager.Instance.Cycle : null;
            if (statusDay != null)
                statusDay.text = cycle != null
                    ? $"Day {cycle.DayNumber} · {(cycle.Phase == DayPhase.Day ? "낮" : "밤")}"
                    : "Day —";

            if (statusPlay != null)
            {
                double sec = SaveManager.Instance != null ? SaveManager.Instance.Playtime : 0;
                var t = System.TimeSpan.FromSeconds(sec);
                statusPlay.text = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
            }
        }

        /// <summary>
        /// 슬롯 목록을 연다. 이 창은 닫지 않는다 — 슬롯 창을 취소하면 여기로 돌아와야 하고,
        /// UIPopup의 depth 우선순위가 ESC를 위쪽 창에만 주므로 겹쳐 두는 편이 자연스럽다.
        /// </summary>
        void OpenSave()
        {
            if (!SaveLoadPanelView.TryOpen(SaveLoadPanelView.Mode.Save))
                Debug.LogWarning("[UI] 씬에 SaveLoadPanel이 없어 저장 창을 열지 못했습니다.");
        }

        void OpenLoad()
        {
            if (!SaveLoadPanelView.TryOpen(SaveLoadPanelView.Mode.Load))
                Debug.LogWarning("[UI] 씬에 SaveLoadPanel이 없어 불러오기 창을 열지 못했습니다.");
        }

        /// <summary>설정 창. 슬롯 창과 같은 규칙으로 이 창을 닫지 않고 그 위에 겹쳐 띄운다.</summary>
        void OpenSettings()
        {
            if (!SettingsPanelView.TryOpen())
                Debug.LogWarning("[UI] 씬에 SettingsPanel이 없어 설정 창을 열지 못했습니다.");
        }

        void ReturnToTitle()
        {
            // 시간을 먼저 되돌린다 — 씬 전환 중에 timeScale이 0으로 남으면 타이틀이 멈춘 채로 뜬다
            Time.timeScale = 1f;
            Close();
            SaveManager.Instance?.ReturnToTitle(saveFirst: true);
        }
    }
}
