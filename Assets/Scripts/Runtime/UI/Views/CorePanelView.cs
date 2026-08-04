using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// SCR-01 코어 패널 — 납품 탭. UITK 이관 1번 화면 (레퍼런스 문서 §05 이관 순서).
///
/// 이 화면이 검증하는 것: 패널·탭·칩·진행바·표·스테퍼·버튼·단계 레일이
/// 한 화면 안에서 같이 성립하는가. 여기서 components.uss가 확정된다.
///
/// 갱신 규칙 — 문서 §05 "Update 폴링 금지":
///   행 생성은 요구 부품 구성이 바뀔 때만(= 수리 단계 변경). 값 변화는 제자리 갱신.
///   트리거는 컨테이너의 Changed 이벤트뿐이고 Update()는 쓰지 않는다.
/// </summary>
[DefaultExecutionOrder(100)]
public class CorePanelView : UITKPopup
{
    static CorePanelView cached;

    CoreBehavior target;

    // 정적 요소 (UXML)
    VisualElement rail, rows, gateFill, actionsChips, complete, table, gate, actions;
    Label gateName, gateCount, gatePct, actionsLabel, deliverLabel;
    Button btnClose, btnAll, btnDeliver, tabDeliver, tabInfo;

    // SCR-01b 수리 확인창
    VisualElement confirmScrim, confirmUnlocks, confirmWarn;
    Label confirmGate, confirmName, confirmDesc, confirmUnlocksLabel, confirmWarnTitle, confirmWarnBody;
    Button confirmOk, confirmCancel, confirmX;

    // 코어 정보 탭
    VisualElement viewDeliver, viewInfo, hpFill, repairFill, radarSlot, radarChip;
    Label hpText, hpMax, repairText, repairMax, radarChipText;
    Label waveNext, waveNumber, waveIncoming, waveNests;
    RadarScope radar;
    BuildingEntity coreEntity;   // 내구도 원본 — 심(Building)이 아니라 씬 껍데기가 갖고 있다

    /// <summary>레이더 해금 단계. 설계상 게이트②(항법·제어 복구) 완료 시 켜진다.</summary>
    const int RadarUnlockTier = 2;

    [Header("개발용")]
    [Tooltip("체크하면 수리 단계와 무관하게 레이더를 '가동 중'으로 본다.\n" +
             "해금 후 화면을 확인하려고 코어를 2단계까지 올릴 필요가 없게 하는 스위치다. " +
             "빌드에 켠 채로 넘기지 말 것.")]
    [SerializeField] bool debugForceRadarUnlocked;

#if UNITY_EDITOR
    /// <summary>인스펙터에서 토글을 만지면 열려 있는 패널에 즉시 반영한다.</summary>
    void OnValidate()
    {
        if (Application.isPlaying && isActiveAndEnabled && infoTabActive) RefreshInfo();
    }
#endif

    readonly List<Row> builtRows = new();
    int builtForTier = -1;

    // 구독 해제를 위해 붙잡아 두는 참조 — Bind 시점의 것과 같아야 한다
    ItemContainer subCore, subHotbar, subBag;

    // ───────────────────────── 열기 ─────────────────────────

    /// <summary>
    /// 씬에 이 패널이 있으면 열고 true. 없으면 false —
    /// 호출부(CoreBehavior)가 기존 uGUI 경로로 넘어갈 수 있게 한다.
    /// </summary>
    /// <summary>
    /// 이 씬이 UITK 코어 패널을 갖고 있는가 — 즉 수리 확인창을 띄울 수 있는가.
    /// CoreBehavior가 자동 진행을 멈출지 판단할 때 쓴다 (SCR-01b).
    /// 비활성 오브젝트까지 찾고 결과를 캐시하므로 매 틱 불려도 된다.
    /// </summary>
    public static bool ExistsInScene()
    {
        if (cached == null)
            cached = FindFirstObjectByType<CorePanelView>(FindObjectsInactive.Include);
        return cached != null;
    }

