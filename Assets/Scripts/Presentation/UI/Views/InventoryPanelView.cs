using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Factory;
using CoreDawn.Inventories;
using CoreDawn.Managers;
using CoreDawn.Data;
using CoreDawn.Combat;
using CoreDawn.Sim;

namespace CoreDawn.UI
{
    /// <summary>
    /// SCR-04 인벤토리 · 수동 제작.
    ///
    /// 위에서 아래로 제작 → 소지품 → 핫바. 패널이 화면 하단에 붙어 핫바가 HUD 핫바와
    /// 같은 자리에 온다 — I로 열고 닫아도 손이 기억하는 위치가 유지된다.
    /// 소지품·핫바·캐리지 조작은 보관소(SCR-08)와 공유하는 뼈대(PlayerItemPanelView)에 있다.
    ///
    /// 수동 제작은 기존 uGUI(제작 입력 슬롯에 재료를 옮겨 담는 방식)와 다르다:
    ///   - 재료를 옮겨 담지 않는다. 가방·핫바의 보유량을 그대로 세고 그대로 쓴다
    ///   - 해금된 레시피는 전부 목록에 오른다 — "잠김" 줄이 없다 (문서 SCR-04)
    ///   - 버튼을 누르고 있는 동안 계속 만든다. 소비는 한 개가 완성되는 순간에만
    ///     일어난다 — 중간에 떼도 잃는 것이 없고, 완성 직전 재료를 빼돌려도
    ///     완성 시점 검사에 걸려 무에서 만들어지지 않는다
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class InventoryPanelView : PlayerItemPanelView
    {
        static InventoryPanelView cached;

        // ── 제작 상태 ──
        RecipeDataSO selected;
        bool holding;
        // 제작은 플레이어 엔티티의 CrafterModule(심)이 한다 — 이 화면은 누르는 동안 진행시키고 그려 줄 뿐
        static CrafterModule Crafter => SimRunner.Players.Entity?.Get<CrafterModule>();
        float progress => Crafter?.ManualProgress ?? 0f;   // 현재 1회분 경과 시간(초)
        string search = "";

        // ── 요소 참조 ──
        VisualElement recipes, mats, detail;
        VisualElement yieldIcon, craftBtnFill;
        Label yieldName, yieldPer, yieldTime, craftBtnText, craftBtnTime, recipesEmpty;
        Button btnClose, btnCraft;
        TextField searchField;

        // ───────────────────────── 열기 ─────────────────────────

        /// <summary>씬에 이 패널이 있으면 열고 true. 없으면 false — 호출부가 기존 uGUI로 넘어간다.</summary>
        public static bool TryOpen()
        {
            if (PlayerInventoryHolder.Instance == null) return false;

            if (cached == null)
                cached = FindFirstObjectByType<InventoryPanelView>(FindObjectsInactive.Include);
            if (cached == null) return false;

            if (!cached.isActiveAndEnabled)
                cached.gameObject.SetActive(true);
            return true;
        }

        // ───────────────────── UITKPopup 계약 ─────────────────────

        protected override void Bind()
        {
            var r = Root;
            recipes      = r.Q("recipes");
            recipesEmpty = r.Q<Label>("recipes-empty");
            mats         = r.Q("mats");
            detail       = r.Q("detail");

            yieldIcon    = r.Q("yield-icon");
            yieldName    = r.Q<Label>("yield-name");
            yieldPer     = r.Q<Label>("yield-per");
            yieldTime    = r.Q<Label>("yield-time");
            craftBtnFill = r.Q("craft-btn-fill");
            craftBtnText = r.Q<Label>("craft-btn-text");
            craftBtnTime = r.Q<Label>("craft-btn-time");

            btnClose    = r.Q<Button>("btn-close");
            btnCraft    = r.Q<Button>("btn-craft");
            searchField = r.Q<TextField>("recipe-search");

            btnClose.clicked += Close;
            searchField.RegisterValueChangedCallback(OnSearchChanged);

            // 검색어는 창을 닫아도 남는다(UIDocument가 살아 있고 필드는 그 안에 있다) — 그대로 두면
            // 지난번에 친 글자로 걸러진 목록이 뜨고, 티어가 올라 새로 열린 레시피가 "해금이 안 된 것처럼" 보인다.
            // 창을 여는 순간은 새 검색이다.
            search = "";
            searchField.SetValueWithoutNotify("");

            // 돋보기 — USS에 SVG가 없어 요소로 넣는다. Bind가 다시 돌아도 하나만
            var searchBox = r.Q("recipe-search-box");
            if (searchBox != null && searchBox.Q<SearchGlyph>() == null)
                searchBox.Insert(0, new SearchGlyph());

            // 홀드 — Button의 Clickable이 포인터를 캡처하므로 Up/CaptureOut이 버튼으로 온다.
            // CaptureOut까지 받아야 창이 닫히거나 포커스를 뺏겨도 홀드가 풀린다
            btnCraft.RegisterCallback<PointerDownEvent>(OnCraftDown, TrickleDown.TrickleDown);
            btnCraft.RegisterCallback<PointerUpEvent>(OnCraftUp);
            btnCraft.RegisterCallback<PointerCaptureOutEvent>(OnCraftCaptureOut);

            BindCommon();
            if (GameManager.Instance != null) GameManager.Instance.TierUnlocked += OnTierUnlocked;
            RecipeRewardUnlockService.RecipeUnlocked += OnRecipeRewardUnlocked;

            holding = false;
            Crafter?.Release();
            if (Crafter != null) Crafter.Overflow += OnOverflow;

            RebuildRecipes();
            RebuildPlayerGrids();
            RefreshDetail();
        }

        protected override void Unbind()
        {
            if (btnClose != null) btnClose.clicked -= Close;
            if (searchField != null) searchField.UnregisterValueChangedCallback(OnSearchChanged);
            if (btnCraft != null)
            {
                btnCraft.UnregisterCallback<PointerDownEvent>(OnCraftDown, TrickleDown.TrickleDown);
                btnCraft.UnregisterCallback<PointerUpEvent>(OnCraftUp);
                btnCraft.UnregisterCallback<PointerCaptureOutEvent>(OnCraftCaptureOut);
            }
            if (GameManager.Instance != null) GameManager.Instance.TierUnlocked -= OnTierUnlocked;
            RecipeRewardUnlockService.RecipeUnlocked -= OnRecipeRewardUnlocked;

            holding = false;
            if (Crafter != null) Crafter.Overflow -= OnOverflow;
            UnbindCommon();
        }

        protected override void OnContainerChanged()
        {
            RebuildPlayerGrids();
            RefreshDetail();   // 보유량이 바뀌면 "필요/보유"와 버튼 상태도 함께
        }

        void OnTierUnlocked(int _) => RebuildRecipes();
        void OnRecipeRewardUnlocked(RecipeDataSO _) => RebuildRecipes();

        void OnSearchChanged(ChangeEvent<string> e)
        {
            search = e.newValue ?? "";
            RebuildRecipes();
        }

        // ───────────────────── 제작 — 홀드 루프 ─────────────────────

        void OnCraftDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            holding = true;
        }

