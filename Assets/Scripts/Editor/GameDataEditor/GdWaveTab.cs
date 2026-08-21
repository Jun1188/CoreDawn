#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// ═══════════════════════════════════════════════════════════
//  웨이브 탭 (Web/js/wave-editor.js 대응)
//
//  Day별 웨이브 옵션을 표 하나로 편집한다. 데이터 정본은 전투 탭(GdCombatTab)의
//  waves 배열 — 여기는 같은 배열을 읽고 쓰는 또 하나의 뷰다(저장소를 둘로 쪼개지
//  않는다). 쓰기는 combat.SetWaves 를 거쳐 day 순 정렬·히스토리까지 한 통로다.
// ═══════════════════════════════════════════════════════════
class GdWaveTab : GdTab
{
    public override string Title => "웨이브";
    readonly GdCombatTab combat;

    public GdWaveTab(GameDataEditorWindow win, GdCombatTab combat) : base(win)
    {
        this.combat = combat;
        combat.onWavesChanged = () => { if (tableHost != null) Render(); };
    }

    VisualElement tableHost;
    Label statLabel, warnLabel;

    class Col
    {
        public readonly string key, th, title;
        public Col(string key, string th, string title) { this.key = key; this.th = th; this.title = title; }
    }
    static readonly Col[] Cols =
    {
        new("day", "Day", "이 웨이브가 적용되기 시작하는 일차"),
        new("requiredCoreTier", "코어 티어", "코어 티어가 낮으면 이전 웨이브가 반복된다"),
        new("baseAmount", "총 마리수", "밤에 전멸시켜야 하는 총량 (물량제 밤의 길이)"),
        new("maxAliveAmount", "동시 상한", "한 번에 살아 있을 수 있는 수"),
        new("spawnInterval", "스폰 간격", "스폰 시도 간격(초)"),
        new("monsterMaxHp", "몬스터 HP", "0 = wave_settings.json → 프리팹 기본값 폴백"),
    };

    public override void Build(VisualElement host)
    {
        host.style.backgroundColor = GdEnum.Bg;

        var top = new VisualElement();
        top.AddToClassList("gd-topbar");
        host.Add(top);
        var title = new Label("웨이브 에디터");
        title.AddToClassList("gd-topbar-title");
        top.Add(title);
        var small = new Label("일차(Day)별 밤 공세 옵션");
        small.AddToClassList("gd-topbar-small");
        top.Add(small);
        var addB = new Button(AddWave) { text = "+ 웨이브 추가" };
        addB.AddToClassList("gd-btn-mini");
        top.Add(addB);
        top.Add(new VisualElement { style = { flexGrow = 1 } });
        statLabel = new Label();
        statLabel.AddToClassList("gd-stat");
        Mono(statLabel);
        top.Add(statLabel);

        var scroll = new ScrollView { style = { flexGrow = 1, paddingLeft = 14, paddingRight = 14, paddingTop = 10 } };
        host.Add(scroll);
        tableHost = new VisualElement();
        scroll.Add(tableHost);
        warnLabel = new Label { style = { color = GdEnum.Warn, fontSize = 12, whiteSpace = WhiteSpace.Normal, marginTop = 8 } };
        scroll.Add(warnLabel);
        scroll.Add(Hint(
            "웨이브 — 일차별 밤 공세. Day 가 오는 시점, Core Tier 는 그 웨이브가 나오기 위한 코어 조건이다" +
            "(모자라면 이전 웨이브가 반복된다). 몬스터 HP 0 = wave_settings.json → 프리팹 기본값 폴백."));

        Render();
    }

    void AddWave()
    {
        var rows = combat.waves.ToList();
        var last = rows.LastOrDefault();
        rows.Add(new GWave
        {
            displayName = "새 웨이브",
            day = last != null ? last.day + 1 : 1,
            requiredCoreTier = last?.requiredCoreTier ?? 0,
            baseAmount = last != null ? last.baseAmount + 2 : 4,
            maxAliveAmount = last != null ? last.maxAliveAmount + 2 : 4,
            spawnInterval = last?.spawnInterval ?? 2,
            monsterMaxHp = last?.monsterMaxHp ?? 0,
        });
        combat.SetWaves(rows);
        Render();
    }

