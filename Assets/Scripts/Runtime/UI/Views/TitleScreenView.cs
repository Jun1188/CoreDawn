using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 타이틀 화면 — New Game / 이어하기 / 불러오기의 진입점.
///
/// 다른 화면들과 달리 <see cref="UITKPopup"/>을 상속하지 않는다. 그쪽 계약은 씬에
/// InputManager가 있다고 전제하는데(UIPopup.OnEnable이 액션 맵을 Push한다), 타이틀 씬에는
/// 플레이어도 입력 파이프라인도 없다. 그래서 UIDocument를 직접 다룬다.
///
/// 같은 이유로 슬롯 목록도 별도 팝업이 아니라 같은 문서 안의 다른 페이지로 갈아끼운다.
/// </summary>
[RequireComponent(typeof(UIDocument))]
[DefaultExecutionOrder(100)]
public class TitleScreenView : MonoBehaviour
{
    [SerializeField] UIDocument document;

    readonly SaveSlotList slots = new();

    VisualElement menuPage, loadPage;
    Label recent, emptyNote;
    ScrollView list;
    Button btnContinue, btnNew, btnLoad, btnQuit;
    Button btnBack, btnBackX, btnConfirm, btnDelete;

    void Awake()
    {
        if (document == null) document = GetComponent<UIDocument>();
    }

    void OnEnable()
    {
        var root = document != null ? document.rootVisualElement : null;
        if (root == null)
        {
            Debug.LogError("[TitleScreenView] UIDocument.rootVisualElement가 null입니다 — " +
                           "Source Asset(TitleScreen.uxml)과 Panel Settings를 확인하세요.", this);
            return;
        }

        // 타이틀에서는 마우스를 써야 한다 — 게임플레이 씬에서 잠가둔 채로 넘어올 수 있다.
        // (UnityEngine.UIElements.Cursor와 이름이 겹쳐 정규화가 필요하다 — UITKPopup과 같은 사정)
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        Time.timeScale = 1f;

        menuPage = root.Q("menu-page");
        loadPage = root.Q("load-page");
        recent = root.Q<Label>("recent");
        emptyNote = root.Q<Label>("empty-note");
        list = root.Q<ScrollView>("slot-list");

        btnContinue = root.Q<Button>("btn-continue");
        btnNew = root.Q<Button>("btn-new");
        btnLoad = root.Q<Button>("btn-load");
        btnQuit = root.Q<Button>("btn-quit");

        btnBack = root.Q<Button>("btn-back");
        btnBackX = root.Q<Button>("btn-back-x");
        btnConfirm = root.Q<Button>("btn-confirm");
        btnDelete = root.Q<Button>("btn-delete");

        btnContinue.clicked += ContinueGame;
        btnNew.clicked += NewGame;
        btnLoad.clicked += ShowLoadPage;
        btnQuit.clicked += QuitGame;

        btnBack.clicked += ShowMenuPage;
        btnBackX.clicked += ShowMenuPage;
        btnConfirm.clicked += LoadSelected;
        btnDelete.clicked += DeleteSelected;

        slots.SelectionChanged += UpdateLoadButtons;
        SaveManager.SlotsChanged += RebuildSlots;

        ShowMenuPage();
    }

    void OnDisable()
    {
        SaveManager.SlotsChanged -= RebuildSlots;
        slots.SelectionChanged -= UpdateLoadButtons;

        if (btnContinue != null) btnContinue.clicked -= ContinueGame;
        if (btnNew != null) btnNew.clicked -= NewGame;
        if (btnLoad != null) btnLoad.clicked -= ShowLoadPage;
        if (btnQuit != null) btnQuit.clicked -= QuitGame;

        if (btnBack != null) btnBack.clicked -= ShowMenuPage;
        if (btnBackX != null) btnBackX.clicked -= ShowMenuPage;
        if (btnConfirm != null) btnConfirm.clicked -= LoadSelected;
        if (btnDelete != null) btnDelete.clicked -= DeleteSelected;

        slots.Clear();
    }

    // ───────────────────────── 페이지 ─────────────────────────

    void ShowMenuPage()
    {
        SetPage(menu: true);

        // 이어할 것이 없으면 버튼을 흐리게 — 눌러봐야 아무 일도 안 일어나는 버튼은 두지 않는다
        var latest = LatestMeta();
        bool has = latest != null;

        btnContinue?.EnableInClassList("ui-btn--disabled", !has);
        btnLoad?.EnableInClassList("ui-btn--disabled", !has);

        if (recent != null)
            recent.text = has
                ? $"최근 기록 — {latest.SceneName} · Day {latest.DayNumber} · {latest.PlaytimeText} · {latest.SavedAtLocalText}"
                : "저장된 게임이 없습니다. 새 게임으로 시작하세요.";
    }

    void ShowLoadPage()
    {
        if (LatestMeta() == null) return;   // 고를 것이 없으면 들어가지 않는다

        SetPage(menu: false);
        slots.Clear();
        RebuildSlots();
    }

    void SetPage(bool menu)
    {
        if (menuPage != null) menuPage.style.display = menu ? DisplayStyle.Flex : DisplayStyle.None;
        if (loadPage != null) loadPage.style.display = menu ? DisplayStyle.None : DisplayStyle.Flex;
    }

    void RebuildSlots()
    {
        int usable = slots.Rebuild(list?.contentContainer, saveMode: false);

        if (emptyNote != null)
            emptyNote.style.display = usable == 0 ? DisplayStyle.Flex : DisplayStyle.None;

        UpdateLoadButtons();
    }

    void UpdateLoadButtons()
    {
        bool has = !string.IsNullOrEmpty(slots.Selected);
        btnConfirm?.EnableInClassList("ui-btn--disabled", !has);
        btnDelete?.EnableInClassList("ui-btn--disabled", !has);
    }

    /// <summary>가장 최근 세이브의 요약. 하나도 없으면 null.</summary>
    static SaveMeta LatestMeta() => SaveManager.Instance != null ? SaveManager.Instance.LatestMeta() : null;

    // ───────────────────────── 동작 ─────────────────────────

    void NewGame() => SaveManager.Instance?.NewGame();

    void ContinueGame()
    {
        if (SaveManager.Instance == null) return;
        if (!SaveManager.Instance.LoadMostRecent())
            Debug.LogWarning("[Title] 이어할 세이브가 없습니다.");
    }

    void LoadSelected()
    {
        if (string.IsNullOrEmpty(slots.Selected) || SaveManager.Instance == null) return;
        SaveManager.Instance.Load(slots.Selected);
    }

    void DeleteSelected()
    {
        if (string.IsNullOrEmpty(slots.Selected) || SaveManager.Instance == null) return;

        SaveManager.Instance.DeleteSlot(slots.Selected);
        slots.Clear();
        RebuildSlots();
    }

    static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
