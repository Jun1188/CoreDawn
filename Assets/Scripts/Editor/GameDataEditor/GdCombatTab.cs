#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Combat;
using CoreDawn.FPS;
using CoreDawn.UI;

namespace CoreDawn.EditorTools
{
    // ═══════════════════════════════════════════════════════════
    //  전투 탭 — Effect · Gun (Web/js/combat-editor.js 대응)
    //
    //  waves 배열의 정본도 여기다 — 웨이브 탭은 같은 배열을 읽고 쓰는 또 하나의
    //  뷰다(원본 wave-editor.js 와 같은 관계. 저장소를 둘로 쪼개지 않는다).
    //
    //  원본과 다른 점 하나: 원본 내보내기는 툴이 모르는 필드(visualKickbackRot 등)를
    //  떨궜다. 여기서는 항목마다 원본 DTO를 들고 있다가 편집 필드만 덮어써서
    //  그 손실을 막는다.
    // ═══════════════════════════════════════════════════════════

    class GEffect
    {
        public string id = "", displayName = "", description = "", kind = "Damage", stacking = "Refresh";
        public float duration, tickInterval;
        public string knockbackMode = "Directional";   // Knockback 전용 — 미는 방향 기준
        public List<string> affects = new();
        [JsonIgnore] public GameDataImporter.EffectDto src;
    }

    class GGun
    {
        public string id = "", displayName = "", description = "", fireMode = "Projectile", ammo = "";
        public bool isAutomatic;
        public float fireRate = 0.2f, range, reloadTime = 1.5f, zoomMultiplier = 1.3f, damageMultiplier = 1;
        public int magSize = 30, pellets = 1;
        public List<string> ammoFilter = new();
        public float xRecoil = 3, yRecoil = 2, zRecoil = 1, visualKickbackZ = 1;
        public float baseSpread = 0.5f, maxSpread = 5, spreadIncreasePerShot = 1, spreadRecoveryRate = 5;
        [JsonIgnore] public GameDataImporter.GunDto src;
    }

    class GWave
    {
        public string id = "", displayName = "", description = "";
        public int day = 1, requiredCoreTier, baseAmount = 4, maxAliveAmount = 4;
        public float spawnInterval = 2, monsterMaxHp;
        [JsonIgnore] public GameDataImporter.WaveDto src;
    }

    class GdCombatTab : GdTab
    {
        public override string Title => "전투";
        public GdCombatTab(GameDataEditorWindow win) : base(win) { }

        // EFFECT_KINDS (enums.js) — EffectKindMap 과 동일
        class KindInfo
        {
            public readonly string v, ko, desc;
            public readonly bool dur, tick, affects, knockback;
            public KindInfo(string v, string ko, string desc, bool dur = false, bool tick = false,
                            bool affects = false, bool knockback = false)
            { this.v = v; this.ko = ko; this.desc = desc; this.dur = dur; this.tick = tick; this.affects = affects; this.knockback = knockback; }
        }
        static readonly KindInfo[] EffectKinds =
        {
            new("Damage", "피해", "value 만큼 체력을 깎는다"),
            new("Heal", "회복", "value 만큼 체력을 채운다"),
            new("Knockback", "넉백", "value = 밀어내는 거리", knockback: true),
            new("DamageOverTime", "지속 피해", "tickInterval 마다 value 피해", dur: true, tick: true),
            new("MoveSpeed", "이동속도", "value = 배율 (0.5 = 절반)", dur: true),
            new("AttackModifier", "공격 증폭", "affects 의 효과를 value 배", dur: true, affects: true),
            new("IncomingDamage", "받는 피해", "value = 받는 피해 배율", dur: true),
        };
        static readonly string[] FireModes = { "Projectile", "Hitscan", "Aura" };
        // KnockbackMode(런타임 enum)와 같은 순서·이름이어야 한다 — 임포터가 이름으로 파싱한다
        static readonly string[] KnockbackModes = { "Directional", "Radial" };
        static readonly string[] KnockbackModeDesc =
        {
            "공격이 날아온 방향으로 민다 — 총알·히트스캔·근접",
            "명중점에서 바깥으로 민다 — 폭발·오라",
        };
        static readonly string[] Stacking = { "Refresh", "Stack" };
        static KindInfo KindOf(string v) => EffectKinds.FirstOrDefault(k => k.v == v) ?? EffectKinds[0];

        internal readonly List<GEffect> effects = new();
        internal readonly List<GGun> guns = new();
        internal readonly List<GWave> waves = new();
        string curTab = "effects";
        int curE, curG;

        GdHistory hist;

        Label statLabel;
        VisualElement listBox, detailBox, warnBox;
        readonly List<Button> subButtons = new();
        internal Action onWavesChanged;   // 웨이브 탭(다른 뷰)에 알림

        // ═════════ 데이터 ↔ root ═════════