    public static bool TryOpen(CoreBehavior core)
    {
        if (core == null) return false;
        if (!ExistsInScene()) return false;

        // 이미 열려 있으면 SetActive(true)가 아무 일도 하지 않아 OnEnable(=Bind)이 뜨지 않는다.
        // 다른 코어를 열 때와, 씬이 패널을 켠 채로 시작한 경우가 여기 해당한다 —
        // 그냥 두면 이전 코어(혹은 빈 화면)를 계속 보여준다.
        if (cached.isActiveAndEnabled)
        {
            cached.Retarget(core);
            return true;
        }

        cached.target = core;              // OnEnable → Bind 전에 넣어야 한다
        cached.gameObject.SetActive(true);
        return true;
    }

    /// <summary>열려 있는 패널을 다른 코어로 다시 묶는다. 구독을 갈아끼우고 행을 새로 만든다.</summary>
    void Retarget(CoreBehavior core)
    {
        Unsubscribe();
        target = core;
        Subscribe();

        builtForTier = -1;   // 코어가 다르면 요구 부품 구성도 다르다
        Refresh();
    }

    // ───────────────────── UITKPopup 계약 ─────────────────────

    protected override void Bind()
    {
        CacheElements();

        btnClose.clicked += Close;
        btnAll.clicked += SelectAll;
        btnDeliver.clicked += OnPrimaryAction;
        tabDeliver.clicked += ShowDeliverTab;
        tabInfo.clicked += ShowInfoTab;

        confirmOk.clicked += ConfirmRepair;
        confirmCancel.clicked += CloseConfirm;
        confirmX.clicked += CloseConfirm;

        CloseConfirm();
        ShowDeliverTab();
        Subscribe();
        builtForTier = -1;   // 강제 재구성
        Refresh();
    }

    protected override void Unbind()
    {
        Unsubscribe();

        if (btnClose != null) btnClose.clicked -= Close;
        if (btnAll != null) btnAll.clicked -= SelectAll;
        if (btnDeliver != null) btnDeliver.clicked -= OnPrimaryAction;
        if (tabDeliver != null) tabDeliver.clicked -= ShowDeliverTab;
        if (tabInfo != null) tabInfo.clicked -= ShowInfoTab;

        if (confirmOk != null) confirmOk.clicked -= ConfirmRepair;
        if (confirmCancel != null) confirmCancel.clicked -= CloseConfirm;
        if (confirmX != null) confirmX.clicked -= CloseConfirm;

        target = null;
    }

    // ───────────────────────── 탭 ─────────────────────────

    bool infoTabActive;

    void ShowDeliverTab() => SelectTab(true);
    void ShowInfoTab() => SelectTab(false);

    void SelectTab(bool deliver)
    {
        infoTabActive = !deliver;
        Show(viewDeliver, deliver);
        Show(viewInfo, infoTabActive);
        ToggleClass(tabDeliver, "ui-tab--active", deliver);
        ToggleClass(tabInfo, "ui-tab--active", infoTabActive);
        if (infoTabActive) RefreshInfo();
    }

