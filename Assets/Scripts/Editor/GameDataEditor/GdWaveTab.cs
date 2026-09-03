using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoreDawn.EditorTools
{
    // ═══════════════════════════════════════════════════════════
    //  웨이브 탭 — 밤 웨이브 규칙(점수식). 정본은 전투 탭의 모델(GdCombatTab.wave) — 이 탭은 같은 객체의 편집 뷰다.
    //  일차별 표는 사라졌다: score = (day × dayPoints + gate × gatePoints) × stimuli × (살아 있는 둥지 / 전체),
    //  점수가 곧 포인트이고 명단(roster)의 cost로 깎으며 스폰한다. 미리보기 표가 일차·게이트·파괴 수별 점수와 구성을 보여 준다.
    // ═══════════════════════════════════════════════════════════
    class GdWaveTab : GdTab
    {
        public override string Title => "웨이브";

        readonly GdCombatTab combat;
        readonly GdMapTab map;   // 미리보기의 둥지 수 = 맵 데이터의 둥지 수(임의 값 없음)

        public GdWaveTab(GameDataEditorWindow win, GdCombatTab combat, GdMapTab map) : base(win)
        {
            this.combat = combat; this.map = map;
            combat.onWavesChanged = () => { if (body != null) Render(); };
        }

        VisualElement body, previewHost;
        Label statLabel, warnLabel;

        public override void Build(VisualElement host)
        {
            host.style.backgroundColor = GdEnum.Bg;

            var top = new VisualElement();
            top.AddToClassList("gd-topbar");
            host.Add(top);
            var title = new Label("웨이브 규칙");
            title.AddToClassList("gd-topbar-title");
            top.Add(title);
            var small = new Label("점수 = (base + 일차×dayPoints + 게이트×gatePoints) × 총량(살아 있는 몫 + 강화분) · 점수가 곧 포인트");
            small.AddToClassList("gd-topbar-small");
            top.Add(small);
            top.Add(new VisualElement { style = { flexGrow = 1 } });
            statLabel = new Label();
            statLabel.AddToClassList("gd-stat");
            Mono(statLabel);
            top.Add(statLabel);

            var scroll = new ScrollView { style = { flexGrow = 1, paddingLeft = 14, paddingRight = 14, paddingTop = 10 } };
            host.Add(scroll);
            body = new VisualElement();
            scroll.Add(body);
            warnLabel = new Label { style = { color = GdEnum.Warn, fontSize = 12, whiteSpace = WhiteSpace.Normal, marginTop = 8 } };
            scroll.Add(warnLabel);
            scroll.Add(Hint(
                "밤마다 살아 있는 둥지 중 무작위 개수를 골라 그 둥지들의 스폰 포인트에서 버스트(뭉텅이)로 스폰한다. 버스트 수·간격은 목표 밤 길이(주야 시계의 밤 = 달 시간과 다름) ÷ 밤당 버스트 수, " +
                "버스트마다 점수/남은 버스트만큼을 명단에서 cost로 깎으며 뽑는다(weight = 지금 뽑힐 수 있는 항목들 사이의 확률 비율). " +
                "둥지를 부수면 총량(비율)은 줄고 자극(배율·버프)은 영구히 오른다. 진입로(맵의 nightSpawnPoints)에서는 점수 몬스터가 " +
                "untilKilledFraction만큼 잡힐 때까지 기본 몹 무리가 주기마다 나온다 — 점수·자극과 무관. 더 나올 것도 나온 것도 없으면 밤이 끝난다."));

            Render();
        }

        void Render()
        {
            body.Clear();
            var w = combat.wave;
            if (w == null) { body.Add(new Label("웨이브 규칙이 없습니다 — data.json의 wave 블록") { style = { color = GdEnum.Warn } }); return; }

            body.Add(GroupTitle("점수식"));
            var g1 = Grid();
            g1.Add(Cell("Base Points (밤마다 기본)", w.basePoints, v => { w.basePoints = Mathf.Max(0, v); Touch(); }));
            g1.Add(Cell("Day Points (일차당)", w.dayPoints, v => { w.dayPoints = Mathf.Max(0, v); Touch(); }));
            g1.Add(Cell("Gate Points (게이트당·합)", w.gatePoints, v => { w.gatePoints = Mathf.Max(0, v); Touch(); }));
            g1.Add(Cell("강화분 진폭 A", w.stimulusAmplitude, v => { w.stimulusAmplitude = Mathf.Max(0, v); Touch(); }));
            g1.Add(Cell("강화분 지수 p (≥1)", w.stimulusExponent, v => { w.stimulusExponent = Mathf.Max(1, v); Touch(); }));
            g1.Add(Cell("강화분 선형 b", w.stimulusLinear, v => { w.stimulusLinear = Mathf.Max(0, v); Touch(); }, last: true));
            body.Add(Hint("총량 = 살아 있는 몫 (1 − r) + 강화분 A·r^p + b·r, r = 파괴 수 / 전체 둥지 수. 버프 자극 = 총량 ÷ 살아 있는 몫(남은 둥지 하나의 강도)."));
            body.Add(g1);

            body.Add(GroupTitle("주야 시계"));
            var g0 = Grid();
            var clock = combat.dayCycle;   // 팩 dayCycle 블록 — 웨이브 규칙이 아니라 시계 설정
            g0.Add(Cell("낮 길이(초)", clock.dayDuration, v => { clock.dayDuration = Mathf.Max(1, v); Touch(); }));
            g0.Add(Cell("밤 길이(초) — 달이 뜨고 지는 시간", clock.nightDuration, v => { clock.nightDuration = Mathf.Max(1, v); Touch(); }, last: true));
            body.Add(g0);

            body.Add(GroupTitle("밤 진행"));
            var g2 = Grid();
            g2.Add(Cell("둥지 수 최소", w.nestsPerNightMin, v => { w.nestsPerNightMin = Mathf.Max(1, (int)v); Touch(); }));
            g2.Add(Cell("둥지 수 최대 (0=전부)", w.nestsPerNightMax, v => { w.nestsPerNightMax = Mathf.Max(0, (int)v); Touch(); }));
            g2.Add(Cell("목표 밤 길이(초)", w.targetNightLength, v => { w.targetNightLength = Mathf.Max(1, v); Touch(); }));
            g2.Add(Cell("밤당 버스트 수", w.burstsPerNight, v => { w.burstsPerNight = Mathf.Max(1, (int)v); Touch(); }));
            g2.Add(Cell("버스트 퍼짐(m)", w.burstSpread, v => { w.burstSpread = Mathf.Max(0, v); Touch(); }, last: true));
            body.Add(g2);

            body.Add(GroupTitle("자극 버프 — 값 = clamp(base + perStimulus × (자극 − 1))"));
            var effectIds = combat.effects.Select(e => e.id).Where(s => !string.IsNullOrEmpty(s)).ToList();
            for (int i = 0; i < w.stimulusBuffs.Count; i++)
            {
                var b = w.stimulusBuffs[i]; int idx = i;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
                var choices = new List<string>(effectIds);
                if (!string.IsNullOrEmpty(b.effect) && !choices.Contains(b.effect)) choices.Add(b.effect + " (없음)");
                var pick = new DropdownField(choices, Mathf.Max(0, choices.IndexOf(b.effect ?? ""))) { style = { width = 220 } };
                pick.RegisterValueChangedCallback(ev => { b.effect = ev.newValue.Replace(" (없음)", ""); Changed(); });
                row.Add(pick);
                row.Add(Mini("base", b.baseValue, v => { b.baseValue = v; Touch(); }));
                row.Add(Mini("per", b.perStimulus, v => { b.perStimulus = v; Touch(); }));
                row.Add(Mini("min", b.min, v => { b.min = v; Touch(); }));
                row.Add(Mini("max", b.max, v => { b.max = v; Touch(); }));
                row.Add(new Button(() => { w.stimulusBuffs.RemoveAt(idx); Changed(); Render(); }) { text = "✕" });
                body.Add(row);
            }
            body.Add(new Button(() => { w.stimulusBuffs.Add(new GStimulusBuff { effect = effectIds.FirstOrDefault() ?? "" }); Changed(); Render(); }) { text = "+ 버프" });

            body.Add(GroupTitle("명단 — cost는 점수에서 깎는 값, weight는 뽑힐 확률 비율, minDay/minGate는 등장 조건"));
            var monsterIds = combat.monsters.Select(m => m.id).Where(s => !string.IsNullOrEmpty(s)).ToList();
            for (int i = 0; i < w.roster.Count; i++)
            {
                var r = w.roster[i]; int idx = i;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
                var choices = new List<string>(monsterIds);
                if (!string.IsNullOrEmpty(r.monster) && !choices.Contains(r.monster)) choices.Add(r.monster + " (없음)");
                var pick = new DropdownField(choices, Mathf.Max(0, choices.IndexOf(r.monster ?? ""))) { style = { width = 220 } };
                pick.RegisterValueChangedCallback(ev => { r.monster = ev.newValue.Replace(" (없음)", ""); Changed(); });
                row.Add(pick);
                row.Add(Mini("cost", r.cost, v => { r.cost = Mathf.Max(0.1f, v); Touch(); }));
                row.Add(Mini("weight", r.weight, v => { r.weight = Mathf.Max(0, v); Touch(); }));
                row.Add(Mini("minDay", r.minDay, v => { r.minDay = Mathf.Max(1, (int)v); Touch(); }));
                row.Add(Mini("minGate", r.minGate, v => { r.minGate = Mathf.Max(0, (int)v); Touch(); }));
                row.Add(new Button(() => { w.roster.RemoveAt(idx); Changed(); Render(); }) { text = "✕" });
                body.Add(row);
            }
            body.Add(new Button(() => { w.roster.Add(new GRoster { monster = monsterIds.FirstOrDefault() ?? "", cost = 10, weight = 1 }); Changed(); Render(); }) { text = "+ 명단" });

            body.Add(GroupTitle("진입로 무리 (nightSpawnPoints) — 점수·자극과 무관한 지루함 방지"));
            var t = w.trickle;
            var g3 = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            {
                var choices = new List<string>(monsterIds);
                if (!string.IsNullOrEmpty(t.monster) && !choices.Contains(t.monster)) choices.Add(t.monster + " (없음)");
                choices.Insert(0, "(없음)");
                var pick = new DropdownField(choices, string.IsNullOrEmpty(t.monster) ? 0 : Mathf.Max(0, choices.IndexOf(t.monster))) { style = { width = 220 } };
                pick.RegisterValueChangedCallback(ev => { t.monster = ev.newValue == "(없음)" ? "" : ev.newValue.Replace(" (없음)", ""); Changed(); });
                g3.Add(pick);
                g3.Add(Mini("group", t.group, v => { t.group = Mathf.Max(1, (int)v); Touch(); }));
                g3.Add(Mini("interval", t.interval, v => { t.interval = Mathf.Max(1, v); Touch(); }));
                g3.Add(Mini("until", t.untilKilledFraction, v => { t.untilKilledFraction = Mathf.Clamp01(v); Touch(); }));
            }
            body.Add(g3);

            previewHost = new VisualElement();
            body.Add(previewHost);
            RenderPreview();

            RenderWarn();
            statLabel.text = $"명단 {w.roster.Count} · 자극 버프 {w.stimulusBuffs.Count}";
        }

        static float Score(GWaveRule w, int day, int gate, int living, int total)
        {
            if (total <= 0 || living <= 0) return 0f;
            return (w.basePoints + day * w.dayPoints + gate * w.gatePoints) * ((float)living / total + Bonus(w, total - living, total));
        }

        // 자극 강화분 h(r) — 심 WaveRuleDef.BonusFor와 같은 식
        static float Bonus(GWaveRule w, int destroyed, int total)
        {
            if (total <= 0) return 0f;
            float r = Mathf.Clamp01(destroyed / (float)total);
            return w.stimulusAmplitude * Mathf.Pow(r, w.stimulusExponent) + w.stimulusLinear * r;
        }

        // 대략 구성 — 가장 싼 항목 기준 마리 수 (weight 추첨은 실행마다 다르다)
        // 점수 → 마리 수 감각용: 명단에서 가장 싼 항목의 cost
        static string CheapestNote(GWaveRule w)
        {
            var priced = w.roster.Where(r => r.cost > 0).ToList();
            return priced.Count == 0 ? "(명단 없음)" : $"{priced.Min(r => r.cost):0}pt/마리";
        }

        void RenderWarn()
        {
            var w = combat.wave; var outp = new List<string>();
            if (w.roster.Count == 0) outp.Add("명단이 비어 있습니다 — 무엇을 스폰할지 없습니다");
            foreach (var r in w.roster)
            {
                if (string.IsNullOrEmpty(r.monster) || !combat.monsters.Any(m => m.id == r.monster)) outp.Add($"명단 — 몬스터 \"{r.monster}\" 를 찾을 수 없습니다");
                if (r.weight <= 0) outp.Add($"명단 — {r.monster}: weight 0은 뽑히지 않습니다");
            }
            if (!w.roster.Any(r => r.minDay <= 1 && r.minGate <= 0)) outp.Add("1일·게이트 0에 뽑힐 수 있는 항목이 없습니다 — 첫 밤에 아무것도 안 나옵니다");
            foreach (var b in w.stimulusBuffs)
                if (string.IsNullOrEmpty(b.effect) || !combat.effects.Any(e => e.id == b.effect)) outp.Add($"자극 버프 — 효과 \"{b.effect}\" 를 찾을 수 없습니다");
            if (!string.IsNullOrEmpty(w.trickle.monster) && !combat.monsters.Any(m => m.id == w.trickle.monster)) outp.Add($"진입로 무리 — 몬스터 \"{w.trickle.monster}\" 를 찾을 수 없습니다");
            warnLabel.text = string.Join("\n", outp);
        }

        // 값이 바뀌는 중(키 입력마다): 반영·경고·미리보기만. 히스토리는 Commit(포커스 아웃)에서 — 키마다 push하면 Ctrl+Z가 글자 단위가 된다
        void Touch() { win.MarkDirty(); RenderWarn(); RenderPreview(); statLabel.text = $"명단 {combat.wave.roster.Count} · 자극 버프 {combat.wave.stimulusBuffs.Count}"; }
        void Commit() => combat.PushHist();
        // 드롭다운·버튼처럼 한 번에 끝나는 변경
        void Changed() { Touch(); Commit(); }

        // 히스토리의 주인은 전투 탭(웨이브·주야 시계 모델이 거기 있다) — 복원 뒤 onWavesChanged로 다시 그려진다
        public override void Undo() => combat.Undo();
        public override void Redo() => combat.Redo();

        // ── 작은 UI 조각 ──
        // ── 미리보기 그래프 — 시리즈 색과 범례 ──
        static readonly Color[] Palette = { GdEnum.Accent, GdEnum.ItemC, GdEnum.Warn, GdEnum.Sel };
        static readonly Color BarBase = GdEnum.Warn, BarBonus = GdEnum.ItemC;   // 기획 차트: 붉은 조각 = 살아 있는 둥지의 스폰, 주황 조각 = 자극 강화분

        static VisualElement Legend(IEnumerable<LineChart.Series> list, string note)
        {
            var row = new VisualElement(); row.AddToClassList("gd-legend");
            foreach (var s in list)
            {
                var it = new VisualElement(); it.AddToClassList("gd-legend-item");
                var sw = new VisualElement { style = { backgroundColor = s.color } }; sw.AddToClassList("gd-legend-swatch"); it.Add(sw);
                it.Add(new Label(s.name)); row.Add(it);
            }
            var n = new Label(note); n.AddToClassList("gd-legend-note"); row.Add(n);
            return row;
        }

        static VisualElement Grid() => new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 6 } };

        // 미리보기만 다시 그린다 — 숫자를 치는 동안 본문 전체를 다시 만들면 입력 포커스가 끊긴다
        void RenderPreview()
        {
            previewHost.Clear();
            var w = combat.wave;
            // ── 미리보기 — 꺾은선. 둥지 수는 맵 데이터에서(맵마다 선 하나) ──
            var maps = map.maps.Where(m => m.nests.Count > 0).ToList();
            previewHost.Add(GroupTitle("미리보기 — 둥지 파괴가 밤 총량에 미치는 영향 (총량 = 자극 × 살아 있는 비율, 전체 = 1)"));
            if (maps.Count == 0)
                previewHost.Add(new Label("맵 데이터에 둥지가 없습니다 — 맵 탭에서 둥지를 놓으면 그 수 기준으로 그린다") { style = { color = GdEnum.Warn } });
            else
            {
                previewHost.Add(Legend(new[] { new LineChart.Series { name = "살아 있는 둥지의 스폰", color = BarBase }, new LineChart.Series { name = "자극 강화분", color = BarBonus } },
                                "x = 파괴한 둥지 수 · 회색 선 = 전체(1.0) · 강화분 = A·r^p + b·r"));
                var rowOfBars = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
                foreach (var m in maps)
                {
                    int total = m.nests.Count;
                    var xs = new string[total]; var baseVals = new float[total]; var bonus = new float[total];
                    for (int d = 0; d < total; d++)
                    {
                        xs[d] = d.ToString(); baseVals[d] = (total - d) / (float)total; bonus[d] = Bonus(w, d, total);
                    }
                    var box = new VisualElement { style = { marginRight = 16 } };
                    box.Add(new Label($"{m.id} · 둥지 {total}") { style = { fontSize = 11, color = GdEnum.Muted, marginBottom = 2 } });
                    var bars = new BarChart(); bars.style.width = 60 + 44 * total; bars.Set(xs, baseVals, bonus, BarBase, BarBonus, reference: 1f);
                    box.Add(bars); rowOfBars.Add(box);
                }
                previewHost.Add(rowOfBars);
            }

            previewHost.Add(GroupTitle("미리보기 — 일차별 점수"));
            {
                int total = maps.Count > 0 ? maps[0].nests.Count : 0;
                var xs = Enumerable.Range(1, 12).Select(d => d.ToString()).ToArray();
                float[] Line(int gate, int destroyed) => Enumerable.Range(1, 12).Select(d => total > 0 ? Score(w, d, gate, total - destroyed, total) : 0f).ToArray();
                var list = new List<LineChart.Series>
                {
                    new() { name = "게이트 0", color = Palette[0], ys = Line(0, 0) },
                    new() { name = "게이트 1", color = Palette[1], ys = Line(1, 0) },
                    new() { name = "게이트 2", color = Palette[2], ys = Line(2, 0) },
                };
                if (total >= 2) list.Add(new() { name = $"게이트 1 · 둥지 2/{total} 파괴 (강화분 +{Bonus(w, 2, total):0.##})", color = Palette[3], ys = Line(1, 2) });
                var chart = new LineChart(); chart.style.width = 640; chart.Set(xs, list, "pt");
                previewHost.Add(Legend(list, total > 0 ? $"x = 일차 · 둥지 {total}개({maps[0].id}) 기준 · 가장 싼 몹 {CheapestNote(w)}" : "맵에 둥지가 없어 점수 0"));
                previewHost.Add(chart);
            }

        }

        VisualElement Cell(string label, float value, Action<float> set, bool last = false)
        {
            var box = new VisualElement { style = { width = 190, marginRight = last ? 0 : 10, marginBottom = 6 } };
            box.Add(new Label(label) { style = { fontSize = 11, color = GdEnum.Muted } });
            var f = new FloatField { value = value };
            f.RegisterValueChangedCallback(e => set(e.newValue));
            f.RegisterCallback<FocusOutEvent>(_ => Commit());   // 입력을 마치면 한 단계로 기록
            box.Add(f);
            return box;
        }

        VisualElement Mini(string label, float value, Action<float> set)
        {
            var box = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 8 } };
            box.Add(new Label(label) { style = { fontSize = 11, color = GdEnum.Muted, marginRight = 3 } });
            var f = new FloatField { value = value, style = { width = 56 } };
            f.RegisterValueChangedCallback(e => set(e.newValue));
            f.RegisterCallback<FocusOutEvent>(_ => Commit());
            box.Add(f);
            return box;
        }
    }

    /// <summary>
    /// 꺾은선 그래프 — Painter2D로 격자·기준선·선·점을 그리고 눈금 글자는 DrawText. 시리즈마다 색 하나, y 최대는 1·2·5 단위로 올림.
    /// 데이터는 Set으로만 들어온다(그리기 안에서 계산하지 않는다).
    /// </summary>
    sealed class LineChart : VisualElement
    {
        public sealed class Series { public string name; public Color color; public float[] ys; }

        readonly List<Series> series = new();
        string[] xLabels = Array.Empty<string>();
        string yUnit = "";
        float yMax = 1f;
        float? refY;

        public LineChart() { AddToClassList("gd-chart"); generateVisualContent += Draw; }

        public void Set(string[] xs, IEnumerable<Series> list, string unit, float? reference = null)
        {
            xLabels = xs ?? Array.Empty<string>();
            series.Clear(); series.AddRange(list);
            yUnit = unit ?? ""; refY = reference;
            float m = refY ?? 0f;
            foreach (var s in series) foreach (var y in s.ys) m = Mathf.Max(m, y);
            yMax = NiceMax(m);
            MarkDirtyRepaint();
        }

        static float NiceMax(float m)
        {
            if (m <= 0f) return 1f;
            float p = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(m)));
            float n = m / p;
            float step = n <= 1f ? 1f : n <= 2f ? 2f : n <= 5f ? 5f : 10f;
            return step * p;
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            const float L = 56f, R = 14f, T = 12f, B = 26f;
            float w = r.width - L - R, h = r.height - T - B;
            if (w <= 10f || h <= 10f) return;
            var p = mgc.painter2D;
            int n = xLabels.Length;
            float X(int i) => n <= 1 ? L + w / 2f : L + w * i / (n - 1);
            float Y(float v) => T + h - h * Mathf.Clamp(v, 0f, yMax) / yMax;

            // 격자 + y 눈금(4칸)
            for (int i = 0; i <= 4; i++)
            {
                float y = T + h - h * i / 4f;
                p.BeginPath(); p.MoveTo(new Vector2(L, y)); p.LineTo(new Vector2(L + w, y));
                p.strokeColor = GdEnum.Line; p.lineWidth = 1f; p.Stroke();
                mgc.DrawText((yMax * i / 4f).ToString("0.##") + yUnit, new Vector2(4f, y - 7f), 10f, GdEnum.Faint);
            }
            // x 눈금
            for (int i = 0; i < n; i++) mgc.DrawText(xLabels[i], new Vector2(X(i) - 5f, T + h + 6f), 10f, GdEnum.Faint);
            // 기준선
            if (refY.HasValue)
            {
                p.BeginPath(); p.MoveTo(new Vector2(L, Y(refY.Value))); p.LineTo(new Vector2(L + w, Y(refY.Value)));
                p.strokeColor = GdEnum.Muted; p.lineWidth = 1f; p.Stroke();
            }
            // 시리즈
            foreach (var s in series)
            {
                int m = Mathf.Min(n, s.ys.Length);
                if (m == 0) continue;
                p.BeginPath();
                for (int i = 0; i < m; i++) { var pt = new Vector2(X(i), Y(s.ys[i])); if (i == 0) p.MoveTo(pt); else p.LineTo(pt); }
                p.strokeColor = s.color; p.lineWidth = 2f; p.Stroke();
                for (int i = 0; i < m; i++)
                {
                    p.BeginPath(); p.Arc(new Vector2(X(i), Y(s.ys[i])), 3f, 0f, 360f);
                    p.fillColor = s.color; p.Fill();
                }
            }
        }
    }

    /// <summary>
    /// 누적 막대 — 막대마다 아래 조각(base)과 위 조각(bonus). 기준선 하나. 그리기는 Painter2D, 눈금 글자는 DrawText.
    /// 기획 차트 "둥지 파괴가 밤 웨이브에 미치는 영향" 꼴 — 아래 = 살아 있는 둥지의 스폰, 위 = 자극 강화분.
    /// </summary>
    sealed class BarChart : VisualElement
    {
        string[] xLabels = Array.Empty<string>();
        float[] baseVals = Array.Empty<float>(), bonusVals = Array.Empty<float>();
        Color baseColor, bonusColor;
        float yMax = 1f;
        float? refY;

        public BarChart() { AddToClassList("gd-chart"); generateVisualContent += Draw; }

        public void Set(string[] xs, float[] baseValues, float[] bonusValues, Color baseC, Color bonusC, float? reference = null)
        {
            xLabels = xs; baseVals = baseValues; bonusVals = bonusValues; baseColor = baseC; bonusColor = bonusC; refY = reference;
            float m = refY ?? 0f;
            for (int i = 0; i < baseVals.Length; i++) m = Mathf.Max(m, baseVals[i] + (i < bonusVals.Length ? Mathf.Max(0f, bonusVals[i]) : 0f));
            yMax = m <= 0f ? 1f : Mathf.Ceil(m * 4f) / 4f;   // 0.25 단위로 올림
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            const float L = 40f, R = 10f, T = 12f, B = 26f;
            float w = r.width - L - R, h = r.height - T - B;
            int n = xLabels.Length;
            if (w <= 10f || h <= 10f || n == 0) return;
            var p = mgc.painter2D;
            float Y(float v) => T + h - h * Mathf.Clamp(v, 0f, yMax) / yMax;
            for (int i = 0; i <= 4; i++)
            {
                float y = T + h - h * i / 4f;
                p.BeginPath(); p.MoveTo(new Vector2(L, y)); p.LineTo(new Vector2(L + w, y));
                p.strokeColor = GdEnum.Line; p.lineWidth = 1f; p.Stroke();
                mgc.DrawText((yMax * i / 4f).ToString("0.##"), new Vector2(4f, y - 7f), 10f, GdEnum.Faint);
            }
            float slot = w / n, bw = slot * 0.6f;
            for (int i = 0; i < n; i++)
            {
                float x0 = L + slot * i + (slot - bw) / 2f;
                float b = i < baseVals.Length ? baseVals[i] : 0f, bo = i < bonusVals.Length ? Mathf.Max(0f, bonusVals[i]) : 0f;
                Rect(p, x0, Y(b), bw, Y(0f) - Y(b), baseColor);
                if (bo > 0f) Rect(p, x0, Y(b + bo), bw, Y(b) - Y(b + bo), bonusColor);
                mgc.DrawText(xLabels[i], new Vector2(x0 + bw / 2f - 4f, T + h + 6f), 10f, GdEnum.Faint);
                mgc.DrawText((b + bo).ToString("0.00"), new Vector2(x0 + bw / 2f - 12f, Y(b + bo) - 13f), 10f, GdEnum.Text);
            }
            if (refY.HasValue)
            {
                p.BeginPath(); p.MoveTo(new Vector2(L, Y(refY.Value))); p.LineTo(new Vector2(L + w, Y(refY.Value)));
                p.strokeColor = GdEnum.Muted; p.lineWidth = 1f; p.Stroke();
            }
        }

        static void Rect(Painter2D p, float x, float y, float w, float h, Color c)
        {
            if (h <= 0f) return;
            p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h)); p.ClosePath();
            p.fillColor = c; p.Fill();
        }
    }
}
