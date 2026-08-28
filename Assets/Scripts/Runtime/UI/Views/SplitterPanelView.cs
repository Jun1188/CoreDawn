using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Factory;

namespace CoreDawn.UI
{
    /// <summary>
    /// SCR-07 분배기 — 출구 필터.
    ///
    /// 창의 배치가 월드의 배치와 같다. 출구 칸을 방위대로 놓으면 "북쪽 출구" 같은 글자가
    /// 필요 없어진다 — 보이는 자리가 실제 자리다.
    ///
    /// 상태 셋을 전부 그림으로 구분한다 (문서 SCR-07):
    ///   전체 허용   네 계통색 점
    ///   특정만      허용 아이템 아이콘
    ///   차단        ✕ + 흐름선이 점선으로 꺼짐
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class SplitterPanelView : UITKPopup
    {
        static SplitterPanelView cached;

        SplitterBehavior target;
        Direction selected;
        bool hasSelection;
        string search = "";

        VisualElement dirmap, rows, selSwatch;
        Label selTitle, listEmpty;
        Button btnClose, btnAllowAll, btnBlockAll;
        TextField searchField;

        readonly List<VisualElement> slots = new();
        UITooltip tooltip;

        // ───────────────────────── 열기 ─────────────────────────

        /// <summary>씬에 이 패널이 있으면 열고 true. 없으면 false — 호출부가 기존 uGUI로 넘어간다.</summary>
        public static bool TryOpen(SplitterBehavior splitter)
        {
            if (splitter == null) return false;

            if (cached == null)
                cached = FindFirstObjectByType<SplitterPanelView>(FindObjectsInactive.Include);
            if (cached == null) return false;

            if (cached.isActiveAndEnabled)
            {
                cached.Retarget(splitter);
                return true;
            }

            cached.target = splitter;      // OnEnable → Bind 전에 넣어야 한다
            cached.gameObject.SetActive(true);
            return true;
        }

        void Retarget(SplitterBehavior splitter)
        {
            target = splitter;
            hasSelection = false;
            search = "";
            if (searchField != null) searchField.SetValueWithoutNotify("");
            RebuildAll();
        }

        // ───────────────────── UITKPopup 계약 ─────────────────────

        protected override void Bind()
        {
            var r = Root;
            dirmap    = r.Q("dirmap");
            rows      = r.Q("rows");
            selSwatch = r.Q("sel-swatch");
            selTitle  = r.Q<Label>("sel-title");
            listEmpty = r.Q<Label>("list-empty");

            btnClose    = r.Q<Button>("btn-close");
            btnAllowAll = r.Q<Button>("btn-allow-all");
            btnBlockAll = r.Q<Button>("btn-block-all");
            searchField = r.Q<TextField>("search");

            btnClose.clicked += Close;
            btnAllowAll.clicked += AllowAll;
            btnBlockAll.clicked += BlockAll;
            searchField.RegisterValueChangedCallback(OnSearchChanged);

            // 돋보기 — USS에 SVG가 없어 요소로 넣는다. Bind가 다시 돌아도 하나만
            var searchBox = r.Q("search-box");
            if (searchBox != null && searchBox.Q<SearchGlyph>() == null)
                searchBox.Insert(0, new SearchGlyph());

            tooltip = new UITooltip(r);

            RebuildAll();
        }

        protected override void Unbind()
        {
            if (btnClose != null) btnClose.clicked -= Close;
            if (btnAllowAll != null) btnAllowAll.clicked -= AllowAll;
            if (btnBlockAll != null) btnBlockAll.clicked -= BlockAll;
            if (searchField != null) searchField.UnregisterValueChangedCallback(OnSearchChanged);

            tooltip?.Dispose();
            tooltip = null;

            target = null;
        }

        void OnSearchChanged(ChangeEvent<string> e)
        {
            search = e.newValue ?? "";
            RebuildRows();
        }

        void RebuildAll()
        {
            RebuildMap();
            RebuildRows();
        }

        // ───────────────────── 출구 맵 ─────────────────────

        /// <summary>이 분배기의 실제 포트만 그린다 — 없는 방향에 빈 칸을 두면 있는 척하게 된다.</summary>
        void RebuildMap()
        {
            if (dirmap == null) return;

            foreach (var s in slots) s.RemoveFromHierarchy();
            slots.Clear();

            var ports = target?.Building?.GetEffectivePorts();
            if (ports == null) return;

            // 출구가 하나도 선택돼 있지 않으면 첫 출력 포트를 고른다
            if (!hasSelection)
                foreach (var p in ports)
                    if (p != null && !p.IsInput) { selected = p.Direction; hasSelection = true; break; }

            foreach (var p in ports)
            {
                if (p == null) continue;
                if (p.IsInput) AddInputLine(p.Direction);
                else           AddOutletSlot(p.Direction);
            }

            UpdateSelectionHeader();
        }

        /// <summary>입력은 칸 없이 흐름선만 — 설정할 것이 없는 자리에 누를 수 있게 생긴 것을 두지 않는다.</summary>
        void AddInputLine(Direction dir)
        {
            var line = MakeFlowLine(dir, longLine: true, on: true, UIFlowColors.In);
            dirmap.Add(line);
            slots.Add(line);
        }

        void AddOutletSlot(Direction dir)
        {
            var state = target.StateOf(dir);
            bool blocked = state == OutletState.Blocked;

            var line = MakeFlowLine(dir, longLine: false, on: !blocked, UIFlowColors.Out);
            dirmap.Add(line);
            slots.Add(line);

            var slot = new VisualElement();
            slot.AddToClassList("ui-dirslot");
            if (state == OutletState.Only) slot.AddToClassList("ui-dirslot--open");
            if (blocked) slot.AddToClassList("ui-dirslot--blocked");
            if (hasSelection && dir == selected) slot.AddToClassList("ui-dirslot--sel");

            PlaceSlot(slot, dir);
            slot.Add(SlotContent(dir, state));

            var captured = dir;
            slot.RegisterCallback<ClickEvent>(_ =>
            {
                selected = captured;
                hasSelection = true;
                RebuildAll();
            });

            dirmap.Add(slot);
            slots.Add(slot);
        }

        VisualElement SlotContent(Direction dir, OutletState state)
        {
            if (state == OutletState.Blocked)
            {
                // ✕ — ::before/::after가 없으니 막대 두 개를 겹쳐 돌린다
                var x = new VisualElement();
                x.AddToClassList("ui-dirslot__x");
                foreach (var mod in new[] { "ui-dirslot__bar--a", "ui-dirslot__bar--b" })
                {
                    var bar = new VisualElement();
                    bar.AddToClassList("ui-dirslot__bar");
                    bar.AddToClassList(mod);
                    x.Add(bar);
                }
                return x;
            }

            if (state == OutletState.All)
            {
                // 전체 허용 — 네 계통색 점. "무엇이든 지나간다"를 계통 네 개로 말한다
                var four = new List<Color>();
                foreach (var line in new[] { ItemLine.Iron, ItemLine.Copper, ItemLine.Crystal, ItemLine.Beast })
                    four.Add(UIFlowColors.Of(line));
                return Grid(four, 2, 20, IconGap);
            }

            // 지정 아이템은 몇 종이든 칸 안에 들어가야 한다 — 넘쳐 잘리면 무엇이 걸렸는지 못 읽는다.
            // 개수에 맞춰 정사각 격자로 배치하고 점 크기를 줄인다 (4→2×2, 9→3×3, 16→4×4, 36→6×6).
            // AllowedAt은 HashSet이라 순서가 없다 — 목록과 같은 기준으로 정렬해야
            // 왼쪽 점과 오른쪽 목록이 같은 물건을 같은 자리에서 말한다
            var colors = new List<Color>();
            foreach (var item in UIItemOrder.Sorted(target.AllowedAt(dir)))
                colors.Add(UIFlowColors.Of(item.line));

            var (cols, size, gap) = FitGrid(colors.Count);
            return Grid(colors, cols, size, gap);
        }

        /// <summary>
        /// 개수에 맞는 (열 수, 점 크기, 간격).
        ///
        /// 모든 열 수를 재보고 **점이 가장 커지는 쪽**을 고른다. "최소 크기를 만족하는 첫 열 수"를
        /// 고르면 안 된다 — 1열도 세로로 줄이면 최소 크기는 넘기므로 항상 1~2열에서 멈춰서,
        /// 점만 작아지고 열은 안 늘어난다.
        /// 크기가 같으면 정사각에 가까운 쪽 (한쪽으로 길쭉한 격자는 읽기 나쁘다).
        /// </summary>
        static (int cols, int size, int gap) FitGrid(int count)
        {
            if (count <= 0) return (1, MinIcon, IconGap);

            int bestCols = 1, bestSize = 0, bestGap = IconGap, bestSkew = int.MaxValue;

            for (int cols = 1; cols <= MaxCols; cols++)
            {
                int gap  = cols <= 3 ? 4 : 3;
                int rows = Mathf.CeilToInt(count / (float)cols);

                // 이 열 수로 잡을 수 있는 최대 점 크기 — 가로·세로 둘 다 들어가야 한다
                int byW  = (InnerBox - (cols - 1) * gap) / cols;
                int byH  = (InnerBox - (rows - 1) * gap) / rows;
                int size = Mathf.Min(byW, byH, MaxIcon);
                int skew = Mathf.Abs(cols - rows);

                if (size > bestSize || (size == bestSize && skew < bestSkew))
                {
                    bestCols = cols; bestSize = size; bestGap = gap; bestSkew = skew;
                }
            }

            return (bestCols, Mathf.Max(MinIcon, bestSize), bestGap);
        }

        /// <summary>
        /// 점 격자. flex-wrap에 맡기지 않고 좌표를 직접 준다 —
        /// 소수점 폭·margin 반올림 때문에 줄바꿈이 계산과 어긋나는 일이 없다.
        /// </summary>
        static VisualElement Grid(List<Color> colors, int cols, int size, int gap)
        {
            int rows = Mathf.Max(1, Mathf.CeilToInt(colors.Count / (float)cols));

            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.style.width  = cols * size + (cols - 1) * gap;
            box.style.height = rows * size + (rows - 1) * gap;

            // 마지막 줄이 덜 찼으면 그 줄만 가운데로 민다 — 오른쪽에 빈칸이 남으면
            // 지운 자리처럼 보인다
            int lastRowCount = colors.Count - (rows - 1) * cols;
            float lastRowPad = (cols - lastRowCount) * (size + gap) * 0.5f;

            for (int i = 0; i < colors.Count; i++)
            {
                int col = i % cols, row = i / cols;

                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.style.position = Position.Absolute;
                dot.style.left = col * (size + gap) + (row == rows - 1 ? lastRowPad : 0f);
                dot.style.top  = row * (size + gap);
                dot.style.width = size;
                dot.style.height = size;
                dot.style.backgroundColor = colors[i];

                float r = size >= 15 ? 4.5f : 3f;
                dot.style.borderTopLeftRadius = r;
                dot.style.borderTopRightRadius = r;
                dot.style.borderBottomLeftRadius = r;
                dot.style.borderBottomRightRadius = r;

                box.Add(dot);
            }
            return box;
        }

        // 출구 칸 66px에서 테두리·여백을 뺀 실사용 폭
        const int InnerBox = 84;
        const int IconGap  = 4;
        const int MinIcon  = 9;     // 이보다 작으면 색을 구분할 수 없다
        const int MaxIcon  = 27;
        const int MaxCols  = 6;     // 36종까지. 그 이상은 점이 뭉개져 세는 의미가 없다

        // 250×250 맵. 본체 78px가 가운데(86~164), 출구 칸 66px가 각 변에 붙는다.
        const float MapSize = 375f, SlotSize = 99f, BodyFrom = 129f, BodyTo = 246f;
        const float SlotOff = (MapSize - SlotSize) * 0.5f;   // 92

        static void PlaceSlot(VisualElement slot, Direction dir)
        {
            switch (dir)
            {
                case Direction.North: slot.style.left = SlotOff; slot.style.top = 0f; break;
                case Direction.South: slot.style.left = SlotOff; slot.style.top = BodyTo + 30f; break;
                case Direction.East:  slot.style.left = BodyTo + 30f; slot.style.top = SlotOff; break;
                default:              slot.style.left = 0f; slot.style.top = SlotOff; break;   // West
            }
        }

        /// <summary>본체와 출구 칸 사이(또는 입력은 바깥에서 본체까지)를 잇는 흐름선.</summary>
        VisualElement MakeFlowLine(Direction dir, bool longLine, bool on, Color color)
        {
            var line = new VisualElement();
            line.AddToClassList("ui-flowline");
            line.pickingMode = PickingMode.Ignore;

            bool vertical = dir == Direction.North || dir == Direction.South;
            float thick = 24f;
            float length = longLine ? 120f : 30f;

            if (vertical)
            {
                line.style.left = (MapSize - thick) * 0.5f;
                line.style.width = thick;
                line.style.height = length;
                line.style.top = dir == Direction.North
                    ? BodyFrom - length
                    : BodyTo;
                if (longLine) line.style.top = dir == Direction.North ? 9f : MapSize - 9f - length;
            }
            else
            {
                line.style.top = (MapSize - thick) * 0.5f;
                line.style.height = thick;
                line.style.width = length;
                line.style.left = dir == Direction.West
                    ? BodyFrom - length
                    : BodyTo;
                if (longLine) line.style.left = dir == Direction.West ? 9f : MapSize - 9f - length;
            }

            if (on)
            {
                // 그라디언트는 USS에 없다 — 램프 텍스처를 깔고 색만 틴트로 입힌다
                line.style.backgroundImage = new StyleBackground(UIFlowColors.Ramp(dir, towardCenter: !longLine));
                line.style.unityBackgroundImageTintColor = color;
            }
            else
            {
                // 꺼진 흐름 — 점선. USS에 점선 테두리가 없어 점을 직접 찍는다
                line.AddToClassList("ui-flowline--off");
                for (int i = 0; i < 3; i++)
                {
                    var dot = new VisualElement();
                    dot.style.position = Position.Absolute;
                    dot.style.width = 6f;
                    dot.style.height = 6f;
                    dot.style.borderTopLeftRadius = 3f;
                    dot.style.borderTopRightRadius = 3f;
                    dot.style.borderBottomLeftRadius = 3f;
                    dot.style.borderBottomRightRadius = 3f;
                    dot.style.backgroundColor = new Color(0.18f, 0.26f, 0.40f);
                    float t = (i + 0.5f) / 3f;
                    if (vertical) { dot.style.left = thick * 0.5f - 3f; dot.style.top = length * t - 3f; }
                    else          { dot.style.top = thick * 0.5f - 3f;  dot.style.left = length * t - 3f; }
                    line.Add(dot);
                }
            }
            return line;
        }

        // ───────────────────── 허용 목록 ─────────────────────

        void UpdateSelectionHeader()
        {
            if (!hasSelection)
            {
                selTitle.text = "출력 포트 없음";
                btnAllowAll.SetEnabled(false);
                btnBlockAll.SetEnabled(false);
                return;
            }

            var state = target.StateOf(selected);

            // 이미 그 상태인 버튼은 누를 이유가 없다 — 눌러도 아무 일이 안 일어나는 버튼은
            // "먹히지 않는다"는 인상을 준다
            btnAllowAll.SetEnabled(state != OutletState.All);
            btnBlockAll.SetEnabled(state != OutletState.Blocked);
            selTitle.text = state switch
            {
                OutletState.Blocked => "선택한 출구 · 차단됨",
                OutletState.Only    => "선택한 출구 · 지정 아이템만",
                _                   => "선택한 출구 · 전체 허용",
            };
            selSwatch.style.backgroundColor = state == OutletState.Blocked ? UIFlowColors.Blocked : UIFlowColors.Out;
        }

        void RebuildRows()
        {
            if (rows == null) return;
            rows.Clear();
            tooltip?.Hide();   // 호버 중이던 행이 교체되면 Leave가 안 온다

            UpdateSelectionHeader();

            var db = ItemDatabaseSO.LoadDefault();
            if (db == null || db.items == null || !hasSelection)
            {
                Show(listEmpty, true);
                listEmpty.text = hasSelection ? "ItemDatabase 없음 (Resources 확인)" : "출력 포트가 없습니다";
                return;
            }

            int shown = 0;

            foreach (var item in UIItemOrder.Sorted(db.items))
            {
                if (item.hideFromMenu) continue;   // 내부 탄약 등 — 벨트에 오를 일이 없는 것은 필터에서 뺀다

                string name = DisplayNameOf(item);
                if (search.Length > 0 && name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                rows.Add(MakeRow(item, name));
                shown++;
            }

            Show(listEmpty, shown == 0);
            if (shown == 0) listEmpty.text = "검색 결과가 없습니다";
        }

        /// <summary>
        /// 이 아이템이 지금 이 출구로 지나갈 수 있는가 — 토글이 보여주는 값.
        ///
        /// 허용 목록이 비어 있는 출구는 "아무것도 허용 안 함"이 아니라 "전부 지나감"이다.
        /// 그래서 목록이 비었을 때 토글을 전부 꺼서 보여주면 화면이 거짓말을 한다 —
        /// 켜진 것이 곧 지나가는 것이어야 한다.
        /// </summary>
        bool AllowedNow(ItemDataSO item) => target.StateOf(selected) switch
        {
            OutletState.Blocked => false,
            OutletState.All     => true,
            _                   => target.IsAllowedAt(selected, item),
        };

        VisualElement MakeRow(ItemDataSO item, string name)
        {
            bool on = AllowedNow(item);

            var row = new VisualElement();
            row.AddToClassList("ui-allowrow");
            row.AddToClassList(on ? "ui-allowrow--on" : "ui-allowrow--off");

            var sw = new VisualElement();
            sw.AddToClassList("ui-allowrow__sw");
            var knob = new VisualElement();
            knob.AddToClassList("ui-allowrow__knob");
            sw.Add(knob);
            row.Add(sw);

            var ic = new VisualElement();
            ic.AddToClassList("ui-allowrow__ic");
            // 문서의 filter:grayscale는 USS에 없다 — 끈 상태는 틴트(색 사각형이면 색 자체)를 죽인다
            UIItemIcon.ApplyToggle(ic, item, on);
            row.Add(ic);

            var nm = new Label(name);
            nm.AddToClassList("ui-allowrow__nm");
            row.Add(nm);

            // "지나가는 중"은 실제로 이 분배기를 통과한 아이템에만 붙는다 —
            // 전체 목록에서 찾는 수고를 덜고, 라인을 잘못 이었을 때도 여기서 드러난다
            var meta = new Label(MetaOf(item));
            meta.AddToClassList("ui-allowrow__meta");
            row.Add(meta);

            tooltip?.AttachItem(row, item);

            var captured = item;
            row.RegisterCallback<ClickEvent>(_ => Toggle(captured));
            return row;
        }

        /// <summary>
        /// 토글 하나. 화면의 "켜짐 = 지나감"을 심의 두 표현(빈 목록 = 전부 / 목록 = 그것만)으로 옮긴다.
        ///
        /// 전부 지나가던 출구에서 하나를 끄면 그때 목록을 실체화한다 — 그 전까지는 비워 두는 편이
        /// 낫다. 목록이 비어 있는 동안은 나중에 추가되는 아이템도 자동으로 지나가기 때문이다.
        /// 같은 이유로 다시 전부 켜지면 목록을 도로 비운다.
        /// </summary>
        void Toggle(ItemDataSO item)
        {
            var state = target.StateOf(selected);

            if (state == OutletState.Blocked)
            {
                // 막힌 출구에서 하나를 켜면 그것만 지나가는 출구가 된다.
                // 켤 수 없게 막아 두면 "전체 허용"으로 되돌리는 길밖에 없어 막다른 골목이 된다.
                target.SetBlocked(selected, false);
                target.AddFilter(selected, item);
                RebuildAll();
                return;
            }

            if (state == OutletState.All)
            {
                var others = AllItems().Where(x => x != item).ToList();
                if (others.Count == 0) target.SetBlocked(selected, true);   // 아이템이 이것뿐이면 곧 차단이다
                else foreach (var o in others) target.AddFilter(selected, o);
            }
            else if (target.IsAllowedAt(selected, item))
            {
                target.RemoveFilter(selected, item);
                // 마지막 하나까지 끄면 아무것도 안 지나간다 = 차단. 빈 목록으로 두면 "전부 허용"으로 뒤집힌다
                if (target.AllowedAt(selected).Count == 0) target.SetBlocked(selected, true);
            }
            else
            {
                target.AddFilter(selected, item);
                if (target.AllowedAt(selected).Count >= AllItems().Count())
                    target.ClearFilter(selected);       // 전부 켜졌다 → 빈 목록(=전부 허용)으로 되돌린다
            }

            RebuildAll();
        }

        static IEnumerable<ItemDataSO> AllItems()
        {
            var db = ItemDatabaseSO.LoadDefault();
            if (db == null || db.items == null) yield break;
            foreach (var i in db.items) if (i != null && !i.hideFromMenu) yield return i;
        }

        string MetaOf(ItemDataSO item)
        {
            if (!target.HasPassed(item)) return "";

            // 다른 출구에도 걸려 있으면 그것부터 알린다 — 한 아이템이 여러 출구로 나뉠 수 있다
            int others = 0;
            foreach (var d in target.DirectionsOf(item))
                if (d != selected) others++;

            return others > 0 ? $"지나가는 중 · 다른 출구 {others}" : "지나가는 중";
        }

        // ───────────────────── 일괄 조작 ─────────────────────

        /// <summary>이 출구를 "무엇이든 지나감"으로 되돌린다 — 막힘 해제 + 허용 목록 비우기.</summary>
        void AllowAll()
        {
            if (!hasSelection) return;
            target.SetBlocked(selected, false);
            target.ClearFilter(selected);
            RebuildAll();
        }

        void BlockAll()
        {
            if (!hasSelection) return;
            target.SetBlocked(selected, true);   // 허용 목록도 함께 비워진다 (심 계약)
            RebuildAll();
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