        void OnCraftUp(PointerUpEvent e)          { holding = false; }
        void OnCraftCaptureOut(PointerCaptureOutEvent e) { holding = false; }

        void Update()
        {
            if (!holding || selected == null || !CanCraftOnce(selected))
            {
                // 떼면 그 자리에서 멈춘다 — 소비 전이므로 잃는 것이 없다
                if (progress > 0f) { Crafter?.Release(); RefreshProgress(); }
                return;
            }

            Crafter.Hold(selected.Def, Time.deltaTime);   // 완성되면 모듈이 재료를 빼고 결과를 넣는다 (넘치면 Overflow → 바닥)
            RefreshProgress();
        }

        /// <summary>재료가 가방+핫바에 전부 있는가 — 진행 중에도 매 프레임 이것으로 판단한다.</summary>
        bool CanCraftOnce(RecipeDataSO r) => r != null && r.Def != null && Crafter != null && Crafter.HasIngredients(r.Def);



        int CountAll(ItemDataSO item) =>
            (Main?.CountOf(item) ?? 0) + (Hotbar?.CountOf(item) ?? 0);


        /// <summary>결과가 가방·핫바에 안 들어갔다 — 소비는 이미 일어났으므로 바닥에 떨어뜨린다.</summary>
        void OnOverflow(ItemDef item, int n) => DropToWorld(item, n);

