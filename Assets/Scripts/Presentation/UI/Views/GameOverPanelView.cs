using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using CoreDawn.Combat;
using CoreDawn.DayTime;
using CoreDawn.Inputs;
using CoreDawn.Save;
using InputEvent = CoreDawn.Inputs.InputEvent;

namespace CoreDawn.UI
{
    /// <summary>
    /// 게임오버 화면 (UITK) — 코어가 파괴됐을 때 뜬다.
    ///
    /// 다른 팝업과 하나만 다르다: <b>닫히지 않는다.</b> ESC도, ✕도, "계속하기"도 없다.
    /// 코어가 없는 세계로 돌아가 봐야 할 수 있는 일이 없으므로, 이 창을 닫는 것은
    /// 플레이어에게 아무 선택지도 주지 않는 것과 같다. 나가는 길은 세 버튼뿐이다:
    /// 마지막 지점에서 다시 / 슬롯 골라 불러오기 / 타이틀로.
    ///
    /// 나머지 계약(입력 소유권, 커서 해제, 모달 스크림, 시간 정지)은 <see cref="UITKPopup"/>과
    /// <see cref="PauseMenuView"/>가 쓰는 것을 그대로 따른다.
    ///
    /// 열리는 경로: <see cref="GameOverPresenter"/>가 BattleManager.GameOver를 구독해
    /// <see cref="GameScreens.OpenGameOver"/>를 부른다. 이 패널 자신은 꺼져 있어 이벤트를
    /// 구독할 수 없기 때문이다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class GameOverPanelView : UITKPopup
    {
        static GameOverPanelView cached;

        Label headline, lede, statusDay, statusPlay, recent, error;
        Button btnRetry, btnLoad, btnTitle;

        /// <summary>세이브가 하나라도 있는가 — 복구 버튼 두 개를 켤지 정한다.</summary>
        bool hasSave;

        /// <summary>씬에 이 패널이 있으면 열고 true. 없으면 false — 호출부가 경고를 낸다.</summary>
        public static bool TryOpen()
        {
            if (cached == null)
                cached = FindFirstObjectByType<GameOverPanelView>(FindObjectsInactive.Include);
            if (cached == null) return false;

            // 이미 열려 있으면 SetActive(true)가 no-op이라 OnEnable이 불리지 않는다 (다른 패널과 같은 처리)
            if (cached.isActiveAndEnabled) { cached.Refresh(); return true; }

            cached.gameObject.SetActive(true);
            return true;
        }

        /// <summary>지금 열려 있는가.</summary>
        public static bool IsOpen => cached != null && cached.isActiveAndEnabled;

        /// <summary>씬에 이 패널이 존재하는가 (닫혀 있어도 true).</summary>
        public static bool ExistsInScene()
        {
            if (cached == null) cached = FindFirstObjectByType<GameOverPanelView>(FindObjectsInactive.Include);
            return cached != null;
        }

        // ───────────────────── UITKPopup 계약 ─────────────────────

        protected override void OnEnable()
        {
            base.OnEnable();

            // 게임오버 직전(코어가 터지고 화면이 뜨기까지의 몇 초)에 열어둔 일시정지 메뉴가
            // 뒤에 남아 있으면 안 된다 — 그 창의 "계속하기"는 이제 갈 곳이 없다.
            PauseMenuView.CloseIfOpen();

            // 일시정지와 같은 계약 — 창이 떠 있는 동안 세계가 멈춘다.
            // 게임오버는 여기서 한 번 멈추면 씬이 바뀔 때까지 다시 흐르지 않는다.
            Time.timeScale = 0f;
        }

        protected override void OnDisable()
        {
            Time.timeScale = 1f;
            base.OnDisable();
        }

        /// <summary>
        /// ESC를 삼키기만 하고 닫지 않는다. 소비하는 것이 중요하다 —
        /// 흘려보내면 뒤쪽 <see cref="PauseMenuHotkey"/>(우선순위 Fallback)가 받아
        /// 게임오버 위에 일시정지 메뉴가 열린다.
        /// </summary>
        public override bool OnInput(in InputEvent e)
        {
            if (e.Phase == InputActionPhase.Performed && e.Id == InputActionId.Cancel) return true;
            return base.OnInput(e);
        }

        /// <summary>닫을 수 없는 창이다. 나가는 길은 세 버튼뿐.</summary>
        public override void Close() { }

        protected override void Bind()
        {
            var r = Root;

            headline = r.Q<Label>("headline");
            lede = r.Q<Label>("lede");
            statusDay = r.Q<Label>("status-day");
            statusPlay = r.Q<Label>("status-play");
            recent = r.Q<Label>("recent");
            error = r.Q<Label>("error");

            btnRetry = r.Q<Button>("btn-retry");
            btnLoad = r.Q<Button>("btn-load");
            btnTitle = r.Q<Button>("btn-title");

            btnRetry.clicked += Retry;
            btnLoad.clicked += OpenLoad;
            btnTitle.clicked += ReturnToTitle;

            // 슬롯 창에서 세이브를 지우면 돌아갈 곳이 없어질 수 있다 — 그때 버튼이 따라 흐려져야 한다
            // (다른 슬롯 화면들과 같은 구독)
            SaveManager.SlotsChanged += Refresh;

            Refresh();
        }

        protected override void Unbind()
        {
            if (btnRetry != null) btnRetry.clicked -= Retry;
            if (btnLoad != null) btnLoad.clicked -= OpenLoad;
            if (btnTitle != null) btnTitle.clicked -= ReturnToTitle;
            SaveManager.SlotsChanged -= Refresh;
        }

        // ───────────────────────── 갱신 ─────────────────────────

        void Refresh()
        {
            SetError(null);

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

            var latest = SaveManager.Instance != null ? SaveManager.Instance.LatestMeta() : null;
            hasSave = latest != null;

            if (recent != null)
                recent.text = hasSave
                    ? $"마지막 기록 — Day {latest.DayNumber} · {latest.PlaytimeText} · {latest.SavedAtLocalText}"
                    : "저장된 기록이 없습니다. 타이틀에서 새로 시작해야 합니다.";

            // 눌러봐야 아무 일도 안 일어나는 버튼은 흐리게 — 타이틀 화면과 같은 문법
            btnRetry?.EnableInClassList("ui-btn--disabled", !hasSave);
            btnLoad?.EnableInClassList("ui-btn--disabled", !hasSave);

            // 세이브가 없으면 문구도 바꾼다. 돌아갈 곳이 없다는 것을 버튼 색만으로 알리지 않는다.
            if (lede != null)
                lede.text = hasSave
                    ? "공장은 멈췄다. 마지막 기록으로 돌아가거나 관제소로 나갈 수 있다."
                    : "공장은 멈췄다. 돌아갈 기록이 없어 관제소로 나가는 길만 남았다.";
        }

        void SetError(string message)
        {
            if (error == null) return;
            error.text = message ?? "";
            error.EnableInClassList("ui-hidden", string.IsNullOrEmpty(message));
        }

        // ───────────────────────── 동작 ─────────────────────────

        /// <summary>가장 최근 세이브로 바로 돌아간다 — 타이틀의 "이어하기"와 같은 경로.</summary>
        void Retry()
        {
            if (!hasSave) return;   // --disabled는 시각 표시일 뿐이라 여기서도 막는다

            // 시간을 먼저 되돌린다 — 로드가 실패해 화면이 남는 경우에도 0으로 굳지 않게
            Time.timeScale = 1f;

            if (SaveManager.Instance != null && SaveManager.Instance.LoadMostRecent()) return;

            Time.timeScale = 0f;
            SetError("세이브를 열지 못했습니다 — 저장된 씬이 Build Settings에 있는지 콘솔에서 확인하세요.");
        }

        /// <summary>
        /// 슬롯 목록을 연다. 이 창은 닫지 않는다 — 슬롯 창을 취소하면 여기로 돌아와야 하고,
        /// UIPopup의 depth 우선순위가 ESC를 위쪽 창에만 주므로 겹쳐 두는 편이 자연스럽다
        /// (일시정지 메뉴가 같은 창을 여는 방식 그대로).
        /// </summary>
        void OpenLoad()
        {
            if (!hasSave) return;

            if (!SaveLoadPanelView.TryOpen(SaveLoadPanelView.Mode.Load))
                SetError("씬에 SaveLoadPanel이 없어 불러오기 창을 열지 못했습니다.");
        }

        /// <summary>
        /// 타이틀로. <b>저장하지 않고</b> 나간다 (saveFirst: false).
        ///
        /// 기본값(true)으로 나가면 자동 저장 슬롯이 게임오버 상태로 덮어씌워지고,
        /// 타이틀의 "이어하기"가 그 죽은 세계로 들어간다. SaveManager 쪽에도 같은 취지의
        /// 가드(ShouldSuppressAutoSave)가 있지만, 여기서 의도를 명시적으로 적어 둔다 —
        /// 두 곳 중 하나가 사라져도 다른 하나가 남는다.
        /// </summary>
        void ReturnToTitle()
        {
            // 씬 전환 중에 timeScale이 0으로 남으면 타이틀이 멈춘 채로 뜬다
            Time.timeScale = 1f;
            SaveManager.Instance?.ReturnToTitle(saveFirst: false);
        }
    }
}
