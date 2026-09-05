using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Inventories;
using CoreDawn.Sim;

namespace CoreDawn.UI
{
    /// <summary>
    /// 아이템·레시피 툴팁 — 포인터를 따라다니는 ui-tooltip 상자 (문서 §03 TTP). 내용은 정의(Def)에서 읽는다.
    ///
    /// 패널 루트마다 하나씩 만들어 쓴다 (UIDocument가 갈라져 있어 전역 하나로는 안 된다).
    /// 대상 요소에 Attach*로 걸면 Enter/Move/Leave를 알아서 처리한다 — 슬롯·행은
    /// 갱신 때마다 새로 만들어지므로 잡은 데이터가 낡을 일이 없다.
    /// </summary>
    public class UITooltip
    {
        readonly VisualElement root, box, sep, stats;
        readonly Label name, type, desc;

        public UITooltip(VisualElement panelRoot)
        {
            root = panelRoot;

            box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("ui-tooltip");
            box.style.position = Position.Absolute;
            box.style.display = DisplayStyle.None;

            name = new Label();
            name.AddToClassList("ui-tooltip__name");
            box.Add(name);

            type = new Label();
            type.AddToClassList("ui-tooltip__type");
            box.Add(type);

            desc = new Label();
            desc.AddToClassList("ui-tooltip__desc");
            box.Add(desc);

            sep = new VisualElement();
            sep.AddToClassList("ui-tooltip__sep");
            box.Add(sep);

            stats = new VisualElement();
            box.Add(stats);

            root.Add(box);
        }

        /// <summary>패널이 닫힐 때(Unbind) 호출 — 다시 열리면 새로 만든다.</summary>
        public void Dispose() => box.RemoveFromHierarchy();

        // ───────────────────── 대상에 걸기 ─────────────────────

        public void AttachItem(VisualElement target, ItemDef item)
        {
            if (item == null) return;
            target.RegisterCallback<PointerEnterEvent>(e => ShowItem(item, e.position));
            target.RegisterCallback<PointerMoveEvent>(e => Move(e.position));
            target.RegisterCallback<PointerLeaveEvent>(_ => Hide());
        }

        public void AttachRecipe(VisualElement target, RecipeDef recipe)
        {
            if (recipe == null) return;
            target.RegisterCallback<PointerEnterEvent>(e => ShowRecipe(recipe, e.position));
            target.RegisterCallback<PointerMoveEvent>(e => Move(e.position));
            target.RegisterCallback<PointerLeaveEvent>(_ => Hide());
        }

        // ───────────────────── 내용 ─────────────────────

        void ShowItem(ItemDef item, Vector2 pos)
        {
            name.text = NameOf(item);
            name.style.color = item.Line != ItemLine.None ? UIFlowColors.Of(item.Line) : StyleKeyword.Null;
            type.text = $"{item.Type.ToString().ToUpperInvariant()} · {LineName(item.Line)}";

            SetDesc(item.Description);

            stats.Clear();
            int have = PlayerCount(item);
            if (have >= 0) AddStat("보유", have.ToString());
            ShowStats(stats.childCount > 0);

            ShowAt(pos);
        }

        void ShowRecipe(RecipeDef recipe, Vector2 pos)
        {
            var output = recipe.Outputs != null && recipe.Outputs.Count > 0 ? recipe.Outputs[0].Item : null;
            int per = recipe.Outputs != null && recipe.Outputs.Count > 0 ? recipe.Outputs[0].Amount : 1;

            name.text = NameOf(output);
            name.style.color = output != null && output.Line != ItemLine.None
                ? UIFlowColors.Of(output.Line) : StyleKeyword.Null;

            float perMinute = recipe.Seconds > 0f ? 60f / recipe.Seconds * per : 0f;
            type.text = $"RECIPE · 회당 {per}개 · {Mathf.RoundToInt(perMinute)}개 / 분";

            SetDesc(!string.IsNullOrEmpty(recipe.Description) ? recipe.Description
                  : output != null ? output.Description : "");

            // 재료마다 필요량과 보유를 한 줄씩 — 만들 수 있는지 여기서 바로 읽힌다
            stats.Clear();
            AddStat("제작 시간", $"{recipe.Seconds:0.0}s");
            if (recipe.Inputs != null)
                foreach (var i in recipe.Inputs)
                {
                    if (i.Item == null) continue;
                    int have = PlayerCount(i.Item);
                    AddStat($"{NameOf(i.Item)} ×{i.Amount}", have >= 0 ? $"보유 {have}" : "");
                }
            ShowStats(true);

            ShowAt(pos);
        }

        void AddStat(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("ui-tooltip__stat");
            var l = new Label(label);
            row.Add(l);
            var v = new Label(value);
            v.AddToClassList("ui-tooltip__stat-value");
            row.Add(v);
            stats.Add(row);
        }

        void SetDesc(string text)
        {
            bool has = !string.IsNullOrEmpty(text);
            desc.text = has ? text : "";
            desc.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void ShowStats(bool on)
        {
            sep.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            stats.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ───────────────────── 배치 ─────────────────────

        void ShowAt(Vector2 pos)
        {
            box.style.display = DisplayStyle.Flex;
            Move(pos);
        }

        public void Move(Vector2 pos)
        {
            if (box.style.display == DisplayStyle.None) return;

            // 커서 오른쪽 아래가 기본. 첫 프레임에는 크기를 모르므로 최대폭으로 가정해 클램프
            float w = float.IsNaN(box.resolvedStyle.width) || box.resolvedStyle.width <= 0f
                ? 390f : box.resolvedStyle.width;
            float h = float.IsNaN(box.resolvedStyle.height) || box.resolvedStyle.height <= 0f
                ? 120f : box.resolvedStyle.height;

            float x = pos.x + 21f;
            float y = pos.y + 21f;
            if (x + w > root.resolvedStyle.width)  x = pos.x - w - 9f;   // 오른쪽이 모자라면 왼쪽으로
            if (y + h > root.resolvedStyle.height) y = pos.y - h - 9f;

            box.style.left = Mathf.Max(0f, x);
            box.style.top  = Mathf.Max(0f, y);
        }

        public void Hide() => box.style.display = DisplayStyle.None;

        // ───────────────────── 잡동사니 ─────────────────────

        static string NameOf(ItemDef item) =>
            item == null ? "" : string.IsNullOrEmpty(item.DisplayName) ? item.Id : item.DisplayName;

        static string LineName(ItemLine line) => line switch
        {
            ItemLine.Iron    => "구조 계통",
            ItemLine.Copper  => "전자 계통",
            ItemLine.Crystal => "동력 계통",
            ItemLine.Beast   => "괴수 소재",
            _                => "계통 없음",
        };

        /// <summary>플레이어 가방+핫바 보유량. 플레이어가 없는 씬은 -1 — 줄 자체를 뺀다.</summary>
        static int PlayerCount(ItemDef item)
        {
            var holder = PlayerInventoryHolder.Instance;
            if (holder == null) return -1;
            return holder.MainContainer.CountOf(item);
        }
    }
}
