#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// ═══════════════════════════════════════════════════════════
//  건물 탭 — BuildingDataSO · 3D 포트 편집 (Web/js/building-editor.js 대응)
//
//  three.js 뷰는 PreviewRenderUtility 로 옮겼다 — 격자·풋프린트·포트 흐름·
//  모델 미리보기, 드래그 회전·휠 줌·우클릭 팬·격자 클릭 포트 추가까지 동일.
//  모델 지정은 웹의 "파일 선택" 대신 유니티 ObjectField 다. JSON 에는 에셋 guid 와
//  파일 이름을 함께 남긴다 — guid 가 진실이고, 이름은 diff 에서 읽히는 표시이자
//  guid 가 죽었을 때의 폴백이다. 이름만 저장하던 시절에는 같은 이름의 모델이 둘 있으면
//  어느 쪽이 걸릴지 정해지지 않았다.
//
//  원본과 다른 점: 웹판 내보내기는 툴이 모르는 필드(fireMode·defaultAmmo·
//  muzzleHeight 등)를 떨궜다. 여기서는 항목마다 원본 DTO를 들고 있다가
//  편집 필드만 덮어써서 그 손실을 막는다. 언두(원본에는 없음)도 붙였다.
// ═══════════════════════════════════════════════════════════

class GPort { public int x, y; public string dir = "East"; public bool isInput; }
class GCost { public string item = ""; public int amount = 1; }

class GTier
{
    public string name = "", description = "";
    public List<GCost> requirements = new();
    public List<string> unlocks = new();
    public int maxHpBonus;
    public bool isFinal;
    [JsonIgnore] public GameDataImporter.TierDto src;
}

class GBuilding
{
    public string id = "";
    public bool idLocked;
    public string kind = "Miner", displayName = "새 건물", description = "", category = "Production";
    public string model = "", modelCurveL = "", modelCurveR = "";
    // guid 가 정본 — 이름은 표시·폴백용 (임포터의 ModelRef 규약과 같다)
    public string modelGuid = "", modelCurveLGuid = "", modelCurveRGuid = "";
    public int sizeX = 1, sizeY = 1;
    public List<GPort> ports = new();
    public List<GCost> buildCost = new();
    public int inputSlots = 1, outputSlots = 1, bufferStackCap, requiredCoreTier, maxHp = 200;
    public bool hideFromBuildMenu;
    public bool isDemolishable = true, isAttackable;   // 공격 가능은 기본 꺼짐 — 둥지·지형물만 켠다
    public float speedMultiplier = 1, speedTilesPerSec = 2;
    public List<string> availableRecipes = new();
    public float damageMultiplier = 1, range = 8, fireRate = 1;
    public List<string> ammoFilter = new();
    public List<GTier> tiers = new();
    public float droneRange = 40, carryCapacity = 20, travelSpeed = 8;
    [JsonIgnore] public GameDataImporter.BuildingDto src;
}

class GdBuildingTab : GdTab
{
    public override string Title => "건물";
    public GdBuildingTab(GameDataEditorWindow win) : base(win) { }

    // ── enums.js KINDS / CATEGORIES / DIRS / DVEC / TIER_MARK ──
    class KindInfo
    {
        public readonly string v, ko, cat;
        public readonly string[] extra;
        public KindInfo(string v, string ko, string cat, params string[] extra)
        { this.v = v; this.ko = ko; this.cat = cat; this.extra = extra; }
    }
    static readonly KindInfo[] Kinds =
    {
        new("Miner",     "채굴기",       "Production", "speedMultiplier"),
        new("Assembler", "조립기",       "Production", "availableRecipes"),
        new("Belt",      "벨트",         "Logistics",  "speedTilesPerSec", "curveLPrefab", "curveRPrefab"),
        new("Splitter",  "분배기",       "Logistics"),
        new("Merger",    "합류기",       "Logistics"),
        new("Storage",   "보관소",       "Storage"),
        new("DronePort", "드론 스테이션", "Logistics",  "droneRange", "carryCapacity", "travelSpeed"),
        new("Core",      "코어",         "Production", "tiers"),
        new("Tower",     "방어 타워",    "Defense",    "damageMultiplier", "range", "fireRate", "ammoFilter"),
        new("Nest",      "몬스터 둥지",   "Defense"),
        new("Tree",      "나무",         "Production"),
    };
    static readonly (string v, string ko)[] Categories =
    { ("Production", "생산"), ("Logistics", "물류"), ("Storage", "저장"), ("Defense", "방어") };
    static readonly string[] Dirs = { "North", "East", "South", "West" };
    static readonly Dictionary<string, Vector2Int> DVec = new()
    {
        ["North"] = new(0, 1), ["East"] = new(1, 0), ["South"] = new(0, -1), ["West"] = new(-1, 0),
    };
    static readonly string[] TierMark = { "⓪", "①", "②", "③", "④", "⑤" };
    static KindInfo KindOf(string v) => Kinds.FirstOrDefault(k => k.v == v) ?? Kinds[0];

    static readonly Color ColIn = GdEnum.FromHex("#4FD8E0");
    static readonly Color ColOut = GdEnum.FromHex("#FF9E4A");

    // ── 데이터 ──
    internal readonly List<GBuilding> buildings = new();
    int curIdx;
    int selPort = -1;
    GBuilding Cur => buildings.ElementAtOrDefault(curIdx);

    GdHistory hist;
    void PushHist() { hist?.Push(); win.MarkDirty(); }

    static string Slug(string s) =>
        new(( s ?? "").Where(c => char.IsLetterOrDigit(c) || c == '_' || (c >= '가' && c <= '힣')).ToArray());
    const string IdPrefix = "Building:";
    static string IdSuffix(GBuilding b) => (b.id ?? "").StartsWith(IdPrefix) ? b.id.Substring(IdPrefix.Length) : b.id ?? "";
    static string Bid(GBuilding b)
    {
        var suf = Slug(IdSuffix(b));
        if (string.IsNullOrEmpty(suf)) suf = Slug(b.displayName);
        return IdPrefix + (string.IsNullOrEmpty(suf) ? "Unnamed" : suf);
    }

    // 회전 미리보기 — Dir.RotateCellCW 와 동일: (x,y) → (y, w-1-x)

    // ── 아이템·레시피 목록 (다른 탭이 정본 — root 에서 읽는다) ──
    (string id, string line, string type)[] KnownItems() =>
        (win.root?.items ?? Array.Empty<GameDataImporter.ItemDto>())
        .Select(i => (i.id ?? "", string.IsNullOrEmpty(i.line) ? "None" : i.line, i.type ?? "")).ToArray();
    (string id, int inputs, int tier)[] KnownRecipes() =>
        (win.root?.recipes ?? Array.Empty<GameDataImporter.RecipeDto>())
        .Select(r => (r.id ?? "", r.inputs?.Length ?? 0, r.tier)).ToArray();

    // ═════════ 데이터 ↔ root ═════════

    // DronePort 필드는 임포터 DTO에 없다 — unknownJson(확장 데이터)으로 왕복한다
    static float ExtraF(GameDataImporter.JsonDtoBase o, string key, float def)
    {
        if (o?.unknownJson != null && o.unknownJson.TryGetValue(key, out var v) && v != null)
            try { return Convert.ToSingle(v); } catch { }
        return def;
    }
    static void SetExtra(GameDataImporter.JsonDtoBase o, string key, float val) =>
        (o.unknownJson ??= new Dictionary<string, object>())[key] =
            val == Mathf.Round(val) ? (long)val : (object)val;   // 정수는 정수로 — 60이 60.0이 되는 diff 노이즈 방지

    public override void OnDataLoaded()
    {
        LoadFrom(win.root?.buildings ?? Array.Empty<GameDataImporter.BuildingDto>());
        curIdx = 0; selPort = -1;
        hist = new GdHistory(Snapshot, Restore, 60);
        hist.Reset();
    }

    void LoadFrom(GameDataImporter.BuildingDto[] dtos)
    {
        buildings.Clear();
        foreach (var o in dtos)
        {
            var k = KindOf(string.IsNullOrEmpty(o.kind) ? "Miner" : o.kind);
            buildings.Add(new GBuilding
            {
                id = o.id ?? "", idLocked = !string.IsNullOrEmpty(o.id),
                kind = k.v,
                displayName = string.IsNullOrEmpty(o.displayName) ? (o.id ?? "").Replace(IdPrefix, "") : o.displayName,
                description = o.description ?? "",
                category = string.IsNullOrEmpty(o.category) ? k.cat : o.category,
                model = o.model ?? "",
                modelCurveL = o.modelCurveL ?? "", modelCurveR = o.modelCurveR ?? "",
                modelGuid = o.modelGuid ?? "",
                modelCurveLGuid = o.modelCurveLGuid ?? "", modelCurveRGuid = o.modelCurveRGuid ?? "",
                sizeX = Mathf.Max(1, o.size?.x ?? 1), sizeY = Mathf.Max(1, o.size?.y ?? 1),
                ports = (o.ports ?? Array.Empty<GameDataImporter.PortDto>())
                    .Select(p => new GPort { x = p.x, y = p.y,
                        dir = string.IsNullOrEmpty(p.dir) ? "East" : p.dir, isInput = p.isInput }).ToList(),
                buildCost = (o.buildCost ?? Array.Empty<GameDataImporter.SlotDto>())
                    .Select(c => new GCost { item = c.item ?? "", amount = Mathf.Max(1, c.amount) }).ToList(),
                inputSlots = o.inputSlots, outputSlots = o.outputSlots, bufferStackCap = o.bufferStackCap,
                maxHp = o.maxHp, requiredCoreTier = o.requiredCoreTier, hideFromBuildMenu = o.hideFromBuildMenu,
                isDemolishable = o.isDemolishable, isAttackable = o.isAttackable,
                speedMultiplier = o.speedMultiplier > 0 ? o.speedMultiplier : 1,
                speedTilesPerSec = o.speedTilesPerSec > 0 ? o.speedTilesPerSec : 2,
                availableRecipes = (o.availableRecipes ?? Array.Empty<string>()).ToList(),
                damageMultiplier = o.damageMultiplier >= 0 ? o.damageMultiplier : 1,
                range = o.range >= 0 ? o.range : 8,
                fireRate = o.fireRate >= 0 ? o.fireRate : 1,
                ammoFilter = (o.ammoFilter ?? Array.Empty<string>()).ToList(),
                droneRange = ExtraF(o, "droneRange", 40),
                carryCapacity = ExtraF(o, "carryCapacity", 20),
                travelSpeed = ExtraF(o, "travelSpeed", 8),
                tiers = (o.tiers ?? Array.Empty<GameDataImporter.TierDto>()).Select(t => new GTier
                {
                    name = t.name ?? "", description = t.description ?? "",
                    requirements = (t.requirements ?? Array.Empty<GameDataImporter.SlotDto>())
                        .Select(r => new GCost { item = r.item ?? "", amount = Mathf.Max(1, r.amount) }).ToList(),
                    unlocks = (t.unlocks ?? Array.Empty<string>()).ToList(),
                    maxHpBonus = t.maxHpBonus, isFinal = t.isFinal, src = t,
                }).ToList(),
                src = o,
            });
        }
    }

    public override void SyncToRoot()
    {
        if (win.root == null || hist == null) return;
        win.root.buildings = buildings.Select(Export).ToArray();
    }