        public override void OnDataLoaded()
        {
            effects.Clear();
            foreach (var e in win.root.effects ?? Array.Empty<GameDataImporter.EffectDto>())
                effects.Add(new GEffect
                {
                    id = e.id ?? "", displayName = e.displayName ?? "", description = e.description ?? "",
                    kind = string.IsNullOrEmpty(e.kind) ? "Damage" : e.kind,
                    duration = Mathf.Max(0, e.duration), stacking = string.IsNullOrEmpty(e.stacking) ? "Refresh" : e.stacking,
                    tickInterval = Mathf.Max(0, e.tickInterval),
                    knockbackMode = string.IsNullOrEmpty(e.knockbackMode) ? "Directional" : e.knockbackMode,
                    affects = (e.affects ?? Array.Empty<string>()).ToList(),
                    src = e,
                });
            guns.Clear();
            foreach (var g in win.root.guns ?? Array.Empty<GameDataImporter.GunDto>())
                guns.Add(new GGun
                {
                    id = g.id ?? "", displayName = g.displayName ?? "", description = g.description ?? "",
                    isAutomatic = g.isAutomatic, fireMode = string.IsNullOrEmpty(g.fireMode) ? "Projectile" : g.fireMode,
                    fireRate = g.fireRate > 0 ? g.fireRate : 0.2f, range = Mathf.Max(0, g.range),
                    pellets = g.pellets > 0 ? g.pellets : 1, magSize = g.magSize > 0 ? g.magSize : 30,
                    reloadTime = g.reloadTime > 0 ? g.reloadTime : 1.5f,
                    zoomMultiplier = g.zoomMultiplier > 0 ? g.zoomMultiplier : 1.3f,
                    ammoFilter = (g.ammoFilter ?? Array.Empty<string>()).ToList(),
                    ammo = g.ammoFilter is { Length: > 0 } af ? af[0] : "",   // 임포터 규약: 첫 항목이 기본
                    damageMultiplier = g.damageMultiplier >= 0 ? g.damageMultiplier : 1,
                    xRecoil = g.xRecoil >= 0 ? g.xRecoil : 3, yRecoil = g.yRecoil >= 0 ? g.yRecoil : 2,
                    zRecoil = g.zRecoil >= 0 ? g.zRecoil : 1,
                    visualKickbackZ = g.visualKickbackZ >= 0 ? g.visualKickbackZ : 1,
                    baseSpread = g.baseSpread >= 0 ? g.baseSpread : 0.5f, maxSpread = g.maxSpread >= 0 ? g.maxSpread : 5,
                    spreadIncreasePerShot = g.spreadIncreasePerShot >= 0 ? g.spreadIncreasePerShot : 1,
                    spreadRecoveryRate = g.spreadRecoveryRate >= 0 ? g.spreadRecoveryRate : 5,
                    src = g,
                });
            waves.Clear();
            foreach (var w in (win.root.waves ?? Array.Empty<GameDataImporter.WaveDto>()).OrderBy(w => w.day))
                waves.Add(new GWave
                {
                    id = w.id ?? "", displayName = w.displayName ?? "", description = w.description ?? "",
                    day = Mathf.Max(1, w.day), requiredCoreTier = Mathf.Max(0, w.requiredCoreTier),
                    baseAmount = w.baseAmount, maxAliveAmount = w.maxAliveAmount,
                    spawnInterval = w.spawnInterval, monsterMaxHp = w.monsterMaxHp,
                    src = w,
                });
            curE = 0; curG = 0;
            hist = new GdHistory(Snapshot, Restore, 60);
            hist.Reset();
        }

        public override void SyncToRoot()
        {
            if (win.root == null) return;
            win.root.effects = effects.Select(ExportEffect).ToArray();
            win.root.guns = guns.Select(ExportGun).ToArray();
            win.root.waves = waves.Select(ExportWave).ToArray();
        }

        // 원본 getEffects — kind 형태에 맞는 필드만 내보낸다 (dur 없으면 duration 생략 등)
        GameDataImporter.EffectDto ExportEffect(GEffect e)
        {
            var k = KindOf(e.kind);
            var o = e.src ?? new GameDataImporter.EffectDto();
            o.id = e.id; o.displayName = e.displayName; o.kind = e.kind;
            o.description = string.IsNullOrEmpty(e.description) ? null : e.description;
            o.duration = k.dur && e.duration > 0 ? e.duration : 0;
            o.stacking = k.dur ? e.stacking : null;
            o.tickInterval = k.tick && e.tickInterval > 0 ? e.tickInterval : 0;
            o.affects = k.affects && e.affects.Count > 0 ? e.affects.ToArray() : null;
            o.knockbackMode = k.knockback ? e.knockbackMode : null;
            return o;
        }

