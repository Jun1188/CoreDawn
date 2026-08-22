using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 세이브 슬롯 목록 — 저장과 불러오기가 같은 화면을 공유한다.
///
/// 한 화면인 이유: 고르는 대상(슬롯)과 보여줄 정보(며칠째·플레이타임·저장 시각)가 같은데
/// 화면을 둘로 나누면 같은 목록을 두 벌 관리하게 된다. 무엇을 하려는 창인지는
/// 제목과 실행 버튼의 글자로 구분한다.
///
/// 줄을 그리고 고르는 일은 <see cref="SaveSlotList"/>가 맡는다 — 타이틀 화면도 같은 목록을 쓴다.
/// </summary>
[DefaultExecutionOrder(100)]
public class SaveLoadPanelView : UITKPopup
{
    public enum Mode { Load, Save }

    static SaveLoadPanelView cached;

    Mode mode = Mode.Load;
    readonly SaveSlotList slots = new();

    Label modeTag, modeTitle, emptyNote;
    ScrollView list;
    Button btnClose, btnCancel, btnConfirm, btnDelete;

    // ───────────────────────── 열기 ─────────────────────────

    /// <summary>씬에 이 패널이 있으면 열고 true. 없으면 false.</summary>
    public static bool TryOpen(Mode mode)
    {
        if (cached == null)
            cached = FindFirstObjectByType<SaveLoadPanelView>(FindObjectsInactive.Include);
        if (cached == null) return false;

        cached.mode = mode;
        cached.slots.Clear();

        // 이미 열려 있으면 SetActive(true)가 no-op이라 OnEnable이 불리지 않는다 —
        // 모드만 바꿔 다시 그린다 (다른 UITK 패널들과 같은 처리)
        if (cached.isActiveAndEnabled)
        {
            cached.ApplyMode();
            cached.Rebuild();
            return true;
        }

        cached.gameObject.SetActive(true);
        return true;
    }

    // ───────────────────── UITKPopup 계약 ─────────────────────

    protected override void Bind()
    {
        var r = Root;

        modeTag = r.Q<Label>("mode-tag");
        modeTitle = r.Q<Label>("mode-title");
        emptyNote = r.Q<Label>("empty-note");
        list = r.Q<ScrollView>("slot-list");

        btnClose = r.Q<Button>("btn-close");
        btnCancel = r.Q<Button>("btn-cancel");
        btnConfirm = r.Q<Button>("btn-confirm");
        btnDelete = r.Q<Button>("btn-delete");

        btnClose.clicked += Close;
        btnCancel.clicked += Close;
        btnConfirm.clicked += Confirm;
        btnDelete.clicked += DeleteSelected;

        slots.SelectionChanged += UpdateButtons;
        SaveManager.SlotsChanged += Rebuild;

        ApplyMode();
        Rebuild();
    }

    protected override void Unbind()
    {
        SaveManager.SlotsChanged -= Rebuild;
        slots.SelectionChanged -= UpdateButtons;

        if (btnClose != null) btnClose.clicked -= Close;
        if (btnCancel != null) btnCancel.clicked -= Close;
        if (btnConfirm != null) btnConfirm.clicked -= Confirm;
        if (btnDelete != null) btnDelete.clicked -= DeleteSelected;

        slots.Clear();
    }

    // ───────────────────────── 그리기 ─────────────────────────

    void ApplyMode()
    {
        bool save = mode == Mode.Save;

        if (modeTag != null) modeTag.text = save ? "SAVE" : "LOAD";
        if (modeTitle != null) modeTitle.text = save ? "저장" : "불러오기";
        if (btnConfirm != null) btnConfirm.text = save ? "저장" : "불러오기";
    }

    string emptyNoteDefault;   // 안내문으로 잠시 바꿔 쓰므로 원문을 기억해 둔다

    void Rebuild()
    {
        int usable = slots.Rebuild(list?.contentContainer, saveMode: mode == Mode.Save);

        if (emptyNote != null)
        {
            emptyNoteDefault ??= emptyNote.text;
            emptyNote.text = emptyNoteDefault;
            emptyNote.style.display = usable == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        UpdateButtons();
    }

    /// <summary>같은 자리에 사유를 띄운다 — 다시 고르면 Rebuild가 원래 안내문으로 되돌린다.</summary>
    void ShowNote(string message)
    {
        if (emptyNote == null) { Debug.LogWarning($"[SaveLoad] {message}"); return; }

        emptyNoteDefault ??= emptyNote.text;
        emptyNote.text = message;
        emptyNote.style.display = DisplayStyle.Flex;
    }

    void UpdateButtons()
    {
        bool has = !string.IsNullOrEmpty(slots.Selected);

        // 삭제는 내용이 있는 슬롯에만 — 빈 칸에 대고 누를 수 있으면 눌러보게 된다
        bool deletable = has && SaveStorage.Exists(slots.Selected);

        btnConfirm?.EnableInClassList("ui-btn--disabled", !has);
        btnDelete?.EnableInClassList("ui-btn--disabled", !deletable);
    }

    // ───────────────────────── 동작 ─────────────────────────

    void Confirm()
    {
        string slot = slots.Selected;
        if (string.IsNullOrEmpty(slot) || SaveManager.Instance == null) return;

        if (mode == Mode.Save)
        {
            // 실패(밤·디스크 오류)했는데 창이 닫히면 저장된 줄 안다 — 사유를 띄우고 열어 둔다
            if (!SaveManager.Instance.Save(slot))
            {
                SaveManager.Instance.CanSaveNow(out string reason);
                ShowNote(reason ?? "저장에 실패했습니다.");
                return;
            }
            Close();
            return;
        }

        // 불러오기는 씬을 갈아끼운다 — 창을 먼저 닫아 timeScale과 커서를 정상으로 되돌린다
        Close();
        SaveManager.Instance.Load(slot);
    }

    void DeleteSelected()
    {
        string slot = slots.Selected;
        if (string.IsNullOrEmpty(slot) || SaveManager.Instance == null) return;
        if (!SaveStorage.Exists(slot)) return;

        SaveManager.Instance.DeleteSlot(slot);
        slots.Clear();
        Rebuild();
    }
}