    GameDataImporter.BuildingDto Export(GBuilding b)
    {
        var o = b.src ?? (b.src = new GameDataImporter.BuildingDto());
        o.id = Bid(b); o.kind = b.kind; o.displayName = b.displayName; o.description = b.description ?? "";
        o.category = b.category; o.model = b.model ?? "";
        o.modelGuid = b.modelGuid ?? "";
        o.size ??= new GameDataImporter.Vec2Dto();
        o.size.x = b.sizeX; o.size.y = b.sizeY;
        o.ports = b.ports.Select(p => new GameDataImporter.PortDto
        { x = p.x, y = p.y, dir = p.dir, isInput = p.isInput }).ToArray();
        o.buildCost = b.buildCost.Select(c => new GameDataImporter.SlotDto
        { item = c.item, amount = c.amount }).ToArray();
        o.inputSlots = b.inputSlots; o.outputSlots = b.outputSlots; o.bufferStackCap = b.bufferStackCap;
        o.maxHp = b.maxHp; o.requiredCoreTier = b.requiredCoreTier; o.hideFromBuildMenu = b.hideFromBuildMenu;
        o.isDemolishable = b.isDemolishable; o.isAttackable = b.isAttackable;

        // 종류별 전용 필드 — 원본 exportJson 과 같은 규칙. 다른 kind 의 잔존값은
        // 임포터가 무시하므로 src 에 남아 있어도 해가 없다.
        if (b.kind == "Miner") o.speedMultiplier = b.speedMultiplier;
        if (b.kind == "Belt")
        {
            o.speedTilesPerSec = b.speedTilesPerSec;
            // 이름·guid 는 한 쌍으로 움직인다 — 한쪽만 남으면 폴백이 엉뚱한 것을 집는다
            if (!string.IsNullOrEmpty(b.modelCurveL) || !string.IsNullOrEmpty(b.modelCurveLGuid))
            { o.modelCurveL = b.modelCurveL; o.modelCurveLGuid = b.modelCurveLGuid; }
            if (!string.IsNullOrEmpty(b.modelCurveR) || !string.IsNullOrEmpty(b.modelCurveRGuid))
            { o.modelCurveR = b.modelCurveR; o.modelCurveRGuid = b.modelCurveRGuid; }
        }
        if (b.kind == "Assembler") o.availableRecipes = b.availableRecipes.ToArray();
        if (b.kind == "DronePort")
        {
            SetExtra(o, "droneRange", b.droneRange);
            SetExtra(o, "carryCapacity", b.carryCapacity);
            SetExtra(o, "travelSpeed", b.travelSpeed);
        }
        if (b.kind == "Core")
            o.tiers = b.tiers.Select(t =>
            {
                var td = t.src ?? (t.src = new GameDataImporter.TierDto());
                td.name = t.name ?? ""; td.description = t.description ?? "";
                td.requirements = t.requirements.Select(r => new GameDataImporter.SlotDto
                { item = r.item, amount = r.amount }).ToArray();
                td.unlocks = t.unlocks.ToArray(); td.maxHpBonus = t.maxHpBonus; td.isFinal = t.isFinal;
                return td;
            }).ToArray();
        if (b.kind == "Tower")
        {
            o.damageMultiplier = b.damageMultiplier; o.range = b.range; o.fireRate = b.fireRate;
            o.ammoFilter = b.ammoFilter.ToArray();
        }
        return o;
    }

    // ═════════ 히스토리 — 내보낸 형태의 스냅샷 (unknownJson 까지 왕복) ═════════

    class Snap { public GameDataImporter.BuildingDto[] list; public int cur; }

    string Snapshot() => JsonConvert.SerializeObject(
        new Snap { list = buildings.Select(Export).ToArray(), cur = curIdx },
        GameDataEditorWindow.JsonSettings);

    void Restore(string json)
    {
        var s = JsonConvert.DeserializeObject<Snap>(json);
        LoadFrom(s.list ?? Array.Empty<GameDataImporter.BuildingDto>());
        curIdx = Mathf.Clamp(s.cur, 0, Mathf.Max(0, buildings.Count - 1));
        selPort = -1;
        win.MarkDirty();
        if (listBox != null) RenderAll();
    }

    public override void Undo() { hist?.Undo(); }
    public override void Redo() { hist?.Redo(); }

    // ═════════ UI ═════════

    Label statLabel;
    VisualElement listBox, warnBox, propsBox, portsBox;
    IMGUIContainer viewGui;
    Label hintOverlay;
    UnityEditor.UIElements.ObjectField topModelField;

    public override void Build(VisualElement host)
    {
        host.style.backgroundColor = GdEnum.Bg;

        // ── b-top ──
        var top = new VisualElement();
        top.AddToClassList("gd-topbar");
        host.Add(top);
        var title = new Label("Building 에디터");
        title.AddToClassList("gd-topbar-title");
        top.Add(title);
        var small = new Label("BuildingDataSO · 포트 3D 편집");
        small.AddToClassList("gd-topbar-small");
        top.Add(small);

        // 웹판 "3D 모델 불러오기" — 유니티에서는 에셋 픽커. JSON 에는 파일 이름만 남는다.
        topModelField = new UnityEditor.UIElements.ObjectField
        { objectType = typeof(GameObject), allowSceneObjects = false,
          tooltip = "본체 모델 — JSON 에는 guid 와 파일 이름이 함께 저장된다 (guid 가 진실)",
          style = { width = 230, marginLeft = 8 } };
        topModelField.RegisterValueChangedCallback(e =>
        {
            var b = Cur; if (b == null) return;
            (b.model, b.modelGuid) = ModelRefOf(e.newValue);
            PushHist();
            RenderProps(); Refresh3D();
        });
        top.Add(topModelField);
        var clearB = new Button(() =>
        {
            var b = Cur; if (b == null || (string.IsNullOrEmpty(b.model) && string.IsNullOrEmpty(b.modelGuid))) return;
            b.model = ""; b.modelGuid = "";
            PushHist();
            RenderProps(); Refresh3D();
        }) { text = "모델 제거" };
        clearB.AddToClassList("gd-btn-mini");
        top.Add(clearB);

        top.Add(new VisualElement { style = { flexGrow = 1 } });
        statLabel = new Label { style = { fontSize = 12, color = GdEnum.Faint } };   // #b-stat
        top.Add(statLabel);

        // ── b-main ──
        var main = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
        host.Add(main);

        // b-left — 264px · panel2 · border-right
        var left = new ScrollView { style = { width = 264, flexShrink = 0, backgroundColor = GdEnum.Panel2,
            borderRightWidth = 1, borderRightColor = GdEnum.Line } };
        left.contentContainer.style.paddingLeft = 14;
        left.contentContainer.style.paddingRight = 14;
        left.contentContainer.style.paddingTop = 14;
        left.contentContainer.style.paddingBottom = 14;
        main.Add(left);

        left.Add(H3("건물"));
        listBox = new VisualElement { style = { marginBottom = 14 } };
        left.Add(listBox);
        var btnRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        left.Add(btnRow);
        var addB = new Button(() =>
        {
            buildings.Add(new GBuilding());
            curIdx = buildings.Count - 1; selPort = -1; ResetCamera();
            PushHist();
            RenderAll();
        }) { text = "+ 새 건물" };
        addB.AddToClassList("gd-btn-mini");
        addB.AddToClassList("gd-btn-primary");
        btnRow.Add(addB);
        var delB = new Button(() =>
        {
            if (buildings.Count == 0) return;
            buildings.RemoveAt(curIdx);
            if (buildings.Count == 0) buildings.Add(new GBuilding());
            curIdx = Mathf.Max(0, curIdx - 1); selPort = -1;
            PushHist();
            RenderAll();
        }) { text = "삭제" };
        delB.AddToClassList("gd-btn-mini");
        delB.AddToClassList("gd-btn-warn");
        btnRow.Add(delB);

        left.Add(DividerEl());
        warnBox = new VisualElement();
        left.Add(warnBox);
        left.Add(Hint(
            "포트 표시 — 건물 면에 붙어 바깥으로 번지는 반투명 흐름. 밝은 띠가 포트의 실제 위치이고, " +
            "시안 = 입력 · 귤색 = 출력이다.\n\n" +
            "포트 규칙 — LocalOffset은 풋프린트 안의 셀, Direction은 아이템이 흐르는 방향. " +
            "포트가 향한 쪽 이웃 칸이 건물 자신이면 안쪽을 향한 것이라 연결되지 않는다(검증에서 잡힘).\n\n" +
            "id — 임포트의 기본 키다. 같은 id로 다시 넣으면 기존 에셋을 덮어쓰고(멱등), 바꾸면 새 에셋이 생긴다. " +
            "접두 Building: 는 고정이고 뒤쪽만 정한다. 한 번 정한 뒤에는 바꾸지 않는다.\n\n" +
            "채굴 속도 — 채굴기는 배율만 갖는다. 실제 시간은 광맥 extractInterval ÷ 배율 — " +
            "\"얼마나 캐기 어려운 광맥인가\"는 광맥이, \"얼마나 좋은 채굴기인가\"는 건물이 말한다.\n\n" +
            "Recipes — 재료 종류가 입력 슬롯보다 많은 레시피는 흐리게 나오고 고를 수 없다. " +
            "제련로·제작기 1입력, 조립기 2입력, 제조기 4입력이라는 곡선이 여기서 강제된다.\n\n" +
            "Ammo Filter — Ammo 타입만 목록에 오른다.\n\n" +
            "건설 비용 — 배치 시 인벤토리에서 차감, 철거 시 전액 환급.\n\n" +
            "모델 — 미리보기는 실제 에셋 머티리얼로 그린다(재질이 빠진 부품만 회색 대체). 위치는 모델의 원점(피벗)을 " +
            "그대로 쓴다 — 유니티에서 놓이는 위치와 같아야 하기 때문이다. JSON에는 파일 이름만 들어가고, " +
            "임포터가 Assets/Models 에서 같은 이름을 찾아 Assets/Prefabs/Buildings/{id}.prefab 을 만들어 SO에 연결한다."));

        // b-center — 3D 뷰 + 오버레이
        var center = new VisualElement { style = { flexGrow = 1, minWidth = 120 } };
        main.Add(center);
        // focusable=false — 뷰가 포커스를 가져가면 Ctrl+S/Z 가 셸 키 핸들러에 닿지 않는다.
        // 키보드는 안 쓰므로 포커스를 paneHost 에 남겨 둔다.
        viewGui = new IMGUIContainer(OnViewGUI) { focusable = false, style = { position = Position.Absolute,
            left = 0, right = 0, top = 0, bottom = 0 } };
        center.Add(viewGui);
        viewGui.RegisterCallback<DetachFromPanelEvent>(_ => CleanupPreview());


        // #b-hint — 좌하단 오버레이
        hintOverlay = new Label("드래그: 회전 · 휠: 줌 · 우클릭 드래그: 이동 · 포트 편집은 오른쪽 격자에서")
        { pickingMode = PickingMode.Ignore, style = { position = Position.Absolute, left = 14, bottom = 12,
            fontSize = 11.5f, color = GdEnum.Faint,
            backgroundColor = new Color(0.043f, 0.071f, 0.125f, 0.72f),
            borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
            borderTopColor = GdEnum.Line, borderBottomColor = GdEnum.Line,
            borderLeftColor = GdEnum.Line, borderRightColor = GdEnum.Line,
            borderTopLeftRadius = 6, borderTopRightRadius = 6, borderBottomLeftRadius = 6, borderBottomRightRadius = 6,
            paddingLeft = 10, paddingRight = 10, paddingTop = 6, paddingBottom = 6 } };
        center.Add(hintOverlay);

        // b-right — 340px · border-left
        var right = new ScrollView { style = { width = 340, flexShrink = 0, backgroundColor = GdEnum.Panel2,
            borderLeftWidth = 1, borderLeftColor = GdEnum.Line } };
        right.contentContainer.style.paddingLeft = 14;
        right.contentContainer.style.paddingRight = 14;
        right.contentContainer.style.paddingTop = 14;
        right.contentContainer.style.paddingBottom = 14;
        main.Add(right);

        right.Add(H3("속성"));
        propsBox = new VisualElement();
        right.Add(propsBox);
        right.Add(DividerEl());

        var portHead = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        portHead.Add(H3("포트"));
        portHead.Add(new Label("▨ 입력") { style = { color = ColIn, fontSize = 11.5f, marginLeft = 8, marginBottom = 10 } });
        portHead.Add(new Label("▨ 출력") { style = { color = ColOut, fontSize = 11.5f, marginLeft = 6, marginBottom = 10 } });
        right.Add(portHead);
        right.Add(new Label("칸의 변을 클릭 — 없음 → 입력 → 출력 → 없음. 위 = North(+y)")
        { style = { fontSize = 11.5f, color = GdEnum.Faint, whiteSpace = WhiteSpace.Normal, marginBottom = 8 } });

        portsBox = new VisualElement();
        right.Add(portsBox);

        RenderAll();
    }