        GameDataImporter.GunDto ExportGun(GGun g)
        {
            var o = g.src ?? new GameDataImporter.GunDto();
            o.id = g.id; o.displayName = g.displayName; o.isAutomatic = g.isAutomatic; o.fireMode = g.fireMode;
            o.description = string.IsNullOrEmpty(g.description) ? null : g.description;
            o.fireRate = g.fireRate; o.range = g.range; o.reloadTime = g.reloadTime; o.zoomMultiplier = g.zoomMultiplier;
            o.magSize = g.magSize;
            o.pellets = g.pellets > 1 ? g.pellets : 0;   // 1 은 기본값 — 샷건만 8 (0 = 생략, 에셋 유지)
            // 임포터 규약: ammoFilter 첫 항목이 기본 탄종 — 기본을 앞으로 정렬해 내보낸다
            var filter = g.ammoFilter.Where(a => a != g.ammo).ToList();
            if (!string.IsNullOrEmpty(g.ammo)) filter.Insert(0, g.ammo);
            o.ammoFilter = filter.Count > 0 ? filter.ToArray() : null;
            o.damageMultiplier = g.damageMultiplier;
            o.xRecoil = g.xRecoil; o.yRecoil = g.yRecoil; o.zRecoil = g.zRecoil;
            o.visualKickbackZ = g.visualKickbackZ;
            o.baseSpread = g.baseSpread; o.maxSpread = g.maxSpread;
            o.spreadIncreasePerShot = g.spreadIncreasePerShot; o.spreadRecoveryRate = g.spreadRecoveryRate;
            return o;
        }

        GameDataImporter.WaveDto ExportWave(GWave w)
        {
            var o = w.src ?? new GameDataImporter.WaveDto();
            o.id = w.id; o.displayName = w.displayName;
            o.description = string.IsNullOrEmpty(w.description) ? null : w.description;
            o.day = w.day; o.requiredCoreTier = w.requiredCoreTier;
            o.baseAmount = w.baseAmount; o.maxAliveAmount = w.maxAliveAmount;
            o.spawnInterval = w.spawnInterval;
            o.monsterMaxHp = w.monsterMaxHp;   // 0 = 설정 폴백 (임포터가 무조건 덮으므로 항상 내보낸다)
            return o;
        }

        // ═════════ 히스토리 — 내보낸 형태를 스냅샷 (원본 EdHistory take/apply와 동일) ═════════

        string Snapshot() => JsonConvert.SerializeObject(new
        {
            effects = effects.Select(ExportEffect),
            guns = guns.Select(ExportGun),
            waves = waves.Select(ExportWave),
        }, GameDataEditorWindow.JsonSettings);

        void Restore(string snap)
        {
            var o = JsonConvert.DeserializeAnonymousType(snap, new
            {
                effects = Array.Empty<GameDataImporter.EffectDto>(),
                guns = Array.Empty<GameDataImporter.GunDto>(),
                waves = Array.Empty<GameDataImporter.WaveDto>(),
            });
            // 보던 탭과 선택은 유지한다 — 되돌리기가 화면을 옮기면 어디를 고쳤는지 놓친다
            var t = curTab; int e = curE, g = curG;
            win.root.effects = o.effects; win.root.guns = o.guns; win.root.waves = o.waves;
            var keep = hist; OnDataLoaded(); hist = keep;
            curTab = t;
            curE = Mathf.Clamp(e, 0, Mathf.Max(0, effects.Count - 1));
            curG = Mathf.Clamp(g, 0, Mathf.Max(0, guns.Count - 1));
            Render();
            onWavesChanged?.Invoke();
            win.MarkDirty();
        }

        public override void Undo() { hist?.Undo(); }
        public override void Redo() { hist?.Redo(); }
        internal void PushHist() { hist?.Push(); win.MarkDirty(); }

        // 웨이브 탭(다른 뷰)이 부른다 — 정렬 규칙(day 순)도 여기서 지킨다
        internal void SetWaves(List<GWave> list)
        {
            waves.Clear();
            waves.AddRange(list.OrderBy(w => w.day));
            PushHist();
        }

        // ═════════ pane (index.html pane-combat 대응) ═════════