        // ───────────────────── 레시피 목록 ─────────────────────

        /// <summary>해금된 레시피 전부 — 해금되면 전부 수동 제작이 가능하므로 "잠김" 줄이 없다.</summary>
        List<RecipeDataSO> UnlockedRecipes()
        {
            var db = RecipeDatabaseSO.LoadDefault();
            if (db == null || db.recipes == null) return new List<RecipeDataSO>();

            return SortRecipes(db.recipes.Where(RecipeDatabaseSO.IsUnlocked));
        }

        /// <summary>목록 순서는 아이템 목록과 같은 기준 — 대표 산출물의 티어 → 계통 → 용도 → 이름.</summary>
        static List<RecipeDataSO> SortRecipes(IEnumerable<RecipeDataSO> list) =>
            list.OrderBy(r => UIItemOrder.TierOf(PrimaryOutput(r)))
                .ThenBy(r => PrimaryOutput(r) != null ? (int)PrimaryOutput(r).line : int.MaxValue)
                .ThenBy(r => PrimaryOutput(r) != null ? (int)PrimaryOutput(r).type : int.MaxValue)
                .ThenBy(r => DisplayNameOf(PrimaryOutput(r)), System.StringComparer.Ordinal)
                .ToList();

        static ItemDataSO PrimaryOutput(RecipeDataSO r) =>
            r != null && r.outputs != null && r.outputs.Length > 0 ? r.outputs[0].item : null;