    void Render()
    {
        tableHost.Clear();
        var waves = combat.waves;

        if (waves.Count == 0)
        {
            tableHost.Add(new Label("웨이브가 없습니다 — 위의 + 웨이브 추가로 시작하세요")
            { style = { color = GdEnum.Faint, fontSize = 12 } });
        }
        else
        {
            // 머리글
            var head = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            void Th(string text, float w, string tip = null)
            {
                var th = new Label(text) { tooltip = tip ?? "", style = { width = w } };
                th.AddToClassList("gd-th");
                head.Add(th);
            }
            Th("Id", 166, "Wave: 접두는 자동으로 붙는다");
            Th("이름", 166);
            foreach (var c in Cols) Th(c.th, 100, c.title);
            Th("", 60);
            tableHost.Add(head);

            for (int i = 0; i < waves.Count; i++)
            {
                var w = waves[i];
                int idx = i;
                var row = new VisualElement();
                row.AddToClassList("gd-td-row");

                // td — padding 5×8. 입력은 마진 0 스트레치라 th 와 같은 폭이 된다(열 정렬)
                void Td(float w2, VisualElement inner)
                {
                    var td = new VisualElement { style = { width = w2,
                        paddingLeft = 8, paddingRight = 8, paddingTop = 5, paddingBottom = 5 } };
                    inner.AddToClassList("gd-field-input");
                    td.Add(inner);
                    row.Add(td);
                }

                string bare = (w.id ?? "").StartsWith("Wave:") ? w.id.Substring(5) : w.id ?? "";
                var idF = Mono(new TextField { value = bare, tooltip = "Wave: 접두는 자동으로 붙는다" });
                idF.RegisterValueChangedCallback(e =>
                {
                    var clean = new string(e.newValue.Where(c => char.IsLetterOrDigit(c) || c == '_' || (c >= '가' && c <= '힣')).ToArray());
                    w.id = string.IsNullOrEmpty(clean) ? "" : "Wave:" + clean;
                    RefreshMeta();
                });
                idF.RegisterCallback<FocusOutEvent>(_ => combat.PushHist());
                Td(166, idF);

                var nameF = new TextField { value = w.displayName };
                nameF.RegisterValueChangedCallback(e => { w.displayName = e.newValue; RefreshMeta(); });
                nameF.RegisterCallback<FocusOutEvent>(_ => combat.PushHist());
                Td(166, nameF);

                void NumCell(System.Func<GWave, float> get, System.Action<GWave, float> set, string tip, bool integer)
                {
                    var f = new FloatField { value = get(w), tooltip = tip };
                    // td.num input — 오른쪽 정렬 (내부 input 요소가 자체 정렬을 갖고 있어 직접 지정)
                    var ti = f.Q("unity-text-input");
                    if (ti != null) ti.style.unityTextAlign = TextAnchor.MiddleRight;
                    f.RegisterValueChangedCallback(e =>
                    {
                        float v = Mathf.Max(0, e.newValue);
                        set(w, integer ? Mathf.RoundToInt(v) : v);
                        RefreshMeta();
                    });
                    // day 는 확정 시 재정렬까지 — 표가 통째로 다시 그려진다
                    f.RegisterCallback<FocusOutEvent>(_ => { combat.SetWaves(combat.waves.ToList()); Render(); });
                    Td(100, f);
                }
                NumCell(x => x.day, (x, v) => x.day = (int)v, Cols[0].title, true);
                NumCell(x => x.requiredCoreTier, (x, v) => x.requiredCoreTier = (int)v, Cols[1].title, true);
                NumCell(x => x.baseAmount, (x, v) => x.baseAmount = (int)v, Cols[2].title, true);
                NumCell(x => x.maxAliveAmount, (x, v) => x.maxAliveAmount = (int)v, Cols[3].title, true);
                NumCell(x => x.spawnInterval, (x, v) => x.spawnInterval = v, Cols[4].title, false);
                NumCell(x => x.monsterMaxHp, (x, v) => x.monsterMaxHp = v, Cols[5].title, false);

                var delB = new Button(() =>
                {
                    var rows = combat.waves.ToList();
                    rows.RemoveAt(idx);
                    combat.SetWaves(rows);
                    Render();
                }) { text = "삭제" };
                delB.AddToClassList("gd-btn-mini");
                delB.AddToClassList("gd-btn-warn");
                Td(60, delB);

                tableHost.Add(row);
            }
        }
        RefreshMeta();
    }

    void RefreshMeta()
    {
        var waves = combat.waves;
        statLabel.text = waves.Count > 0
            ? $"웨이브 {waves.Count}개 · Day {waves.Min(w => w.day)}~{waves.Max(w => w.day)}"
            : "";

        var warn = new List<string>();
        var seen = new HashSet<string>();
        foreach (var w in waves)
        {
            if (string.IsNullOrEmpty(w.id)) warn.Add($"Day {w.day}: id가 비어 있습니다 — 임포트에서 스킵됩니다");
            string key = w.day + "/" + w.requiredCoreTier;
            if (!seen.Add(key)) warn.Add($"Day {w.day} · 티어 {w.requiredCoreTier} 조합이 중복입니다 — 뒤 항목만 적용됩니다");
            if (w.maxAliveAmount > w.baseAmount) warn.Add($"Day {w.day}: 동시 상한이 총량보다 큽니다 — 상한이 의미 없습니다");
            if (!(w.spawnInterval > 0)) warn.Add($"Day {w.day}: 스폰 간격은 0보다 커야 합니다");
        }
        warnLabel.text = string.Join("\n", warn);
        win.RefreshSharedStat();
    }
}
#endif