        public override void Build(VisualElement host)
        {
            host.style.backgroundColor = GdEnum.Bg;

            var top = new VisualElement();
            top.AddToClassList("gd-topbar");
            host.Add(top);
            var title = new Label("전투 에디터");
            title.AddToClassList("gd-topbar-title");
            top.Add(title);
            var small = new Label("효과 · 화기");
            small.AddToClassList("gd-topbar-small");
            top.Add(small);
            var undoB = new Button(() => Undo()) { text = "↶", tooltip = "실행 취소 (Ctrl+Z)" };
            undoB.AddToClassList("gd-btn-mini");
            top.Add(undoB);
            var redoB = new Button(() => Redo()) { text = "↷", tooltip = "다시 실행 (Ctrl+Y)" };
            redoB.AddToClassList("gd-btn-mini");
            top.Add(redoB);
            top.Add(new VisualElement { style = { flexGrow = 1 } });
            statLabel = new Label();
            statLabel.AddToClassList("gd-stat");
            Mono(statLabel);
            top.Add(statLabel);

            var main = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            host.Add(main);

            // ── c-left ──
            var left = new ScrollView { style = { width = 300 } };
            left.AddToClassList("gd-leftcol");
            main.Add(left);

            var subRow = new VisualElement();
            subRow.AddToClassList("gd-subtabs");
            left.Add(subRow);
            subButtons.Clear();
            foreach (var (label, key) in new[] { ("효과", "effects"), ("화기", "guns") })
            {
                var b = new Button(() => { curTab = key; SyncSubButtons(); Render(); }) { text = label };
                b.AddToClassList("gd-subtab");
                subRow.Add(b);
                subButtons.Add(b);
            }
            SyncSubButtons();

            listBox = new VisualElement { style = { marginTop = 6, minHeight = 200 } };
            left.Add(listBox);

            var btnRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
            left.Add(btnRow);
            var addB = new Button(AddEntry) { text = "+ 추가" };
            addB.AddToClassList("gd-btn-mini");
            addB.AddToClassList("gd-btn-primary");
            btnRow.Add(addB);
            var delB = new Button(DelEntry) { text = "삭제" };
            delB.AddToClassList("gd-btn-mini");
            delB.AddToClassList("gd-btn-warn");
            btnRow.Add(delB);

            left.Add(DividerEl());
            warnBox = new VisualElement();
            left.Add(warnBox);
            left.Add(Hint(
                "효과 — 공격이 명중했을 때 일어나는 일의 단위. kind 가 동작을 정하고, " +
                "크기(value)는 이 효과를 쓰는 쪽(탄약·총)이 정한다.\n\n" +
                "화기 — 플레이어가 드는 총. 피해는 탄약이 갖고 총은 배율만 갖는다. " +
                "Fire Rate 는 연사가 아니라 발 간격(초)이라 작을수록 빠르다.\n\n" +
                "웨이브 — 일차별 밤 공세. Day 가 오는 시점, Core Tier 는 그 웨이브가 나오기 위한 코어 조건이다" +
                "(모자라면 이전 웨이브가 반복된다).\n\n" +
                "탄약·무기 아이템은 아이템 탭에서 만든다 — type 을 Ammo/Weapon 으로 두면 모듈 항목이 나타난다."));

            // ── c-right ──
            var right = new ScrollView { style = { flexGrow = 1, paddingLeft = 24, paddingRight = 24, paddingTop = 18,
                backgroundColor = GdEnum.Bg } };
            main.Add(right);
            right.Add(H3("속성"));
            detailBox = new VisualElement { style = { maxWidth = 520 } };
            right.Add(detailBox);

            Render();
        }

        void SyncSubButtons()
        {
            for (int i = 0; i < subButtons.Count; i++)
                subButtons[i].EnableInClassList("gd-subtab--on", (i == 0) == (curTab == "effects"));
        }

        void AddEntry()
        {
            if (curTab == "effects")
            {
                effects.Add(new GEffect { displayName = "새 효과", duration = 3, tickInterval = 0.5f });
                curE = effects.Count - 1;
            }
            else
            {
                guns.Add(new GGun { displayName = "새 화기", isAutomatic = true, range = 99 });
                curG = guns.Count - 1;
            }
            Render();
            PushHist();
        }

        void DelEntry()
        {
            if (curTab == "effects" && effects.Count > 0) { effects.RemoveAt(curE); curE = Mathf.Max(0, curE - 1); }
            else if (curTab == "guns" && guns.Count > 0) { guns.RemoveAt(curG); curG = Mathf.Max(0, curG - 1); }
            Render();
            PushHist();
        }

        // ═════════ 렌더 ═════════

        void Render()
        {
            if (listBox == null) return;
            RenderList();
            RenderDetail();
            RenderWarn();
            statLabel.text = $"효과 {effects.Count} · 화기 {guns.Count} · 웨이브 {waves.Count}";
            win.RefreshSharedStat();
        }

        void RenderList()
        {
            listBox.Clear();
            int count = curTab == "effects" ? effects.Count : guns.Count;
            if (count == 0)
            {
                listBox.Add(new Label("항목이 없습니다") { style = { color = GdEnum.Faint, fontSize = 11 } });
                return;
            }
            for (int i = 0; i < count; i++)
            {
                int idx = i;
                bool sel = i == (curTab == "effects" ? curE : curG);
                string nm = curTab == "effects" ? effects[i].displayName : guns[i].displayName;
                string kd = curTab == "effects" ? effects[i].kind : guns[i].fireMode;
                var row = new VisualElement();
                row.AddToClassList("gd-bitem");
                if (sel) row.AddToClassList("gd-bitem--sel");
                var nmL = new Label(string.IsNullOrEmpty(nm) ? "(이름 없음)" : nm) { pickingMode = PickingMode.Ignore };
                nmL.AddToClassList("gd-bitem-nm");
                row.Add(nmL);
                var kdL = new Label(kd) { pickingMode = PickingMode.Ignore };
                kdL.AddToClassList("gd-bitem-kd");
                Mono(kdL);
                row.Add(kdL);
                row.RegisterCallback<PointerDownEvent>(_ =>
                {
                    if (curTab == "effects") curE = idx; else curG = idx;
                    Render();
                });
                listBox.Add(row);
            }
        }

        // ── 공용 폼 조각 ──