    void CacheElements()
    {
        var r = Root;
        rail = r.Q("rail");
        rows = r.Q("rows");
        table = r.Q("table");
        gate = r.Q("gate");
        actions = r.Q("actions");
        complete = r.Q("complete");
        actionsChips = r.Q("actions-chips");
        gateFill = r.Q("gate-fill");

        gateName = r.Q<Label>("gate-name");
        gateCount = r.Q<Label>("gate-count");
        gatePct = r.Q<Label>("gate-pct");
        actionsLabel = r.Q<Label>("actions-label");
        deliverLabel = r.Q<Label>("btn-deliver-label");

        btnClose = r.Q<Button>("btn-close");
        btnAll = r.Q<Button>("btn-all");
        btnDeliver = r.Q<Button>("btn-deliver");
        tabDeliver = r.Q<Button>("tab-deliver");
        tabInfo = r.Q<Button>("tab-info");

        viewDeliver = r.Q("view-deliver");
        viewInfo = r.Q("view-info");
        hpFill = r.Q("hp-fill");
        repairFill = r.Q("repair-fill");
        hpText = r.Q<Label>("hp-text");
        hpMax = r.Q<Label>("hp-max");
        repairText = r.Q<Label>("repair-text");
        repairMax = r.Q<Label>("repair-max");

        radarSlot = r.Q("radar-slot");

        confirmScrim        = r.Q("confirm-scrim");
        confirmGate         = r.Q<Label>("confirm-gate");
        confirmName         = r.Q<Label>("confirm-name");
        confirmDesc         = r.Q<Label>("confirm-desc");
        confirmUnlocksLabel = r.Q<Label>("confirm-unlocks-label");
        confirmUnlocks      = r.Q("confirm-unlocks");
        confirmWarn         = r.Q("confirm-warn");
        confirmWarnTitle    = r.Q<Label>("confirm-warn-title");
        confirmWarnBody     = r.Q<Label>("confirm-warn-body");
        confirmOk           = r.Q<Button>("confirm-ok");
        confirmCancel       = r.Q<Button>("confirm-cancel");
        confirmX            = r.Q<Button>("confirm-x");
        radarChip = r.Q("radar-chip");
        radarChipText = r.Q<Label>("radar-chip-text");
        waveNext = r.Q<Label>("wave-next");
        waveNumber = r.Q<Label>("wave-number");
        waveIncoming = r.Q<Label>("wave-incoming");
        waveNests = r.Q<Label>("wave-nests");

        // Painter2D로 그리는 요소라 UXML이 아니라 코드에서 붙인다 (한 번만)
        if (radar == null && radarSlot != null)
        {
            radar = new RadarScope();
            radarSlot.Add(radar);
        }
    }

    // ─────────────────── 코어 정보 탭 (SCR-01c) ───────────────────

    /// <summary>씬의 코어 껍데기를 찾는다 — 내구도는 심(Building)이 아니라 BuildingEntity가 원본이다.</summary>
    static BuildingEntity FindCoreEntity()
    {
        foreach (var e in BuildingEntity.All)
            if (e != null && e.IsCore) return e;
        return null;
    }

    void RefreshInfo()
    {
        if (target == null) return;

        // 코어 내구도
        if (coreEntity == null) coreEntity = FindCoreEntity();
        var health = coreEntity != null ? coreEntity.Health : null;

        if (health != null && health.MaxHealth > 0f)
        {
            int cur = Mathf.CeilToInt(health.CurrentHealth);
            int max = Mathf.CeilToInt(health.MaxHealth);
            hpText.text = cur.ToString("N0");
            hpMax.text = $"/ {max:N0}";
            SetBarFill(hpFill, health.CurrentHealth / health.MaxHealth);
        }
        else
        {
            // 코어가 씬에 없거나 아직 배치 전 — 0으로 속이지 않고 모름을 표시한다
            hpText.text = "—";
            hpMax.text = "";
            SetBarFill(hpFill, 0f);
        }

        // 수리 진행
        int done = target.CurrentTierIndex;
        int total = target.TierCount;
        repairText.text = total > 0 ? $"{done}단계" : "—";
        repairMax.text = total > 0 ? $"/ {total}" : "";
        SetBarFill(repairFill, total > 0 ? (float)done / total : 0f);

        bool radarUnlocked = done >= RadarUnlockTier || debugForceRadarUnlocked;
        RefreshWaveDummy(radarUnlocked);
        RefreshRadar(radarUnlocked);
    }

    /// <summary>
    /// 웨이브 시스템이 없어 전부 더미다 — <see cref="CoreInfoDummyData"/> 참조.
    /// 다만 규모(INCOMING)와 둥지 수는 레이더가 알려주는 값이라,
    /// 레이더가 꺼져 있으면 아는 척하지 않고 ???로 둔다.
    /// </summary>
    void RefreshWaveDummy(bool radarUnlocked)
    {
        const string Unknown = "???";

        if (waveNext != null) waveNext.text = CoreInfoDummyData.WaveNextIn;
        if (waveNumber != null) waveNumber.text = CoreInfoDummyData.WaveNumber;

        if (waveIncoming != null)
            waveIncoming.text = radarUnlocked ? CoreInfoDummyData.WaveIncoming : Unknown;

        if (waveNests != null)
            waveNests.text = radarUnlocked
                ? $"{CoreInfoDummyData.WaveNestsCleared} / {CoreInfoDummyData.WaveNestsTotal}"
                : Unknown;

        // 모르는 값은 위협색으로 강조하지 않는다 — 붉은 ???는 아는 정보처럼 읽힌다
        ToggleClass(waveIncoming, "core-wave__danger", radarUnlocked);
        ToggleClass(waveIncoming, "core-wave__unknown", !radarUnlocked);
        ToggleClass(waveNests, "core-wave__unknown", !radarUnlocked);
    }

