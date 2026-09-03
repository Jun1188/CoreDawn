#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.UI;

namespace CoreDawn.EditorTools
{
    // ═══════════════════════════════════════════════════════════
    //  몬스터 탭 — 종류(MonsterDataSO)의 체력·이동·공격·보스 수치와 프리팹.
    //
    //  데이터 정본은 전투 탭(GdCombatTab)의 monsters 배열 — 여기는 그 배열을 읽고 쓰는 또 하나의 뷰다
    //  (웨이브 탭과 같은 관계). 쓰기는 combat.SetMonsters / PushHist 를 거쳐 히스토리까지 한 통로.
    //  좌 목록 / 우 상세 — 필드가 많아 표(웨이브 탭)로는 안 읽힌다.
    // ═══════════════════════════════════════════════════════════
    class GdMonsterTab : GdTab
    {
        public override string Title => "몬스터";
        readonly GdCombatTab combat;

        public GdMonsterTab(GameDataEditorWindow win, GdCombatTab combat) : base(win)
        {
            this.combat = combat;
            combat.onMonstersChanged = () => { if (listBox != null) Render(); };
        }

        VisualElement listBox, detailBox;
        Label statLabel, warnLabel;
        int cur;

        internal override (string section, string id) RawCursor => ("entities", GdPack.Bare(combat.monsters.ElementAtOrDefault(cur)?.id));
        internal override void SelectRaw(string section, string id)
        {
            int i = combat.monsters.FindIndex(m => GdPack.Bare(m.id) == id);
            if (i < 0) return;
            cur = i;
            if (listBox != null) Render();
        }

        public override void Build(VisualElement host)
        {
            host.style.backgroundColor = GdEnum.Bg;

            var top = new VisualElement();
            top.AddToClassList("gd-topbar");
            host.Add(top);
            var title = new Label("몬스터 에디터");
            title.AddToClassList("gd-topbar-title");
            top.Add(title);
            var small = new Label("종류별 체력·이동·공격·보스 리쉬");
            small.AddToClassList("gd-topbar-small");
            top.Add(small);
            var addB = new Button(AddMonster) { text = "+ 몬스터 추가" };
            addB.AddToClassList("gd-btn-mini");
            top.Add(addB);
            var delB = new Button(DeleteCurrent) { text = "삭제" };
            delB.AddToClassList("gd-btn-mini");
            delB.AddToClassList("gd-btn-warn");
            top.Add(delB);
            top.Add(new VisualElement { style = { flexGrow = 1 } });
            statLabel = new Label();
            statLabel.AddToClassList("gd-stat");
            Mono(statLabel);
            top.Add(statLabel);

            var main = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            host.Add(main);

            var left = new ScrollView { style = { width = 260 } };
            left.AddToClassList("gd-leftcol");
            main.Add(left);
            listBox = new VisualElement { style = { marginTop = 6, minHeight = 200 } };
            left.Add(listBox);

            var right = new ScrollView { style = { flexGrow = 1, paddingLeft = 14, paddingRight = 14, paddingTop = 10 } };
            main.Add(right);
            detailBox = new VisualElement();
            right.Add(detailBox);
            warnLabel = new Label { style = { color = GdEnum.Warn, fontSize = 12, whiteSpace = WhiteSpace.Normal, marginTop = 8 } };
            right.Add(warnLabel);
            right.Add(Hint(
                "몬스터 — 종류 하나의 정의. 웨이브(웨이브 탭)가 어떤 종류를 몇 마리 내보낼지 고르고, 그날의 강약은 웨이브 버프(효과)로 준다. " +
                "프리팹은 씬 표현일 뿐이다 — HP·이동·공격 수치는 여기서 정하고 프리팹의 값은 무시된다."));

            Render();
        }

        void AddMonster()
        {
            var rows = combat.monsters.ToList();
            rows.Add(new GMonster { id = "", displayName = "새 몬스터" });
            combat.SetMonsters(rows);
            cur = rows.Count - 1;
            Render();
        }

        void DeleteCurrent()
        {
            if (combat.monsters.Count == 0) return;
            var rows = combat.monsters.ToList();
            rows.RemoveAt(Mathf.Clamp(cur, 0, rows.Count - 1));
            combat.SetMonsters(rows);
            cur = Mathf.Clamp(cur, 0, Mathf.Max(0, rows.Count - 1));
            Render();
        }

        void Render()
        {
            RenderList();
            RenderDetail();
            RefreshMeta();
        }