        VisualElement IdField(string prefix, string cur, Action<string> set)
        {
            string bare = cur != null && cur.StartsWith(prefix) ? cur.Substring(prefix.Length) : cur ?? "";
            var row = new VisualElement();
            row.AddToClassList("gd-idrow");
            var pfx = new Label(prefix);
            pfx.AddToClassList("gd-idrow-pfx");
            Mono(pfx);
            row.Add(pfx);
            var f = Mono(new TextField { value = bare });
            f.RegisterValueChangedCallback(e =>
            {
                var clean = new string(e.newValue.Where(c => char.IsLetterOrDigit(c) || c == '_' || (c >= '가' && c <= '힣')).ToArray());
                set(string.IsNullOrEmpty(clean) ? "" : prefix + clean);
                RenderList();
                RenderWarn();
            });
            HookHist(f);
            row.Add(f);
            return Field2("Id", row);
        }

        void HookHist(VisualElement f)
        {
            // 입력이 확정될 때 한 번만 기록한다 — 타이핑마다 쌓으면 되돌리기가 쓸모없어진다
            f.RegisterCallback<FocusOutEvent>(_ => PushHist());
        }

        VisualElement NumGrid(string label, params VisualElement[] cells)
        {
            var box = new VisualElement();
            box.Add(GroupTitle(label));
            var grid = new VisualElement();
            grid.AddToClassList("gd-grid");
            foreach (var c in cells) { c.AddToClassList("gd-gcell"); grid.Add(c); }
            box.Add(grid);
            return box;
        }

        VisualElement TextRow(string label, string value, Action<string> set, bool multiline = false)
        {
            var f = new TextField { value = value ?? "", multiline = multiline };
            if (multiline)
            {
                f.AddToClassList("gd-multiline");
                f.verticalScrollerVisibility = ScrollerVisibility.Auto;
            }
            f.RegisterValueChangedCallback(e => set(e.newValue));
            HookHist(f);
            return Field2(label, f);
        }

        VisualElement Cell(string label, float val, Action<float> set, string tip = null, float widthPercent = 33f)
        {
            var c = MiniCell(label, val, v => { set(v); RenderWarn(); }, tip, widthPercent);
            HookHist(c);
            return c;
        }

        // ── 속성 폼 ──

        void RenderDetail()
        {
            detailBox.Clear();
            if (curTab == "effects") RenderEffectDetail();
            else RenderGunDetail();
        }

        void RenderEffectDetail()
        {
            var e = effects.ElementAtOrDefault(curE);
            if (e == null) return;
            var k = KindOf(e.kind);

            var kindChoices = EffectKinds.Select(x => $"{x.v} — {x.ko}").ToList();
            var kindD = new DropdownField(kindChoices, Mathf.Max(0, Array.FindIndex(EffectKinds, x => x.v == e.kind)));
            kindD.RegisterValueChangedCallback(ev =>
            {
                int i = kindChoices.IndexOf(ev.newValue);
                if (i >= 0) e.kind = EffectKinds[i].v;
                RenderDetail(); RenderList(); RenderWarn(); PushHist();
            });
            detailBox.Add(Field2("Kind", kindD));
            detailBox.Add(new Label(k.desc) { style = { color = GdEnum.Faint, fontSize = 11, marginBottom = 6, marginLeft = 118 } });

            detailBox.Add(IdField("Effect:", e.id, v => e.id = v));
            detailBox.Add(TextRow("Display Name", e.displayName, v => { e.displayName = v; RenderList(); }));
            detailBox.Add(TextRow("Description", e.description, v => e.description = v, multiline: true));

            if (k.dur)
            {
                var durF = new FloatField { value = e.duration };
                durF.RegisterValueChangedCallback(ev => { e.duration = Mathf.Max(0, ev.newValue); RenderWarn(); });
                HookHist(durF);
                detailBox.Add(Field2("Duration (초)", durF));
                var stackD = new DropdownField(Stacking.ToList(), Mathf.Max(0, Array.IndexOf(Stacking, e.stacking)));
                stackD.RegisterValueChangedCallback(ev => { e.stacking = ev.newValue; PushHist(); });
                detailBox.Add(Field2("Stacking", stackD));
            }
            if (k.tick)
            {
                var tickF = new FloatField { value = e.tickInterval };
                tickF.RegisterValueChangedCallback(ev => { e.tickInterval = Mathf.Max(0, ev.newValue); RenderWarn(); });
                HookHist(tickF);
                detailBox.Add(Field2("Tick (초)", tickF));
            }

            if (k.knockback)
            {
                int cur = Mathf.Max(0, Array.IndexOf(KnockbackModes, e.knockbackMode));
                var modeD = new DropdownField(KnockbackModes.ToList(), cur);
                var modeNote = new Label(KnockbackModeDesc[cur])
                    { style = { color = GdEnum.Faint, fontSize = 11, marginBottom = 6, marginLeft = 118 } };
                modeD.RegisterValueChangedCallback(ev =>
                {
                    int i = Array.IndexOf(KnockbackModes, ev.newValue);
                    if (i < 0) return;
                    e.knockbackMode = KnockbackModes[i];
                    modeNote.text = KnockbackModeDesc[i];
                    PushHist();
                });
                detailBox.Add(Field2("방향 기준", modeD));
                detailBox.Add(modeNote);
            }

            if (k.affects)
            {
                detailBox.Add(GroupTitle("Affects · 증폭할 효과"));
                var list = new ScrollView();
                list.AddToClassList("gd-ammolist");
                detailBox.Add(list);
                var others = effects.Where(x => x.id != e.id).ToList();
                if (others.Count == 0)
                    list.Add(new Label("다른 효과가 없습니다") { style = { color = GdEnum.Faint, fontSize = 11 } });
                foreach (var x in others)
                {
                    bool on = e.affects.Contains(x.id);
                    var row = new VisualElement();
                    row.AddToClassList("gd-ammorow");
                    if (on) row.AddToClassList("gd-ammorow--on");
                    var tog = new Toggle { value = on };
                    tog.RegisterValueChangedCallback(ev =>
                    {
                        if (ev.newValue) { if (!e.affects.Contains(x.id)) e.affects.Add(x.id); }
                        else e.affects.Remove(x.id);
                        RenderDetail(); RenderWarn(); PushHist();
                    });
                    row.Add(tog);
                    var bar = new VisualElement { style = { backgroundColor = x.kind == "Damage" ? GdEnum.Warn : GdEnum.Accent } };
                    bar.AddToClassList("gd-ammorow-bar");
                    row.Add(bar);
                    var nmL = new Label(x.id);
                    nmL.AddToClassList("gd-ammorow-nm");
                    Mono(nmL);
                    row.Add(nmL);
                    var tyL = new Label(x.kind);
                    tyL.AddToClassList("gd-ammorow-ty");
                    row.Add(tyL);
                    list.Add(row);
                }
            }
        }