    void RefreshRadar(bool unlocked)
    {
        if (radar == null) return;

        radar.Locked = !unlocked;
        radar.SetBlips(unlocked ? CoreInfoDummyData.Blips() : null);

        if (radarChipText != null) radarChipText.text = unlocked ? "가동 중" : "신호 없음";
        ToggleClass(radarChip, "ui-chip--done", unlocked);
    }

    // ──────────────── 컨테이너 구독 (폴링 대체) ────────────────

    void Subscribe()
    {
        subCore = target != null ? target.Container : null;
        subHotbar = Hotbar;
        subBag = Bag;

        if (subCore != null) subCore.Changed += OnContainerChanged;
        if (subHotbar != null) subHotbar.Changed += OnContainerChanged;
        if (subBag != null) subBag.Changed += OnContainerChanged;

        // 요구가 다 채워지면 버튼이 "납품"에서 "수리 시작"으로 바뀐다 (SCR-01b)
        if (target != null) target.ReadyChanged += OnContainerChanged;

        // 코어 내구도도 폴링하지 않고 이벤트로 받는다
        coreEntity = FindCoreEntity();
        if (coreEntity != null) coreEntity.OnHealthChanged += OnCoreHealthChanged;
    }

    void Unsubscribe()
    {
        if (subCore != null) subCore.Changed -= OnContainerChanged;
        if (subHotbar != null) subHotbar.Changed -= OnContainerChanged;
        if (subBag != null) subBag.Changed -= OnContainerChanged;
        subCore = subHotbar = subBag = null;

        if (target != null) target.ReadyChanged -= OnContainerChanged;

        if (coreEntity != null) coreEntity.OnHealthChanged -= OnCoreHealthChanged;
        coreEntity = null;
    }

    void OnContainerChanged() => Refresh();

    void OnCoreHealthChanged(float current, float max)
    {
        if (infoTabActive) RefreshInfo();
    }

    // ───────────────────────── 갱신 ─────────────────────────

    void Refresh()
    {
        if (target == null || Root == null) return;

        bool hasWork = target.HasNextTier;
        Show(table, hasWork);
        Show(gate, hasWork);
        Show(actions, hasWork);
        Show(complete, !hasWork);

        RebuildRail();
        if (infoTabActive) RefreshInfo();   // 티어가 오르면 수리 진행도 같이 움직인다
        if (!hasWork) { builtForTier = -1; return; }

        var progress = target.GetProgress();
        if (builtForTier != target.CurrentTierIndex || builtRows.Count != progress.Count)
            RebuildRows(progress);

        RefreshValues(progress);
    }

    void RebuildRows(IReadOnlyList<(ItemDataSO item, int required, int current)> progress)
    {
        rows.Clear();
        builtRows.Clear();

        for (int i = 0; i < progress.Count; i++)
        {
            var row = MakeRow(progress[i].item);
            builtRows.Add(row);
            rows.Add(row.Root);
        }
        builtForTier = target.CurrentTierIndex;
    }