        void RenderList()
        {
            listBox.Clear();
            var list = combat.monsters;
            if (list.Count == 0)
            {
                listBox.Add(new Label("몬스터가 없습니다 — 위의 + 몬스터 추가로 시작하세요")
                { style = { color = GdEnum.Faint, fontSize = 12, whiteSpace = WhiteSpace.Normal } });
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                int idx = i;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                row.AddToClassList("gd-bitem");
                if (i == cur) row.AddToClassList("gd-bitem--sel");
                row.RegisterCallback<ClickEvent>(_ => { cur = idx; Render(); });
                var nm = new Label(string.IsNullOrEmpty(m.displayName) ? "(이름 없음)" : m.displayName) { style = { flexGrow = 1 } };
                row.Add(nm);
                var meta = new Label($"HP {m.maxHp:0}") { style = { color = GdEnum.Muted, fontSize = 11 } };
                Mono(meta);
                row.Add(meta);
                listBox.Add(row);
            }
        }

        void RenderDetail()
        {
            detailBox.Clear();
            var m = combat.monsters.ElementAtOrDefault(cur);
            if (m == null) return;

            // ── 식별 ──
            detailBox.Add(H3("식별"));
            string bare = GdPack.Bare(m.id);
            var idF = Mono(new TextField { value = bare, tooltip = "coredawn:entity/ 접두는 자동으로 붙는다. 세이브가 이 id로 종류를 되살린다 — 바꾸면 옛 세이브의 몬스터가 기본 종류로 돌아온다" });
            idF.RegisterValueChangedCallback(e =>
            {
                var clean = new string(e.newValue.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                m.id = string.IsNullOrEmpty(clean) ? "" : GdPack.Id("entity", clean);
                RefreshMeta();
            });
            idF.RegisterCallback<FocusOutEvent>(_ => { combat.PushHist(); RenderList(); });
            detailBox.Add(Field2("Id", idF));
            detailBox.Add(Text("이름", m.displayName, v => { m.displayName = v; RenderList(); RefreshMeta(); }));
            detailBox.Add(Text("설명", m.description, v => m.description = v, multiline: true));

            // ── 모델 — 팩 glb(스킨 + 클립) + 슬롯 재질. 조립기가 콜라이더·컴포넌트를 붙여 세운다
            detailBox.Add(H3("뷰"));
            detailBox.Add(Field2("모델", GdPackAssets.ModelList(m.models, () => (win.root?.materials ?? Array.Empty<GameDataJson.MaterialDto>()).Select(x => x.id).ToList(), () => { combat.PushHist(); RefreshMeta(); })));
            detailBox.Add(GdViewUI.Build(m.view ??= new GameDataJson.ViewDto { type = "Monster" }, combat.PushHist, win.SoundIds));

            // ── 수치 ──
            detailBox.Add(H3("체력"));
            detailBox.Add(Num("최대 체력", m.maxHp, v => { m.maxHp = Mathf.Max(1, v); RenderList(); RefreshMeta(); }));

            detailBox.Add(H3("이동"));
            detailBox.Add(Num("이동 속도 (m/s)", m.moveSpeed, v => m.moveSpeed = Mathf.Max(0, v)));
            detailBox.Add(Num("회전 속도 (도/초)", m.rotateSpeed, v => m.rotateSpeed = Mathf.Max(0, v)));
            detailBox.Add(Num("군중 반지름 (m)", m.crowdRadius, v => m.crowdRadius = Mathf.Max(0, v)));
            detailBox.Add(Num("넉백 감쇠율", m.knockbackDamping, v => m.knockbackDamping = Mathf.Max(0.01f, v)));
            var groundT = new Toggle("지면에 붙임 (끄면 비행 유닛)") { value = m.stickToGround };
            groundT.RegisterValueChangedCallback(e => { m.stickToGround = e.newValue; combat.PushHist(); });
            detailBox.Add(groundT);

            detailBox.Add(H3("공격"));
            detailBox.Add(Num("사거리 (m)", m.attackRange, v => m.attackRange = Mathf.Max(0, v)));
            detailBox.Add(Num("공격 간격 (초)", m.attackCooldown, v => m.attackCooldown = Mathf.Max(0.01f, v)));
            detailBox.Add(EffectRows("명중 효과", m.attackEffects, GdPack.Id("effect", "damage"), 10));

            detailBox.Add(H3("보스 리쉬·인내심 (보스로 배치될 때만)"));
            detailBox.Add(Num("최대 인내심 (초)", m.maxPatience, v => m.maxPatience = Mathf.Max(0, v)));
            detailBox.Add(Num("인내심 반경 (m, 0 = 교전 구역 추적 반경)", m.patienceRadius, v => m.patienceRadius = Mathf.Max(0, v)));
            detailBox.Add(Num("밖에 있을 때 소모 (초당)", m.outsidePatienceDrain, v => m.outsidePatienceDrain = Mathf.Max(0, v)));
            detailBox.Add(Num("카이팅당할 때 소모 (초당)", m.rangedPokePatienceDrain, v => m.rangedPokePatienceDrain = Mathf.Max(0, v)));
            detailBox.Add(Num("교전 중 회복 (초당)", m.patienceRecoverRate, v => m.patienceRecoverRate = Mathf.Max(0, v)));
            detailBox.Add(Num("강제 귀환 배수", m.absoluteLeashMultiplier, v => m.absoluteLeashMultiplier = Mathf.Max(1, v)));
            detailBox.Add(Num("복귀 중 재생 (최대 체력 비율/초)", m.returnRegenPerSecond, v => m.returnRegenPerSecond = Mathf.Max(0, v)));
            detailBox.Add(Num("복귀 제한 시간 (초, 0 = 없음)", m.returnTimeout, v => m.returnTimeout = Mathf.Max(0, v)));

            // 숫자 칸의 확정(FocusOut)마다 히스토리 — 상세 전체에 한 번만 건다
            detailBox.RegisterCallback<FocusOutEvent>(_ => combat.PushHist(), TrickleDown.TrickleDown);
        }

        /// <summary>효과 항목 편집 행 — 탄약의 attackEffects(그래프 탭)와 같은 문법: 드롭다운 + 값 + 삭제.</summary>
        VisualElement EffectRows(string label, List<GEff> entries, string defaultEffect, float defaultValue)
        {
            var holder = new VisualElement();
            var effectIds = combat.effects.Select(e => e.id).Where(id => !string.IsNullOrEmpty(id)).ToList();

            void Rebuild()
            {
                holder.Clear();
                holder.Add(GroupTitle(label));
                if (entries.Count == 0)
                    holder.Add(new Label("효과 없음 — 명중해도 아무 일도 일어나지 않습니다") { style = { color = GdEnum.Faint, fontSize = 11 } });
                for (int i = 0; i < entries.Count; i++)
                {
                    var eff = entries[i];
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                    var choices = new List<string>(effectIds);
                    if (!string.IsNullOrEmpty(eff.effect) && !choices.Contains(eff.effect)) choices.Add(eff.effect + " (없음)");
                    var pick = new DropdownField(choices, Mathf.Max(0, choices.IndexOf(eff.effect ?? ""))) { style = { flexGrow = 1 } };
                    if (!string.IsNullOrEmpty(eff.effect) && choices.Contains(eff.effect)) pick.SetValueWithoutNotify(eff.effect);
                    pick.RegisterValueChangedCallback(ev => { eff.effect = ev.newValue.Replace(" (없음)", ""); combat.PushHist(); RefreshMeta(); });
                    row.Add(pick);
                    var val = new FloatField { value = eff.value, style = { width = 56 } };
                    val.RegisterValueChangedCallback(ev => { eff.value = ev.newValue; RefreshMeta(); });
                    row.Add(val);
                    int idx = i;
                    row.Add(new Button(() => { entries.RemoveAt(idx); combat.PushHist(); Rebuild(); RefreshMeta(); }) { text = "✕" });
                    holder.Add(row);
                }
                var foot = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                foot.Add(new Button(() =>
                {
                    entries.Add(new GEff { effect = effectIds.Contains(defaultEffect) ? defaultEffect : effectIds.FirstOrDefault() ?? defaultEffect, value = defaultValue });
                    combat.PushHist(); Rebuild(); RefreshMeta();
                }) { text = "+ 효과" });
                holder.Add(foot);
            }
            Rebuild();
            return holder;
        }

        void RefreshMeta()
        {
            var list = combat.monsters;
            statLabel.text = list.Count > 0 ? $"몬스터 {list.Count}종" : "";

            var warn = new List<string>();
            var ids = new HashSet<string>();
            foreach (var m in list)
            {
                string nm = string.IsNullOrEmpty(m.displayName) ? "(이름 없음)" : m.displayName;
                if (string.IsNullOrEmpty(m.id)) warn.Add($"{nm}: id가 비어 있습니다 — 임포트에서 스킵됩니다");
                else if (!ids.Add(m.id)) warn.Add($"{nm}: id \"{m.id}\" 가 중복입니다");
                if (m.models.Count == 0 || string.IsNullOrEmpty(m.models[0].file)) warn.Add($"{nm}: 모델이 없습니다 — 코드 조립 캡슐로 나옵니다");
                if (!(m.maxHp > 0)) warn.Add($"{nm}: 최대 체력은 0보다 커야 합니다");
                if (m.attackEffects.Count == 0) warn.Add($"{nm}: 명중 효과가 없어 때려도 아무 일도 없습니다");
                foreach (var e in m.attackEffects)
                    if (!combat.effects.Any(x => x.id == e.effect)) warn.Add($"{nm}: 효과 \"{e.effect}\" 를 찾을 수 없습니다");
            }
            var used = new HashSet<string>(combat.wave.roster.Select(r => r.monster).Append(combat.wave.trickle.monster).Where(s => !string.IsNullOrEmpty(s)));
            foreach (var m in list)
                if (!string.IsNullOrEmpty(m.id) && !used.Contains(m.id))
                    warn.Add($"{m.displayName}: 웨이브 명단·진입로 무리가 이 종류를 쓰지 않습니다 (둥지 보스·방어자는 씬/프리팹이 정한다)");
            warnLabel.text = string.Join("\n", warn);
            win.RefreshSharedStat();
        }
    }
}
#endif