        void RebuildRecipes()
        {
            if (recipes == null) return;
            recipes.Clear();
            tooltip?.Hide();   // 호버 중이던 행이 교체되면 Leave가 안 온다

            var list = UnlockedRecipes();

            // 선택이 목록에서 사라졌으면(검색이 아니라 데이터가 바뀐 경우) 첫 항목으로
            if (selected != null && !list.Contains(selected)) selected = null;

            int shown = 0;
            foreach (var r in list)
            {
                string name = DisplayNameOf(PrimaryOutput(r));
                if (search.Length > 0 && name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (selected == null) selected = r;
                recipes.Add(MakeRecipeRow(r, name));
                shown++;
            }

            Show(recipesEmpty, shown == 0);
            if (recipesEmpty != null && shown == 0)
                recipesEmpty.text = search.Length > 0 ? "검색 결과가 없습니다" : "해금된 레시피가 없습니다";

            RefreshDetail();
        }

        VisualElement MakeRecipeRow(RecipeDataSO r, string name)
        {
            var row = new VisualElement();
            row.AddToClassList("ui-row");
            row.AddToClassList("ui-row--compact");
            if (r == selected) row.AddToClassList("ui-row--selected");

            var ic = new VisualElement();
            ic.AddToClassList("ui-slot__icon");
            ic.AddToClassList("ui-slot__icon--xs");
            var item = PrimaryOutput(r);
            if (item != null) UIItemIcon.Apply(ic, item);
            row.Add(ic);

            var nm = new Label(name);
            nm.AddToClassList("ui-row__name");
            row.Add(nm);

            // 회당 시간 — 목록에서 레시피끼리 비교하는 용도 (문서: 시간이 나오는 세 자리 중 첫째)
            var meta = new Label($"{r.craftTime:0.0}s");
            meta.AddToClassList("ui-row__meta");
            row.Add(meta);

            tooltip?.AttachRecipe(row, r);

            var captured = r;
            row.RegisterCallback<ClickEvent>(_ =>
            {
                selected = captured;
                Crafter?.Release();
                RebuildRecipes();
            });
            return row;
        }

        // ───────────────────── 선택 레시피 상세 ─────────────────────

        void RefreshDetail()
        {
            if (detail == null) return;

            bool has = selected != null;
            detail.style.visibility = has ? Visibility.Visible : Visibility.Hidden;
            if (!has) return;

            var item = PrimaryOutput(selected);
            int per = selected.outputs != null && selected.outputs.Length > 0 ? selected.outputs[0].amount : 1;

            UIItemIcon.Apply(yieldIcon, item);
            yieldName.text = DisplayNameOf(item);
            if (item != null) yieldName.style.color = UIFlowColors.Of(item.line);
            yieldPer.text  = $"회당 {per}개 산출";
            yieldTime.text = $"◷ {selected.craftTime:0.0}s";   // 시간 둘째 자리 — 상세 배지

            RebuildMats();
            RefreshProgress();
        }

        void RebuildMats()
        {
            mats.Clear();
            if (selected?.inputs == null) return;

            foreach (var input in selected.inputs)
            {
                if (input.item == null) continue;

                int have = CountAll(input.item);

                var row = new VisualElement();
                row.AddToClassList("ui-mat");
                // 계통색 띠는 부족해도 그대로 — 어느 라인의 재료인지는 여전히 유효한 정보다
                var lineClass = UIItemPalette.MatClass(input.item);
                if (lineClass != null) row.AddToClassList(lineClass);

                var nm = new Label(DisplayNameOf(input.item));
                nm.AddToClassList("ui-mat__name");
                row.Add(nm);

                // 보유 수가 앞, 필요 수가 뒤 — 코어 납품·건설 비용 표시(보유/필요)와 같은 방향.
                // 부족하면 보유 수만 붉어진다 (문서: 부족한 재료는 보유 수를 붉게)
                var have_ = new Label(have.ToString());
                have_.AddToClassList("ui-mat__n");
                if (have < input.amount) have_.AddToClassList("ui-mat__n--short");
                row.Add(have_);

                var need = new Label($"/ {input.amount}");
                need.AddToClassList("inv-mat__dim");
                row.Add(need);

                mats.Add(row);
            }
        }

        /// <summary>진행 표시는 버튼 하나에만 — 채움과 남은 시간이 한 덩어리로 읽힌다 (문서 SCR-04).</summary>
        void RefreshProgress()
        {
            if (selected == null) return;

            bool crafting = holding && progress > 0f;
            float t = Mathf.Clamp01(selected.craftTime > 0f ? progress / selected.craftTime : 1f);

            SetBarFill(craftBtnFill, t);   // 버튼 안이 차오르면 한 개 완성

            // 진행 중에는 현재 1회분의 잔여, 아니면 회당 시간
            craftBtnTime.text = crafting
                ? $"{Mathf.Max(0f, selected.craftTime - progress):0.0}s"
                : $"{selected.craftTime:0.0}s";

            bool can = CanCraftOnce(selected);
            btnCraft.SetEnabled(can);
            ToggleClass(btnCraft, "ui-btn--disabled", !can);
            craftBtnText.text = can ? "누르고 있는 동안 제작" : MissingLabel();
        }

        /// <summary>버튼 라벨이 무엇이 없는지 말한다 — "재료 부족"만으로는 다시 재료 줄을 읽어야 한다.</summary>
        string MissingLabel()
        {
            string first = null;
            int count = 0;

            if (selected?.inputs != null)
                foreach (var input in selected.inputs)
                {
                    if (input.item == null || CountAll(input.item) >= input.amount) continue;
                    count++;
                    first ??= DisplayNameOf(input.item);
                }

            return count switch
            {
                0 => "재료 부족",                       // 도달 불가 방어 — can=false인데 목록상 부족이 없는 경우
                1 => $"{first} 부족",
                _ => $"{first} 외 {count - 1}종 부족",
            };
        }
    }
}