    void RenderAll() { RenderList(); RenderProps(); RenderPorts(); RenderWarn(); Refresh3D(); }

    // ── 목록 ──
    void RenderList()
    {
        listBox.Clear();
        for (int i = 0; i < buildings.Count; i++)
        {
            var b = buildings[i];
            int idx = i;
            var item = new VisualElement();
            item.AddToClassList("gd-bitem");
            item.EnableInClassList("gd-bitem--sel", i == curIdx);
            var nm = new Label(b.displayName);
            nm.AddToClassList("gd-bitem-nm");
            item.Add(nm);
            var kd = new Label(b.kind);
            kd.AddToClassList("gd-bitem-kd");
            Mono(kd);
            item.Add(kd);
            item.RegisterCallback<PointerDownEvent>(_ =>
            {
                curIdx = idx; selPort = -1; ResetCamera();
                RenderAll();
            });
            listBox.Add(item);
        }
    }

    // ── 속성 ──
    void RenderProps()
    {
        propsBox.Clear();
        var b = Cur;
        if (b == null) return;
        var k = KindOf(b.kind);

        // Kind
        var kindChoices = Kinds.Select(x => $"{x.v} — {x.ko}").ToList();
        var kindD = new DropdownField(kindChoices, Mathf.Max(0, Array.FindIndex(Kinds, x => x.v == b.kind)));
        kindD.RegisterValueChangedCallback(e =>
        {
            int i = kindChoices.IndexOf(e.newValue);
            if (i < 0) return;
            b.kind = Kinds[i].v;
            b.category = Kinds[i].cat;
            PushHist();
            RenderAll();
        });
        propsBox.Add(Field2("Kind", kindD));

        // Display Name — 처음 지을 때만 빈 id 를 채운다
        var nameF = new TextField { value = b.displayName };
        nameF.RegisterValueChangedCallback(e =>
        {
            b.displayName = e.newValue;
            if (!b.idLocked && string.IsNullOrEmpty(Slug(IdSuffix(b))))
            {
                var suf = Slug(b.displayName);
                b.id = string.IsNullOrEmpty(suf) ? "" : IdPrefix + suf;
            }
            RenderList(); RenderWarn();
        });
        HookHist(nameF);
        propsBox.Add(Field2("Display Name", nameF));

        // Id — idrow
        var idRow = new VisualElement();
        idRow.AddToClassList("gd-idrow");
        var pfx = new Label(IdPrefix);
        pfx.AddToClassList("gd-idrow-pfx");
        Mono(pfx);
        idRow.Add(pfx);
        var idF = Mono(new TextField { value = IdSuffix(b) });
        idF.RegisterValueChangedCallback(e =>
        {
            var cleaned = Slug(e.newValue);
            b.id = string.IsNullOrEmpty(cleaned) ? "" : IdPrefix + cleaned;
            b.idLocked = !string.IsNullOrEmpty(cleaned);
            RenderList(); RenderWarn();
        });
        HookHist(idF);
        idRow.Add(idF);
        propsBox.Add(Field2("Id", idRow));

        // Description
        var descF = new TextField { value = b.description, multiline = true };
        descF.AddToClassList("gd-multiline");
        descF.verticalScrollerVisibility = ScrollerVisibility.Auto;
        descF.RegisterValueChangedCallback(e => b.description = e.newValue);
        HookHist(descF);
        propsBox.Add(Field2("Description", descF));

        // Category
        var catChoices = Categories.Select(c => $"{c.v} — {c.ko}").ToList();
        var catD = new DropdownField(catChoices, Mathf.Max(0, Array.FindIndex(Categories, c => c.v == b.category)));
        catD.RegisterValueChangedCallback(e =>
        {
            int i = catChoices.IndexOf(e.newValue);
            if (i >= 0) { b.category = Categories[i].v; PushHist(); }
        });
        propsBox.Add(Field2("Category", catD));

        // Model — 이름(mono) + ✕
        propsBox.Add(Field2("Model", ModelNameRow(() => (b.model, b.modelGuid),
            (n, g) => { b.model = n; b.modelGuid = g; })));
        propsBox.Add(Field2("", new Label(string.IsNullOrEmpty(b.model)
            ? "모델이 없으면 풋프린트 크기의 큐브로 만들어진다"
            : "프리팹은 임포트할 때 이 모델로 자동 생성된다")
        { style = { fontSize = 11, color = GdEnum.Faint, whiteSpace = WhiteSpace.Normal } }));

        // Size (X, Y) — row2. 줄이면 풋프린트 밖 포트를 함께 지운다 —
        // 격자에서 보이지도 지울 수도 없는 유령이 되기 때문(실수는 Ctrl+Z).
        var sizeRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        sizeRow.Add(SmallInt(b.sizeX, v => { b.sizeX = Mathf.Clamp(v, 1, 8); PruneOutside(b); RenderAll(); }, flex: true));
        sizeRow.Add(SmallInt(b.sizeY, v => { b.sizeY = Mathf.Clamp(v, 1, 8); PruneOutside(b); RenderAll(); }, flex: true, last: true));
        propsBox.Add(Field2("Size (X, Y)", sizeRow));

        // Buffer Slots — row3
        var bufRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        bufRow.Add(SmallInt(b.inputSlots, v => { b.inputSlots = Mathf.Max(0, v); RenderWarn(); }, "입력 슬롯", flex: true));
        bufRow.Add(SmallInt(b.outputSlots, v => { b.outputSlots = Mathf.Max(0, v); RenderWarn(); }, "출력 슬롯", flex: true));
        bufRow.Add(SmallInt(b.bufferStackCap, v => { b.bufferStackCap = Mathf.Max(0, v); RenderWarn(); },
            "슬롯 하나에 쌓이는 최대 개수 (0 = 아이템 기본 스택). 벨트류는 1", flex: true, last: true));
        propsBox.Add(Field2("Buffer Slots", bufRow));

        // Max HP · Required Tier
        propsBox.Add(Field2("Max HP", IntField(b.maxHp, v => { b.maxHp = Mathf.Max(1, v); RenderWarn(); },
            "밤 웨이브에 몬스터가 때릴 때 버티는 내구도")));
        propsBox.Add(Field2("Required Tier", IntField(b.requiredCoreTier,
            v => { b.requiredCoreTier = Mathf.Max(0, v); RenderWarn(); })));

        // Build Cost
        propsBox.Add(CostBlock("BUILD COST · 철거 시 전액 환급", b.buildCost, RenderProps));

        // Hide In Menu
        var hideT = new Toggle { value = b.hideFromBuildMenu };
        hideT.RegisterValueChangedCallback(e => { b.hideFromBuildMenu = e.newValue; PushHist(); RenderWarn(); });
        propsBox.Add(Field2("Hide In Menu", hideT));

        // 파괴 규칙 — 없애는 손이 둘이라 규칙도 둘이다 (코어: 철거 X, 둥지: 철거 X · 공격 O)
        var demoT = new Toggle { value = b.isDemolishable,
            tooltip = "끄면 철거 모드가 이 건물을 아예 조준하지 않는다 — 코어·둥지" };
        demoT.RegisterValueChangedCallback(e => { b.isDemolishable = e.newValue; PushHist(); RenderWarn(); });
        propsBox.Add(Field2("Demolishable", demoT));

        var atkT = new Toggle { value = b.isAttackable,
            tooltip = "플레이어의 공격이 통하는가. 기본은 꺼짐 — 둥지·나무처럼 부술 것만 켠다. 몬스터의 공격은 이 값과 무관하다" };
        atkT.RegisterValueChangedCallback(e => { b.isAttackable = e.newValue; PushHist(); RenderWarn(); });
        propsBox.Add(Field2("Attackable", atkT));

        // ── kind 별 추가 필드 (NUM_FIELDS 대응) ──
        foreach (var f in k.extra)
        {
            switch (f)
            {
                case "speedMultiplier":
                    propsBox.Add(Field2("Speed Multiplier", NumField(b.speedMultiplier,
                        v => { b.speedMultiplier = Mathf.Max(0.1f, v); RenderWarn(); },
                        "채굴 시간 = 광맥의 extractInterval ÷ 이 배율")));
                    break;
                case "speedTilesPerSec":
                    propsBox.Add(Field2("Speed (tiles/s)", NumField(b.speedTilesPerSec,
                        v => { b.speedTilesPerSec = Mathf.Max(0, v); })));
                    break;
                case "curveLPrefab":
                    propsBox.Add(Field2("Curve L", ModelNameRow(() => (b.modelCurveL, b.modelCurveLGuid),
                        (n, g) => { b.modelCurveL = n; b.modelCurveLGuid = g; })));
                    break;
                case "curveRPrefab":
                    propsBox.Add(Field2("Curve R", ModelNameRow(() => (b.modelCurveR, b.modelCurveRGuid),
                        (n, g) => { b.modelCurveR = n; b.modelCurveRGuid = g; })));
                    break;
                case "droneRange":
                    propsBox.Add(Field2("Range (타일)", NumField(b.droneRange,
                        v => { b.droneRange = Mathf.Max(1, v); RenderWarn(); },
                        "짝지을 수 있는 다른 스테이션까지의 최대 거리")));
                    break;
                case "carryCapacity":
                    propsBox.Add(Field2("Carry", NumField(b.carryCapacity,
                        v => { b.carryCapacity = Mathf.Max(1, v); RenderWarn(); }, "드론 1회 운반량")));
                    break;
                case "travelSpeed":
                    propsBox.Add(Field2("Speed (타일/초)", NumField(b.travelSpeed,
                        v => { b.travelSpeed = Mathf.Max(0.5f, v); RenderWarn(); })));
                    break;
                case "damageMultiplier":
                    propsBox.Add(Field2("Damage ×", NumField(b.damageMultiplier,
                        v => { b.damageMultiplier = Mathf.Max(0, v); RenderWarn(); },
                        "실제 피해 = 탄약의 기본 피해 × 이 배수. 0 = 공격하지 않음")));
                    break;
                case "range":
                    propsBox.Add(Field2("Range (타일)", NumField(b.range,
                        v => { b.range = Mathf.Max(0, v); RenderWarn(); })));
                    break;
                case "fireRate":
                    propsBox.Add(Field2("Fire Rate (회/초)", NumField(b.fireRate,
                        v => { b.fireRate = Mathf.Max(0.1f, v); RenderWarn(); })));
                    break;
                case "ammoFilter": propsBox.Add(AmmoSection(b)); break;
                case "availableRecipes": propsBox.Add(RecipeSection(b)); break;
                case "tiers": propsBox.Add(TierSection(b)); break;
            }
        }

        // 모델 픽커(상단바)를 현재 건물과 동기화
        SyncTopModelField(b);
    }

