#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Tutorial;
using CoreDawn.UI;

namespace CoreDawn.EditorTools
{
    // ═══════════════════════════════════════════════════════════
    //  튜토리얼 탭 — 안내 카드의 순서·문구·완료 조건을 편집한다.
    //
    //  데이터는 GameData.json 의 tutorial 배열. 저장+임포트하면 GameDataImporter 가
    //  Assets/Data/Tutorial/*.asset 과 조건 서브에셋, Resources/TutorialDatabase 를 갱신한다.
    //
    //  조건 종류는 런타임 클래스(TutorialConditionSO 파생)에서 긁는다 — 프로그래머가
    //  조건 클래스를 하나 만들면 등록 없이 이 탭의 "+ 조건" 메뉴에 바로 뜬다. 파라미터도
    //  그 클래스의 public 필드를 반사로 읽어 그린다(count/seconds/tier/itemType/item).
    // ═══════════════════════════════════════════════════════════
    class GdTutorialTab : GdTab
    {
        public override string Title => "튜토리얼";
        public GdTutorialTab(GameDataEditorWindow win) : base(win) { }

        List<GameDataImporter.TutorialStepDto> steps = new();
        int sel = -1;
        GdHistory hist;

        VisualElement listHost, detailHost;
        Label statLabel, warnLabel;

        // ── 조건 종류 — 런타임 클래스에서 ──

        class CondKind
        {
            public Type type;
            public string key, label;
            public FieldInfo[] fields;
        }

        static List<CondKind> kinds;
        static List<CondKind> Kinds => kinds ??= TypeCache.GetTypesDerivedFrom<TutorialConditionSO>()
            .Where(t => !t.IsAbstract)
            .Select(t => new CondKind
            {
                type = t, key = KeyOf(t), label = LabelOf(t),
                fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance),
            })
            .OrderBy(k => k.label, StringComparer.Ordinal)
            .ToList();

        static string KeyOf(Type t) => t.Name.EndsWith("Condition", StringComparison.Ordinal) ? t.Name[..^9] : t.Name;
        static string LabelOf(Type t) => t.GetCustomAttribute<TutorialConditionMenuAttribute>()?.Label ?? KeyOf(t);
        static CondKind KindOf(string key) => string.IsNullOrEmpty(key) ? null
            : Kinds.FirstOrDefault(k => k.key == key || k.type.Name == key);

        // ── 데이터 왕복 ──

        public override void OnDataLoaded()
        {
            ImportFromRoot();
            hist = new GdHistory(Snapshot, Restore, 60);
            hist.Reset();
        }

        void ImportFromRoot()
        {
            // root 와 객체를 공유하지 않는다 — 언두가 root 를 직접 건드리면 저장 전 상태가 뒤섞인다
            steps = (win.root?.tutorial ?? Array.Empty<GameDataImporter.TutorialStepDto>()).Select(Clone).ToList();
            SortSteps();
            sel = steps.Count > 0 ? 0 : -1;
        }

        public override void SyncToRoot()
        {
            if (win.root == null) return;
            win.root.tutorial = steps.Select(Clone).ToArray();
        }

        static GameDataImporter.TutorialStepDto Clone(GameDataImporter.TutorialStepDto d)
            => JsonConvert.DeserializeObject<GameDataImporter.TutorialStepDto>(JsonConvert.SerializeObject(d));

        void SortSteps()
        {
            var cur = sel >= 0 && sel < steps.Count ? steps[sel] : null;
            steps = steps.OrderBy(s => s.order).ThenBy(s => s.id ?? "", StringComparer.Ordinal).ToList();
            sel = cur != null ? steps.IndexOf(cur) : -1;
        }

        string Snapshot() => JsonConvert.SerializeObject(new { steps, sel });

        void Restore(string snap)
        {
            var o = JsonConvert.DeserializeAnonymousType(snap, new { steps = new List<GameDataImporter.TutorialStepDto>(), sel = -1 });
            steps = o.steps ?? new();
            sel = Mathf.Clamp(o.sel, -1, steps.Count - 1);
            Render();
        }