    /// <summary>값만 제자리 갱신 — 요소를 새로 만들지 않는다.</summary>
    void RefreshValues(IReadOnlyList<(ItemDataSO item, int required, int current)> progress)
    {
        int metCount = 0;
        int totalRequired = 0, totalCurrent = 0;
        int chosenTotal = 0;

        for (int i = 0; i < builtRows.Count && i < progress.Count; i++)
        {
            var row = builtRows[i];
            var (item, required, current) = progress[i];

            int need = Mathf.Max(0, required - current);
            int have = PlayerCountOf(item);
            int max = need == 0 ? 0 : Mathf.Min(need, have, target.Container.RoomFor(item));

            row.Chosen = Mathf.Clamp(row.Chosen, 0, max);
            chosenTotal += row.Chosen;

            totalRequired += required;
            totalCurrent += Mathf.Min(current, required);
            if (need == 0) metCount++;

            row.ChipName.text = DisplayNameOf(item);
            row.ChipN.text = $"{current}/{required}";
            SetBarFill(row.BarFill, required > 0 ? (float)current / required : 1f);
            row.Have.text = have.ToString();

            // 부족 = 이 게이트를 채우려면 앞으로 더 "구해와야" 하는 양
            int shortfall = Mathf.Max(0, need - have);
            row.Missing.text = need == 0 ? "충족" : shortfall > 0 ? shortfall.ToString() : "—";
            SetNumTone(row.Missing, need == 0 ? "ui-num--ok" : shortfall > 0 ? "ui-num--short" : "ui-num--dim");

            ToggleClass(row.Root, "ui-table__row--done", need == 0);
            ToggleClass(row.Chip, "ui-chip--done", need == 0);
            ToggleClass(row.Stepper, "ui-stepper--off", max == 0);
            row.Stepper.SetEnabled(max > 0);
            row.StepValue.text = row.Chosen.ToString();
        }

        gateName.text = CurrentTierLabel();
        gateCount.text = $"{metCount} / {progress.Count} 부품 충족";

        float pct = totalRequired > 0 ? (float)totalCurrent / totalRequired : 0f;
        SetBarFill(gateFill, pct);
        gatePct.text = $"{Mathf.FloorToInt(pct * 100f)}%";

        RefreshActions(chosenTotal);
    }

    void RefreshActions(int chosenTotal)
    {
        actionsChips.Clear();

        foreach (var row in builtRows)
        {
            if (row.Chosen <= 0) continue;
            var chip = new VisualElement();
            chip.AddToClassList("ui-chip");
            AddIf(chip, UIItemPalette.ChipClass(row.Item));
            chip.style.marginRight = 8;

            chip.Add(new Label(DisplayNameOf(row.Item)));
            var n = new Label(row.Chosen.ToString());
            n.AddToClassList("ui-chip__n");
            chip.Add(n);
            actionsChips.Add(chip);
        }

        // 부품이 다 모이면 납품 줄이 통째로 "수리 시작"으로 바뀐다 (SCR-01b) —
        // 더 넣을 것이 없으니 부품 선택 UI를 남겨둘 이유가 없다
        bool ready = target != null && target.IsReadyToRepair;
        ToggleClass(btnDeliver, "ui-btn--danger", ready && IsFinalTier);
        ToggleClass(btnDeliver, "ui-btn--primary", !(ready && IsFinalTier));
        Show(actionsChips, !ready);
        btnAll.style.display = ready ? DisplayStyle.None : DisplayStyle.Flex;

        // 오른쪽으로 미는 일은 원래 btn-all(.ui-push)이 했다. 준비 상태에서 그걸 숨기므로
        // 밀어낼 것이 사라져 버튼이 라벨 바로 옆에 붙는다 — 그때는 버튼 자신이 민다.
        ToggleClass(btnDeliver, "ui-push", ready);
        ToggleClass(btnDeliver, "core-actions__go", ready);

        if (ready)
        {
            btnDeliver.SetEnabled(true);
            deliverLabel.text = IsFinalTier ? "예열 시작" : "수리 시작";
            actionsLabel.text = "모든 부품이 준비됐습니다";
            ToggleClass(actionsLabel, "core-actions__ready", true);
            return;
        }

        ToggleClass(actionsLabel, "core-actions__ready", false);

        bool can = chosenTotal > 0;
        btnDeliver.SetEnabled(can);
        // 비활성 버튼은 "확인"이 아니라 왜 못 누르는지를 말한다 (문서 §03 BTN)
        deliverLabel.text = can ? "납품" : "선택한 부품 없음";
        actionsLabel.text = can ? "이번에 납품" : "납품할 부품을 선택하세요";

        bool anyAvailable = false;
        foreach (var row in builtRows)
            if (row.Stepper.enabledSelf) { anyAvailable = true; break; }
        btnAll.SetEnabled(anyAvailable);
    }