        void RenderGunDetail()
        {
            var g = guns.ElementAtOrDefault(curG);
            if (g == null) return;

            detailBox.Add(IdField("Gun:", g.id, v => g.id = v));
            detailBox.Add(TextRow("Display Name", g.displayName, v => { g.displayName = v; RenderList(); }));
            detailBox.Add(TextRow("Description", g.description, v => g.description = v, multiline: true));

            var modeD = new DropdownField(FireModes.ToList(), Mathf.Max(0, Array.IndexOf(FireModes, g.fireMode)));
            modeD.RegisterValueChangedCallback(ev => { g.fireMode = ev.newValue; RenderList(); RenderWarn(); PushHist(); });
            detailBox.Add(Field2("Fire Mode", modeD));
            var autoF = new Toggle { value = g.isAutomatic };
            autoF.RegisterValueChangedCallback(ev => { g.isAutomatic = ev.newValue; PushHist(); });
            detailBox.Add(Field2("Automatic", autoF));
            var dmgF = new FloatField { value = g.damageMultiplier };
            dmgF.RegisterValueChangedCallback(ev => { g.damageMultiplier = Mathf.Max(0, ev.newValue); RefreshDps(); RenderWarn(); });
            HookHist(dmgF);
            detailBox.Add(Field2("Damage ×", dmgF, "탄약의 피해형 항목에 곱해진다"));

            BuildAmmoFilter(g);

            detailBox.Add(NumGrid("사격",
                Cell("Fire Rate", g.fireRate, v => { g.fireRate = v; RefreshDps(); }, "발 간격(초). 작을수록 빠르다"),
                Cell("Mag Size", g.magSize, v => { g.magSize = Mathf.Max(0, Mathf.RoundToInt(v)); RefreshDps(); }),
                Cell("Reload", g.reloadTime, v => g.reloadTime = v),
                Cell("Range", g.range, v => g.range = v),
                Cell("Pellets", g.pellets, v => { g.pellets = Mathf.Max(1, Mathf.RoundToInt(v)); RefreshDps(); }, "방아쇠당 발사 수. 샷건 8, 나머지 1"),
                Cell("Zoom 배율", g.zoomMultiplier, v => g.zoomMultiplier = Mathf.Max(1, v),
                    "조준 확대 배율 — FOV 절대값이 아니라 기본 화각 대비 배율")));
            detailBox.Add(NumGrid("반동",
                Cell("X", g.xRecoil, v => g.xRecoil = v),
                Cell("Y", g.yRecoil, v => g.yRecoil = v),
                Cell("Z", g.zRecoil, v => g.zRecoil = v),
                Cell("Kickback Z", g.visualKickbackZ, v => g.visualKickbackZ = v)));
            detailBox.Add(NumGrid("탄퍼짐",
                Cell("Base", g.baseSpread, v => g.baseSpread = v),
                Cell("Max", g.maxSpread, v => g.maxSpread = v),
                Cell("+/발", g.spreadIncreasePerShot, v => g.spreadIncreasePerShot = v),
                Cell("회복", g.spreadRecoveryRate, v => g.spreadRecoveryRate = v)));

            dpsBox = new VisualElement { style = { marginTop = 8 } };
            detailBox.Add(dpsBox);
            RefreshDps();
        }

