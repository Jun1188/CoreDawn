using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Factory;
using CoreDawn.Placement;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.UI
{
    /// <summary>
    /// SCR-05 배치 모드 HUD · SCR-06 철거 모드 HUD.
    ///
    /// 배치·철거 중 화면에 남는 유일한 창이다. 건설 메뉴(B)에서 고르고 나면 창이 닫히고
    /// 이 카드만 남는다 — 배치는 월드를 보면서 하는 일이라 화면을 덮으면 안 된다.
    ///
    /// <see cref="UITKPopup"/>을 쓰지 않는 이유: 팝업은 입력 맵을 Push하고 커서를 푼다.
    /// HUD는 둘 다 하면 안 된다 (배치 중에도 좌클릭이 BuildController로 가야 한다).
    /// 그래서 UIDocument만 들고 표시/숨김만 맡는다.
    ///
    /// 갱신은 <see cref="PlacementSystem"/>의 상태를 매 프레임 읽는다. 팝업은 이벤트로
    /// 갱신하는 게 원칙이지만(문서 §05), 여기 값들은 조준·인벤토리·홀드 진행처럼
    /// 매 프레임 변하는 것이라 이벤트로 바꿔도 결국 매 프레임 발화한다.
    /// 대신 실제로 바뀌었을 때만 UI를 다시 만든다 (아래 _shownSo/_shownMode 가드).
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(UIDocument))]
    public class BuildModeHUDView : MonoBehaviour
    {
        [SerializeField] UIDocument document;
        [SerializeField] PlacementSystem placement;

        VisualElement root, card, leadIcon, leadIconImg, leadRing, cost, keys, sep1, sep2;
        Label badge, cardName, cardSub, costLabel;
        HoldRing ring;

        // 다시 만들어야 하는지 판정하는 키 — 이게 그대로면 매 프레임 손대지 않는다
        PlacementSystem.BuildMode _shownMode = PlacementSystem.BuildMode.None;
        EntityDef _shownSo;
        BuildingModule _shownTarget;
        int _shownCostStamp;

        void Awake()
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (placement == null) placement = FindFirstObjectByType<PlacementSystem>();
        }

        void OnEnable()
        {
            var r = document != null ? document.rootVisualElement : null;
            if (r == null)
            {
                Debug.LogError("[BuildModeHUDView] UIDocument.rootVisualElement가 null입니다. " +
                               "Source Asset(BuildModeHUD.uxml)과 Panel Settings를 확인하세요.", this);
                enabled = false;
                return;
            }

            root        = r.Q("screen-root");
            card        = r.Q("card");
            badge       = r.Q<Label>("mode-badge");
            leadIcon    = r.Q("lead-icon");
            leadIconImg = r.Q("lead-icon-img");
            leadRing    = r.Q("lead-ring");
            cardName    = r.Q<Label>("card-name");
            cardSub     = r.Q<Label>("card-sub");
            costLabel   = r.Q<Label>("cost-label");
            cost        = r.Q("cost");
            keys        = r.Q("keys");
            sep1        = r.Q("sep-1");
            sep2        = r.Q("sep-2");

            ring = new HoldRing();
            leadRing.Add(ring);

            Show(root, false);
            _shownMode = PlacementSystem.BuildMode.None;
            _shownSo = null;
            _shownTarget = null;
        }

        void Update()
        {
            if (placement == null) { Show(root, false); return; }

            var mode = placement.Mode;
            if (mode == PlacementSystem.BuildMode.None)
            {
                if (_shownMode != PlacementSystem.BuildMode.None)
                {
                    Show(root, false);
                    _shownMode = PlacementSystem.BuildMode.None;
                    _shownSo = null;
                    _shownTarget = null;
                }
                return;
            }

            Show(root, true);

            if (mode == PlacementSystem.BuildMode.Placing) UpdatePlacing();
            else                                          UpdateDemolishing();

            _shownMode = mode;
        }

        // ───────────────────────── 배치 (SCR-05) ─────────────────────────

        void UpdatePlacing()
        {
            var so = placement.CurrentBuilding;
            if (so == null) { Show(root, false); return; }

            // 보유량이 바뀌면 칩의 색과 숫자가 바뀐다 — 재료를 캐는 동안 실시간으로 반영된다
            int stamp = CostStamp(so);
            bool rebuild = _shownMode != PlacementSystem.BuildMode.Placing
                        || _shownSo != so
                        || _shownCostStamp != stamp;
            if (!rebuild) return;

            _shownSo = so;
            _shownTarget = null;
            _shownCostStamp = stamp;

            badge.text = "BUILD MODE";
            ToggleClass(badge, "ui-modebadge--build", true);
            ToggleClass(badge, "ui-modebadge--demolish", false);
            ToggleClass(card, "ui-placecard--demolish", false);

            Show(leadIcon, true);
            Show(leadRing, false);
            var sprite = ViewCatalogSO.IconOf(so);
            leadIconImg.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : default;

            cardName.text = NameOf(so);
            ToggleClass(cardName, "ui-placecard__name--danger", false);
            Show(cardSub, false);

            // 비용은 "보유/필요"로 함께 적는다 — 31/10이면 31 보유, 10 필요. 코어 납품·진행률 표시(현재/필요)와
            // 같은 방향이라 화면마다 분수를 거꾸로 읽을 일이 없다.
            // 부족한 칩만 통째로 붉어진다. 숫자가 이미 말하므로 문장을 덧붙이지 않는다.
            cost.Clear();
            int chips = 0;
            if (BuildCost.HasCost(so))
            {
                foreach (var c in so.Get<BuildingModuleDef>().Cost)
                {
                    if (c.Item == null || c.Amount <= 0) continue;
                    int have = BuildCost.PlayerCountOf(c.Item);
                    cost.Add(Chip(c.Item, $"{have}/{c.Amount}", have < c.Amount ? "ui-chip--short"
                                                                                : UIItemPalette.ChipClass(c.Item)));
                    chips++;
                }
            }
            MarkLast(cost, "ui-chip--last");
            costLabel.text = chips > 0 ? "비용" : "무료";
            Show(cost, chips > 0);
            Show(sep1, true);
            Show(sep2, true);

            keys.Clear();
            keys.Add(KeyHint("R", "회전"));
            if (so.Has<ConveyorModuleDef>()) keys.Add(KeyHint("T", "모양"));
            keys.Add(KeyHint("LMB", "배치"));
            keys.Add(KeyHint("RMB", "취소"));
            MarkLast(keys, "ui-key--last");
        }

        /// <summary>비용 표시가 달라지는 입력값을 한 정수로 접는다 — 매 프레임 칩을 다시 만들지 않기 위함.</summary>
        static int CostStamp(EntityDef so)
        {
            if (!BuildCost.HasCost(so)) return 0;
            int h = 17;
            foreach (var c in so.Get<BuildingModuleDef>().Cost)
                if (c.Item != null) h = h * 31 + BuildCost.PlayerCountOf(c.Item);
            return h;
        }

        // ───────────────────────── 철거 (SCR-06) ─────────────────────────

        void UpdateDemolishing()
        {
            var target = placement.HoveredBuilding;

            // 진행 링은 매 프레임 값만 갱신한다 (MarkDirtyRepaint는 값이 바뀔 때만 돈다)
            ring.Progress = placement.DemolishHoldProgress;

            Show(card, true);
            ring.Text = "HOLD";

            if (target == null)
            {
                // 카드는 계속 띄우고 무엇을 해야 하는지만 바꾼다 — 카드가 사라졌다 나타나면
                // 조준을 옮길 때마다 화면 아래가 깜빡이고, 지금 무슨 모드인지도 흐려진다
                if (_shownTarget == null && _shownMode == PlacementSystem.BuildMode.Demolishing) return;
                _shownTarget = null;

                badge.text = "DEMOLISH MODE";
                ToggleClass(badge, "ui-modebadge--build", false);
                ToggleClass(badge, "ui-modebadge--demolish", true);
                ToggleClass(card, "ui-placecard--demolish", true);

                Show(leadIcon, false);
                Show(leadRing, true);

                cardName.text = "철거할 건물을 선택하세요";
                ToggleClass(cardName, "ui-placecard__name--danger", false);
                Show(cardSub, false);

                cost.Clear();
                Show(costLabel, false);
                Show(cost, false);
                Show(sep1, false);
                Show(sep2, true);

                keys.Clear();
                keys.Add(KeyHint("RMB", "나가기"));
                MarkLast(keys, "ui-key--last");
                return;
            }

            bool rebuild = _shownMode != PlacementSystem.BuildMode.Demolishing || _shownTarget != target;
            if (!rebuild) return;

            _shownTarget = target;
            _shownSo = null;

            badge.text = "DEMOLISH MODE";
            ToggleClass(badge, "ui-modebadge--build", false);
            ToggleClass(badge, "ui-modebadge--demolish", true);
            ToggleClass(card, "ui-placecard--demolish", true);

            Show(leadIcon, false);
            Show(leadRing, true);

            var def = target.Def;
            cardName.text = $"{target.DisplayName} 철거";
            ToggleClass(cardName, "ui-placecard__name--danger", true);
            cardSub.text = "누르고 있는 동안 진행";
            Show(cardSub, true);

            // 환급 칩은 초록 — "돌려받는다"를 색으로 먼저 말한다. 전액 환급이라 비용과 같은 수치다.
            cost.Clear();
            int chips = 0;
            if (BuildCost.HasCost(def))
            {
                foreach (var c in def.Get<BuildingModuleDef>().Cost)
                {
                    if (c.Item == null || c.Amount <= 0) continue;
                    cost.Add(Chip(c.Item, c.Amount.ToString(), "ui-chip--done"));
                    chips++;
                }
            }
            MarkLast(cost, "ui-chip--last");
            costLabel.text = chips > 0 ? "환급" : "";
            Show(costLabel, chips > 0);
            Show(cost, chips > 0);
            Show(sep1, chips > 0);
            Show(sep2, true);

            keys.Clear();
            keys.Add(KeyHint("LMB", "철거"));
            keys.Add(KeyHint("RMB", "나가기"));
            MarkLast(keys, "ui-key--last");
        }

        // ───────────────────────── 조각 ─────────────────────────

        static VisualElement Chip(ItemDef item, string amountText, string modifier)
        {
            var chip = new VisualElement { pickingMode = PickingMode.Ignore };
            chip.AddToClassList("ui-chip");
            if (modifier != null) chip.AddToClassList(modifier);

            chip.Add(new Label(NameOf(item)));

            var n = new Label(amountText);
            n.AddToClassList("ui-chip__n");
            chip.Add(n);
            return chip;
        }

        static VisualElement KeyHint(string cap, string label)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("ui-key");

            var k = new Label(cap);
            k.AddToClassList("ui-key__cap");
            e.Add(k);
            e.Add(new Label(label));
            return e;
        }

        /// <summary>USS에 :last-child가 없어 마지막 자식에만 클래스를 붙여 여백을 지운다.</summary>
        static void MarkLast(VisualElement container, string lastClass)
        {
            int n = container.childCount;
            for (int i = 0; i < n; i++)
                ToggleClass(container[i], lastClass, i == n - 1);
        }

        static string NameOf(Def def) =>
            def == null ? "" : string.IsNullOrEmpty(def.DisplayName) ? def.Id : def.DisplayName;

        static void Show(VisualElement e, bool on)
        {
            if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static void ToggleClass(VisualElement e, string className, bool on)
        {
            if (e == null) return;
            if (on) e.AddToClassList(className);
            else e.RemoveFromClassList(className);
        }
    }
}