    // ───────────────── SCR-01b 수리 확인창 ─────────────────

    CoreTierDefinition CurrentTier
    {
        get
        {
            var tiers = target?.Data?.tiers;
            int i = target?.CurrentTierIndex ?? -1;
            return tiers != null && i >= 0 && i < tiers.Length ? tiers[i] : null;
        }
    }

    bool IsFinalTier => CurrentTier?.isFinal ?? false;

    /// <summary>납품 버튼의 두 얼굴 — 아직 모자라면 납품, 다 모였으면 확인창.</summary>
    void OnPrimaryAction()
    {
        if (target != null && target.IsReadyToRepair) OpenConfirm();
        else Deliver();
    }

    void OpenConfirm()
    {
        var tier = CurrentTier;
        if (tier == null) return;

        confirmGate.text = $"GATE {(target.CurrentTierIndex + 1):00}";
        confirmName.text = string.IsNullOrEmpty(tier.tierLabel)
            ? $"{target.CurrentTierIndex + 1}단계" : tier.tierLabel;

        confirmDesc.text = tier.description ?? "";
        Show(confirmDesc, !string.IsNullOrEmpty(tier.description));

        // 해금 목록 — 계통색은 마지막 단계만 crystal, 나머지는 copper (문서 목업과 동일)
        string matClass = tier.isFinal ? "ui-mat--crystal" : "ui-mat--copper";
        confirmUnlocks.Clear();
        int n = 0;
        if (tier.unlocks != null)
        {
            foreach (var u in tier.unlocks)
            {
                if (string.IsNullOrEmpty(u)) continue;
                confirmUnlocks.Add(UnlockRow(matClass, u));
                n++;
            }
        }

        // 내구도 줄은 unlocks에 손으로 적지 않는다 — maxHpBonus에서 만들어 맨 아래에 붙인다.
        // 같은 수치를 데이터와 문구 두 곳에 적으면 반드시 어긋나고, 어긋난 쪽이 UI면 플레이어가 속는다.
        if (tier.maxHpBonus > 0)
        {
            confirmUnlocks.Add(UnlockRow(matClass, $"코어 내구도 +{tier.maxHpBonus:N0}"));
            n++;
        }
        Show(confirmUnlocksLabel, n > 0);
        Show(confirmUnlocks, n > 0);

        // 경고는 되돌릴 수 없는 단계에만. 전부에 붙이면 정작 위험한 마지막에서 눈에 안 띈다
        Show(confirmWarn, tier.isFinal);
        if (tier.isFinal)
        {
            confirmWarnTitle.text = "예열이 시작되면 멈출 수 없습니다";
            confirmWarnBody.text  = "예열 동안 행성의 모든 무리가 코어로 몰려옵니다. 끝까지 지켜내면 이륙합니다.";
        }

        confirmOk.text = tier.isFinal ? "예열 시작" : "수리 시작";
        ToggleClass(confirmOk, "ui-btn--danger", tier.isFinal);
        ToggleClass(confirmOk, "ui-btn--primary", !tier.isFinal);

        Show(confirmScrim, true);
    }

    /// <summary>해금 한 줄. 손으로 적은 항목과 maxHpBonus에서 생성한 줄이 같은 모양이다 —
    /// 플레이어에게는 둘 다 "이 단계를 마치면 생기는 것"이라 구분할 이유가 없다.</summary>
    static VisualElement UnlockRow(string matClass, string text)
    {
        var row = new VisualElement();
        row.AddToClassList("ui-mat");
        row.AddToClassList(matClass);

        var label = new Label(text);
        label.AddToClassList("ui-mat__name");
        row.Add(label);
        return row;
    }