        VisualElement dpsBox;

        IEnumerable<GameDataImporter.ItemDto> Items() => win.root.items ?? Array.Empty<GameDataImporter.ItemDto>();

        // 장전 가능한 탄종 — 게임에서 V 로 돌려 쓴다. 기본 탄약은 항상 포함된다 (gunAmmoFilter)
        void BuildAmmoFilter(GGun g)
        {
            detailBox.Add(GroupTitle("탄종"));
            detailBox.Add(new Label("장전해 돌려 쓸 수 있는 탄약 (게임에서 V 로 전환)")
            { style = { color = GdEnum.Faint, fontSize = 11, marginBottom = 4 } });
            var holder = new ScrollView();
            holder.AddToClassList("gd-ammolist");
            detailBox.Add(holder);
            void Rebuild()
            {
                holder.Clear();
                var ammoList = Items().Where(i => i.type == "Ammo").ToList();
                if (ammoList.Count == 0)
                { holder.Add(new Label("Ammo 타입 아이템이 없습니다") { style = { color = GdEnum.Faint, fontSize = 11 } }); return; }
                foreach (var item in ammoList)
                {
                    string id = item.id;
                    bool on = g.ammoFilter.Contains(id);
                    bool isDefault = id == g.ammo;
                    var row = new VisualElement();
                    row.AddToClassList("gd-ammorow");
                    if (on) row.AddToClassList("gd-ammorow--on");
                    var tog = new Toggle { value = on };
                    tog.RegisterValueChangedCallback(ev =>
                    {
                        if (ev.newValue)
                        {
                            if (!g.ammoFilter.Contains(id)) g.ammoFilter.Add(id);
                            if (string.IsNullOrEmpty(g.ammo)) g.ammo = id;   // 첫 탄종이 기본이 된다
                        }
                        else
                        {
                            g.ammoFilter.Remove(id);
                            if (g.ammo == id) g.ammo = g.ammoFilter.FirstOrDefault() ?? "";   // 기본을 빼면 남은 것 중 하나가 기본
                        }
                        Rebuild(); RefreshDps(); RenderWarn(); PushHist();
                    });
                    row.Add(tog);
                    var bar = new VisualElement { style = { backgroundColor =
                        isDefault ? GdEnum.Accent : on ? GdEnum.Border : GdEnum.FromHex("#1A2740") } };
                    bar.AddToClassList("gd-ammorow-bar");
                    row.Add(bar);
                    var nmL = new Label(id);
                    nmL.AddToClassList("gd-ammorow-nm");
                    Mono(nmL);
                    row.Add(nmL);
                    if (on && isDefault)
                    {
                        var tyL = new Label("기본") { style = { color = GdEnum.Accent, fontSize = 10 } };
                        row.Add(tyL);
                    }
                    else if (on)
                    {
                        var setBtn = new Label("기본으로") { tooltip = "기본 탄약으로 지정" };
                        setBtn.AddToClassList("gd-setdef");
                        setBtn.RegisterCallback<PointerDownEvent>(ev =>
                        {
                            g.ammo = id;
                            Rebuild(); RefreshDps(); RenderWarn(); PushHist();
                            ev.StopPropagation();
                        });
                        row.Add(setBtn);
                    }
                    holder.Add(row);
                }
            }
            Rebuild();
        }

        // 총의 실제 DPS — 탄약의 피해 합 × 배율 ÷ 발 간격 (dpsBox)
        void RefreshDps()
        {
            if (dpsBox == null) return;
            dpsBox.Clear();
            var g = guns.ElementAtOrDefault(curG);
            if (g == null) return;
            var ammo = Items().FirstOrDefault(i => i.id == g.ammo);
            var box = new VisualElement();
            box.AddToClassList("gd-dpsbox");
            if (ammo == null)
            {
                box.AddToClassList("gd-dpsbox--off");
                box.Add(new Label("탄약을 지정하면 DPS 가 계산됩니다"));
                dpsBox.Add(box);
                return;
            }
            float dmg = (ammo.attackEffects ?? Array.Empty<GameDataImporter.EffectEntryDto>())
                .Where(x => effects.Any(ef => ef.id == x.effect && ef.kind == "Damage")).Sum(x => x.value);
            if (dmg <= 0)
            {
                box.AddToClassList("gd-dpsbox--off");
                box.Add(new Label($"{g.ammo} 에 피해 효과가 없습니다"));
                dpsBox.Add(box);
                return;
            }
            int pellets = Mathf.Max(1, g.pellets);
            float perShot = dmg * g.damageMultiplier * pellets;
            float dps = g.fireRate > 0 ? perShot / g.fireRate : 0;
            int trigger = pellets > 0 ? g.magSize / pellets : 0;
            // .dpsbox b — 값만 mono 14px 본문색 (이름 12px muted 는 USS)
            void Stat(string name, string val)
            {
                var r = new VisualElement { style = { flexDirection = FlexDirection.Row,
                    alignItems = Align.Center, marginRight = 16 } };
                r.Add(new Label(name));
                var v = Mono(new Label(val));
                v.style.color = GdEnum.Text;
                v.style.fontSize = 14;
                v.style.marginLeft = 4;
                r.Add(v);
                box.Add(r);
            }
            Stat("1발 피해", $"{perShot:0.#}" + (pellets > 1 ? $" ({pellets}발)" : ""));
            Stat("초당 피해", $"{dps:0.#}");
            Stat("탄창 총합", $"{perShot * trigger:0.#}");
            dpsBox.Add(box);
        }