    void SyncTopModelField(GBuilding b)
    {
        if (topModelField == null) return;
        topModelField.SetValueWithoutNotify(FindModelAsset(b?.modelGuid, b?.model));
    }

    /// <summary>
    /// guid 로 먼저 찾고 실패하면 이름으로 찾는다 — 임포터의 ResolveModel 과 같은 우선순위여야
    /// 에디터에 보이는 모델과 임포트가 집는 모델이 어긋나지 않는다.
    /// </summary>
    static GameObject FindModelAsset(string guid, string fileName)
    {
        if (!string.IsNullOrEmpty(guid))
        {
            var byGuid = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (byGuid != null) return byGuid;
        }
        if (string.IsNullOrEmpty(fileName)) return null;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        foreach (var g in AssetDatabase.FindAssets($"t:GameObject {stem}"))
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            if (string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileNameWithoutExtension(path), stem, StringComparison.OrdinalIgnoreCase))
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }

    /// <summary>픽커에서 고른 에셋 → (파일 이름, guid). 비우면 둘 다 빈 문자열.</summary>
    static (string name, string guid) ModelRefOf(UnityEngine.Object asset)
    {
        if (asset == null) return ("", "");
        var path = AssetDatabase.GetAssetPath(asset);
        return (Path.GetFileName(path), AssetDatabase.AssetPathToGUID(path));
    }

    // 모델 행 — 픽커 + ✕ (웹의 "지정/✕" 대응). 이름과 guid 를 한 쌍으로 읽고 쓴다.
    VisualElement ModelNameRow(Func<(string name, string guid)> get, Action<string, string> set)
    {
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        var cur = get();
        var pick = new UnityEditor.UIElements.ObjectField
        { objectType = typeof(GameObject), allowSceneObjects = false,
          tooltip = "JSON 에는 guid 와 파일 이름이 함께 저장된다 — guid 가 진실",
          value = FindModelAsset(cur.guid, cur.name), style = { flexGrow = 1 } };
        pick.RegisterValueChangedCallback(e =>
        {
            var (n, g) = ModelRefOf(e.newValue);
            set(n, g);
            PushHist();
            RenderProps(); Refresh3D();
        });
        row.Add(pick);
        var x = new Label("✕") { tooltip = "지우기", style = { color = GdEnum.Faint, paddingLeft = 3, paddingRight = 3 } };
        x.RegisterCallback<PointerDownEvent>(_ =>
        {
            set("", "");
            PushHist();
            RenderProps(); Refresh3D();
        });
        row.Add(x);
        return row;
    }

    // ── 숫자칸 헬퍼 ──
    FloatField NumField(float value, Action<float> set, string tooltip = null)
    {
        var f = new FloatField { value = value, tooltip = tooltip ?? "" };
        f.RegisterValueChangedCallback(e => set(e.newValue));
        HookHist(f);
        return f;
    }
    IntegerField IntField(int value, Action<int> set, string tooltip = null)
    {
        var f = new IntegerField { value = value, tooltip = tooltip ?? "" };
        f.RegisterValueChangedCallback(e => set(e.newValue));
        HookHist(f);
        return f;
    }
    IntegerField SmallInt(int value, Action<int> set, string tooltip = null, bool flex = false, bool last = false)
    {
        var f = IntField(value, set, tooltip);
        if (flex) { f.style.flexGrow = 1; f.style.flexBasis = 0; }
        if (!last) f.style.marginRight = 6;
        f.AddToClassList("gd-field-input");
        return f;
    }
    void HookHist(VisualElement f) => f.RegisterCallback<FocusOutEvent>(_ => PushHist());

    // ── 건설 비용 / 요구 부품 — .costblock .costrow ──
    VisualElement CostBlock(string title, List<GCost> list, Action rerender)
    {
        var box = new VisualElement { style = { marginTop = 14, borderTopWidth = 1, borderTopColor = GdEnum.Line,
            paddingTop = 12 } };
        var ttl = new Label(title);
        ttl.AddToClassList("gd-groupttl");
        ttl.style.marginTop = 0;
        box.Add(ttl);
        box.Add(CostRows(list, rerender));
        return box;
    }

    VisualElement CostRows(List<GCost> list, Action rerender)
    {
        var host = new VisualElement();
        var items = KnownItems();
        var choices = items.Select(i => i.id).ToList();
        if (list.Count == 0)
            host.Add(new Label("비용 없음") { style = { fontSize = 11.5f, color = GdEnum.Faint, marginBottom = 6 } });
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            int idx = i;
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center,
                marginBottom = 5 } };
            var line = items.FirstOrDefault(x => x.id == c.item).line;
            row.Add(new VisualElement { style = { width = 3, height = 24, borderTopLeftRadius = 2,
                borderTopRightRadius = 2, borderBottomLeftRadius = 2, borderBottomRightRadius = 2,
                backgroundColor = GdEnum.LineColor(line ?? "None"), marginRight = 6, flexShrink = 0 } });
            var opts = choices.ToList();
            if (!string.IsNullOrEmpty(c.item) && !opts.Contains(c.item)) opts.Insert(0, c.item);
            if (opts.Count == 0) opts.Add(c.item ?? "");
            var drop = new DropdownField(opts, Mathf.Max(0, opts.IndexOf(c.item)))
            { style = { flexGrow = 1, flexShrink = 1 } };
            drop.AddToClassList("gd-field-input");
            drop.RegisterValueChangedCallback(e => { c.item = e.newValue; PushHist(); rerender(); });
            row.Add(drop);
            var amt = new IntegerField { value = c.amount, style = { width = 56, marginLeft = 6 } };
            amt.AddToClassList("gd-field-input");
            amt.RegisterValueChangedCallback(e => { c.amount = Mathf.Max(1, e.newValue); RenderWarn(); });
            HookHist(amt);
            row.Add(amt);
            var del = new Label("✕") { style = { color = GdEnum.Faint, paddingLeft = 5, fontSize = 12 } };
            del.RegisterCallback<PointerDownEvent>(_ => { list.RemoveAt(idx); PushHist(); rerender(); });
            row.Add(del);
            host.Add(row);
        }
        var addC = new Button(() =>
        {
            list.Add(new GCost { item = items.Length > 0 ? items[0].id : "Item:IronPlate", amount = 1 });
            PushHist();
            rerender();
        }) { text = "+ 재료", style = { alignSelf = Align.FlexStart } };
        addC.AddToClassList("gd-btn-mini");
        host.Add(addC);
        return host;
    }

    // ── Ammo Filter — .ammolist ──
    VisualElement AmmoSection(GBuilding b)
    {
        var box = new VisualElement { style = { marginTop = 12 } };
        box.Add(GroupTitle("Ammo Filter · 받을 수 있는 탄약"));
        // 스크롤뷰 자체가 .ammolist — 안쪽 컨테이너에 max-height 를 주면 행들이 압축된다
        var listEl = new ScrollView { style = { maxHeight = 168 } };
        listEl.AddToClassList("gd-ammolist");
        box.Add(listEl);

        var sel = new HashSet<string>(b.ammoFilter);
        var items = KnownItems();
        var cand = items.Where(i => i.type == "Ammo" || sel.Contains(i.id)).ToList();
        int rows = 0;
        foreach (var id in sel.Where(id => !items.Any(i => i.id == id)))
        { listEl.Add(AmmoRow(id, "없음", GdEnum.Border, true, on =>
            { ToggleListValue(b.ammoFilter, id, on); }, missing: true)); rows++; }
        foreach (var i in cand)
        { listEl.Add(AmmoRow(i.id, i.type, GdEnum.LineColor(i.line), sel.Contains(i.id), on =>
            { ToggleListValue(b.ammoFilter, i.id, on); })); rows++; }
        if (rows == 0)
            listEl.Add(new Label("탄약이 없습니다") { style = { fontSize = 11.5f, color = GdEnum.Faint } });
        return box;
    }

    void ToggleListValue(List<string> list, string id, bool on)
    {
        if (on) { if (!list.Contains(id)) list.Add(id); }
        else list.Remove(id);
        PushHist();
        RenderWarn();
    }

    VisualElement AmmoRow(string id, string ty, Color barColor, bool on, Action<bool> toggle,
        bool missing = false, bool over = false, string tooltip = null)
    {
        var row = new VisualElement { tooltip = tooltip ?? "" };
        row.AddToClassList("gd-ammorow");
        row.EnableInClassList("gd-ammorow--on", on);
        if (over) row.style.opacity = 0.4f;
        var tog = new Toggle { value = on };
        tog.SetEnabled(!over);
        row.Add(tog);
        var bar = new VisualElement { style = { backgroundColor = barColor } };
        bar.AddToClassList("gd-ammorow-bar");
        row.Add(bar);
        var nm = new Label(id);
        nm.AddToClassList("gd-ammorow-nm");
        Mono(nm);
        row.Add(nm);
        var tyL = new Label(ty);
        tyL.AddToClassList("gd-ammorow-ty");
        if (missing) tyL.style.color = GdEnum.Warn;
        row.Add(tyL);
        tog.RegisterValueChangedCallback(e =>
        {
            row.EnableInClassList("gd-ammorow--on", e.newValue);
            toggle(e.newValue);
        });
        row.RegisterCallback<PointerDownEvent>(e =>
        {
            if (over || e.target is Toggle || (e.target as VisualElement)?.GetFirstAncestorOfType<Toggle>() != null) return;
            tog.value = !tog.value;
        });
        return row;
    }

    // ── Recipes — 입력 슬롯 초과 레시피는 흐리게 + 잠금 ──
    VisualElement RecipeSection(GBuilding b)
    {
        var box = new VisualElement { style = { marginTop = 12 } };
        box.Add(GroupTitle("Recipes · 이 설비가 돌릴 레시피"));
        var listEl = new ScrollView { style = { maxHeight = 168 } };
        listEl.AddToClassList("gd-ammolist");
        box.Add(listEl);

        var sel = new HashSet<string>(b.availableRecipes);
        var recipes = KnownRecipes();
        int rows = 0;
        foreach (var id in sel.Where(id => !recipes.Any(r => r.id == id)))
        { listEl.Add(AmmoRow(id, "없음", GdEnum.Border, true, on =>
            { ToggleListValue(b.availableRecipes, id, on); }, missing: true)); rows++; }
        foreach (var r in recipes)
        {
            bool over = r.inputs > b.inputSlots;
            listEl.Add(AmmoRow(r.id, $"{r.inputs}입력 · T{r.tier}",
                over ? GdEnum.FromHex("#3B4B66") : GdEnum.Accent, sel.Contains(r.id), on =>
                { ToggleListValue(b.availableRecipes, r.id, on); }, over: over,
                tooltip: over ? $"재료 {r.inputs}종 > 입력 슬롯 {b.inputSlots}" : null));
            rows++;
        }
        if (rows == 0)
            listEl.Add(new Label("레시피 목록이 없습니다 — 아이템·레시피 탭에서 만드세요")
            { style = { fontSize = 11.5f, color = GdEnum.Faint } });
        return box;
    }

    // ── Repair Tiers — .tiercard ──
    VisualElement TierSection(GBuilding b)
    {
        var box = new VisualElement { style = { marginTop = 12 } };
        box.Add(GroupTitle("Repair Tiers · 게이트별 납품 목록"));
        if (b.tiers.Count == 0)
            box.Add(new Label("단계가 없습니다") { style = { fontSize = 11.5f, color = GdEnum.Faint, marginBottom = 6 } });

        for (int ti = 0; ti < b.tiers.Count; ti++)
        {
            var t = b.tiers[ti];
            int tIdx = ti;
            var borderC = t.isFinal ? new Color(1f, 0.365f, 0.451f, 0.45f) : GdEnum.Line;
            var card = new VisualElement { style = { borderTopWidth = 1, borderBottomWidth = 1,
                borderLeftWidth = 1, borderRightWidth = 1,
                borderTopColor = borderC, borderBottomColor = borderC,
                borderLeftColor = borderC, borderRightColor = borderC,
                borderTopLeftRadius = 6, borderTopRightRadius = 6,
                borderBottomLeftRadius = 6, borderBottomRightRadius = 6,
                backgroundColor = GdEnum.Panel, marginBottom = 8, overflow = Overflow.Hidden } };
            box.Add(card);

            // 머리 — ⓪ 마크 + 이름 + ✕
            var hd = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center,
                paddingLeft = 8, paddingRight = 8, paddingTop = 6, paddingBottom = 6,
                backgroundColor = GdEnum.Bg, borderBottomWidth = 1, borderBottomColor = GdEnum.Line } };
            card.Add(hd);
            var mark = new Label(tIdx < TierMark.Length ? TierMark[tIdx] : tIdx.ToString())
            { style = { fontSize = 14, color = t.isFinal ? GdEnum.Warn : GdEnum.Accent, width = 18,
                unityTextAlign = TextAnchor.MiddleCenter, flexShrink = 0 } };
            Mono(mark);
            hd.Add(mark);
            var nmF = new TextField { value = t.name, style = { flexGrow = 1, marginLeft = 6 } };
            nmF.AddToClassList("gd-field-input");
            nmF.RegisterValueChangedCallback(e => { t.name = e.newValue; RenderWarn(); });
            HookHist(nmF);
            hd.Add(nmF);
            var delX = new Label("✕") { style = { color = GdEnum.Faint, paddingLeft = 6, fontSize = 12 } };
            delX.RegisterCallback<PointerDownEvent>(_ => { b.tiers.RemoveAt(tIdx); PushHist(); RenderProps(); RenderWarn(); });
            hd.Add(delX);

            // 몸통
            var bd = new VisualElement { style = { paddingLeft = 8, paddingRight = 8, paddingTop = 8, paddingBottom = 8 } };
            card.Add(bd);
            var descF = new TextField { value = t.description, multiline = true,
                tooltip = "게임에 보일 한 줄 설명" };
            descF.AddToClassList("gd-multiline");
            descF.AddToClassList("gd-field-input");
            descF.RegisterValueChangedCallback(e => t.description = e.newValue);
            HookHist(descF);
            bd.Add(descF);

            bd.Add(TierSub("요구 부품"));
            bd.Add(CostRows(t.requirements, () => { RenderProps(); RenderWarn(); }));

            bd.Add(TierSub("해금 · 확인 창에 그대로 표시된다"));
            var unlockF = new TextField { value = string.Join("\n", t.unlocks), multiline = true,
                tooltip = "줄바꿈으로 구분" };
            unlockF.AddToClassList("gd-multiline");
            unlockF.AddToClassList("gd-field-input");
            unlockF.RegisterValueChangedCallback(e =>
                t.unlocks = e.newValue.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList());
            HookHist(unlockF);
            bd.Add(unlockF);

            if (t.maxHpBonus > 0)
                bd.Add(new Label($"+ 코어 내구도 +{t.maxHpBonus:N0}  자동 — 아래 값에서 생성")
                { style = { fontSize = 11, color = GdEnum.Ok, marginTop = 5, paddingLeft = 7, paddingRight = 7,
                    paddingTop = 3, paddingBottom = 3, borderTopLeftRadius = 4, borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
                    backgroundColor = new Color(0.365f, 0.827f, 0.62f, 0.08f),
                    borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = new Color(0.365f, 0.827f, 0.62f, 0.28f),
                    borderBottomColor = new Color(0.365f, 0.827f, 0.62f, 0.28f),
                    borderLeftColor = new Color(0.365f, 0.827f, 0.62f, 0.28f),
                    borderRightColor = new Color(0.365f, 0.827f, 0.62f, 0.28f) } });

            // 발 — HP 보너스 · 최종 단계
            var foot = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center,
                marginTop = 10, paddingTop = 8, borderTopWidth = 1, borderTopColor = GdEnum.Line } };
            bd.Add(foot);
            foot.Add(new Label("최대 HP +") { tooltip = "0보다 크면 해금 목록에 자동으로 한 줄 추가된다",
                style = { fontSize = 11.5f, color = GdEnum.Muted } });
            var hpF = new IntegerField { value = t.maxHpBonus, style = { width = 66, marginLeft = 4 } };
            hpF.AddToClassList("gd-field-input");
            hpF.RegisterValueChangedCallback(e => { t.maxHpBonus = Mathf.Max(0, e.newValue); });
            hpF.RegisterCallback<FocusOutEvent>(_ => { PushHist(); RenderProps(); });
            foot.Add(hpF);
            var finT = new Toggle { value = t.isFinal, text = "최종 단계 (경고 표시)",
                style = { marginLeft = 12, fontSize = 11.5f, color = GdEnum.Muted } };
            finT.RegisterValueChangedCallback(e =>
            {
                for (int j = 0; j < b.tiers.Count; j++) b.tiers[j].isFinal = j == tIdx && e.newValue;
                PushHist();
                RenderProps(); RenderWarn();
            });
            foot.Add(finT);
        }

        var addT = new Button(() =>
        {
            b.tiers.Add(new GTier { name = "새 단계" });
            PushHist();
            RenderProps(); RenderWarn();
        }) { text = "+ 단계", style = { marginTop = 6, alignSelf = Align.FlexStart } };
        addT.AddToClassList("gd-btn-mini");
        addT.AddToClassList("gd-btn-primary");
        box.Add(addT);
        return box;
    }

    static Label TierSub(string text)
    {
        var l = new Label(text.ToUpperInvariant()) { style = { fontSize = 9.5f, letterSpacing = 1.2f,
            color = GdEnum.Accent, marginTop = 12, marginBottom = 5, paddingBottom = 3,
            borderBottomWidth = 1, borderBottomColor = new Color(0.31f, 0.847f, 0.878f, 0.15f) } };
        return l;
    }

    // ── 포트 — 평면 격자 편집기 (발자국을 위에서 본 그림, 변 클릭 = 없음→입력→출력→없음) ──
    //  원본 HTML 툴은 3D 뷰에서 편집했지만 포트는 결국 (칸, 면) 조합이라
    //  평면 격자가 같은 정보를 더 또렷하게 보여준다. 3D 뷰는 보기 전용.
    void RenderPorts()
    {
        portsBox.Clear();
        var b = Cur;
        if (b == null) return;

        GPort PortAt(int x, int y, string dir) =>
            b.ports.FirstOrDefault(p => p.x == x && p.y == y &&
                string.Equals(p.dir, dir, StringComparison.OrdinalIgnoreCase));

        void Cycle(int x, int y, string dir)
        {
            var p = PortAt(x, y, dir);
            if (p == null) b.ports.Add(new GPort { x = x, y = y, dir = dir, isInput = true });
            else if (p.isInput) p.isInput = false;
            else b.ports.Remove(p);
            PushHist();
            RenderPorts(); RenderWarn(); Refresh3D();
        }

        // 풋프린트 밖 포트(불러온 데이터가 이미 어긋난 경우) — 격자에 못 그리니 일괄 삭제 버튼으로
        int outside = b.ports.Count(p => p.x >= b.sizeX || p.y >= b.sizeY);
        if (outside > 0)
        {
            var fix = new Button(() => { PruneOutside(b); PushHist(); RenderAll(); })
            { text = $"풋프린트 밖 포트 {outside}개 삭제", style = { alignSelf = Align.FlexStart, marginBottom = 6 } };
            fix.AddToClassList("gd-btn-mini");
            fix.AddToClassList("gd-btn-warn");
            portsBox.Add(fix);
        }

        // 안쪽 변인가 — 이웃 칸이 건물 자신이면 포트를 놓을 수 없다 (검증도 막는다)
        bool Outer(int x, int y, string dir)
        {
            var v = DVec[dir];
            int nx = x + v.x, ny = y + v.y;
            return nx < 0 || ny < 0 || nx >= b.sizeX || ny >= b.sizeY;
        }

        // 큰 풋프린트는 패널 폭(약 300px)에 맞춰 칸을 줄인다
        const int CompassPx = 16;
        int cellPx = Mathf.Min(52, Mathf.FloorToInt((300f - CompassPx * 2) / Mathf.Max(1, b.sizeX)));
        int edgePx = Mathf.Max(6, cellPx / 5);
        var colNone = new Color(0, 0, 0, 0.25f);
        int gridW = cellPx * b.sizeX, gridH = cellPx * b.sizeY;

        // 방위 표시 — N(위) 강조, 나머지는 흐리게
        Label Compass(string s, bool strong) => new(s) { pickingMode = PickingMode.Ignore,
            style = { color = strong ? GdEnum.Accent : GdEnum.Faint, fontSize = 10,
                unityFontStyleAndWeight = FontStyle.Bold, unityTextAlign = TextAnchor.MiddleCenter } };

        var wrap = new VisualElement { style = { alignItems = Align.FlexStart, marginLeft = 3 } };
        portsBox.Add(wrap);
        var nRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        wrap.Add(nRow);
        nRow.Add(new VisualElement { style = { width = CompassPx } });
        var nL = Compass("N", true); nL.style.width = gridW; nRow.Add(nL);

        var midRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        wrap.Add(midRow);
        var wL = Compass("W", false); wL.style.width = CompassPx; wL.style.height = gridH; midRow.Add(wL);
        var gridHost = new VisualElement();
        midRow.Add(gridHost);
        var eL = Compass("E", false); eL.style.width = CompassPx; eL.style.height = gridH; midRow.Add(eL);

        var sRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        wrap.Add(sRow);
        sRow.Add(new VisualElement { style = { width = CompassPx } });
        var sL = Compass("S", false); sL.style.width = gridW; sRow.Add(sL);

        for (int gy = b.sizeY - 1; gy >= 0; gy--)   // 화면 위가 +y(North)
        {
            var rowVe = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            gridHost.Add(rowVe);
            for (int gx = 0; gx < b.sizeX; gx++)
            {
                var cell = new VisualElement { style = { width = cellPx, height = cellPx,
                    backgroundColor = new Color(1, 1, 1, 0.06f),
                    borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = new Color(0, 0, 0, 0.4f), borderBottomColor = new Color(0, 0, 0, 0.4f),
                    borderLeftColor = new Color(0, 0, 0, 0.4f), borderRightColor = new Color(0, 0, 0, 0.4f) } };
                rowVe.Add(cell);
                cell.Add(new Label($"{gx},{gy}") { pickingMode = PickingMode.Ignore,
                    style = { position = Position.Absolute, left = 0, right = 0, top = 0, bottom = 0,
                        opacity = 0.35f, unityTextAlign = TextAnchor.MiddleCenter } });

                void Edge(string dir, StyleLength? top, StyleLength? bottom,
                    StyleLength? left, StyleLength? right, float w, float h)
                {
                    int x = gx, y = gy;
                    var p = PortAt(x, y, dir);
                    // 안쪽 변은 놓을 수 없으니 스트립을 그리지 않는다 —
                    // 이미 포트가 있으면(잘못된 데이터) 지울 수 있게 남겨둔다
                    if (p == null && !Outer(x, y, dir)) return;
                    var strip = new VisualElement { style = { position = Position.Absolute, width = w, height = h,
                        backgroundColor = p == null ? colNone : p.isInput ? ColIn : ColOut } };
                    if (top.HasValue) strip.style.top = top.Value;
                    if (bottom.HasValue) strip.style.bottom = bottom.Value;
                    if (left.HasValue) strip.style.left = left.Value;
                    if (right.HasValue) strip.style.right = right.Value;
                    strip.RegisterCallback<ClickEvent>(_ => Cycle(x, y, dir));
                    strip.tooltip = $"({x},{y}) {dir} — " + (p == null ? "없음" : p.isInput ? "입력" : "출력");
                    cell.Add(strip);
                }
                Edge("North", 0f, null, edgePx, null, cellPx - edgePx * 2, edgePx);
                Edge("South", null, 0f, edgePx, null, cellPx - edgePx * 2, edgePx);
                Edge("West", edgePx, null, 0f, null, edgePx, cellPx - edgePx * 2);
                Edge("East", edgePx, null, null, 0f, edgePx, cellPx - edgePx * 2);
            }
        }
    }

    static void PruneOutside(GBuilding b) =>
        b.ports.RemoveAll(p => p.x >= b.sizeX || p.y >= b.sizeY);

    // ── 검증 (원본 validate 전체) ──
    List<string> Validate()
    {
        var b = Cur;
        var outp = new List<string>();
        if (b == null) return outp;

        // identity
        var id = Bid(b);
        if (string.IsNullOrEmpty(Slug(IdSuffix(b))) && string.IsNullOrEmpty(Slug(b.displayName)))
            outp.Add("id 가 비어 있습니다 — 임포트의 기본 키입니다");
        else if (buildings.Count(x => Bid(x) == id) > 1) outp.Add($"id 중복 — {id}");
        if (string.IsNullOrWhiteSpace(b.displayName)) outp.Add("displayName 이 비어 있습니다 — 임포터가 거부합니다");

        var items = KnownItems();
        var recipes = KnownRecipes();
        bool HasItem(string i) => items.Any(x => x.id == i);

        var seen = new HashSet<string>();
        for (int i = 0; i < b.ports.Count; i++)
        {
            var p = b.ports[i];
            var tag = $"포트 {i + 1} ({p.x},{p.y},{p.dir})";
            if (p.x >= b.sizeX || p.y >= b.sizeY)
                outp.Add($"{tag} — LocalOffset 이 풋프린트({b.sizeX}×{b.sizeY}) 밖입니다");
            var v = DVec[p.dir];
            int nx = p.x + v.x, ny = p.y + v.y;
            if (nx >= 0 && ny >= 0 && nx < b.sizeX && ny < b.sizeY)
                outp.Add($"{tag} — 건물 안쪽을 향합니다. 이웃 칸이 자기 자신이라 연결되지 않습니다");
            var key = $"{p.x},{p.y},{p.dir}";
            if (!seen.Add(key)) outp.Add($"{tag} — 같은 칸·같은 방향에 포트가 중복됩니다");
        }

        int nIn = b.ports.Count(p => p.isInput), nOut = b.ports.Count - nIn;
        string[] passThrough = { "Belt", "Splitter", "Merger" };
        if (nIn > 0 && b.inputSlots < 1) outp.Add("입력 포트가 있는데 inputSlots 가 0 입니다");
        if (nOut > 0 && b.outputSlots < 1 && !passThrough.Contains(b.kind))
            outp.Add("출력 포트가 있는데 outputSlots 가 0 입니다");
        if (passThrough.Contains(b.kind))
        {
            if (b.outputSlots != 0) outp.Add($"{b.kind} 은 통과형이라 outputSlots 가 0 이어야 합니다");
            if (b.bufferStackCap != 1) outp.Add($"{b.kind} 은 한 번에 하나만 물므로 bufferStackCap 이 1 이어야 합니다");
        }
        if (b.kind == "Miner" && b.inputSlots != 0) outp.Add("채굴기는 입력 슬롯이 0 이어야 합니다");
        if (b.kind == "Belt" && (nIn != 1 || nOut != 1))
            outp.Add("벨트는 입력 1 · 출력 1 이어야 합니다 (모양별 포트는 런타임에 BuildPorts 가 계산)");
        if (b.kind == "Miner" && nIn > 0) outp.Add("채굴기는 입력 포트를 갖지 않습니다");
        if (b.kind == "Assembler")
        {
            if (b.availableRecipes.Count == 0) outp.Add("돌릴 레시피가 없습니다 — Recipes 에서 하나 이상 고르세요");
            foreach (var rid in b.availableRecipes)
            {
                var r = recipes.FirstOrDefault(x => x.id == rid);
                if (recipes.Length > 0 && r.id == null) outp.Add($"Recipes — 레시피 id \"{rid}\" 를 찾을 수 없습니다");
                else if (r.id != null && r.inputs > b.inputSlots)
                    outp.Add($"레시피 {rid} 는 재료 {r.inputs}종인데 입력 슬롯이 {b.inputSlots}개입니다");
            }
        }
        var costSeen = new HashSet<string>();
        for (int i = 0; i < b.buildCost.Count; i++)
        {
            var c = b.buildCost[i];
            if (string.IsNullOrEmpty(c.item)) outp.Add($"건설 비용 {i + 1} — 아이템이 비어 있습니다");
            else if (items.Length > 0 && !HasItem(c.item)) outp.Add($"건설 비용 — 아이템 id \"{c.item}\" 을 찾을 수 없습니다");
            if (c.amount < 1) outp.Add("건설 비용 — 수량은 1 이상이어야 합니다");
            if (!costSeen.Add(c.item)) outp.Add($"건설 비용 — 같은 아이템이 중복됩니다 ({c.item})");
        }
        if (b.buildCost.Count == 0 && !b.hideFromBuildMenu)
            outp.Add("건설 비용이 없습니다 — 무료 건물이 의도한 것인지 확인하세요");
        if (!(b.maxHp > 0)) outp.Add("Max HP 는 1 이상이어야 합니다 — 밤 웨이브에 즉시 파괴됩니다");
        if (b.kind == "Miner" && !(b.speedMultiplier > 0))
            outp.Add("Speed Multiplier 는 0보다 커야 합니다 (채굴 시간 = 광맥 extractInterval ÷ 배율)");
        string[] needsPort = { "Miner", "Assembler", "Belt", "Splitter", "Merger", "Storage", "Core", "DronePort" };
        if (b.ports.Count == 0 && needsPort.Contains(b.kind) && !b.hideFromBuildMenu)
            outp.Add("포트가 하나도 없습니다 — 벨트·기계와 연결될 수 없습니다");
        if (b.kind == "DronePort")
        {
            if (!(b.droneRange > 0)) outp.Add("Range 는 0보다 커야 합니다 — 짝지을 스테이션을 찾지 못합니다");
            if (!(b.carryCapacity > 0)) outp.Add("Carry 는 1 이상이어야 합니다");
            if (!(b.travelSpeed > 0)) outp.Add("Speed 는 0보다 커야 합니다");
            if (nIn == 0) outp.Add("입력 포트가 없습니다 — 보낼 물건을 받을 수 없습니다");
            if (nOut == 0) outp.Add("출력 포트가 없습니다 — 받은 물건을 내보낼 수 없습니다");
        }
        if (b.kind == "Core")
        {
            var ts = b.tiers;
            if (ts.Count == 0) outp.Add("수리 단계가 없습니다 — 게이트를 하나 이상 만드세요");
            if (ts.Count(t => t.isFinal) > 1) outp.Add("최종 단계는 하나만 지정할 수 있습니다");
            if (ts.Count > 0 && !ts.Any(t => t.isFinal)) outp.Add("최종 단계가 지정되지 않았습니다 — 엔딩 조건이 없습니다");
            for (int i = 0; i < ts.Count; i++)
            {
                var t = ts[i];
                var tag = $"{(i < TierMark.Length ? TierMark[i] : i.ToString())} {(string.IsNullOrEmpty(t.name) ? "이름 없음" : t.name)}";
                if (string.IsNullOrEmpty(t.name)) outp.Add($"{tag} — 단계 이름이 비어 있습니다");
                if (t.requirements.Count == 0) outp.Add($"{tag} — 요구 부품이 없습니다");
                var seen2 = new HashSet<string>();
                foreach (var r in t.requirements)
                {
                    if (string.IsNullOrEmpty(r.item)) outp.Add($"{tag} — 부품이 비어 있습니다");
                    else if (items.Length > 0 && !HasItem(r.item)) outp.Add($"{tag} — 아이템 id \"{r.item}\" 을 찾을 수 없습니다");
                    if (r.amount < 1) outp.Add($"{tag} — 수량은 1 이상이어야 합니다");
                    if (!seen2.Add(r.item)) outp.Add($"{tag} — 같은 부품이 중복됩니다");
                }
                if (t.unlocks.Count == 0) outp.Add($"{tag} — 해금 내용이 없습니다 (확인 창이 비어 보입니다)");
            }
        }
        if (b.kind == "Tower")
        {
            bool atk = b.damageMultiplier > 0;
            if (!(b.range >= 0)) outp.Add("Range 는 0 이상이어야 합니다");
            if (!(b.fireRate > 0)) outp.Add("Fire Rate 는 0보다 커야 합니다");
            if (atk && b.ammoFilter.Count == 0)
                outp.Add("공격 타워인데 받을 수 있는 탄약이 없습니다 — Ammo Filter 를 지정하세요 (피해 = 탄약 피해 × 배수)");
            // 건설 비용에 그 탄약이 들어 있으면 "설치할 때 장전되는 일회용"으로 본다 (지뢰)
            bool oneShot = b.ammoFilter.Count > 0 && b.ammoFilter.All(a => b.buildCost.Any(c => c.item == a));
            if (b.ammoFilter.Count > 0 && !oneShot && nIn == 0)
                outp.Add("탄약을 쓰는데 입력 포트가 없습니다 — 벨트로 보급하거나 건설 비용에 포함하세요");
            foreach (var a in b.ammoFilter)
                if (items.Length > 0 && !HasItem(a)) outp.Add($"Ammo Filter — 아이템 id \"{a}\" 을 찾을 수 없습니다");
        }
        return outp;
    }

    void RenderWarn()
    {
        if (warnBox == null) return;
        warnBox.Clear();
        var ws = Validate();
        if (ws.Count == 0) warnBox.Add(OkMsg("✓ 검증 통과"));
        else
        {
            warnBox.Add(H3("검증"));
            foreach (var w in ws) warnBox.Add(WarnItem(w));
        }
        var b = Cur;
        statLabel.text = b != null
            ? $"건물 {buildings.Count} · 포트 {b.ports.Count} (입력 {b.ports.Count(p => p.isInput)} / 출력 {b.ports.Count(p => !p.isInput)})"
            : $"건물 {buildings.Count}";
        win.RefreshSharedStat();
    }

    // ═════════ 3D 뷰 — PreviewRenderUtility (three.js 씬 대응) ═════════

    PreviewRenderUtility pr;
    Material lineMat, modelMat, boxMat, dotMat;
    GameObject modelInstance;   // 프리뷰 씬에 실체화한 모델 (정식 렌더 경로)
    GameObject ppVolumeGo;      // 톤매핑 볼륨 — 렌더 순간에만 활성
    UnityEngine.Rendering.VolumeProfile ppProfile;
    readonly List<(Mesh mesh, Matrix4x4 mat, Material material, int sub)> drawList = new();
    readonly List<Mesh> ownedMeshes = new();

    // 궤도 카메라 — 기본 시점: 남서에서 북동을 바라봐 NE 가 화면 위로 온다
    float camYaw = 45f, camPitch = 28f, camDist = 6.6f;
    Vector3 camTarget = new(0, 0.3f, 0);

    void ResetCamera()
    {
        camYaw = 45f; camPitch = 28f; camDist = 6.6f;
        camTarget = new Vector3(0, 0.3f, 0);
        viewGui?.MarkDirtyRepaint();
    }
    Vector2 downPos;
    bool dragging;

    void EnsurePreview()
    {
        // 플레이 모드 전환이 프리뷰 씬 오브젝트를 지웠으면 통째로 다시 만든다
        if (pr != null && pr.camera == null) CleanupPreview();
        if (pr != null) return;
        pr = new PreviewRenderUtility();
        pr.camera.fieldOfView = 45;
        pr.camera.nearClipPlane = 0.1f;
        pr.camera.farClipPlane = 200;
        pr.camera.clearFlags = CameraClearFlags.SolidColor;
        pr.camera.backgroundColor = GdEnum.Bg;
        // ACES 톤매핑이 하이라이트를 눌러 주므로 라이트는 넉넉하게
        pr.lights[0].intensity = 1.8f;
        pr.lights[0].transform.rotation = Quaternion.LookRotation(new Vector3(-4, -7, -3));
        pr.lights[1].intensity = 0.5f;
        pr.lights[1].color = GdEnum.Accent;
        pr.lights[1].transform.rotation = Quaternion.LookRotation(new Vector3(4, -2, 3));
        pr.ambientColor = new Color(0.20f, 0.24f, 0.31f);

        // 톤매핑 — FactoryColor 는 발광 강도 5라(팔레트 주황 ×5) 톤매핑 없이는
        // 채널 클램프로 순백이 된다. 게임 카메라와 같은 ACES 로 눌러 준다.
        // 볼륨은 렌더 순간에만 켠다 — VolumeManager 가 전역이라 상시 켜두면
        // 게임 뷰 카메라에도 얹힐 수 있다.
        pr.camera.allowHDR = true;
        // URP 는 Preview 타입 카메라의 포스트프로세싱을 강제로 끈다 — Game 으로 우회
        pr.camera.cameraType = CameraType.Game;
        var acd = pr.camera.gameObject.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()
            ?? pr.camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        acd.renderPostProcessing = true;
        ppVolumeGo = new GameObject("GdPreviewVolume") { hideFlags = HideFlags.HideAndDontSave };
        var vol = ppVolumeGo.AddComponent<UnityEngine.Rendering.Volume>();
        vol.isGlobal = true;
        ppProfile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        ppProfile.hideFlags = HideFlags.HideAndDontSave;
        var tm = ppProfile.Add<UnityEngine.Rendering.Universal.Tonemapping>(true);
        tm.mode.Override(UnityEngine.Rendering.Universal.TonemappingMode.ACES);
        var ca = ppProfile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
        ca.postExposure.Override(2f);   // ACES 가 중간톤을 눌러 어두워지는 만큼 보정
        vol.sharedProfile = ppProfile;
        pr.AddSingleGO(ppVolumeGo);
        ppVolumeGo.SetActive(false);
        lineMat = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
        // 이 프로젝트는 URP — 빌트인 Standard 는 프리뷰에서 마젠타로 깨진다
        modelMat = NewLit(GdEnum.FromHex("#9FB2CC"), 0.15f, 0.35f, transparent: false);
        // 대체 박스 — three 원본: MeshStandardMaterial(#223350 · metalness .25 · opacity .9)
        var boxC = GdEnum.Line; boxC.a = 0.9f;
        boxMat = NewLit(boxC, 0.25f, 0.3f, transparent: true);
        // 원점 구슬 — 불투명·깊이 기록. 반투명 박스 아래에 은은히 비친다(원본과 동일).
        var unlitSh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        dotMat = new Material(unlitSh) { hideFlags = HideFlags.HideAndDontSave, color = GdEnum.Ok };
        Refresh3D();
    }

    static Material NewLit(Color c, float metallic, float smooth, bool transparent)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        bool urp = sh.name.Contains("Universal");
        var m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave, color = c };
        m.SetFloat("_Metallic", metallic);
        m.SetFloat(urp ? "_Smoothness" : "_Glossiness", smooth);
        if (transparent)
        {
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            if (urp) { m.SetFloat("_Surface", 1); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); }
            else { m.SetFloat("_Mode", 2); m.EnableKeyword("_ALPHABLEND_ON"); }
            m.renderQueue = 3000;
        }
        return m;
    }

    void CleanupPreview()
    {
        ClearMeshes();
        if (modelInstance != null) { UnityEngine.Object.DestroyImmediate(modelInstance); modelInstance = null; }
        if (ppProfile != null) { UnityEngine.Object.DestroyImmediate(ppProfile); ppProfile = null; }
        ppVolumeGo = null;   // 프리뷰 씬 소속 — pr.Cleanup 이 지운다
        if (lineMat != null) UnityEngine.Object.DestroyImmediate(lineMat);
        if (modelMat != null) UnityEngine.Object.DestroyImmediate(modelMat);
        if (boxMat != null) UnityEngine.Object.DestroyImmediate(boxMat);
        if (dotMat != null) UnityEngine.Object.DestroyImmediate(dotMat);
        lineMat = modelMat = boxMat = dotMat = null;
        pr?.Cleanup();
        pr = null;
    }

    void ClearMeshes()
    {
        foreach (var m in ownedMeshes) if (m != null) UnityEngine.Object.DestroyImmediate(m);
        ownedMeshes.Clear();
        drawList.Clear();
    }

    // three 는 오른손 좌표라 y→-z 였지만, 왼손인 유니티에 그대로 옮기면 위에서 볼 때
    // 동서가 거울상이 된다 — y→+z 로 가야 격자 패널(N 위·E 오른쪽)과 일치한다.
    static Vector3 CellToWorld(int x, int y, (int x, int y) size) =>
        new(x - (size.x - 1) * 0.5f, 0, y - (size.y - 1) * 0.5f);

    Mesh NewMesh()
    {
        var m = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        ownedMeshes.Add(m);
        return m;
    }

    // 씬 재구성 — refresh3D() 대응
    void Refresh3D()
    {
        if (pr == null) { viewGui?.MarkDirtyRepaint(); return; }
        ClearMeshes();
        if (modelInstance != null) { UnityEngine.Object.DestroyImmediate(modelInstance); modelInstance = null; }
        var b = Cur;
        if (b == null) { viewGui.MarkDirtyRepaint(); return; }
        var size = (x: b.sizeX, y: b.sizeY);

        BuildGrid();
        BuildFootprint(size);
        BuildPortFlows(b, size);
        BuildModel(b, size);
        viewGui.MarkDirtyRepaint();
    }

    void BuildGrid()
    {
        // GridHelper(24, 24, #2E4266, #1A2740) · opacity .5
        var verts = new List<Vector3>();
        var cols = new List<Color>();
        var center = GdEnum.Border; center.a = 0.5f;
        var lineC = GdEnum.FromHex("#1A2740"); lineC.a = 0.5f;
        for (int i = -12; i <= 12; i++)
        {
            var c = i == 0 ? center : lineC;
            verts.Add(new Vector3(i, 0, -12)); verts.Add(new Vector3(i, 0, 12)); cols.Add(c); cols.Add(c);
            verts.Add(new Vector3(-12, 0, i)); verts.Add(new Vector3(12, 0, i)); cols.Add(c); cols.Add(c);
        }
        var m = NewMesh();
        m.SetVertices(verts);
        m.SetColors(cols);
        m.SetIndices(Enumerable.Range(0, verts.Count).ToArray(), MeshTopology.Lines, 0);
        drawList.Add((m, Matrix4x4.identity, lineMat, 0));
    }

    void BuildFootprint((int x, int y) size)
    {
        var fillVerts = new List<Vector3>(); var fillCols = new List<Color>(); var fillIdx = new List<int>();
        var edgeVerts = new List<Vector3>(); var edgeCols = new List<Color>();
        var fillC = GdEnum.Accent; fillC.a = 0.055f;
        var edgeC = GdEnum.Border;
        const float h = 0.48f;   // 0.96 반폭
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                var p = CellToWorld(x, y, size);
                int i0 = fillVerts.Count;
                fillVerts.Add(p + new Vector3(-h, 0.002f, -h));
                fillVerts.Add(p + new Vector3(h, 0.002f, -h));
                fillVerts.Add(p + new Vector3(h, 0.002f, h));
                fillVerts.Add(p + new Vector3(-h, 0.002f, h));
                for (int j = 0; j < 4; j++) fillCols.Add(fillC);
                fillIdx.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });

                var c0 = p + new Vector3(-h, 0.004f, -h);
                var c1 = p + new Vector3(h, 0.004f, -h);
                var c2 = p + new Vector3(h, 0.004f, h);
                var c3 = p + new Vector3(-h, 0.004f, h);
                edgeVerts.AddRange(new[] { c0, c1, c1, c2, c2, c3, c3, c0 });
                for (int j = 0; j < 8; j++) edgeCols.Add(edgeC);
            }
        var fm = NewMesh();
        fm.SetVertices(fillVerts); fm.SetColors(fillCols);
        fm.SetIndices(fillIdx.ToArray(), MeshTopology.Triangles, 0);
        drawList.Add((fm, Matrix4x4.identity, lineMat, 0));
        var em = NewMesh();
        em.SetVertices(edgeVerts); em.SetColors(edgeCols);
        em.SetIndices(Enumerable.Range(0, edgeVerts.Count).ToArray(), MeshTopology.Lines, 0);
        drawList.Add((em, Matrix4x4.identity, lineMat, 0));

        // origin(0,0) — 회전 기준점 (초록 구슬). 불투명·깊이 기록이라
        // 반투명 박스 아래에 은은히 비친다 — 원본 three 씬과 동일.
        var o = CellToWorld(0, 0, size);
        var sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
        if (sphere != null)
            drawList.Add((sphere, Matrix4x4.TRS(o + new Vector3(0, 0.05f, 0),
                Quaternion.identity, Vector3.one * 0.11f), dotMat, 0));
    }

    // 포트 흐름 — 바닥판 + 지느러미 + 발광 띠 (+ 선택 링). 정점 알파로 그라데이션.
    const float FlowLen = 0.58f, FlowW = 0.78f, FlowH = 0.36f;

    void BuildPortFlows(GBuilding b, (int x, int y) size)
    {
        var ports = b.ports;
        for (int i = 0; i < ports.Count; i++)
        {
            var p = ports[i];
            var c = CellToWorld(p.x, p.y, size);
            var v = DVec[p.dir];
            var outward = new Vector3(v.x, 0, v.y);
            var pos = c + outward * 0.5f;
            bool selc = false;   // 보기 전용 — 선택 없음
            var col = p.isInput ? ColIn : ColOut;
            float power = selc ? 1.5f : 1f;
            var rotQ = Quaternion.FromToRotation(Vector3.right, outward);
            var mtx = Matrix4x4.TRS(pos, rotQ, Vector3.one);

            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
            void Quad(Vector3 a, Vector3 bq, Vector3 cq, Vector3 d, float aA, float aB, float aC, float aD)
            {
                int i0 = verts.Count;
                verts.AddRange(new[] { a, bq, cq, d });
                cols.Add(new Color(col.r, col.g, col.b, Mathf.Clamp01(aA * power)));
                cols.Add(new Color(col.r, col.g, col.b, Mathf.Clamp01(aB * power)));
                cols.Add(new Color(col.r, col.g, col.b, Mathf.Clamp01(aC * power)));
                cols.Add(new Color(col.r, col.g, col.b, Mathf.Clamp01(aD * power)));
                tris.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }
            // 바닥판 — 면(near, x=0)에서 밖(x=FlowLen)으로 옅어진다
            Quad(new Vector3(0, 0.012f, -FlowW / 2), new Vector3(0, 0.012f, FlowW / 2),
                 new Vector3(FlowLen, 0.012f, FlowW / 2), new Vector3(FlowLen, 0.012f, -FlowW / 2),
                 0.55f, 0.55f, 0f, 0f);
            // 지느러미 — 세로판, 위·밖으로 옅어진다
            Quad(new Vector3(0, 0, 0), new Vector3(0, FlowH, 0),
                 new Vector3(FlowLen, FlowH, 0), new Vector3(FlowLen, 0, 0),
                 0.55f, 0f, 0f, 0f);
            // 발광 띠 — 포트의 정확한 위치
            float lip = selc ? 0.95f : 0.6f;
            Quad(new Vector3(0f, 0.02f, -FlowW * 0.4f), new Vector3(0f, 0.02f, FlowW * 0.4f),
                 new Vector3(0.05f, 0.02f, FlowW * 0.4f), new Vector3(0.05f, 0.02f, -FlowW * 0.4f),
                 lip, lip, lip, lip);
            var m = NewMesh();
            m.SetVertices(verts); m.SetColors(cols);
            m.SetIndices(tris.ToArray(), MeshTopology.Triangles, 0);
            drawList.Add((m, mtx, lineMat, 0));

            if (selc)
            {
                // 선택 링 — 보라 원
                var ringVerts = new List<Vector3>(); var ringCols = new List<Color>();
                var ringC = GdEnum.Sel; ringC.a = 0.9f;
                const int rs = 32;
                for (int s = 0; s < rs; s++)
                {
                    float a0 = s / (float)rs * Mathf.PI * 2, a1 = (s + 1) / (float)rs * Mathf.PI * 2;
                    ringVerts.Add(new Vector3(Mathf.Cos(a0) * 0.17f, 0.03f, Mathf.Sin(a0) * 0.17f));
                    ringVerts.Add(new Vector3(Mathf.Cos(a1) * 0.17f, 0.03f, Mathf.Sin(a1) * 0.17f));
                    ringCols.Add(ringC); ringCols.Add(ringC);
                }
                var rm = NewMesh();
                rm.SetVertices(ringVerts); rm.SetColors(ringCols);
                rm.SetIndices(Enumerable.Range(0, ringVerts.Count).ToArray(), MeshTopology.Lines, 0);
                drawList.Add((rm, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one), lineMat, 0));
            }
        }
    }

    void BuildModel(GBuilding b, (int x, int y) size)
    {
        var asset = FindModelAsset(b.modelGuid, b.model);
        if (asset != null)
        {
            // 부품 수집 — 웹 툴은 텍스처가 없어 회색으로 통일했지만, 유니티에는
            // 실제 에셋 머티리얼이 있으니 그대로 그린다. 서브메시(다중 재질)까지 전부.
            var parts = new List<(Mesh mesh, Matrix4x4 l2w, Material[] mats)>();
            foreach (var f in asset.GetComponentsInChildren<MeshFilter>())
            {
                if (f.sharedMesh == null) continue;
                var r = f.GetComponent<MeshRenderer>();
                parts.Add((f.sharedMesh, f.transform.localToWorldMatrix, r != null ? r.sharedMaterials : null));
            }
            foreach (var sk in asset.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (sk.sharedMesh == null) continue;
                parts.Add((sk.sharedMesh, sk.transform.localToWorldMatrix, sk.sharedMaterials));
            }
            if (parts.Count > 0)
            {
                // fitModel — 바운딩 XZ 를 풋프린트에 맞춰 스케일만. 피벗 위치는 그대로.
                var bounds = new Bounds();
                bool first = true;
                foreach (var p in parts)
                {
                    var wb = GeometryUtility.CalculateBounds(p.mesh.vertices, p.l2w);
                    if (first) { bounds = wb; first = false; } else bounds.Encapsulate(wb);
                }
                float target = Mathf.Max(size.x, size.y) * 0.9f;
                float scale = target / Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.z), 0.0001f);
                // DrawMesh 직접 호출은 URP(SRP 배처)에서 셰이더 그래프의 텍스처
                // 프로퍼티가 바인딩되지 않는다(팔레트가 흰색으로 뜸) — 진짜
                // 게임오브젝트로 프리뷰 씬에 인스턴스해 정식 렌더 경로를 태운다.
                modelInstance = pr.InstantiatePrefabInScene(asset);
                modelInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                modelInstance.transform.localScale = asset.transform.localScale * scale;
                return;
            }
        }
        // 대체 박스 — 조명 받는 반투명 Standard(#223350) + 테두리선. 원본:
        // BoxGeometry(0.86) + MeshStandardMaterial + EdgesGeometry(#2E4266)
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var boxTrs = Matrix4x4.TRS(new Vector3(0, 0.275f, 0), Quaternion.identity,
            new Vector3(size.x * 0.86f, 0.55f, size.y * 0.86f));
        drawList.Add((cube, boxTrs, boxMat, 0));

        var eVerts = new List<Vector3>(); var eCols = new List<Color>();
        var edgeC2 = GdEnum.Border;
        Vector3[] c8 = new Vector3[8];
        for (int i = 0; i < 8; i++)
            c8[i] = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
        int[,] e12 = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 },
                       { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
        for (int i = 0; i < 12; i++)
        {
            eVerts.Add(c8[e12[i, 0]]); eVerts.Add(c8[e12[i, 1]]);
            eCols.Add(edgeC2); eCols.Add(edgeC2);
        }
        var em2 = NewMesh();
        em2.SetVertices(eVerts); em2.SetColors(eCols);
        em2.SetIndices(Enumerable.Range(0, eVerts.Count).ToArray(), MeshTopology.Lines, 0);
        drawList.Add((em2, boxTrs, lineMat, 0));
    }

    // ── IMGUI — 그리기 + 입력 ──
    void OnViewGUI()
    {
        var rect = viewGui.contentRect;
        if (rect.width < 20 || rect.height < 20) return;
        EnsurePreview();
        HandleViewInput(rect);

        if (Event.current.type != EventType.Repaint) return;
        pr.BeginPreview(new Rect(0, 0, rect.width, rect.height), GUIStyle.none);
        var camPos = camTarget + Quaternion.Euler(camPitch, camYaw, 0) * new Vector3(0, 0, -camDist);
        pr.camera.transform.SetPositionAndRotation(camPos,
            Quaternion.LookRotation(camTarget - camPos, Vector3.up));
        pr.camera.aspect = rect.width / rect.height;
        foreach (var (mesh, mat, material, sub) in drawList)
            pr.DrawMesh(mesh, mat, material, sub);
        // camera.Render() 직접 호출은 프리뷰 조명·앰비언트 격리를 건너뛰어
        // 열린 씬의 라이팅이 샌다 — 정식 경로(Render)로 돌려야 pr.lights가 먹는다.
        if (ppVolumeGo != null) ppVolumeGo.SetActive(true);
        pr.Render(true);
        if (ppVolumeGo != null) ppVolumeGo.SetActive(false);
        var tex = pr.EndPreview();
        GUI.DrawTexture(new Rect(0, 0, rect.width, rect.height), tex, ScaleMode.StretchToFill, false);
        DrawCompass(rect);
    }

    // 격자 가장자리 밖에 방위 글자를 카메라로 투영해 얹는다 — 회전해도 따라온다
    static GUIStyle compassStrong, compassDim;
    void DrawCompass(Rect rect)
    {
        if (compassStrong == null)
        {
            compassStrong = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter };
            compassStrong.normal.textColor = GdEnum.Accent;
            compassDim = new GUIStyle(compassStrong);
            compassDim.normal.textColor = GdEnum.Faint;
        }
        // DVEC → 월드: North=[0,1] → +Z. 풋프린트 가장자리 바로 밖 — 기본 줌에서 보이게.
        var b = Cur;
        var size = b != null ? (x: b.sizeX, y: b.sizeY) : (x: 1, y: 1);
        float dx = size.x * 0.5f + 1.1f, dz = size.y * 0.5f + 1.1f;
        (string s, Vector3 p)[] marks =
        {
            ("N", new Vector3(0, 0, dz)), ("S", new Vector3(0, 0, -dz)),
            ("E", new Vector3(dx, 0, 0)), ("W", new Vector3(-dx, 0, 0)),
        };
        foreach (var (s, p) in marks)
        {
            var vp = pr.camera.WorldToViewportPoint(p);
            if (vp.z <= 0 || vp.x < 0.02f || vp.x > 0.98f || vp.y < 0.02f || vp.y > 0.98f) continue;
            var gp = new Vector2(vp.x * rect.width, (1f - vp.y) * rect.height);
            GUI.Label(new Rect(gp.x - 10, gp.y - 10, 20, 20), s, s == "N" ? compassStrong : compassDim);
        }
    }

    void HandleViewInput(Rect rect)
    {
        var e = Event.current;
        switch (e.type)
        {
            case EventType.MouseDown when rect.Contains(e.mousePosition):
                downPos = e.mousePosition;
                dragging = true;
                e.Use();
                break;
            case EventType.MouseDrag when dragging:
                if (e.button == 0)
                {
                    camYaw += e.delta.x * 0.5f;
                    camPitch = Mathf.Clamp(camPitch + e.delta.y * 0.4f, 5, 85);
                }
                else if (e.button is 1 or 2)
                {
                    var camRot = Quaternion.Euler(camPitch, camYaw, 0);
                    camTarget -= camRot * new Vector3(e.delta.x, -e.delta.y, 0) * (camDist * 0.0016f);
                }
                viewGui.MarkDirtyRepaint();
                e.Use();
                break;
            case EventType.MouseUp when dragging:
                dragging = false;
                e.Use();
                break;
            case EventType.ScrollWheel when rect.Contains(e.mousePosition):
                camDist = Mathf.Clamp(camDist * (1 + e.delta.y * 0.04f), 2, 40);
                viewGui.MarkDirtyRepaint();
                e.Use();
                break;
        }
    }

}
#endif
