using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Factory;
using CoreDawn.Inventories;
using CoreDawn.Managers;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.UI
{
    /// <summary>
    /// SCR-09 설비 — 레시피와 가동 상태.
    ///
    /// 제련로·제작기·조립기·제조기가 이 화면 하나를 쓴다 — 데이터상 전부 같은 SO이고
    /// 입력 슬롯 수(1·1·2·4)만 다르므로 IN 칸이 그 수만큼 늘어날 뿐이다.
    /// 입력은 세로로 쌓다가 넷이 되면 2×2로 접고, 입력이 하나면 남는 폭을 진행바가 쓴다.
    ///
    /// 시작/중지는 상태줄 오른쪽 끝 — 멈춰도 진행률과 버퍼는 보존되므로
    /// "잠깐 자원을 아끼려고 세워둔다"가 안전한 조작이 된다.
    ///
    /// 아래 두 단(소지품·핫바)은 보관소(SCR-08)와 같은 뼈대를 그대로 쓴다 — 설비 버퍼에
    /// 손으로 재료를 넣고 결과를 빼내려면 소지품이 같은 화면에 있어야 하고, 조작 문법
    /// (좌 집기/놓기 · 우 절반/한 개 · Shift 빠른 이동)도 창마다 다르면 안 되기 때문이다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class MachinePanelView : PlayerItemPanelView
    {
        static MachinePanelView cached;

        AssemblerBehavior target;
        string search = "";

        VisualElement recipes, detail, inSlots, outSlots, ioArrow, machineBar, machineFill, statusBadge;
        VisualElement yieldIcon;
        Label machineName, yieldName, yieldSub, yieldTime, machinePct, machineEta, statusText, statusNote, recipesEmpty;
        Button btnClose, btnToggle;
        TextField searchField;

        MachineState shownState = (MachineState)(-1);
        bool shownPaused;

        // ───────────────────────── 열기 ─────────────────────────

        /// <summary>씬에 이 패널이 있으면 열고 true. 없으면 false — 설비 상호작용이 조용히 무시된다.</summary>
        public static bool TryOpen(AssemblerBehavior machine)
        {
            if (machine == null) return false;

            if (cached == null)
                cached = FindFirstObjectByType<MachinePanelView>(FindObjectsInactive.Include);
            if (cached == null) return false;

            if (cached.isActiveAndEnabled)
            {
                cached.Retarget(machine);
                return true;
            }

            cached.target = machine;      // OnEnable → Bind 전에 넣어야 한다
            cached.gameObject.SetActive(true);
            return true;
        }

        void Retarget(AssemblerBehavior machine)
        {
            UnhookContainers();
            target = machine;
            HookContainers();
            search = "";
            if (searchField != null) searchField.SetValueWithoutNotify("");
            RebuildAll();
        }

        // ───────────────────── UITKPopup 계약 ─────────────────────

        protected override void Bind()
        {
            var r = Root;
            recipes      = r.Q("recipes");
            recipesEmpty = r.Q<Label>("recipes-empty");
            detail       = r.Q("detail");
            inSlots      = r.Q("in-slots");
            outSlots     = r.Q("out-slots");
            ioArrow      = r.Q("io-arrow");
            machineBar   = r.Q("machine-bar");
            machineFill  = r.Q("machine-fill");
            statusBadge  = r.Q("status");
            yieldIcon    = r.Q("yield-icon");

            machineName = r.Q<Label>("machine-name");
            yieldName   = r.Q<Label>("yield-name");
            yieldSub    = r.Q<Label>("yield-sub");
            yieldTime   = r.Q<Label>("yield-time");
            machinePct  = r.Q<Label>("machine-pct");
            machineEta  = r.Q<Label>("machine-eta");
            statusText  = r.Q<Label>("status-text");
            statusNote  = r.Q<Label>("status-note");

            btnClose  = r.Q<Button>("btn-close");
            btnToggle = r.Q<Button>("btn-toggle");
            searchField = r.Q<TextField>("machine-search");

            btnClose.clicked += Close;
            btnToggle.clicked += TogglePaused;
            searchField.RegisterValueChangedCallback(OnSearchChanged);

            // 검색어는 창을 닫아도 남는다 — Retarget(열린 채 다른 설비로)만 지우고 있었다.
            // 지난 검색어로 걸러진 목록은 티어가 올라 새로 열린 레시피를 감춰 "해금이 안 된 것처럼" 보인다.
            search = "";
            searchField.SetValueWithoutNotify("");

            // 돋보기 — USS에 SVG가 없어 요소로 넣는다. Bind가 다시 돌아도 하나만
            var searchBox = r.Q("machine-search-box");
            if (searchBox != null && searchBox.Q<SearchGlyph>() == null)
                searchBox.Insert(0, new SearchGlyph());

            BindCommon();   // 소지품·핫바 격자, 캐리지, 창 밖 던지기, 툴팁

            // 보상 해금만 듣고 티어 해금은 안 듣고 있었다 — 창이 열린 채 티어가 오르는 경우
            // (코어 패널이 없는 씬의 벨트 자동 수리, 세이브 복원)에 목록이 그대로 낡는다. 인벤토리 패널과 같은 쌍.
            if (GameManager.Instance != null) GameManager.Instance.TierUnlocked += OnTierUnlocked;
            RecipeRewardUnlockService.RecipeUnlocked += OnRecipeRewardUnlocked;

            HookContainers();
            shownState = (MachineState)(-1);
            RebuildAll();
        }

        protected override void Unbind()
        {
            if (btnClose != null) btnClose.clicked -= Close;
            if (btnToggle != null) btnToggle.clicked -= TogglePaused;
            if (searchField != null) searchField.UnregisterValueChangedCallback(OnSearchChanged);
            if (GameManager.Instance != null) GameManager.Instance.TierUnlocked -= OnTierUnlocked;
            RecipeRewardUnlockService.RecipeUnlocked -= OnRecipeRewardUnlocked;

            UnhookContainers();
            target = null;

            UnbindCommon();   // 툴팁 정리 + 들고 있던 스택 회수
        }

        void HookContainers()
        {
            var b = target?.Building;
            if (b == null) return;
            b.Input.Changed  += RebuildSlots;
            b.Output.Changed += RebuildSlots;
        }

        void UnhookContainers()
        {
            var b = target?.Building;
            if (b == null) return;
            b.Input.Changed  -= RebuildSlots;
            b.Output.Changed -= RebuildSlots;
        }

        /// <summary>소지품·핫바가 바뀌었다 — 설비 버퍼 쪽은 HookContainers가 따로 RebuildSlots를 건다.</summary>
        protected override void OnContainerChanged() => RebuildPlayerGrids();

        /// <summary>
        /// Shift+클릭 — 설비 버퍼↔플레이어를 오간다. 플레이어 쪽은 가방부터, 넘치면 핫바.
        ///
        /// 플레이어에서 올려 보낼 때는 입력 버퍼로만 간다. 출력 버퍼로 밀어 넣을 이유가 없고,
        /// 잘못 넣으면 그대로 하류 벨트로 흘러가 라인에 엉뚱한 물건이 섞인다.
        /// 입력은 현재 레시피의 재료만 받으므로(AcceptFilter) 재료가 아니면 그냥 들어가지 않는다.
        /// </summary>
        protected override void QuickMove(ItemContainer src, int index)
        {
            var b = target?.Building;
            if (b == null) { base.QuickMove(src, index); return; }

            var stack = src.PeekAt(index);
            if (stack == null || stack.item == null || stack.amount <= 0) return;

            if (src == b.Input || src == b.Output)
            {
                MoveStack(stack, Main);
                if (stack.amount > 0) MoveStack(stack, Hotbar);
            }
            else
            {
                MoveStack(stack, b.Input);
            }

            src.Touch();
            if (stack.amount <= 0) src.TakeAt(index);
        }

        void OnSearchChanged(ChangeEvent<string> e)
        {
            search = e.newValue ?? "";
            RebuildRecipes();
        }

        void OnRecipeRewardUnlocked(RecipeDataSO _) => RebuildRecipes();
        void OnTierUnlocked(int _) => RebuildRecipes();

        void TogglePaused()
        {
            if (target == null) return;
            target.SetPaused(!target.Paused);
            RefreshRunning(force: true);
        }

        void RebuildAll()
        {
            RebuildPlayerGrids();   // 설비를 못 찾아도 소지품은 그린다 — 빈 창이 뜨는 것보다 낫다

            if (target == null) return;
            machineName.text = string.IsNullOrEmpty(target.Data.displayName) ? target.Data.name : target.Data.displayName;
            RebuildRecipes();
            RebuildSlots();
            RefreshDetailHead();
            RefreshRunning(force: true);
        }

        // ── 진행·상태는 매 프레임 변하는 값 — 폴링하되 바뀐 것만 다시 만든다 ──

        void Update()
        {
            if (target == null) return;
            RefreshRunning(force: false);
        }

        // ───────────────────── 레시피 목록 ─────────────────────

        void RebuildRecipes()
        {
            if (recipes == null || target == null) return;
            recipes.Clear();
            tooltip?.Hide();   // 호버 중이던 행이 교체되면 Leave가 안 온다

            var list = target.GetUnlockedRecipes()
                .OrderBy(r => UIItemOrder.TierOf(PrimaryOutput(r)))
                .ThenBy(r => PrimaryOutput(r) != null ? (int)PrimaryOutput(r).line : int.MaxValue)
                .ThenBy(r => PrimaryOutput(r) != null ? (int)PrimaryOutput(r).type : int.MaxValue)
                .ThenBy(r => DisplayNameOf(PrimaryOutput(r)), System.StringComparer.Ordinal)
                .ToList();

            int shown = 0;
            foreach (var r in list)
            {
                string name = DisplayNameOf(PrimaryOutput(r));
                if (search.Length > 0 && name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                recipes.Add(MakeRecipeRow(r, name));
                shown++;
            }

            Show(recipesEmpty, shown == 0);
            if (recipesEmpty != null && shown == 0)
                recipesEmpty.text = search.Length > 0 ? "검색 결과가 없습니다" : "돌릴 수 있는 레시피가 없습니다";
        }

        VisualElement MakeRecipeRow(RecipeDataSO r, string name)
        {
            var row = new VisualElement();
            row.AddToClassList("ui-row");
            row.AddToClassList("ui-row--compact");
            if ((RecipeDef)r == target.CurrentRecipe) row.AddToClassList("ui-row--selected");

            var ic = new VisualElement();
            ic.AddToClassList("ui-slot__icon");
            var item = PrimaryOutput(r);
            if (item != null) UIItemIcon.Apply(ic, item);
            row.Add(ic);

            var nm = new Label(name);
            nm.AddToClassList("ui-row__name");
            row.Add(nm);

            var meta = new Label($"{r.craftTime:0.0}s");
            meta.AddToClassList("ui-row__meta");
            row.Add(meta);

            tooltip?.AttachRecipe(row, r);

            var captured = r;
            row.RegisterCallback<ClickEvent>(_ =>
            {
                // 진행 중이던 조합은 심이 취소하고 재료를 되돌린다 (AssemblerBehavior.SetRecipe)
                target.SetRecipe(captured);
                RebuildAll();
            });
            return row;
        }

        static ItemDataSO PrimaryOutput(RecipeDataSO r) =>
            r != null && r.outputs != null && r.outputs.Length > 0 ? r.outputs[0].item : null;

        // ───────────────────── 상세 — 산출 머리글 ─────────────────────

        void RefreshDetailHead()
        {
            var recipe = target.CurrentRecipe;
            bool has = recipe != null;
            detail.style.visibility = has ? Visibility.Visible : Visibility.Hidden;
            if (!has) return;

            var item = PrimaryOutput(recipe);
            int per = recipe.Outputs != null && recipe.Outputs.Count > 0 ? recipe.Outputs[0].Amount : 1;

            UIItemIcon.Apply(yieldIcon, item);
            yieldName.text = DisplayNameOf(item);
            if (item != null) yieldName.style.color = UIFlowColors.Of(item.line);

            // N개/분 — 벨트 처리량과 비교해 라인을 몇 개 물릴지 계산하는 값이라 항상 띄운다
            float perMinute = recipe.Seconds > 0f ? 60f / recipe.Seconds * per : 0f;
            yieldSub.text = $"회당 {per}개 · {Mathf.RoundToInt(perMinute)}개 / 분";
            yieldTime.text = $"◷ {recipe.Seconds:0.0}s";
        }

        // ───────────────────── IO 슬롯 ─────────────────────

        void RebuildSlots()
        {
            var b = target?.Building;
            if (b == null || inSlots == null) return;

            // 입력: 세로로 쌓다가 넷이면 2×2. 입력이 하나면 남는 폭을 진행바가 쓴다
            ToggleClass(inSlots, "ui-io__slots--grid", b.Input.SlotCount >= 4);
            ToggleClass(ioArrow, "ui-io__arrow--wide", b.Input.SlotCount <= 1);

            tooltip?.Hide();   // 슬롯 교체 중 Leave 유실 방어
            FillSlots(inSlots, b.Input);
            FillSlots(outSlots, b.Output);
        }

        /// <summary>
        /// IN/OUT 칸 — 소지품 격자와 같은 부품(MakeSlot)을 쓴다. 그래서 조작 문법도 같다:
        /// 좌 집기/놓기 · 우 절반/한 개 · Shift 빠른 이동. 규칙(수용 필터·종류당 1스택·스택 상한)은
        /// 컨테이너가 지키므로 여기서 따로 막을 것이 없다 — 재료가 아니면 놓기가 그냥 실패한다.
        /// </summary>
        void FillSlots(VisualElement parent, ItemContainer c)
        {
            parent.Clear();
            for (int i = 0; i < c.SlotCount; i++)
            {
                var slot = MakeSlot(c, i, keyLabel: null);
                if (i == c.SlotCount - 1) slot.AddToClassList("ui-slot--last");
                parent.Add(slot);
            }
        }

        // ───────────────────── 진행·상태 ─────────────────────

        void RefreshRunning(bool force)
        {
            if (target == null || target.CurrentRecipe == null) return;

            // 진행바·잔여는 매 프레임, 상태 배지·버튼은 바뀔 때만
            SetBarFill(machineFill, target.Progress);
            machinePct.text = $"{Mathf.RoundToInt(target.Progress * 100f)}%";

            var state = target.State;
            machineEta.text = state switch
            {
                MachineState.Running => $"{target.RemainingTime:0.0}s",
                MachineState.Stopped => "멈춤",
                _                    => "대기",
            };

            if (!force && state == shownState && target.Paused == shownPaused) return;
            shownState = state;
            shownPaused = target.Paused;

            ToggleClass(machineBar, "ui-machine--paused", target.Paused);

            foreach (var cls in new[] { "ui-status--run", "ui-status--wait", "ui-status--block", "ui-status--stop" })
                statusBadge.RemoveFromClassList(cls);
            (string badgeClass, string text) = state switch
            {
                MachineState.Running      => ("ui-status--run", "가동 중"),
                MachineState.WaitingInput => ("ui-status--wait", "재료 대기"),
                MachineState.OutputBlocked=> ("ui-status--block", "출력 막힘"),
                _                         => ("ui-status--stop", "중지됨"),
            };
            statusBadge.AddToClassList(badgeClass);
            statusText.text = text;

            // 참고문 — 가동 중엔 소비량, 중지 중엔 보존 안내. 나머지는 상태 배지가 이미 말한다
            statusNote.text = state switch
            {
                MachineState.Running => ConsumptionText(target.CurrentRecipe),
                MachineState.Stopped => "진행은 그대로 보존된다",
                _                    => "",
            };

            btnToggle.text = target.Paused ? "▶ 시작" : "■ 중지";
            ToggleClass(btnToggle, "ui-btn--primary", target.Paused);
        }

        static string ConsumptionText(RecipeDataSO r)
        {
            if (r?.inputs == null || r.inputs.Length == 0) return "";
            return string.Join(" · ", r.inputs
                .Where(i => i.item != null)
                .Select(i => $"{DisplayNameOf(i.item)} {i.amount}")) + " 소비";
        }

        // ───────────────────── 잡동사니 ─────────────────────

        static string DisplayNameOf(ItemDataSO item) =>
            item == null ? "" : string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName;

        static void Show(VisualElement e, bool on)
        {
            if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