    void CloseConfirm() => Show(confirmScrim, false);

    void ConfirmRepair()
    {
        // 창이 떠 있는 동안 벨트가 내용물을 도로 빼갔을 수 있다 — 실패하면 창만 닫고 화면을 갱신한다
        target?.TryStartRepair();
        CloseConfirm();
        builtForTier = -1;   // 단계가 바뀌면 요구 부품 구성도 바뀐다
        Refresh();
    }

    // ───────────────────────── 조작 ─────────────────────────

    void Adjust(Row row, int delta)
    {
        row.Chosen = Mathf.Max(0, row.Chosen + delta);
        Refresh();   // 상한 클램프는 RefreshValues가 한다
    }

    void SetToMax(Row row)
    {
        row.Chosen = int.MaxValue;
        Refresh();
    }

    void SelectAll()
    {
        foreach (var row in builtRows) row.Chosen = int.MaxValue;
        Refresh();
    }

    void Deliver()
    {
        if (target == null) return;

        bool moved = false;
        foreach (var row in builtRows)
        {
            if (row.Chosen <= 0) continue;

            // 마지막 갱신 이후 인벤토리가 바뀌었을 수 있으므로 다시 검사한다
            int n = Mathf.Min(row.Chosen, PlayerCountOf(row.Item), target.Container.RoomFor(row.Item));
            if (n <= 0) continue;
            if (!PlayerConsume(row.Item, n)) continue;

            if (!target.Container.TryAdd(row.Item, n))
            {
                // 넣지 못했으면 되돌린다 — 아이템이 증발하면 안 된다
                PlayerInventoryHolder.Instance?.AddItemToPlayer(row.Item, n);
                continue;
            }

            row.Chosen = 0;
            moved = true;
        }

        if (moved) Refresh();   // Changed 이벤트로도 오지만, 실패 경로까지 확실히 반영
    }

    // ───────────────────── 행·레일 만들기 ─────────────────────

    class Row
    {
        public ItemDataSO Item;
        public int Chosen;
        public VisualElement Root, Chip, BarFill, Stepper;
        public Label ChipName, ChipN, Have, Missing, StepValue;
    }

    Row MakeRow(ItemDataSO item)
    {
        var row = new Row { Item = item };

        row.Root = new VisualElement();
        row.Root.AddToClassList("ui-table__row");

        // 부품 칩
        row.Chip = new VisualElement();
        row.Chip.AddToClassList("ui-chip");
        row.Chip.AddToClassList("ui-chip--spread");
        row.Chip.AddToClassList("ui-col-part");
        AddIf(row.Chip, UIItemPalette.ChipClass(item));
        row.ChipName = new Label();
        row.ChipN = new Label();
        row.ChipN.AddToClassList("ui-chip__n");
        row.Chip.Add(row.ChipName);
        row.Chip.Add(row.ChipN);
        row.Root.Add(row.Chip);

        // 납품 진행
        var bar = new VisualElement();
        bar.AddToClassList("ui-bar");
        bar.AddToClassList("ui-col-bar");
        AddIf(bar, UIItemPalette.BarClass(item));
        row.BarFill = new VisualElement();
        row.BarFill.AddToClassList("ui-bar__fill");
        bar.Add(row.BarFill);
        row.Root.Add(bar);

        // 보유 / 부족
        row.Have = MakeNum();
        row.Missing = MakeNum();
        row.Root.Add(row.Have);
        row.Root.Add(row.Missing);

        // 이번 납품 — 스테퍼
        var act = new VisualElement();
        act.AddToClassList("ui-col-act");

        row.Stepper = new VisualElement();
        row.Stepper.AddToClassList("ui-stepper");

        var minus = new Button(() => Adjust(row, -1)) { text = "-" };
        minus.AddToClassList("ui-stepper__btn");
        row.StepValue = new Label("0");
        row.StepValue.AddToClassList("ui-stepper__n");
        var plus = new Button(() => Adjust(row, +1)) { text = "+" };
        plus.AddToClassList("ui-stepper__btn");
        var max = new Button(() => SetToMax(row)) { text = "MAX" };
        max.AddToClassList("ui-stepper__max");

        row.Stepper.Add(minus);
        row.Stepper.Add(row.StepValue);
        row.Stepper.Add(plus);
        row.Stepper.Add(max);
        act.Add(row.Stepper);
        row.Root.Add(act);

        return row;
    }