        void PushHist() { hist?.Push(); win.MarkDirty(); }
        public override void Undo() { if (hist?.Undo() == true) win.MarkDirty(); }
        public override void Redo() { if (hist?.Redo() == true) win.MarkDirty(); }

        public override bool DeleteSelection()
        {
            if (sel < 0 || sel >= steps.Count) return false;
            steps.RemoveAt(sel);
            sel = Mathf.Min(sel, steps.Count - 1);
            PushHist();
            Render();
            return true;
        }

        // ── 화면 ──

        public override void Build(VisualElement host)
        {
            host.style.backgroundColor = GdEnum.Bg;

            var top = new VisualElement();
            top.AddToClassList("gd-topbar");
            host.Add(top);
            var title = new Label("튜토리얼 에디터");
            title.AddToClassList("gd-topbar-title");
            top.Add(title);
            var small = new Label("안내 카드의 순서 · 문구 · 완료 조건");
            small.AddToClassList("gd-topbar-small");
            top.Add(small);
            var addB = new Button(AddStep) { text = "+ 스텝 추가" };
            addB.AddToClassList("gd-btn-mini");
            top.Add(addB);
            top.Add(new VisualElement { style = { flexGrow = 1 } });
            statLabel = new Label();
            statLabel.AddToClassList("gd-stat");
            Mono(statLabel);
            top.Add(statLabel);

            var body = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            host.Add(body);

            var left = new ScrollView { style = { width = 300, borderRightWidth = 1, borderRightColor = GdEnum.Line } };
            listHost = new VisualElement { style = { paddingTop = 6, paddingBottom = 6 } };
            left.Add(listHost);
            body.Add(left);

            var right = new ScrollView { style = { flexGrow = 1, paddingLeft = 14, paddingRight = 14, paddingTop = 10 } };
            detailHost = new VisualElement();
            right.Add(detailHost);
            warnLabel = new Label { style = { color = GdEnum.Warn, fontSize = 12, whiteSpace = WhiteSpace.Normal, marginTop = 8 } };
            right.Add(warnLabel);
            right.Add(Hint(
                "스텝은 order 순으로 뜬다. 완료 조건은 전부 충족해야 다음으로 넘어가며, 조건이 없는 스텝은 영영 끝나지 않는다(저작 중). " +
                "id 는 세이브 키다 — 배포 뒤에는 바꾸지 말 것. 새 조건 종류가 필요하면 TutorialConditionSO 를 상속한 클래스 하나를 만들면 " +
                "여기 메뉴에 바로 뜬다."));
            body.Add(right);

            Render();
        }

        void Render()
        {
            if (listHost == null) return;
            RenderList();
            RenderDetail();
            RefreshMeta();
        }

        void AddStep()
        {
            var last = steps.LastOrDefault();
            var s = new GameDataImporter.TutorialStepDto
            {
                id = "", displayName = "새 안내", order = last != null ? last.order + 10 : 10,
                tag = "GUIDE", body = "", keyHints = Array.Empty<string>(),
                conditions = Array.Empty<GameDataImporter.TutorialConditionDto>(),
            };
            steps.Add(s);
            sel = steps.Count - 1;
            PushHist();
            Render();
        }

        // ── 왼쪽 목록 ──