        // ═════════ 검증 (validate) ═════════

        List<string> Validate()
        {
            var outp = new List<string>();
            void Identity(string id, string display, IEnumerable<string> allIds)
            {
                if (string.IsNullOrEmpty(id)) outp.Add("id 가 비어 있습니다 — 임포트의 기본 키입니다");
                else if (allIds.Count(x => x == id) > 1) outp.Add($"id 중복 — {id}");
                if (string.IsNullOrEmpty((display ?? "").Trim())) outp.Add("displayName 이 비어 있습니다 — 임포터가 거부합니다");
            }

            if (curTab == "effects")
            {
                var x = effects.ElementAtOrDefault(curE);
                if (x == null) return outp;
                Identity(x.id, x.displayName, effects.Select(e => e.id));
                var k = KindOf(x.kind);
                if (k.dur && !(x.duration > 0)) outp.Add("지속 효과인데 duration 이 0 입니다 — 즉시 사라집니다");
                if (k.tick && !(x.tickInterval > 0)) outp.Add("DamageOverTime 인데 tickInterval 이 0 입니다");
                if (k.affects && x.affects.Count == 0) outp.Add("AttackModifier 인데 증폭할 효과가 없습니다");
                foreach (var id in x.affects)
                    if (!effects.Any(e => e.id == id)) outp.Add($"Affects — \"{id}\" 를 찾을 수 없습니다");
                bool used = Items().Any(i => (i.attackEffects ?? Array.Empty<GameDataImporter.EffectEntryDto>())
                    .Any(e => e.effect == x.id));
                if (!used && !string.IsNullOrEmpty(x.id)) outp.Add("어떤 탄약도 이 효과를 쓰지 않습니다");
            }
            else
            {
                var x = guns.ElementAtOrDefault(curG);
                if (x == null) return outp;
                Identity(x.id, x.displayName, guns.Select(gg => gg.id));
                if (!(x.fireRate > 0)) outp.Add("Fire Rate 는 0보다 커야 합니다 (발 간격 초)");
                if (string.IsNullOrEmpty(x.ammo)) outp.Add("탄약이 지정되지 않았습니다 — 사격해도 아무 일도 일어나지 않습니다");
                else
                {
                    var a = Items().FirstOrDefault(i => i.id == x.ammo);
                    if (a == null) outp.Add($"탄약 \"{x.ammo}\" 를 아이템 목록에서 찾을 수 없습니다");
                    else if (a.type != "Ammo") outp.Add($"\"{x.ammo}\" 는 Ammo 타입이 아닙니다");
                    else if ((a.attackEffects?.Length ?? 0) == 0) outp.Add($"\"{x.ammo}\" 에 명중 효과가 없습니다");
                    else if (x.fireMode == "Projectile" && !(a.speed > 0)) outp.Add($"Projectile 인데 탄약 \"{x.ammo}\" 의 speed 가 0 입니다");
                }
                if (x.pellets < 1) outp.Add("Pellets 는 1 이상이어야 합니다");
                if (!string.IsNullOrEmpty(x.ammo) && !x.ammoFilter.Contains(x.ammo))
                    outp.Add("기본 탄약이 탄종 목록에 없습니다 — 장전할 수 없습니다");
                foreach (var id in x.ammoFilter)
                {
                    var a = Items().FirstOrDefault(i => i.id == id);
                    if (Items().Any() && a == null) outp.Add($"탄종 — \"{id}\" 를 찾을 수 없습니다");
                    else if (a != null && a.type != "Ammo") outp.Add($"탄종 — \"{id}\" 는 Ammo 타입이 아닙니다");
                }
                if (!(x.magSize > 0)) outp.Add("Mag Size 는 1 이상이어야 합니다");
                if (x.maxSpread < x.baseSpread) outp.Add("Max Spread 가 Base Spread 보다 작습니다");
                if (!(x.damageMultiplier > 0)) outp.Add("Damage × 가 0 입니다 — 피해를 주지 않습니다");
            }
            return outp;
        }

        void RenderWarn()
        {
            if (warnBox == null) return;
            warnBox.Clear();
            var w = Validate();
            if (w.Count == 0)
                warnBox.Add(OkMsg("✓ 검증 통과"));
            else
            {
                warnBox.Add(H3("검증"));
                foreach (var m in w)
                    warnBox.Add(WarnItem(m));
            }
        }
    }
}
#endif