    static Label MakeNum()
    {
        var l = new Label();
        l.AddToClassList("ui-num");
        l.AddToClassList("ui-col-num");
        return l;
    }

    void RebuildRail()
    {
        rail.Clear();

        var tiers = target.Data != null ? target.Data.tiers : null;
        if (tiers == null || tiers.Length == 0) { Show(rail, false); return; }
        Show(rail, true);

        int cur = target.CurrentTierIndex;

        for (int i = 0; i < tiers.Length; i++)
        {
            var step = new VisualElement();
            step.AddToClassList("ui-rail__step");
            if (i == tiers.Length - 1) step.AddToClassList("ui-rail__step--last");
            if (i < cur) step.AddToClassList("ui-rail__step--done");
            else if (i == cur) step.AddToClassList("ui-rail__step--active");

            var connector = new VisualElement();
            connector.AddToClassList("ui-rail__line");
            step.Add(connector);

            var dot = new Label((i + 1).ToString());
            dot.AddToClassList("ui-rail__dot");
            step.Add(dot);

            var text = new VisualElement();
            var label = new Label(string.IsNullOrEmpty(tiers[i].tierLabel) ? $"{i + 1}단계" : tiers[i].tierLabel);
            label.AddToClassList("ui-rail__label");
            var sub = new Label(i < cur ? "완료" : i == cur ? "진행 중" : "잠김");
            sub.AddToClassList("ui-rail__sub");
            text.Add(label);
            text.Add(sub);
            step.Add(text);

            rail.Add(step);
        }
    }

    string CurrentTierLabel()
    {
        var tiers = target.Data != null ? target.Data.tiers : null;
        int i = target.CurrentTierIndex;
        if (tiers == null || i < 0 || i >= tiers.Length) return "";
        return string.IsNullOrEmpty(tiers[i].tierLabel) ? $"{i + 1}단계" : tiers[i].tierLabel;
    }

    // ───────────────────── 플레이어 인벤토리 ─────────────────────

    static ItemContainer Hotbar =>
        PlayerInventoryHolder.Instance != null ? PlayerInventoryHolder.Instance.HotbarContainer : null;

    static ItemContainer Bag =>
        PlayerInventoryHolder.Instance != null ? PlayerInventoryHolder.Instance.MainContainer : null;

    static int PlayerCountOf(ItemDataSO item)
    {
        int n = 0;
        if (Hotbar != null) n += Hotbar.CountOf(item);
        if (Bag != null) n += Bag.CountOf(item);
        return n;
    }

    /// <summary>가방부터 소진하고 모자라면 핫바에서 뺀다 — 핫바를 최대한 유지한다.</summary>
    static bool PlayerConsume(ItemDataSO item, int n)
    {
        if (n <= 0 || PlayerCountOf(item) < n) return false;

        int fromBag = Bag != null ? Mathf.Min(n, Bag.CountOf(item)) : 0;
        if (fromBag > 0) Bag.TryConsume(item, fromBag);

        int rest = n - fromBag;
        if (rest > 0 && Hotbar != null) Hotbar.TryConsume(item, rest);
        return true;
    }

    // ───────────────────────── 잡동사니 ─────────────────────────

    static string DisplayNameOf(ItemDataSO item) =>
        item == null ? "" : string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName;

    static void AddIf(VisualElement e, string className)
    {
        if (!string.IsNullOrEmpty(className)) e.AddToClassList(className);
    }

    static void Show(VisualElement e, bool on)
    {
        if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }

    static void SetNumTone(Label l, string toneClass)
    {
        l.RemoveFromClassList("ui-num--ok");
        l.RemoveFromClassList("ui-num--short");
        l.RemoveFromClassList("ui-num--dim");
        l.AddToClassList(toneClass);
    }
}