        void RenderList()
        {
            listHost.Clear();

            if (steps.Count == 0)
            {
                listHost.Add(new Label("스텝이 없습니다 — 위의 + 스텝 추가로 시작하세요")
                { style = { color = GdEnum.Faint, fontSize = 12, paddingLeft = 12, paddingTop = 8 } });
                return;
            }

            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                int idx = i;
                bool on = i == sel;

                var row = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Row, alignItems = Align.Center,
                        paddingLeft = 12, paddingRight = 10, paddingTop = 6, paddingBottom = 6,
                        backgroundColor = on ? GdEnum.Panel2 : Color.clear,
                        borderLeftWidth = 3, borderLeftColor = on ? GdEnum.Accent : Color.clear,
                    },
                };

                var ord = Mono(new Label(s.order.ToString()) { style = { width = 36, color = GdEnum.Faint, fontSize = 11 } });
                row.Add(ord);

                var col = new VisualElement { style = { flexGrow = 1 } };
                string bare = Bare(s.id);
                col.Add(new Label(string.IsNullOrEmpty(bare) ? "(id 없음)" : bare)
                { style = { color = string.IsNullOrEmpty(bare) ? GdEnum.Warn : GdEnum.Text, fontSize = 13 } });
                col.Add(new Label($"{s.tag}  ·  조건 {s.conditions?.Length ?? 0}")
                { style = { color = GdEnum.Muted, fontSize = 11 } });
                row.Add(col);

                row.RegisterCallback<ClickEvent>(_ => { sel = idx; Render(); });
                listHost.Add(row);
            }
        }

        // ── 오른쪽 상세 ──

        void RenderDetail()
        {
            detailHost.Clear();

            if (sel < 0 || sel >= steps.Count)
            {
                detailHost.Add(new Label("왼쪽에서 스텝을 고르거나 + 스텝 추가") { style = { color = GdEnum.Faint, fontSize = 12 } });
                return;
            }

            var s = steps[sel];

            detailHost.Add(GroupTitle("스텝"));

            var idF = Text("id 이름 (\"Tutorial:\" 자동 접두 — 세이브 키, 배포 뒤 변경 금지)", Bare(s.id),
                v => s.id = string.IsNullOrEmpty(Sanitize(v)) ? "" : "Tutorial:" + Sanitize(v));
            Commit(idF, () => { RenderList(); RefreshMeta(); });
            detailHost.Add(idF);

            var nameF = Text("displayName (에디터 표시용 이름)", s.displayName, v => s.displayName = v);
            Commit(nameF, RefreshMeta);
            detailHost.Add(nameF);

            var orderF = Int("order (작을수록 먼저 — 10 단위로 띄우면 사이에 끼워 넣기 쉽다)", s.order, v => s.order = v);
            Commit(orderF, () => { SortSteps(); RenderList(); RefreshMeta(); });
            detailHost.Add(orderF);

            detailHost.Add(GroupTitle("카드"));

            var tagF = Text("tag (왼쪽 위 배지 — 대문자 짧은 낱말: GUIDE / BUILD / NIGHT)", s.tag, v => s.tag = v);
            Commit(tagF, RenderList);
            detailHost.Add(tagF);

            var bodyF = Text("body (본문 — 한 문장 두 줄 안쪽으로. 줄바꿈은 그대로 표시된다)", s.body, v => s.body = v, multiline: true);
            Commit(bodyF, null);
            detailHost.Add(bodyF);

            var keysF = Text("keyHints (본문 아래 키캡 — 공백으로 구분: W A S D)",
                string.Join(" ", s.keyHints ?? Array.Empty<string>()),
                v => s.keyHints = v.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            Commit(keysF, null);
            detailHost.Add(keysF);

            detailHost.Add(GroupTitle("진행 속도"));

            var minF = Num("minSeconds (읽을 시간, 초 — 카드가 들어오는 연출 시간은 자동으로 더해진다)", s.minSeconds,
                v => s.minSeconds = Mathf.Max(0f, v));
            Commit(minF, null);
            detailHost.Add(minF);

            var inOrder = new Toggle("requireInOrder — 앞질러 해도 건너뛰지 않는다 (자기 차례가 와야 판정 시작)") { value = s.requireInOrder };
            inOrder.tooltip = "숫자키·T처럼 다른 안내를 따르다 얻어걸리기 쉬운 동작, 그리고 밤처럼 반드시 읽혀야 하는 경고에 켠다";
            inOrder.RegisterValueChangedCallback(e => { s.requireInOrder = e.newValue; PushHist(); });
            detailHost.Add(inOrder);

            detailHost.Add(GroupTitle("완료 조건 — 전부 충족해야 끝난다"));

            var conds = (s.conditions ?? Array.Empty<GameDataImporter.TutorialConditionDto>()).ToList();
            if (conds.Count == 0)
                detailHost.Add(WarnItem("조건이 없습니다 — 이 안내는 영영 끝나지 않습니다 (저작 중일 때만 그대로 두세요)"));

            for (int i = 0; i < conds.Count; i++)
                detailHost.Add(BuildConditionBox(s, conds, i));

            var addC = new Button(() => ShowAddConditionMenu(s)) { text = "+ 조건 추가 ▾" };
            addC.AddToClassList("gd-btn-mini");
            addC.style.marginTop = 6;
            detailHost.Add(addC);
        }

        VisualElement BuildConditionBox(GameDataImporter.TutorialStepDto s, List<GameDataImporter.TutorialConditionDto> conds, int i)
        {
            var c = conds[i];
            var kind = KindOf(c.type);

            var box = new VisualElement
            {
                style =
                {
                    borderLeftWidth = 2, borderLeftColor = kind != null ? GdEnum.Accent : GdEnum.Warn,
                    backgroundColor = GdEnum.Panel, paddingLeft = 10, paddingRight = 8, paddingTop = 6, paddingBottom = 6,
                    marginBottom = 6,
                },
            };

            var head = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var labels = Kinds.Select(k => k.label).ToList();
            int kindIdx = kind != null ? Kinds.IndexOf(kind) : -1;
            if (kindIdx < 0) { labels.Insert(0, $"{c.type} — 알 수 없음"); kindIdx = 0; }

            var typeD = new DropdownField(labels, kindIdx) { style = { flexGrow = 1 } };
            typeD.RegisterValueChangedCallback(e =>
            {
                var k = Kinds.FirstOrDefault(x => x.label == e.newValue);
                if (k == null) return;
                c.type = k.key;
                PushHist();
                RenderDetail(); RenderList(); RefreshMeta();
            });
            head.Add(typeD);

            var delB = new Button(() =>
            {
                conds.RemoveAt(i);
                s.conditions = conds.ToArray();
                PushHist();
                RenderDetail(); RenderList(); RefreshMeta();
            }) { text = "삭제" };
            delB.AddToClassList("gd-btn-mini");
            delB.AddToClassList("gd-btn-warn");
            delB.style.marginLeft = 6;
            head.Add(delB);
            box.Add(head);

            if (kind == null)
            {
                box.Add(WarnItem("이 조건 종류를 아는 클래스가 없습니다 — 임포트에서 제외됩니다. 종류를 다시 고르세요."));
                return box;
            }

            // 파라미터 — 조건 클래스의 public 필드를 그대로 그린다
            bool any = false;
            foreach (var f in kind.fields)
            {
                any = true;
                switch (f.Name)
                {
                    case "count":
                    {
                        var el = Int("count — 몇 번 / 몇 개", c.count, v => c.count = Mathf.Max(1, v));
                        Commit(el, null); box.Add(el); break;
                    }
                    case "seconds":
                    {
                        var el = Num("seconds — 이동을 누적해야 하는 초 (시점 회전은 1/4만)", c.seconds, v => c.seconds = Mathf.Max(0.1f, v));
                        Commit(el, null); box.Add(el); break;
                    }
                    case "tier":
                    {
                        var el = Int("tier — 목표 코어 티어 (횟수가 아니라 도달 지점)", c.tier, v => c.tier = Mathf.Max(1, v));
                        Commit(el, null); box.Add(el); break;
                    }
                    case "itemType":
                    {
                        var choices = new List<string> { "(클래스 기본값)" };
                        choices.AddRange(GdEnum.ItemTypes.Select(t => $"{t.v} — {t.ko}"));
                        int idx = 0;
                        if (!string.IsNullOrEmpty(c.itemType))
                        {
                            int at = Array.FindIndex(GdEnum.ItemTypes, t => t.v == c.itemType);
                            if (at >= 0) idx = at + 1;
                            else { choices.Add($"{c.itemType} — 알 수 없음"); idx = choices.Count - 1; }
                        }
                        box.Add(Drop("itemType — 아이템 분류", choices, idx, sel2 =>
                        {
                            c.itemType = sel2 == 0 ? null : (sel2 - 1 < GdEnum.ItemTypes.Length ? GdEnum.ItemTypes[sel2 - 1].v : c.itemType);
                            PushHist();
                        }));
                        break;
                    }
                    case "item":
                    {
                        var ids = (win.root?.items ?? Array.Empty<GameDataImporter.ItemDto>())
                            .Select(it => it.id).Where(id => !string.IsNullOrEmpty(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
                        var choices = new List<string> { "(없음)" };
                        choices.AddRange(ids);
                        int idx = 0;
                        if (!string.IsNullOrEmpty(c.item))
                        {
                            int at = ids.IndexOf(c.item);
                            if (at >= 0) idx = at + 1;
                            else { choices.Add($"{c.item} — 알 수 없음"); idx = choices.Count - 1; }
                        }
                        box.Add(Drop("item — 특정 아이템 id", choices, idx, sel2 =>
                        {
                            c.item = sel2 == 0 ? null : (sel2 - 1 < ids.Count ? ids[sel2 - 1] : c.item);
                            PushHist();
                        }));
                        break;
                    }
                    default:
                        box.Add(Hint($"{f.Name} — 이 에디터가 모르는 필드입니다. 임포트 뒤 SO 인스펙터에서 편집하세요."));
                        break;
                }
            }
            if (!any) box.Add(Hint("설정할 값이 없는 조건입니다."));

            return box;
        }

        void ShowAddConditionMenu(GameDataImporter.TutorialStepDto s)
        {
            var menu = new GenericMenu();
            foreach (var k in Kinds)
            {
                var captured = k;
                menu.AddItem(new GUIContent(k.label), false, () =>
                {
                    var list = (s.conditions ?? Array.Empty<GameDataImporter.TutorialConditionDto>()).ToList();
                    list.Add(new GameDataImporter.TutorialConditionDto { type = captured.key });
                    s.conditions = list.ToArray();
                    PushHist();
                    RenderDetail(); RenderList(); RefreshMeta();
                });
            }
            menu.ShowAsContext();
        }

        // ── 검사 ──

        void RefreshMeta()
        {
            statLabel.text = steps.Count > 0 ? $"스텝 {steps.Count}개 · 조건 {steps.Sum(s => s.conditions?.Length ?? 0)}개" : "";

            var warn = new List<string>();
            var seen = new HashSet<string>();
            foreach (var s in steps)
            {
                string label = string.IsNullOrEmpty(Bare(s.id)) ? $"order {s.order}" : Bare(s.id);
                if (string.IsNullOrEmpty(s.id)) warn.Add($"{label}: id가 비어 있습니다 — 임포트에서 스킵됩니다");
                else if (!seen.Add(s.id)) warn.Add($"{label}: id가 중복입니다 — 뒤 항목이 앞을 덮어씁니다");
                if (string.IsNullOrEmpty(s.displayName)) warn.Add($"{label}: displayName이 비어 있습니다 — 임포트에서 스킵됩니다");
                if (s.conditions == null || s.conditions.Length == 0) warn.Add($"{label}: 조건이 없어 영영 끝나지 않습니다");
                else foreach (var c in s.conditions)
                    if (KindOf(c.type) == null) warn.Add($"{label}: 알 수 없는 조건 type '{c.type}'");
            }
            warnLabel.text = string.Join("\n", warn);
            win.RefreshSharedStat();
        }

        // ── 유틸 ──

        static string Bare(string id) => (id ?? "").StartsWith("Tutorial:") ? id.Substring(9) : id ?? "";

        static string Sanitize(string s)
            => new string((s ?? "").Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || (ch >= '가' && ch <= '힣')).ToArray());

        /// <summary>입력을 떠날 때 히스토리에 쌓고 뒤처리 — 타이핑마다 스냅샷이 쌓이지 않게 (웨이브 탭과 같은 규칙).</summary>
        void Commit(VisualElement field, Action after)
        {
            VisualElement inner = field.Q<TextField>();
            if (inner == null) inner = field.Q<IntegerField>();
            if (inner == null) inner = field.Q<FloatField>();
            if (inner == null) return;

            inner.RegisterCallback<FocusOutEvent>(_ => { PushHist(); after?.Invoke(); });
        }
    }
}
#endif
