#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.UI;

namespace CoreDawn.EditorTools
{
    // ═══════════════════════════════════════════════════════════
    //  맵 탭 — 고정 맵. 타일(지면·강·절벽) + 코어 · 자원 노드 · 둥지
    //  (Web/js/map-editor.js 대응)
    //
    //  GameData 와 달리 맵은 MapData.json 으로 따로 나간다 — 저장은 셸의
    //  저장 버튼이 SaveExtraFiles 로 함께 처리한다(웹판의 모달 입출력 대체).
    //
    //  캔버스: 타일은 1타일=1픽셀 텍스처(포인트 필터)를 확대해 깔고,
    //  눈금·배치물·브러시는 Painter2D 오버레이로 얹는다 — 401×401(16만 칸)도
    //  타일 그리기가 텍스처 한 장이라 느려지지 않는다.
    // ═══════════════════════════════════════════════════════════

    class GMapNode
    {
        public string item = "Item:IronOre";
        public int x, y, size = 1;
        public float extractInterval = 1;
        public int maxStock = 20;
        [JsonIgnore] public MapImporter.NodeDto src;
    }

    class GSpawnPt { public int x, y; public bool hasBoss; }

    class GNest
    {
        public int x, y;
        public float warningRange = 25, triggerRange = 15;
        public int defenseSpawnAmount = 3;
        public float defenseSpawnCooldown = 10;
        public List<GSpawnPt> spawnPoints = new() { new GSpawnPt() };
        public float engageMinRange = 4, engageMaxRange = 18, chaseRange = 24, leashRange = 32;
        public bool engageDayOnly = true;
        public int bossRecoveryDays = 3, nestRecoveryDays = 5;
        [JsonIgnore] public MapImporter.NestDto src;
    }

    class GMap
    {
        public string id = "";
        public string displayName = "", description = "";
        public int width = 121, height = 121;
        public int coreX, coreY;
        public byte[] tiles;
        public List<GMapNode> nodes = new();
        public List<GNest> nests = new();
        public List<Vector2Int> nightSpawnPoints = new();
        public List<Vector2Int> trees = new();
        [JsonIgnore] public MapImporter.MapDto src;
    }

    class GdMapTab : GdTab
    {
        public override string Title => "맵";
        public GdMapTab(GameDataEditorWindow win) : base(win) { }

        // ── TILES / NODE_KINDS (map-editor.js 상단) ──
        class TileInfo
        {
            public readonly string ko; public readonly Color color; public readonly bool walk, build;
            public TileInfo(string ko, string hex, bool walk, bool build)
            { this.ko = ko; color = GdEnum.FromHex(hex); this.walk = walk; this.build = build; }
        }
        static readonly TileInfo[] Tiles =
        {
            new("지면", "#3E6B45", walk: true, build: true),
            new("강", "#1B4A6B", walk: true, build: false),
            new("절벽", "#3A2A2A", walk: false, build: false),
        };
        static TileInfo TileOf(int v) => v >= 0 && v < Tiles.Length ? Tiles[v] : Tiles[0];

        static readonly (string item, string ko, Color color)[] NodeKinds =
        {
            ("Item:IronOre", "철광석", GdEnum.FromHex("#E8A54B")),
            ("Item:CopperOre", "구리광석", GdEnum.FromHex("#4FD8E0")),
            ("Item:CrystalOre", "크리스탈 광석", GdEnum.FromHex("#B48CFF")),
        };

        // ── 배치물 레이어 ──
        // key 는 도구 키와 일부러 같다 — 숨겨 둔 레이어의 도구를 고르면 그 레이어가 다시 켜진다.
        // filled=false 는 캔버스에서도 테두리만 있는 칸(밤 진입로)이라 칩도 속을 비운다.
        class LayerInfo
        {
            public readonly string key, ko; public readonly Color color; public readonly bool filled;
            public LayerInfo(string key, string ko, Color color, bool filled)
            { this.key = key; this.ko = ko; this.color = color; this.filled = filled; }
        }
        static readonly LayerInfo[] Layers =
        {
            new("core", "코어", GdEnum.Ok, true),
            new("node", "자원", GdEnum.ItemC, true),
            new("nest", "둥지", GdEnum.Warn, true),
            new("night", "밤 진입로", GdEnum.ItemC, false),
            new("tree", "나무", GdEnum.FromHex("#4FBF6A"), true),
        };

        // ── 데이터 ──
        readonly List<GMap> maps = new();
        int curMap;
        GMap M => maps.ElementAtOrDefault(curMap);

        string tool = "paint";
        int paintTile = 2, brushSize = 3;

        // 나무 자동생성 수치 — 브러시 크기와 같은 층위의 도구 상태다(맵에 저장되는 값이 아니다)
        float treeSpacing = 3.2f, treeSpacingJitter = 1.1f;
        float treeCoreClear = 10f, treeObjectClear = 3.5f, treeEdgeClear = 1f;
        int treeSeed = 20260824;
        (string type, int i)? sel;
        bool showRings = true, showHalo = true, showGrid = true;

        // 숨긴 레이어 — 화면에서 빠지고 클릭에도 집히지 않는다. 검증은 숨긴 것도 그대로 본다
        readonly HashSet<string> hiddenLayers = new();
        bool Vis(string layer) => !hiddenLayers.Contains(layer);

        // 뷰 변환 — k = 타일당 픽셀
        float viewX, viewY, viewK = 4;
        bool fitted;

        GdHistory hist;
        void PushHist() { hist?.Push(); win.MarkDirty(); }

        static int Odd(int v) { v = Mathf.Clamp(v, 21, 401); return v % 2 == 1 ? v : v + 1; }
        static int Idx(GMap m, int x, int y) => y * m.width + x;
        static bool InB(GMap m, int x, int y) => x >= 0 && y >= 0 && x < m.width && y < m.height;
        static float ROf(GMap m) => Mathf.Min(m.width, m.height) / 2f;   // 코어에서 가장자리까지
        static int[] GuideRings(GMap m) =>
            new[] { Mathf.RoundToInt(ROf(m) / 3), Mathf.RoundToInt(ROf(m) * 2 / 3), Mathf.RoundToInt(ROf(m)) };

        static GMap BlankMap(int n = 1)
        {
            const int w = 121, h = 121;   // 홀수 — 코어 3×3 이 정확히 중앙에 온다
            return new GMap
            {
                id = "Map:New" + n, displayName = "새 맵",
                width = w, height = h,
                coreX = (w >> 1) - 1, coreY = (h >> 1) - 1,
                tiles = new byte[w * h],
            };
        }

        // ═════════ 파일 입출력 — MapData.json (셸 저장에 통합) ═════════

        public override void OnDataLoaded()
        {
            maps.Clear();
            try
            {
                var root = JsonConvert.DeserializeObject<MapImporter.Root>(File.ReadAllText(MapImporter.JsonPath));
                foreach (var m in root?.maps ?? Array.Empty<MapImporter.MapDto>())
                {
                    int w = Odd(m.width > 0 ? m.width : 121), h = Odd(m.height > 0 ? m.height : 121);
                    var g = new GMap
                    {
                        id = m.id ?? "", displayName = m.displayName ?? "", description = m.description ?? "",
                        width = w, height = h,
                        coreX = m.core?.x ?? 59, coreY = m.core?.y ?? 59,
                        tiles = DecodeTiles(m.tiles, w, h),
                        nodes = (m.nodes ?? Array.Empty<MapImporter.NodeDto>()).Select(n => new GMapNode
                        {
                            item = string.IsNullOrEmpty(n.item) ? "Item:IronOre" : n.item,
                            x = n.x, y = n.y, size = n.size > 0 ? n.size : 1,
                            extractInterval = n.extractInterval > 0 ? n.extractInterval : 1,
                            maxStock = n.maxStock > 0 ? n.maxStock : 20,
                            src = n,
                        }).ToList(),
                        nests = (m.nests ?? Array.Empty<MapImporter.NestDto>()).Select(n => new GNest
                        {
                            x = n.x, y = n.y,
                            warningRange = n.warningRange > 0 ? n.warningRange : 25,
                            triggerRange = n.triggerRange > 0 ? n.triggerRange : 15,
                            defenseSpawnAmount = n.defenseSpawnAmount > 0 ? n.defenseSpawnAmount : 3,
                            defenseSpawnCooldown = n.defenseSpawnCooldown > 0 ? n.defenseSpawnCooldown : 10,
                            spawnPoints = (n.spawnPoints is { Length: > 0 } sp
                                ? sp.Select(p => new GSpawnPt { x = p.x, y = p.y, hasBoss = p.hasBoss })
                                : new[] { new GSpawnPt() }.AsEnumerable()).ToList(),
                            engageMinRange = n.engageMinRange, engageMaxRange = n.engageMaxRange,
                            chaseRange = n.chaseRange, leashRange = n.leashRange, engageDayOnly = n.engageDayOnly,
                            bossRecoveryDays = n.bossRecoveryDays, nestRecoveryDays = n.nestRecoveryDays,
                            src = n,
                        }).ToList(),
                        nightSpawnPoints = (m.nightSpawnPoints ?? Array.Empty<MapImporter.CellDto>())
                            .Select(p => new Vector2Int(p.x, p.y)).ToList(),
                        trees = (m.trees ?? Array.Empty<MapImporter.CellDto>())
                            .Select(p => new Vector2Int(p.x, p.y)).ToList(),
                        src = m,
                    };
                    maps.Add(g);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[GdMapTab] {MapImporter.JsonPath} 읽기 실패 — {e.Message}"); }
            if (maps.Count == 0) maps.Add(BlankMap());
            curMap = 0; sel = null; fitted = false;
            hist = new GdHistory(Snapshot, Restore, 60);
            hist.Reset();
        }

        public override void SaveExtraFiles(bool import)
        {
            if (hist == null) return;
            var root = new MapImporter.Root { maps = maps.Select(Export).ToArray() };
            File.WriteAllText(MapImporter.JsonPath,
                JsonConvert.SerializeObject(root, GameDataEditorWindow.JsonSettings) + "\n");
            AssetDatabase.ImportAsset(MapImporter.JsonPath);
            if (import) MapImporter.ImportAll();
        }

        static byte[] DecodeTiles(string[] src, int w, int h)
        {
            var t = new byte[w * h];
            if (src == null) return t;
            for (int y = 0; y < Mathf.Min(h, src.Length); y++)
            {
                var line = src[y] ?? "";
                for (int x = 0; x < Mathf.Min(w, line.Length); x++)
                    t[y * w + x] = (byte)Mathf.Clamp(line[x] - '0', 0, 9);
            }
            return t;
        }

        // 타일은 행마다 한 줄씩 문자열 — JSON 에서 맵 모양이 그대로 보인다
        static string[] EncodeTiles(byte[] tiles, int w, int h)
        {
            var rows = new string[h];
            var sb = new System.Text.StringBuilder(w);
            for (int y = 0; y < h; y++)
            {
                sb.Clear();
                for (int x = 0; x < w; x++) sb.Append((char)('0' + tiles[y * w + x]));
                rows[y] = sb.ToString();
            }
            return rows;
        }

        MapImporter.MapDto Export(GMap g)
        {
            var o = g.src ?? (g.src = new MapImporter.MapDto());
            o.id = g.id; o.displayName = g.displayName; o.description = g.description ?? "";
            o.width = g.width; o.height = g.height;
            o.core ??= new MapImporter.CellDto();
            o.core.x = g.coreX; o.core.y = g.coreY;
            o.tiles = EncodeTiles(g.tiles, g.width, g.height);
            o.nodes = g.nodes.Select(n =>
            {
                var d = n.src ?? (n.src = new MapImporter.NodeDto());
                d.item = n.item; d.x = n.x; d.y = n.y; d.size = n.size;
                d.extractInterval = n.extractInterval; d.maxStock = n.maxStock;
                return d;
            }).ToArray();
            o.nests = g.nests.Select(n =>
            {
                var d = n.src ?? (n.src = new MapImporter.NestDto());
                d.x = n.x; d.y = n.y;
                d.warningRange = n.warningRange; d.triggerRange = n.triggerRange;
                d.defenseSpawnAmount = n.defenseSpawnAmount; d.defenseSpawnCooldown = n.defenseSpawnCooldown;
                d.spawnPoints = n.spawnPoints.Select(p => new MapImporter.SpawnDto
                { x = p.x, y = p.y, hasBoss = p.hasBoss }).ToArray();
                d.engageMinRange = n.engageMinRange; d.engageMaxRange = n.engageMaxRange;
                d.chaseRange = n.chaseRange; d.leashRange = n.leashRange; d.engageDayOnly = n.engageDayOnly;
                d.bossRecoveryDays = n.bossRecoveryDays; d.nestRecoveryDays = n.nestRecoveryDays;
                return d;
            }).ToArray();
            o.nightSpawnPoints = g.nightSpawnPoints
                .Select(p => new MapImporter.CellDto { x = p.x, y = p.y }).ToArray();
            o.trees = g.trees.Select(p => new MapImporter.CellDto { x = p.x, y = p.y }).ToArray();
            return o;
        }

        // ═════════ 히스토리 — 내보낸 형태의 스냅샷. 복원 때 뷰는 건드리지 않는다 ═════════

        class Snap { public MapImporter.MapDto[] maps; public int cur; }

        string Snapshot() => JsonConvert.SerializeObject(
            new Snap { maps = maps.Select(Export).ToArray(), cur = curMap },
            GameDataEditorWindow.JsonSettings);

        void Restore(string json)
        {
            var s = JsonConvert.DeserializeObject<Snap>(json);
            var keep = (viewX, viewY, viewK);
            LoadFromDtos(s.maps ?? Array.Empty<MapImporter.MapDto>());
            curMap = Mathf.Clamp(s.cur, 0, Mathf.Max(0, maps.Count - 1));
            (viewX, viewY, viewK) = keep;
            sel = null;
            win.MarkDirty();
            if (listBox != null) RenderAll();
        }

        void LoadFromDtos(MapImporter.MapDto[] dtos)
        {
            // OnDataLoaded 의 본문과 같은 변환 — 파일 대신 스냅샷에서 온다
            var json = JsonConvert.SerializeObject(new MapImporter.Root { maps = dtos },
                GameDataEditorWindow.JsonSettings);
            maps.Clear();
            var root = JsonConvert.DeserializeObject<MapImporter.Root>(json);
            foreach (var m in root?.maps ?? Array.Empty<MapImporter.MapDto>())
            {
                int w = Odd(m.width > 0 ? m.width : 121), h = Odd(m.height > 0 ? m.height : 121);
                maps.Add(new GMap
                {
                    id = m.id ?? "", displayName = m.displayName ?? "", description = m.description ?? "",
                    width = w, height = h,
                    coreX = m.core?.x ?? 59, coreY = m.core?.y ?? 59,
                    tiles = DecodeTiles(m.tiles, w, h),
                    nodes = (m.nodes ?? Array.Empty<MapImporter.NodeDto>()).Select(n => new GMapNode
                    {
                        item = n.item, x = n.x, y = n.y, size = n.size,
                        extractInterval = n.extractInterval, maxStock = n.maxStock, src = n,
                    }).ToList(),
                    nests = (m.nests ?? Array.Empty<MapImporter.NestDto>()).Select(n => new GNest
                    {
                        x = n.x, y = n.y,
                        warningRange = n.warningRange, triggerRange = n.triggerRange,
                        defenseSpawnAmount = n.defenseSpawnAmount, defenseSpawnCooldown = n.defenseSpawnCooldown,
                        spawnPoints = (n.spawnPoints ?? Array.Empty<MapImporter.SpawnDto>())
                            .Select(p => new GSpawnPt { x = p.x, y = p.y, hasBoss = p.hasBoss }).ToList(),
                        engageMinRange = n.engageMinRange, engageMaxRange = n.engageMaxRange,
                        chaseRange = n.chaseRange, leashRange = n.leashRange, engageDayOnly = n.engageDayOnly,
                        bossRecoveryDays = n.bossRecoveryDays, nestRecoveryDays = n.nestRecoveryDays, src = n,
                    }).ToList(),
                    nightSpawnPoints = (m.nightSpawnPoints ?? Array.Empty<MapImporter.CellDto>())
                        .Select(p => new Vector2Int(p.x, p.y)).ToList(),
                    trees = (m.trees ?? Array.Empty<MapImporter.CellDto>())
                        .Select(p => new Vector2Int(p.x, p.y)).ToList(),
                    src = m,
                });
            }
            if (maps.Count == 0) maps.Add(BlankMap());
        }

        public override void Undo() { hist?.Undo(); }
        public override void Redo() { hist?.Redo(); }

        public override bool DeleteSelection()
        {
            var m = M;
            if (m == null || sel == null) return false;
            if (sel.Value.type == "node" && sel.Value.i < m.nodes.Count) m.nodes.RemoveAt(sel.Value.i);
            else if (sel.Value.type == "nest" && sel.Value.i < m.nests.Count) m.nests.RemoveAt(sel.Value.i);
            else return false;
            sel = null;
            PushHist();
            RenderAll();
            return true;
        }

        // ═════════ UI ═════════

        Label statLabel, hintLabel;
        VisualElement listBox, warnBox, propsBox, paintRow, treeRow;
        VisualElement canvasHost, overlay;
        Image tileImage;
        Texture2D tileTex;
        readonly List<Button> toolButtons = new();
        readonly List<Button> tileButtons = new();
        readonly List<(Button b, VisualElement chip, Label lab, LayerInfo info)> layerButtons = new();
        Button allLayersBtn;

        public override void Build(VisualElement host)
        {
            host.style.backgroundColor = GdEnum.Bg;

            // ── m-top ──
            var top = new VisualElement();
            top.AddToClassList("gd-topbar");
            host.Add(top);
            var title = new Label("맵 에디터");
            title.AddToClassList("gd-topbar-title");
            top.Add(title);
            var small = new Label("고정 맵 · 타일 · 배치물");
            small.AddToClassList("gd-topbar-small");
            top.Add(small);
            Button Mini(string text, Action act, string tip = null)
            {
                var b = new Button(act) { text = text, tooltip = tip ?? "" };
                b.AddToClassList("gd-btn-mini");
                top.Add(b);
                return b;
            }
            Mini("↶", () => Undo(), "실행 취소 (Ctrl+Z)");
            Mini("↷", () => Redo(), "다시 실행 (Ctrl+Y)");
            Mini("화면 맞춤", () => { FitView(); RedrawCanvas(); });
            Mini("전체 채우기", () =>
            {
                var m = M; if (m == null) return;
                if (!EditorUtility.DisplayDialog("전체 채우기",
                    $"맵 전체를 {TileOf(paintTile).ko}(으)로 채웁니다.", "채운다", "취소")) return;
                for (int i = 0; i < m.tiles.Length; i++) m.tiles[i] = (byte)paintTile;
                PushHist();
                RenderAll();
            });
            top.Add(new VisualElement { style = { flexGrow = 1 } });
            hintLabel = Mono(new Label { style = { fontSize = 11.5f, color = GdEnum.Faint, marginRight = 10 } });
            top.Add(hintLabel);
            statLabel = Mono(new Label { style = { fontSize = 11.5f, color = GdEnum.Faint } });
            top.Add(statLabel);

            // ── m-main ──
            var main = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            host.Add(main);

            // m-left 260px
            var left = new ScrollView { style = { width = 260, flexShrink = 0, backgroundColor = GdEnum.Panel2,
                borderRightWidth = 1, borderRightColor = GdEnum.Line } };
            left.contentContainer.style.paddingLeft = 14;
            left.contentContainer.style.paddingRight = 14;
            left.contentContainer.style.paddingTop = 14;
            left.contentContainer.style.paddingBottom = 14;
            main.Add(left);

            listBox = new VisualElement { style = { marginBottom = 10 } };
            left.Add(listBox);
            var addRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            left.Add(addRow);
            var addB = new Button(() =>
            {
                maps.Add(BlankMap(maps.Count + 1));
                curMap = maps.Count - 1; sel = null; fitted = false;
                PushHist();
                RenderAll(); FitView(); RedrawCanvas();
            }) { text = "+ 맵" };
            addB.AddToClassList("gd-btn-mini");
            addB.AddToClassList("gd-btn-primary");
            addRow.Add(addB);
            var delB = new Button(() =>
            {
                if (maps.Count < 2) return;
                maps.RemoveAt(curMap);
                curMap = Mathf.Max(0, curMap - 1); sel = null;
                PushHist();
                RenderAll(); FitView(); RedrawCanvas();
            }) { text = "삭제" };
            delB.AddToClassList("gd-btn-mini");
            delB.AddToClassList("gd-btn-warn");
            addRow.Add(delB);

            left.Add(DividerEl());
            left.Add(SectTtl("도구"));
            var toolsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            left.Add(toolsRow);
            toolButtons.Clear();
            foreach (var (key, label) in new[] { ("paint", "지형"), ("node", "자원"), ("nest", "둥지"),
                                                 ("core", "코어"), ("night", "밤 진입로"), ("tree", "나무"),
                                                 ("select", "선택") })
            {
                string k = key;
                var b = new Button(() =>
                {
                    tool = k;
                    SyncToolButtons();
                    paintRow.style.display = tool == "paint" ? DisplayStyle.Flex : DisplayStyle.None;
                    treeRow.style.display = tool == "tree" ? DisplayStyle.Flex : DisplayStyle.None;
                    sel = null;
                    // 숨겨 둔 레이어의 도구를 골랐다면 켠다 — 안 보이는 곳에 놓는 사고를 막는다
                    if (hiddenLayers.Remove(k)) SyncLayerButtons();
                    RenderAll();
                }) { text = label };
                b.AddToClassList("gd-btn-mini");
                b.AddToClassList("gd-subtab");   // 투명 배경 · on=시안 (#m-tools 규격과 동일)
                // UITK 는 wrap 전에 shrink 부터 해서 한 줄에 짜부라진다 — 3열 격자로 고정
                b.style.flexGrow = 1;
                b.style.flexBasis = Length.Percent(30);
                toolsRow.Add(b);
                toolButtons.Add(b);
            }

            paintRow = new VisualElement();
            left.Add(paintRow);
            paintRow.Add(SectTtl("타일"));
            var tilesRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            paintRow.Add(tilesRow);
            tileButtons.Clear();
            for (int ti = 0; ti < Tiles.Length; ti++)
            {
                int t = ti;
                // 색 칩을 글자 옆에 인라인으로 — 절대 배치는 글자와 겹친다
                var b = new Button(() => { paintTile = t; SyncTileButtons(); })
                { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center,
                    justifyContent = Justify.Center, flexGrow = 1, flexBasis = Length.Percent(30) } };
                b.AddToClassList("gd-btn-mini");
                b.AddToClassList("gd-subtab");
                var chip = new VisualElement { pickingMode = PickingMode.Ignore, style = { width = 11, height = 11,
                    flexShrink = 0, marginRight = 5,
                    borderTopLeftRadius = 2, borderTopRightRadius = 2, borderBottomLeftRadius = 2, borderBottomRightRadius = 2,
                    backgroundColor = Tiles[ti].color,
                    borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = new Color(1, 1, 1, 0.15f), borderBottomColor = new Color(1, 1, 1, 0.15f),
                    borderLeftColor = new Color(1, 1, 1, 0.15f), borderRightColor = new Color(1, 1, 1, 0.15f) } };
                b.Add(chip);
                b.Add(new Label(Tiles[ti].ko) { pickingMode = PickingMode.Ignore,
                    style = { fontSize = 12, color = GdEnum.Muted } });
                tilesRow.Add(b);
                tileButtons.Add(b);
            }
            var brushRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center,
                marginTop = 8 } };
            paintRow.Add(brushRow);
            brushRow.Add(new Label("브러시") { style = { fontSize = 11.5f, color = GdEnum.Faint, marginRight = 8 } });
            var brushSlider = new SliderInt(1, 15) { value = brushSize, style = { flexGrow = 1 } };
            var brushVal = Mono(new Label(brushSize.ToString()) { style = { minWidth = 16, marginLeft = 8,
                unityTextAlign = TextAnchor.MiddleRight, color = GdEnum.Text, fontSize = 11.5f } });
            brushSlider.RegisterValueChangedCallback(e =>
            {
                brushSize = e.newValue | 1;   // 홀수만 — 브러시가 칸 중심에 온다
                brushVal.text = brushSize.ToString();
            });
            brushRow.Add(brushSlider);
            brushRow.Add(brushVal);
            // ── 나무 패널 — 손으로 찍는 것 말고 필요한 두 가지: 한 번에 깔기, 한 번에 지우기 ──
            treeRow = new VisualElement { style = { display = DisplayStyle.None } };
            left.Add(treeRow);
            treeRow.Add(SectTtl("나무"));

            VisualElement NumRow(string label, float value, string tip, Action<float> set)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row,
                    alignItems = Align.Center, marginBottom = 4 }, tooltip = tip };
                row.Add(new Label(label) { style = { fontSize = 11.5f, color = GdEnum.Faint,
                    width = 74, flexShrink = 0 } });
                var f = new FloatField { value = value, style = { flexGrow = 1 } };
                f.AddToClassList("gd-field-input");
                f.RegisterValueChangedCallback(e => set(e.newValue));
                row.Add(f);
                treeRow.Add(row);
                return row;
            }

            NumRow("간격", treeSpacing, "나무끼리 최소 간격(칸). 이 값이 곧 밀도다 — 키우면 성겨진다.",
                   v => treeSpacing = Mathf.Max(1f, v));
            NumRow("간격 흔들림", treeSpacingJitter, "간격에 주는 흔들림(칸). 0이면 어디나 똑같이 촘촘해 인공적이다.",
                   v => treeSpacingJitter = Mathf.Max(0f, v));
            NumRow("코어 여유", treeCoreClear,
                   "코어에서 이만큼(칸) 안쪽에는 심지 않는다 — 시작 공장을 펼 자리다.",
                   v => treeCoreClear = Mathf.Max(0f, v));
            NumRow("배치물 여유", treeObjectClear,
                   "광맥·둥지·밤 진입로에서 이만큼(칸) 떨어져야 심는다.",
                   v => treeObjectClear = Mathf.Max(0f, v));
            NumRow("가장자리 여유", treeEdgeClear,
                   "강·절벽 같은 못 짓는 칸에서 이만큼(칸) 떨어져야 심는다.",
                   v => treeEdgeClear = Mathf.Max(0f, v));
            NumRow("씨앗", treeSeed, "같은 맵·같은 값·같은 씨앗이면 언제나 같은 숲이 나온다.",
                   v => treeSeed = Mathf.RoundToInt(v));

            var treeBtnRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
            treeRow.Add(treeBtnRow);
            var genB = new Button(() =>
            {
                var m = M; if (m == null) return;
                if (m.trees.Count > 0 && !EditorUtility.DisplayDialog("나무 자동생성",
                    $"이미 심긴 {m.trees.Count}그루를 지우고 다시 깝니다.", "다시 깐다", "취소")) return;
                int n = GenerateTrees(m);
                PushHist();
                RenderAll();
                EditorUtility.DisplayDialog("나무 자동생성", $"{n}그루를 심었습니다.", "확인");
            }) { text = "자동생성", tooltip = "코어에서 걸어갈 수 있는 지면에 간격을 두고 깐다" };
            genB.AddToClassList("gd-btn-mini");
            genB.AddToClassList("gd-btn-primary");
            genB.style.flexGrow = 1;
            treeBtnRow.Add(genB);

            var clrB = new Button(() =>
            {
                var m = M; if (m == null || m.trees.Count == 0) return;
                if (!EditorUtility.DisplayDialog("나무 전체삭제",
                    $"이 맵의 나무 {m.trees.Count}그루를 모두 지웁니다.", "지운다", "취소")) return;
                m.trees.Clear();
                PushHist();
                RenderAll();
            }) { text = "전체삭제", tooltip = "이 맵의 나무를 모두 지운다" };
            clrB.AddToClassList("gd-btn-mini");
            clrB.AddToClassList("gd-btn-warn");
            clrB.style.flexGrow = 1;
            clrB.style.marginLeft = 5;
            treeBtnRow.Add(clrB);

            SyncToolButtons();
            SyncTileButtons();

            // ── 레이어 — 배치물만 종류별로 끄고 켠다 (지형은 바탕이라 대상이 아니다) ──
            left.Add(SectTtl("레이어"));
            var layersRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            left.Add(layersRow);
            layerButtons.Clear();
            foreach (var L in Layers)
            {
                var info = L;
                var b = new Button(() => SetLayerVis(info.key, !Vis(info.key)))
                { tooltip = $"{info.ko} 표시/숨김 — 숨기면 클릭에도 집히지 않는다",
                  style = { flexDirection = FlexDirection.Row, alignItems = Align.Center,
                    justifyContent = Justify.Center, flexGrow = 1, flexBasis = Length.Percent(46) } };
                b.AddToClassList("gd-btn-mini");
                b.AddToClassList("gd-subtab");
                var chip = new VisualElement { pickingMode = PickingMode.Ignore, style = { width = 11, height = 11,
                    flexShrink = 0, marginRight = 5,
                    borderTopLeftRadius = 2, borderTopRightRadius = 2, borderBottomLeftRadius = 2, borderBottomRightRadius = 2,
                    borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1 } };
                b.Add(chip);
                var lab = new Label(info.ko) { pickingMode = PickingMode.Ignore, style = { fontSize = 12 } };
                b.Add(lab);
                layersRow.Add(b);
                layerButtons.Add((b, chip, lab, info));
            }
            allLayersBtn = new Button(() =>
            {
                bool anyHidden = hiddenLayers.Count > 0;
                hiddenLayers.Clear();
                if (!anyHidden) { foreach (var L in Layers) hiddenLayers.Add(L.key); sel = null; }
                SyncLayerButtons();
                RenderProps(); RedrawCanvas();
            }) { text = "모두 숨김", style = { marginTop = 4 } };
            allLayersBtn.AddToClassList("gd-btn-mini");
            allLayersBtn.AddToClassList("gd-subtab");
            left.Add(allLayersBtn);
            SyncLayerButtons();

            left.Add(SectTtl("표시"));
            void Chk(string label, bool val, Action<bool> set)
            {
                // 웹 원본처럼 체크박스가 앞, 라벨이 뒤 — 라벨 클릭도 토글
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row,
                    alignItems = Align.Center, marginBottom = 4 } };
                var t = new Toggle { value = val };
                t.RegisterValueChangedCallback(e => { set(e.newValue); RedrawCanvas(); });
                row.Add(t);
                var l = new Label(label) { style = { fontSize = 12, color = GdEnum.Muted, marginLeft = 2 } };
                l.RegisterCallback<PointerDownEvent>(_ => t.value = !t.value);
                row.Add(l);
                left.Add(row);
            }
            Chk("거리 눈금", showRings, v => showRings = v);
            Chk("둥지 반경", showHalo, v => showHalo = v);
            Chk("격자", showGrid, v => showGrid = v);

            left.Add(DividerEl());
            warnBox = new VisualElement();
            left.Add(warnBox);
            left.Add(Hint(
                "지형 — 지면은 통행·건설 모두 가능, 강은 지나갈 수 있지만 짓지 못하고, " +
                "절벽은 통행 자체가 막힌다. 절벽으로 동선을 가르고 강으로 건설 자리를 제한한다.\n\n" +
                "조작 — 휠로 확대, Shift+드래그 또는 가운데 버튼으로 이동.\n" +
                "우클릭으로 지운다 — 배치물 위에서는 그것을, 빈 곳에서는 지형을 지면으로 되돌린다.\n" +
                "배치 도구로 이미 놓인 것을 누르면 새로 놓지 않고 선택·이동된다.\n\n" +
                "레이어 — 끄면 화면에서 빠지고 클릭에도 집히지 않는다. 겹쳐 놓인 것을 골라낼 때 쓴다. " +
                "숨긴 레이어의 도구를 고르면 다시 켜진다. 검증은 숨긴 것도 그대로 본다.\n\n" +
                "나무는 칸을 영구히 막는다 — 그 자리에는 아무것도 짓지 못한다. " +
                "자동생성은 코어에서 걸어갈 수 있는 지면에만 간격을 두고 깐다(절벽에 막힌 땅은 건너뛴다). " +
                "손으로 찍거나 문질러 더할 수도 있고, 우클릭으로 한 그루씩 지운다.\n\n" +
                "밤 진입로는 웨이브가 맵으로 들어오는 대문이다 — 둥지의 스폰 지점과 다르다. " +
                "둥지 것은 낮에 다가갔을 때 방어 몬스터가 튀어나오는 자리이고, 진입로는 밤에 코어로 밀려드는 길이다.\n\n" +
                "검증이 잡는 것 — 둥지에서 코어로 가는 길이 막혔는지, 자원이 절벽에 갇혔는지, " +
                "Ring 1 에 철이 있는지, 둥지가 없는 방향이 있는지."));

            // m-center — 캔버스
            canvasHost = new VisualElement { style = { flexGrow = 1, minWidth = 120, overflow = Overflow.Hidden,
                backgroundColor = GdEnum.FromHex("#080D16") } };
            main.Add(canvasHost);
            tileImage = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill,
                style = { position = Position.Absolute } };
            canvasHost.Add(tileImage);
            overlay = new VisualElement { pickingMode = PickingMode.Ignore, style = { position = Position.Absolute,
                left = 0, right = 0, top = 0, bottom = 0 } };
            overlay.generateVisualContent += DrawOverlay;
            canvasHost.Add(overlay);
            RegisterCanvasInput();
            canvasHost.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!fitted && canvasHost.contentRect.width > 20) { FitView(); fitted = true; }
                RedrawCanvas();
            });

            // m-right 300px
            var right = new ScrollView { style = { width = 300, flexShrink = 0, backgroundColor = GdEnum.Panel2,
                borderLeftWidth = 1, borderLeftColor = GdEnum.Line } };
            right.contentContainer.style.paddingLeft = 14;
            right.contentContainer.style.paddingRight = 14;
            right.contentContainer.style.paddingTop = 14;
            right.contentContainer.style.paddingBottom = 14;
            main.Add(right);
            right.Add(H3("속성"));
            propsBox = new VisualElement();
            right.Add(propsBox);

            RenderAll();
        }

        static Label SectTtl(string text)
        {
            var l = new Label(text.ToUpperInvariant()) { style = { fontSize = 10, letterSpacing = 1.4f,
                color = GdEnum.Faint, marginTop = 12, marginBottom = 6 } };
            return l;
        }

        void SyncToolButtons()
        {
            string[] keys = { "paint", "node", "nest", "core", "night", "tree", "select" };
            for (int i = 0; i < toolButtons.Count; i++)
                toolButtons[i].EnableInClassList("gd-subtab--on", keys[i] == tool);
        }

        void SyncTileButtons()
        {
            for (int i = 0; i < tileButtons.Count; i++)
                tileButtons[i].EnableInClassList("gd-subtab--on", i == paintTile);
        }

        void SetLayerVis(string layer, bool on)
        {
            if (on) hiddenLayers.Remove(layer);
            else hiddenLayers.Add(layer);
            // 숨긴 레이어의 것을 잡고 있으면 놓는다 — 안 보이는 걸 드래그하고 있을 수는 없다
            if (!on && sel != null && sel.Value.type == layer) sel = null;
            SyncLayerButtons();
            RenderProps(); RedrawCanvas();
        }

        void SyncLayerButtons()
        {
            foreach (var (b, chip, lab, info) in layerButtons)
            {
                bool on = Vis(info.key);
                b.EnableInClassList("gd-subtab--on", on);
                lab.style.color = on ? GdEnum.Text : GdEnum.Faint;
                var c = info.color;
                c.a = on ? 1f : 0.3f;
                chip.style.backgroundColor = info.filled ? c : Color.clear;
                chip.style.borderTopColor = c; chip.style.borderBottomColor = c;
                chip.style.borderLeftColor = c; chip.style.borderRightColor = c;
            }
            if (allLayersBtn != null) allLayersBtn.text = hiddenLayers.Count > 0 ? "모두 보기" : "모두 숨김";
        }

        void RenderAll() { RenderList(); RenderProps(); RenderWarn(); RebuildTileTex(); RedrawCanvas(); }

        // ── 목록 ──
        void RenderList()
        {
            listBox.Clear();
            for (int i = 0; i < maps.Count; i++)
            {
                var m = maps[i];
                int mi = i;
                var item = new VisualElement();
                item.AddToClassList("gd-bitem");
                item.EnableInClassList("gd-bitem--sel", i == curMap);
                var nm = new Label(string.IsNullOrEmpty(m.displayName) ? "(이름 없음)" : m.displayName);
                nm.AddToClassList("gd-bitem-nm");
                item.Add(nm);
                var kd = new Label($"{m.width}×{m.height}");
                kd.AddToClassList("gd-bitem-kd");
                Mono(kd);
                item.Add(kd);
                item.RegisterCallback<PointerDownEvent>(_ =>
                {
                    curMap = mi; sel = null;
                    RenderAll(); FitView(); RedrawCanvas();
                });
                listBox.Add(item);
            }
        }

        void RenderStat()
        {
            var m = M;
            statLabel.text = m != null
                ? $"{m.width}×{m.height} · 노드 {m.nodes.Count} · 둥지 {m.nests.Count} · 나무 {m.trees.Count}"
                : "";
        }

        // ── 속성 패널 ──
        void RenderProps()
        {
            propsBox.Clear();
            RenderStat();
            var m = M;
            if (m == null) return;

            if (sel != null && sel.Value.type == "node" && sel.Value.i < m.nodes.Count)
            { RenderNodeProps(m, m.nodes[sel.Value.i]); return; }
            if (sel != null && sel.Value.type == "nest" && sel.Value.i < m.nests.Count)
            { RenderNestProps(m, m.nests[sel.Value.i]); return; }

            propsBox.Add(MTitle("맵"));

            var idRow = new VisualElement();
            idRow.AddToClassList("gd-idrow");
            var pfx = Mono(new Label("Map:"));
            pfx.AddToClassList("gd-idrow-pfx");
            idRow.Add(pfx);
            string bare = (m.id ?? "").StartsWith("Map:") ? m.id.Substring(4) : m.id ?? "";
            var idF = Mono(new TextField { value = bare });
            idF.RegisterValueChangedCallback(e =>
            {
                var clean = new string(e.newValue.Where(c => char.IsLetterOrDigit(c) || c == '_' || (c >= '가' && c <= '힣')).ToArray());
                m.id = string.IsNullOrEmpty(clean) ? "" : "Map:" + clean;
                RenderWarn();
            });
            HookHist(idF);
            idRow.Add(idF);
            propsBox.Add(Field2("Id", idRow));

            var nameF = new TextField { value = m.displayName };
            nameF.RegisterValueChangedCallback(e => { m.displayName = e.newValue; RenderList(); RenderWarn(); });
            HookHist(nameF);
            propsBox.Add(Field2("이름", nameF));

            var descF = new TextField { value = m.description, multiline = true };
            descF.AddToClassList("gd-multiline");
            descF.verticalScrollerVisibility = ScrollerVisibility.Auto;
            descF.RegisterValueChangedCallback(e => m.description = e.newValue);
            HookHist(descF);
            propsBox.Add(Field2("설명", descF));

            // 크기 — 홀수만. 겹치는 부분은 남기고, 가운데 있던 코어는 새 가운데로 옮긴다
            propsBox.Add(GroupTitle("크기"));
            var sizeGrid = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            propsBox.Add(sizeGrid);
            sizeGrid.Add(GCell("가로", m.width, v => ResizeMap(m, Odd((int)v), m.height),
                "홀수만 — 코어 3×3 이 정확히 가운데 오려면 중심 칸이 하나여야 한다"));
            sizeGrid.Add(GCell("세로", m.height, v => ResizeMap(m, m.width, Odd((int)v)), "홀수만", last: true));

            propsBox.Add(RingStat(m));
        }

        void ResizeMap(GMap m, int w, int h)
        {
            if (w == m.width && h == m.height) return;
            var t = new byte[w * h];
            for (int y = 0; y < Mathf.Min(h, m.height); y++)
                for (int x = 0; x < Mathf.Min(w, m.width); x++)
                    t[y * w + x] = m.tiles[y * m.width + x];
            bool wasCentered = m.coreX == (m.width >> 1) - 1 && m.coreY == (m.height >> 1) - 1;
            m.width = w; m.height = h; m.tiles = t;
            if (wasCentered) { m.coreX = (w >> 1) - 1; m.coreY = (h >> 1) - 1; }
            else { m.coreX = Mathf.Clamp(m.coreX, 0, w - 3); m.coreY = Mathf.Clamp(m.coreY, 0, h - 3); }
            PushHist();
            RenderAll(); FitView(); RedrawCanvas();
        }

        void RenderNodeProps(GMap m, GMapNode n)
        {
            propsBox.Add(MTitle("자원 노드"));
            var kindChoices = NodeKinds.Select(k => k.ko).ToList();
            var kindD = new DropdownField(kindChoices,
                Mathf.Max(0, Array.FindIndex(NodeKinds, k => k.item == n.item)));
            kindD.RegisterValueChangedCallback(e =>
            {
                int i = kindChoices.IndexOf(e.newValue);
                if (i >= 0) { n.item = NodeKinds[i].item; PushHist(); RenderAll(); }
            });
            propsBox.Add(Field2("종류", kindD));

            var sizeChoices = new List<string> { "1×1", "2×2", "3×3" };
            var sizeD = new DropdownField(sizeChoices, Mathf.Clamp(n.size - 1, 0, 2));
            sizeD.RegisterValueChangedCallback(e =>
            {
                n.size = sizeChoices.IndexOf(e.newValue) + 1;
                PushHist(); RenderAll();
            });
            propsBox.Add(Field2("크기", sizeD));

            propsBox.Add(Field2("채굴 간격", NumF(n.extractInterval, v => n.extractInterval = Mathf.Max(0.1f, v),
                "배율 1 기준 1개당 초. 값이 클수록 캐기 어려운 광맥")));
            propsBox.Add(Field2("최대 재고", IntF(n.maxStock, v => n.maxStock = Mathf.Max(1, v))));
            propsBox.Add(Field2("위치", XyRow(m, () => (n.x, n.y), (x, y) => { n.x = x; n.y = y; })));

            var del = new Button(() => { m.nodes.Remove(n); sel = null; PushHist(); RenderAll(); })
            { text = "삭제", style = { marginTop = 10, alignSelf = Align.FlexStart } };
            del.AddToClassList("gd-btn-mini");
            del.AddToClassList("gd-btn-warn");
            propsBox.Add(del);
        }

        void RenderNestProps(GMap m, GNest n)
        {
            propsBox.Add(MTitle("몬스터 둥지"));
            propsBox.Add(Field2("위치", XyRow(m, () => (n.x, n.y), (x, y) => { n.x = x; n.y = y; })));

            propsBox.Add(GroupTitle("방어 반응 · 낮에 접근했을 때"));
            var g1 = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            propsBox.Add(g1);
            g1.Add(GCell("경고 반경", n.warningRange, v => { n.warningRange = Mathf.Max(1, v); RedrawCanvas(); },
                "플레이어가 이 안에 들어오면 경고가 뜬다"));
            g1.Add(GCell("진입 반경", n.triggerRange, v => { n.triggerRange = Mathf.Max(1, v); RedrawCanvas(); },
                "이 안으로 들어오면 방어 몬스터가 튀어나온다", last: true));
            var g2 = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
            propsBox.Add(g2);
            g2.Add(GCell("스폰 수", n.defenseSpawnAmount, v => n.defenseSpawnAmount = Mathf.Max(1, (int)v),
                "한 번에 나오는 방어 몬스터 수"));
            g2.Add(GCell("쿨타임", n.defenseSpawnCooldown, v => n.defenseSpawnCooldown = Mathf.Max(1, v),
                "다시 나오기까지 걸리는 시간(초)", last: true));

            propsBox.Add(GroupTitle("스폰 지점 · 밤 웨이브가 나오는 자리"));
            for (int i = 0; i < n.spawnPoints.Count; i++)
            {
                var p = n.spawnPoints[i];
                int pi = i;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center,
                    marginBottom = 4 } };
                propsBox.Add(row);
                var no = Mono(new Label($"#{i + 1}") { style = { width = 24, fontSize = 10.5f, color = GdEnum.Faint } });
                row.Add(no);
                var xF = new IntegerField { value = p.x, tooltip = "둥지 기준 상대 좌표",
                    style = { flexGrow = 1, flexBasis = 0 } };
                xF.AddToClassList("gd-field-input");
                xF.RegisterValueChangedCallback(e => { p.x = e.newValue; RedrawCanvas(); });
                HookHist(xF);
                row.Add(xF);
                var yF = new IntegerField { value = p.y, style = { flexGrow = 1, flexBasis = 0, marginLeft = 5 } };
                yF.AddToClassList("gd-field-input");
                yF.RegisterValueChangedCallback(e => { p.y = e.newValue; RedrawCanvas(); });
                HookHist(yF);
                row.Add(yF);
                var bossT = new Toggle("보스") { value = p.hasBoss, tooltip = "이 지점에 보스가 붙는다",
                    style = { marginLeft = 5, fontSize = 10.5f } };
                bossT.RegisterValueChangedCallback(e => { p.hasBoss = e.newValue; PushHist(); RedrawCanvas(); });
                row.Add(bossT);
                var x = new Label("✕") { style = { color = GdEnum.Faint, fontSize = 12, paddingLeft = 4 } };
                x.RegisterCallback<PointerDownEvent>(_ =>
                {
                    if (n.spawnPoints.Count < 2) return;
                    n.spawnPoints.RemoveAt(pi);
                    PushHist(); RenderAll();
                });
                row.Add(x);
            }
            var addSp = new Button(() =>
            {
                n.spawnPoints.Add(new GSpawnPt { x = 2, y = 2 });
                PushHist(); RenderAll();
            }) { text = "+ 지점", style = { marginTop = 4, alignSelf = Align.FlexStart } };
            addSp.AddToClassList("gd-btn-mini");
            propsBox.Add(addSp);

            propsBox.Add(GroupTitle("교전 · 언제 얼마나 달려드는가"));
            var g3 = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            propsBox.Add(g3);
            g3.Add(GCell("최소", n.engageMinRange, v => { n.engageMinRange = Mathf.Max(0, v); },
                "이보다 가까우면 추가 스폰을 멈춘다 — 코앞에서 무한히 쏟아지지 않게"));
            g3.Add(GCell("최대", n.engageMaxRange, v => { n.engageMaxRange = Mathf.Max(0, v); },
                "이 밖이면 아예 반응하지 않는다"));
            g3.Add(GCell("추격", n.chaseRange, v => { n.chaseRange = Mathf.Max(0, v); },
                "이미 교전한 몬스터가 쫓아오는 한계", last: true));
            var g4 = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6,
                alignItems = Align.FlexEnd } };
            propsBox.Add(g4);
            g4.Add(GCell("귀환", n.leashRange, v => { n.leashRange = Mathf.Max(0, v); },
                "이보다 멀어지면 둥지로 돌아간다"));
            var dayT = new Toggle("낮에만 · 밤에는 웨이브가 주도") { value = n.engageDayOnly,
                style = { marginLeft = 6, fontSize = 10.5f, color = GdEnum.Faint } };
            dayT.RegisterValueChangedCallback(e => { n.engageDayOnly = e.newValue; PushHist(); });
            g4.Add(dayT);

            propsBox.Add(GroupTitle("복구 · 부순 뒤 다시 서기까지"));
            var g5 = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            propsBox.Add(g5);
            g5.Add(GCell("보스(일)", n.bossRecoveryDays, v => n.bossRecoveryDays = Mathf.Max(0, (int)v)));
            g5.Add(GCell("둥지(일)", n.nestRecoveryDays, v => n.nestRecoveryDays = Mathf.Max(0, (int)v), last: true));

            var del = new Button(() => { m.nests.Remove(n); sel = null; PushHist(); RenderAll(); })
            { text = "둥지 삭제", style = { marginTop = 14, alignSelf = Align.FlexStart } };
            del.AddToClassList("gd-btn-mini");
            del.AddToClassList("gd-btn-warn");
            propsBox.Add(del);
        }

        // ── 폼 조각 ──
        static Label MTitle(string text)
        {
            return new Label(text.ToUpperInvariant()) { style = { fontSize = 11, letterSpacing = 1.3f,
                color = GdEnum.Accent, marginBottom = 10, paddingBottom = 5,
                borderBottomWidth = 1, borderBottomColor = new Color(0.31f, 0.847f, 0.878f, 0.22f) } };
        }

        FloatField NumF(float value, Action<float> set, string tooltip = null)
        {
            var f = new FloatField { value = value, tooltip = tooltip ?? "" };
            f.RegisterValueChangedCallback(e => set(e.newValue));
            HookHist(f);
            return f;
        }

        IntegerField IntF(int value, Action<int> set, string tooltip = null)
        {
            var f = new IntegerField { value = value, tooltip = tooltip ?? "" };
            f.RegisterValueChangedCallback(e => set(e.newValue));
            HookHist(f);
            return f;
        }

        // .gcell — 라벨 위 + 좁은 입력 (gungrid 한 칸)
        VisualElement GCell(string label, float value, Action<float> set, string tooltip = null, bool last = false)
        {
            var box = new VisualElement { style = { flexGrow = 1, flexBasis = 0 } };
            if (!last) box.style.marginRight = 6;
            if (tooltip != null) box.tooltip = tooltip;
            box.Add(new Label(label) { style = { fontSize = 10.5f, color = GdEnum.Faint, marginBottom = 3 } });
            var f = new FloatField { value = value };
            f.AddToClassList("gd-field-input");
            f.RegisterValueChangedCallback(e => set(e.newValue));
            HookHist(f);
            box.Add(f);
            return box;
        }

        // .xyrow — x · y · 코어 거리
        VisualElement XyRow(GMap m, Func<(int x, int y)> get, Action<int, int> set)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var (cx, cy) = (m.coreX + 1.5f, m.coreY + 1.5f);
            var distL = Mono(new Label { tooltip = "코어에서의 거리",
                style = { fontSize = 11, color = GdEnum.Faint, marginLeft = 5, flexShrink = 0 } });
            void RefreshDist() { var (x, y) = get(); distL.text = $"{Mathf.Round(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)))}칸"; }
            var xF = new IntegerField { value = get().x, style = { flexGrow = 1, flexBasis = 0 } };
            xF.AddToClassList("gd-field-input");
            xF.RegisterValueChangedCallback(e =>
            {
                set(Mathf.Clamp(e.newValue, 0, m.width - 1), get().y);
                RefreshDist(); RedrawCanvas();
            });
            HookHist(xF);
            row.Add(xF);
            var yF = new IntegerField { value = get().y, style = { flexGrow = 1, flexBasis = 0, marginLeft = 5 } };
            yF.AddToClassList("gd-field-input");
            yF.RegisterValueChangedCallback(e =>
            {
                set(get().x, Mathf.Clamp(e.newValue, 0, m.height - 1));
                RefreshDist(); RedrawCanvas();
            });
            HookHist(yF);
            row.Add(yF);
            RefreshDist();
            row.Add(distL);
            return row;
        }

        void HookHist(VisualElement f) => f.RegisterCallback<FocusOutEvent>(_ => PushHist());

        // Ring 별 자원 분포 — 테크트리 진행과 직결되므로 눈으로 확인한다
        VisualElement RingStat(GMap m)
        {
            var box = new VisualElement();
            box.Add(GroupTitle("분포"));
            float cx = m.coreX + 1.5f, cy = m.coreY + 1.5f;
            var g = GuideRings(m);
            int RingOfXY(int x, int y)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                return d <= g[0] ? 0 : d <= g[1] ? 1 : d <= g[2] ? 2 : 3;
            }
            var head = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            box.Add(head);
            Label Cell(string text, Color c, bool first = false)
            {
                var l = Mono(new Label(text) { style = { fontSize = 11.5f, color = c,
                    unityTextAlign = first ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter,
                    flexGrow = 1, flexBasis = 0, paddingTop = 3, paddingBottom = 3 } });
                return l;
            }
            head.Add(Cell("", GdEnum.Faint, first: true));
            head.Add(Cell("철", GdEnum.Faint));
            head.Add(Cell("구리", GdEnum.Faint));
            head.Add(Cell("크리", GdEnum.Faint));
            head.Add(Cell("둥지", GdEnum.Faint));
            string[] names = { "안쪽", "중간", "바깥", "모서리" };
            for (int r = 0; r < 4; r++)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row,
                    borderTopWidth = 1, borderTopColor = GdEnum.FromHex("#1A2740") } };
                box.Add(row);
                row.Add(Cell(names[r], GdEnum.Muted, first: true));
                for (int k = 0; k < NodeKinds.Length; k++)
                {
                    int cnt = m.nodes.Count(n => n.item == NodeKinds[k].item && RingOfXY(n.x, n.y) == r);
                    row.Add(Cell(cnt > 0 ? cnt.ToString() : "·", cnt > 0 ? NodeKinds[k].color : GdEnum.Faint));
                }
                int nest = m.nests.Count(n => RingOfXY(n.x, n.y) == r);
                row.Add(Cell(nest > 0 ? nest.ToString() : "·", nest > 0 ? GdEnum.Warn : GdEnum.Faint));
            }
            return box;
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
            win.RefreshSharedStat();
        }

        // ═════════ 검증 (원본 validate 전체) ═════════
        // 고정 맵에서 가장 무서운 건 "갈 수 없는 곳"이다 — 플로우필드는 도달 불가
        // 목표를 처리하지 못하고, 벽 뒤 광맥은 없는 것과 같다.

        byte[] Reachable(GMap m)
        {
            var seen = new byte[m.width * m.height];
            var q = new List<int>();
            for (int y = m.coreY; y < m.coreY + 3; y++)
                for (int x = m.coreX; x < m.coreX + 3; x++)
                    if (InB(m, x, y)) { seen[Idx(m, x, y)] = 1; q.Add(x); q.Add(y); }
            for (int p = 0; p < q.Count; p += 2)
            {
                int x = q[p], y = q[p + 1];
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (!InB(m, nx, ny)) continue;
                    int id = Idx(m, nx, ny);
                    if (seen[id] != 0 || !TileOf(m.tiles[id]).walk) continue;
                    seen[id] = 1; q.Add(nx); q.Add(ny);
                }
            }
            return seen;
        }

        List<string> Validate()
        {
            var m = M;
            var outp = new List<string>();
            if (m == null) return outp;

            // identity
            if (string.IsNullOrEmpty((m.id ?? "").Replace("Map:", "")))
                outp.Add("id 가 비어 있습니다 — 임포트의 기본 키입니다");
            else if (maps.Count(x => x.id == m.id) > 1) outp.Add($"id 중복 — {m.id}");
            if (string.IsNullOrWhiteSpace(m.displayName)) outp.Add("displayName 이 비어 있습니다 — 임포터가 거부합니다");

            TileInfo TileAt(int x, int y) => InB(m, x, y) ? TileOf(m.tiles[Idx(m, x, y)]) : null;
            float cx = m.coreX + 1.5f, cy = m.coreY + 1.5f;
            float Dist(float x, float y) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            float R = ROf(m);

            // 코어 — 3×3 이 전부 건설 가능해야 한다. 강도 안 된다
            string coreBad = null;
            for (int y = m.coreY; y < m.coreY + 3 && coreBad == null; y++)
                for (int x = m.coreX; x < m.coreX + 3 && coreBad == null; x++)
                {
                    var t = TileAt(x, y);
                    if (t == null) coreBad = "코어가 맵 밖으로 나갔습니다";
                    else if (!t.build) coreBad = $"코어 자리에 {t.ko}이(가) 있습니다 — 지을 수 없습니다";
                }
            if (coreBad != null) outp.Add(coreBad);

            var seen = Reachable(m);

            // 자원 노드 — 겹침·타일 종류·도달성을 모두 본다
            var nodeCells = new Dictionary<(int, int), int>();
            for (int i = 0; i < m.nodes.Count; i++)
            {
                var n = m.nodes[i];
                var tag = $"자원 #{i + 1}";
                if (!InB(m, n.x, n.y) || !InB(m, n.x + n.size - 1, n.y + n.size - 1))
                { outp.Add($"{tag} 이(가) 맵 밖에 있습니다"); continue; }
                if (seen[Idx(m, n.x, n.y)] == 0) outp.Add($"{tag} 이(가) 절벽에 갇혔습니다 — 캘 수 없습니다");
                string onBad = null;
                for (int y = n.y; y < n.y + n.size; y++)
                    for (int x = n.x; x < n.x + n.size; x++)
                    {
                        var t = TileAt(x, y);
                        if (t != null && !t.build && onBad == null) onBad = t.ko;
                        if (nodeCells.TryGetValue((x, y), out var other))
                            outp.Add($"{tag} 이(가) 자원 #{other + 1} 과 겹칩니다");
                        else nodeCells[(x, y)] = i;
                    }
                if (onBad != null) outp.Add($"{tag} 이(가) {onBad} 위에 있습니다 — 채굴기를 놓을 수 없습니다");
                if (Dist(n.x, n.y) > R) outp.Add($"{tag} 이(가) 맵 모서리 쪽에 치우쳐 있습니다");
            }

            // 둥지 — 스폰 지점까지 확인한다. 막힌 자리면 웨이브가 통째로 안 나온다
            var nestCells = new HashSet<(int, int)>();
            for (int i = 0; i < m.nests.Count; i++)
            {
                var n = m.nests[i];
                var tag = $"둥지 #{i + 1}";
                if (!InB(m, n.x, n.y)) { outp.Add($"{tag} 이(가) 맵 밖에 있습니다"); continue; }
                if (seen[Idx(m, n.x, n.y)] == 0) outp.Add($"{tag} 에서 코어로 갈 수 없습니다 — 몬스터가 영영 오지 못합니다");
                var t = TileAt(n.x, n.y);
                if (t != null && !t.walk) outp.Add($"{tag} 이(가) {t.ko} 위에 있습니다");
                if (!nestCells.Add((n.x, n.y))) outp.Add($"{tag} 이(가) 다른 둥지와 같은 칸에 있습니다");
                if (Dist(n.x, n.y) <= R / 3) outp.Add($"{tag} 이(가) 코어에 너무 가깝습니다 — 시작하자마자 몰립니다");
                for (int j = 0; j < n.spawnPoints.Count; j++)
                {
                    var p = n.spawnPoints[j];
                    int sx = n.x + p.x, sy = n.y + p.y;
                    var st = TileAt(sx, sy);
                    if (st == null) outp.Add($"{tag} 스폰 #{j + 1} 이(가) 맵 밖입니다");
                    else if (!st.walk) outp.Add($"{tag} 스폰 #{j + 1} 이(가) {st.ko} 위입니다 — 나오지 못합니다");
                    else if (seen[Idx(m, sx, sy)] == 0) outp.Add($"{tag} 스폰 #{j + 1} 에서 코어로 갈 수 없습니다");
                }
                if (n.triggerRange > n.warningRange)
                    outp.Add($"{tag} — 진입 반경이 경고 반경보다 큽니다. 경고 없이 튀어나옵니다");
                // 교전 구역은 안쪽부터 넓어져야 한다: 최소 < 최대 < 추격 < 귀환
                if (n.engageMaxRange > 0)
                {
                    if (n.engageMinRange >= n.engageMaxRange) outp.Add($"{tag} — 교전 최소가 최대보다 큽니다");
                    if (n.chaseRange < n.engageMaxRange)
                        outp.Add($"{tag} — 추격 범위가 교전 최대보다 좁습니다. 붙자마자 돌아섭니다");
                    if (n.leashRange < n.chaseRange) outp.Add($"{tag} — 귀환 거리가 추격보다 짧습니다");
                }
            }

            // 밤 진입로 — 웨이브가 들어오는 대문. 없으면 밤이 오지 않는다
            var nightSeen = new HashSet<(int, int)>();
            for (int i = 0; i < m.nightSpawnPoints.Count; i++)
            {
                var p = m.nightSpawnPoints[i];
                var tag = $"밤 진입로 #{i + 1}";
                var t = TileAt(p.x, p.y);
                if (t == null) { outp.Add($"{tag} 이(가) 맵 밖입니다"); continue; }
                if (!t.walk) outp.Add($"{tag} 이(가) {t.ko} 위입니다 — 들어오지 못합니다");
                else if (seen[Idx(m, p.x, p.y)] == 0) outp.Add($"{tag} 에서 코어로 갈 수 없습니다");
                if (!nightSeen.Add((p.x, p.y))) outp.Add($"{tag} 이(가) 다른 진입로와 겹칩니다");
                if (Dist(p.x, p.y) < R * 0.6f) outp.Add($"{tag} 이(가) 코어에 가깝습니다 — 진입로는 가장자리에 둔다");
            }
            if (m.nightSpawnPoints.Count == 0)
                outp.Add("밤 진입로가 없습니다 — 웨이브가 맵으로 들어올 자리가 없습니다");

            // 나무 — 칸을 영구히 막으므로 잘못 놓이면 그 자리가 통째로 죽는다.
            // 개수는 수백이라 낱개로 알리지 않고 한 줄로 묶는다.
            int treeBad = 0, treeOnCore = 0, treeDup = 0;
            var treeSeen = new HashSet<(int, int)>();
            foreach (var t in m.trees)
            {
                var tt = TileAt(t.x, t.y);
                if (tt == null || !tt.build) treeBad++;
                if (t.x >= m.coreX && t.x < m.coreX + 3 && t.y >= m.coreY && t.y < m.coreY + 3) treeOnCore++;
                if (!treeSeen.Add((t.x, t.y))) treeDup++;
            }
            if (treeBad > 0) outp.Add($"나무 {treeBad}그루가 강·절벽·맵 밖에 있습니다 — 세울 수 없습니다");
            if (treeOnCore > 0) outp.Add($"나무 {treeOnCore}그루가 코어 자리에 있습니다 — 코어를 세우지 못합니다");
            if (treeDup > 0) outp.Add($"나무 {treeDup}그루가 다른 나무와 같은 칸에 있습니다");

            if (m.nests.Count == 0) outp.Add("둥지가 없습니다 — 낮에 칠 대상이 없습니다");
            if (m.nodes.Count == 0) outp.Add("자원 노드가 없습니다");

            // 계통별 Ring 분포 — 철은 안쪽, 크리스탈은 바깥이어야 테크트리가 성립한다
            var iron = m.nodes.Where(n => n.item == "Item:IronOre").ToList();
            var crys = m.nodes.Where(n => n.item == "Item:CrystalOre").ToList();
            if (iron.Count > 0 && iron.Min(n => Dist(n.x, n.y)) > R / 3)
                outp.Add("코어 근처에 철광석이 없습니다 — 초반에 아무것도 못 만듭니다");
            if (crys.Count > 0 && crys.Max(n => Dist(n.x, n.y)) < R / 3)
                outp.Add("크리스탈이 전부 코어 근처에 있습니다 — 확장할 이유가 사라집니다");

            // 방위 균형 — 한쪽만 위험하면 그쪽에만 포탑을 몰아 짓고 끝난다
            if (m.nests.Count >= 4)
            {
                var q = new int[4];
                foreach (var n in m.nests)
                {
                    float a = Mathf.Atan2(n.y - cy, n.x - cx);
                    q[(int)(((a + Mathf.PI) / (Mathf.PI / 2)) % 4)]++;
                }
                if (q.Any(v => v == 0)) outp.Add("둥지가 없는 방향이 있습니다");
            }
            return outp;
        }

        // ═════════ 캔버스 ═════════

        void FitView()
        {
            var m = M;
            if (m == null || canvasHost == null) return;
            var r = canvasHost.contentRect;
            if (r.width < 10 || r.height < 10) return;
            viewK = Mathf.Min(r.width / m.width, r.height / m.height) * 0.92f;
            viewX = (r.width - m.width * viewK) / 2;
            viewY = (r.height - m.height * viewK) / 2;
        }

        (int x, int y) ToCell(Vector2 p) =>
            (Mathf.FloorToInt((p.x - viewX) / viewK), Mathf.FloorToInt((p.y - viewY) / viewK));

        // 타일 텍스처 — 1타일 = 1픽셀. 포인트 필터로 확대해도 칸이 또렷하다
        void RebuildTileTex()
        {
            var m = M;
            if (m == null || tileImage == null) return;
            if (tileTex == null || tileTex.width != m.width || tileTex.height != m.height)
            {
                if (tileTex != null) UnityEngine.Object.DestroyImmediate(tileTex);
                tileTex = new Texture2D(m.width, m.height, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                tileImage.image = tileTex;
            }
            var px = new Color32[m.width * m.height];
            for (int y = 0; y < m.height; y++)
                for (int x = 0; x < m.width; x++)
                    px[(m.height - 1 - y) * m.width + x] = TileOf(m.tiles[y * m.width + x]).color;   // 화면 y ↓
            tileTex.SetPixels32(px);
            tileTex.Apply(false);
        }

        void UpdateTileCells(GMap m, int x0, int y0, int x1, int y1)
        {
            if (tileTex == null) return;
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(m.height - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(m.width - 1, x1); x++)
                    tileTex.SetPixel(x, m.height - 1 - y, TileOf(m.tiles[Idx(m, x, y)]).color);
            tileTex.Apply(false);
        }

        void RedrawCanvas()
        {
            var m = M;
            if (m == null || tileImage == null) return;
            tileImage.style.left = viewX;
            tileImage.style.top = viewY;
            tileImage.style.width = m.width * viewK;
            tileImage.style.height = m.height * viewK;
            overlay.MarkDirtyRepaint();
        }

        // ── 오버레이 — 눈금 · 배치물 · 코어 · 브러시 (Painter2D) ──
        (int x, int y)? hoverCell;

        void DrawOverlay(MeshGenerationContext ctx)
        {
            var m = M;
            if (m == null) return;
            var p = ctx.painter2D;
            float k = viewK;
            Vector2 P(float x, float y) => new(viewX + x * k, viewY + y * k);
            void FillRect(float x, float y, float w, float h, Color c)
            {
                p.fillColor = c;
                p.BeginPath();
                p.MoveTo(P(x, y)); p.LineTo(P(x + w, y)); p.LineTo(P(x + w, y + h)); p.LineTo(P(x, y + h));
                p.ClosePath();
                p.Fill();
            }
            void StrokeRect(float x, float y, float w, float h, Color c, float lw)
            {
                p.strokeColor = c; p.lineWidth = lw;
                p.BeginPath();
                p.MoveTo(P(x, y)); p.LineTo(P(x + w, y)); p.LineTo(P(x + w, y + h)); p.LineTo(P(x, y + h));
                p.ClosePath();
                p.Stroke();
            }

            var rect = overlay.contentRect;
            int vx0 = Mathf.Max(0, Mathf.FloorToInt(-viewX / k)), vx1 = Mathf.Min(m.width, Mathf.CeilToInt((rect.width - viewX) / k));
            int vy0 = Mathf.Max(0, Mathf.FloorToInt(-viewY / k)), vy1 = Mathf.Min(m.height, Mathf.CeilToInt((rect.height - viewY) / k));

            // 격자 — 확대했을 때만
            if (showGrid && k >= 6)
            {
                p.strokeColor = new Color(1, 1, 1, 0.05f); p.lineWidth = 1;
                p.BeginPath();
                for (int x = vx0; x <= vx1; x++) { p.MoveTo(P(x, vy0)); p.LineTo(P(x, vy1)); }
                for (int y = vy0; y <= vy1; y++) { p.MoveTo(P(vx0, y)); p.LineTo(P(vx1, y)); }
                p.Stroke();
            }

            float ccx = m.coreX + 1.5f, ccy = m.coreY + 1.5f;

            // 거리 눈금 — 점선 원 (호를 끊어 그린다)
            if (showRings)
            {
                var ringCols = new[] { new Color(0.365f, 0.827f, 0.62f, 0.35f),
                    new Color(0.31f, 0.847f, 0.878f, 0.3f), new Color(0.706f, 0.549f, 1f, 0.3f) };
                var rings = GuideRings(m);
                for (int i = 0; i < rings.Length; i++)
                {
                    p.strokeColor = ringCols[i]; p.lineWidth = 1.5f;
                    float rr = rings[i] * k;
                    int segs = Mathf.Clamp(Mathf.RoundToInt(rr / 9), 16, 96);
                    for (int s2 = 0; s2 < segs; s2 += 2)
                    {
                        float a0 = s2 / (float)segs * Mathf.PI * 2, a1 = (s2 + 1) / (float)segs * Mathf.PI * 2;
                        p.BeginPath();
                        p.Arc(P(ccx, ccy), rr, a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg);
                        p.Stroke();
                    }
                }
            }

            // 둥지 반경(halo) — 노드 아래 깔린다. 반경은 둥지의 것이라 둥지를 숨기면 같이 빠진다
            if (showHalo && Vis("nest"))
                foreach (var n in m.nests)
                {
                    var c = P(n.x + 0.5f, n.y + 0.5f);
                    p.fillColor = new Color(1f, 0.365f, 0.451f, 0.07f);
                    p.BeginPath(); p.Arc(c, n.warningRange * k, 0, 360); p.Fill();
                    p.fillColor = new Color(1f, 0.365f, 0.451f, 0.13f);   // 진입 반경 — 여기 들어가면 튀어나온다
                    p.BeginPath(); p.Arc(c, n.triggerRange * k, 0, 360); p.Fill();
                }

            // 자원 노드
            if (Vis("node"))
                for (int i = 0; i < m.nodes.Count; i++)
                {
                    var n = m.nodes[i];
                    var kind = NodeKinds.FirstOrDefault(q => q.item == n.item);
                    FillRect(n.x, n.y, n.size, n.size, kind.item != null ? kind.color : NodeKinds[0].color);
                    if (sel != null && sel.Value.type == "node" && sel.Value.i == i)
                        StrokeRect(n.x, n.y, n.size, n.size, Color.white, 2);
                }

            // 둥지 본체 + 스폰 지점
            if (Vis("nest"))
                for (int i = 0; i < m.nests.Count; i++)
                {
                    var n = m.nests[i];
                    bool boss = n.spawnPoints.Any(sp => sp.hasBoss);
                    var col = boss ? GdEnum.FromHex("#FF3355") : GdEnum.Warn;
                    foreach (var sp in n.spawnPoints)
                        FillRect(n.x + sp.x + 0.2f, n.y + sp.y + 0.2f, 0.6f, 0.6f,
                            sp.hasBoss ? new Color(1f, 0.2f, 0.33f, 0.85f) : new Color(1f, 0.69f, 0.737f, 0.8f));
                    FillRect(n.x, n.y, 1, 1, col);
                    if (k < 8) StrokeRect(n.x - 2.5f / k, n.y - 2.5f / k, 1 + 5f / k, 1 + 5f / k, col, 1.5f);
                    if (sel != null && sel.Value.type == "nest" && sel.Value.i == i)
                        StrokeRect(n.x, n.y, 1, 1, Color.white, 2);
                }

            // 밤 진입로 — 노란 테두리 칸
            if (Vis("night"))
                foreach (var np in m.nightSpawnPoints)
                {
                    FillRect(np.x, np.y, 1, 1, new Color(0.91f, 0.647f, 0.294f, 0.35f));
                    StrokeRect(np.x, np.y, 1, 1, GdEnum.ItemC, Mathf.Max(1.5f, k * 0.12f));
                }

            // 나무 — 칸을 채우는 작은 원. 사각형으로 그리면 지형 타일과 구분이 안 된다
            if (Vis("tree"))
            {
                var trunk = GdEnum.FromHex("#4FBF6A");
                foreach (var t in m.trees)
                {
                    p.fillColor = trunk;
                    p.BeginPath();
                    p.Arc(P(t.x + 0.5f, t.y + 0.5f), Mathf.Max(1.5f, k * 0.34f), 0, 360);
                    p.Fill();
                }
            }

            // 코어 3×3
            if (Vis("core"))
            {
                FillRect(m.coreX, m.coreY, 3, 3, GdEnum.Ok);
                StrokeRect(m.coreX, m.coreY, 3, 3, GdEnum.Bg, 1.5f);
            }

            // 브러시 미리보기
            if (hoverCell != null && tool == "paint")
            {
                int h2 = brushSize >> 1;
                StrokeRect(hoverCell.Value.x - h2, hoverCell.Value.y - h2, brushSize, brushSize,
                    new Color(1, 1, 1, 0.6f), 1.5f);
            }
        }

        // ── 캔버스 입력 ──
        // drag.type: pan | paint | erase | move
        (string type, float sx, float sy, float vx, float vy, int ox, int oy)? drag;

        void RegisterCanvasInput()
        {
            canvasHost.RegisterCallback<PointerDownEvent>(e =>
            {
                var m = M; if (m == null) return;
                var local = (Vector2)e.localPosition;
                var (cx2, cy2) = ToCell(local);

                // 가운데 버튼(2) 또는 Shift = 팬
                if (e.button == 2 || e.shiftKey)
                {
                    drag = ("pan", local.x, local.y, viewX, viewY, 0, 0);
                    canvasHost.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }

                // 우클릭(1) = 지우기 — 배치물이 있으면 그것을, 없으면 지형을 지면으로
                if (e.button == 1)
                {
                    var hit = HitTest(m, cx2, cy2);
                    if (hit != null)
                    {
                        if (hit.Value.type == "node") m.nodes.RemoveAt(hit.Value.i);
                        else m.nests.RemoveAt(hit.Value.i);
                        sel = null; PushHist(); RenderAll();
                        e.StopPropagation();
                        return;
                    }
                    int ni = Vis("night") ? m.nightSpawnPoints.FindIndex(p => p.x == cx2 && p.y == cy2) : -1;
                    if (ni >= 0)
                    {
                        m.nightSpawnPoints.RemoveAt(ni);
                        PushHist(); RenderAll();
                        e.StopPropagation();
                        return;
                    }
                    int ti = Vis("tree") ? m.trees.FindIndex(p => p.x == cx2 && p.y == cy2) : -1;
                    if (ti >= 0)
                    {
                        m.trees.RemoveAt(ti);
                        PushHist(); RenderAll();
                        e.StopPropagation();
                        return;
                    }
                    if (tool == "paint")
                    {
                        drag = ("erase", 0, 0, 0, 0, 0, 0);
                        canvasHost.CapturePointer(e.pointerId);
                        EraseAt(m, cx2, cy2);
                    }
                    e.StopPropagation();
                    return;
                }
                if (e.button != 0) return;

                // 어떤 도구든, 이미 뭔가 있는 칸을 누르면 새로 놓지 않고 그것을 고른다(+드래그 이동)
                {
                    var hitAny = HitTest(m, cx2, cy2);
                    if (hitAny != null)
                    {
                        sel = hitAny;
                        drag = ("move", 0, 0, 0, 0, cx2, cy2);
                        canvasHost.CapturePointer(e.pointerId);
                        RenderAll();
                        e.StopPropagation();
                        return;
                    }
                }

                if (tool == "paint")
                {
                    drag = ("paint", 0, 0, 0, 0, 0, 0);
                    canvasHost.CapturePointer(e.pointerId);
                    PaintAt(m, cx2, cy2);
                    e.StopPropagation();
                    return;
                }
                if (tool is "node" or "nest")
                {
                    if (!InB(m, cx2, cy2)) return;
                    if (tool == "node")
                    {
                        m.nodes.Add(new GMapNode { x = cx2, y = cy2 });
                        sel = ("node", m.nodes.Count - 1);
                    }
                    else
                    {
                        m.nests.Add(new GNest { x = cx2, y = cy2 });
                        sel = ("nest", m.nests.Count - 1);
                    }
                    PushHist(); RenderAll();
                    e.StopPropagation();
                    return;
                }
                // 밤 진입로 — 있으면 빼고, 없으면 넣는다 (토글)
                if (tool == "night")
                {
                    if (!InB(m, cx2, cy2)) return;
                    int at = m.nightSpawnPoints.FindIndex(p => p.x == cx2 && p.y == cy2);
                    if (at >= 0) m.nightSpawnPoints.RemoveAt(at);
                    else m.nightSpawnPoints.Add(new Vector2Int(cx2, cy2));
                    PushHist(); RenderAll();
                    e.StopPropagation();
                    return;
                }
                // 나무 — 있으면 빼고 없으면 넣는다(토글). 브러시로 문지를 수 있게 드래그도 받는다
                if (tool == "tree")
                {
                    if (!InB(m, cx2, cy2)) return;
                    ToggleTree(m, cx2, cy2);
                    drag = ("tree", 0, 0, 0, 0, cx2, cy2);
                    canvasHost.CapturePointer(e.pointerId);
                    PushHist(); RenderAll();
                    e.StopPropagation();
                    return;
                }
                if (tool == "core")
                {
                    if (!InB(m, cx2, cy2)) return;
                    m.coreX = Mathf.Clamp(cx2 - 1, 0, m.width - 3);
                    m.coreY = Mathf.Clamp(cy2 - 1, 0, m.height - 3);
                    PushHist(); RenderAll();
                    e.StopPropagation();
                    return;
                }
                if (tool == "select")
                {
                    var hit = HitTest(m, cx2, cy2);
                    sel = hit;
                    if (hit != null)
                    {
                        drag = ("move", 0, 0, 0, 0, cx2, cy2);
                        canvasHost.CapturePointer(e.pointerId);
                    }
                    RenderAll();
                    e.StopPropagation();
                }
            });

            canvasHost.RegisterCallback<PointerMoveEvent>(e =>
            {
                var m = M; if (m == null) return;
                var local = (Vector2)e.localPosition;
                var c = ToCell(local);
                hoverCell = c;
                if (InB(m, c.x, c.y))
                    hintLabel.text = $"{c.x}, {c.y} · {TileOf(m.tiles[Idx(m, c.x, c.y)]).ko}";

                if (drag == null) { if (tool == "paint") overlay.MarkDirtyRepaint(); return; }
                var d = drag.Value;
                switch (d.type)
                {
                    case "pan":
                        viewX = d.vx + local.x - d.sx;
                        viewY = d.vy + local.y - d.sy;
                        RedrawCanvas();
                        break;
                    case "paint": PaintAt(m, c.x, c.y); break;
                    case "erase": EraseAt(m, c.x, c.y); break;
                    case "tree":
                        // 지나간 칸에 없으면 심는다. 문지르는 동안 토글하면 같은 칸을 오가며 깜빡인다
                        if (InB(m, c.x, c.y) && !m.trees.Any(t => t.x == c.x && t.y == c.y))
                        {
                            m.trees.Add(new Vector2Int(c.x, c.y));
                            overlay.MarkDirtyRepaint();
                        }
                        break;
                    case "move":
                        if (sel == null) break;
                        var arr = sel.Value.type == "node";
                        int dx = c.x - d.ox, dy = c.y - d.oy;
                        if (dx == 0 && dy == 0) break;
                        if (arr && sel.Value.i < m.nodes.Count)
                        {
                            var n = m.nodes[sel.Value.i];
                            n.x = Mathf.Clamp(n.x + dx, 0, m.width - 1);
                            n.y = Mathf.Clamp(n.y + dy, 0, m.height - 1);
                        }
                        else if (!arr && sel.Value.i < m.nests.Count)
                        {
                            var n = m.nests[sel.Value.i];
                            n.x = Mathf.Clamp(n.x + dx, 0, m.width - 1);
                            n.y = Mathf.Clamp(n.y + dy, 0, m.height - 1);
                        }
                        drag = (d.type, d.sx, d.sy, d.vx, d.vy, c.x, c.y);
                        overlay.MarkDirtyRepaint();
                        break;
                }
            });

            canvasHost.RegisterCallback<PointerUpEvent>(e =>
            {
                if (canvasHost.HasPointerCapture(e.pointerId)) canvasHost.ReleasePointer(e.pointerId);
                if (drag != null && drag.Value.type is "paint" or "erase" or "move" or "tree")
                {
                    PushHist();
                    RenderWarn(); RenderProps();
                }
                drag = null;
            });

            canvasHost.RegisterCallback<WheelEvent>(e =>
            {
                var local = (Vector2)e.mousePosition - (Vector2)canvasHost.worldBound.position;
                float k2 = Mathf.Clamp(viewK * (e.delta.y < 0 ? 1.15f : 1f / 1.15f), 1, 40);
                viewX = local.x - (local.x - viewX) * k2 / viewK;
                viewY = local.y - (local.y - viewY) * k2 / viewK;
                viewK = k2;
                RedrawCanvas();
                e.StopPropagation();
            });

            canvasHost.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (tileTex != null) { UnityEngine.Object.DestroyImmediate(tileTex); tileTex = null; }
            });
        }

        // 숨긴 레이어는 건너뛴다 — 안 보이는 것이 집히면 겹친 것을 골라낼 방법이 없다
        (string type, int i)? HitTest(GMap m, int cx, int cy)
        {
            if (Vis("nest"))
                for (int i = m.nests.Count - 1; i >= 0; i--)
                    if (m.nests[i].x == cx && m.nests[i].y == cy) return ("nest", i);
            if (Vis("node"))
                for (int i = m.nodes.Count - 1; i >= 0; i--)
                {
                    var n = m.nodes[i];
                    if (cx >= n.x && cx < n.x + n.size && cy >= n.y && cy < n.y + n.size) return ("node", i);
                }
            return null;
        }

        // ═════════ 나무 자동생성 ═════════

        /// <summary>위치를 잘 섞인 값으로 바꾼다 — 좌표를 곱해 더하는 식은 대각선 줄무늬를 만든다.</summary>
        static int TreeHash(int x, int y, int salt)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + salt * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) & 0x7fffffff;
            }
        }

        /// <summary>
        /// 코어에서 걸어갈 수 있는 지면에 나무를 깐다. 이미 있던 나무는 지우고 새로 깐다.
        ///
        /// <b>왜 코어 도달성인가.</b> 나무는 칸을 영구히 막는다. 코어에서 절벽에 막혀 갈 수 없는
        /// 땅은 플레이어가 짓지도 지나가지도 못하는 자리라, 거기 심은 나무는 아무것도 막지 않으면서
        /// 데이터만 늘린다. 걸어갈 수 있는 땅만이 "언젠가 공장이 될 자리"다.
        ///
        /// 강을 건너갈 수 있는 것으로 치는 이유: 강 너머 땅도 코어에서 갈 수 있다.
        /// "심을 수 있는가"는 지면 타일이라는 별도 조건으로 따로 본다.
        /// </summary>
        int GenerateTrees(GMap m)
        {
            m.trees.Clear();

            // 1) 코어에서의 도달성 — 절벽만이 진짜 차단이다(TileRules와 같은 규칙)
            var reach = new bool[m.width * m.height];
            var queue = new Queue<Vector2Int>();
            for (int y = m.coreY; y < m.coreY + 3; y++)
                for (int x = m.coreX; x < m.coreX + 3; x++)
                    if (InB(m, x, y)) { reach[Idx(m, x, y)] = true; queue.Enqueue(new Vector2Int(x, y)); }

            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0),
                               new Vector2Int(0, 1), new Vector2Int(0, -1) };
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in dirs)
                {
                    var n = c + d;
                    if (!InB(m, n.x, n.y)) continue;
                    int i = Idx(m, n.x, n.y);
                    if (reach[i] || !TileOf(m.tiles[i]).walk) continue;
                    reach[i] = true;
                    queue.Enqueue(n);
                }
            }

            // 2) 못 짓는 칸(강·절벽·맵 밖)까지의 거리 — 물가에 바짝 붙은 나무를 물린다
            var edge = new float[m.width * m.height];
            var eq = new Queue<Vector2Int>();
            for (int y = 0; y < m.height; y++)
                for (int x = 0; x < m.width; x++)
                {
                    bool border = x == 0 || y == 0 || x == m.width - 1 || y == m.height - 1;
                    if (!TileOf(m.tiles[Idx(m, x, y)]).build || border)
                    { edge[Idx(m, x, y)] = 0f; eq.Enqueue(new Vector2Int(x, y)); }
                    else edge[Idx(m, x, y)] = float.MaxValue;
                }
            while (eq.Count > 0)
            {
                var c = eq.Dequeue();
                float d0 = edge[Idx(m, c.x, c.y)] + 1f;
                foreach (var d in dirs)
                {
                    var n = c + d;
                    if (!InB(m, n.x, n.y)) continue;
                    int i = Idx(m, n.x, n.y);
                    if (edge[i] <= d0) continue;
                    edge[i] = d0;
                    eq.Enqueue(n);
                }
            }

            // 3) 비켜야 할 원들 — 칸마다 목록을 훑으면 16만 칸 × 수십 개다. 미리 굳혀 둔다
            var circles = new List<(Vector2 c, float r)>
            {
                (new Vector2(m.coreX + 1.5f, m.coreY + 1.5f), treeCoreClear),
            };
            foreach (var n in m.nodes)
            {
                float half = Mathf.Max(1, n.size) * 0.5f;
                circles.Add((new Vector2(n.x + half, n.y + half), treeObjectClear + half));
            }
            foreach (var n in m.nests)
            {
                circles.Add((new Vector2(n.x + 0.5f, n.y + 0.5f), treeObjectClear));
                foreach (var sp in n.spawnPoints)
                    circles.Add((new Vector2(n.x + sp.x + 0.5f, n.y + sp.y + 0.5f), treeObjectClear));
            }
            foreach (var p in m.nightSpawnPoints)
                circles.Add((new Vector2(p.x + 0.5f, p.y + 0.5f), treeObjectClear));

            // 4) 간격을 지키며 깐다 — 푸아송 원반과 같은 규칙이되 후보가 이미 칸으로 이산화돼
            //    있어 별도 자료구조 없이 공간 해시만으로 끝난다
            float bucket = Mathf.Max(1f, treeSpacing + treeSpacingJitter);
            var buckets = new Dictionary<Vector2Int, List<Vector2>>();
            bool TooClose(Vector2 pt, float need)
            {
                var b0 = new Vector2Int(Mathf.FloorToInt(pt.x / bucket), Mathf.FloorToInt(pt.y / bucket));
                for (int by = -1; by <= 1; by++)
                    for (int bx = -1; bx <= 1; bx++)
                        if (buckets.TryGetValue(new Vector2Int(b0.x + bx, b0.y + by), out var list))
                            foreach (var o in list)
                                if ((o - pt).sqrMagnitude < need * need) return true;
                return false;
            }

            for (int y = 0; y < m.height; y++)
                for (int x = 0; x < m.width; x++)
                {
                    int i = Idx(m, x, y);
                    if (!reach[i]) continue;                          // 코어에서 못 간다
                    if (!TileOf(m.tiles[i]).build) continue;          // 강·절벽에는 안 심는다
                    if (edge[i] < treeEdgeClear) continue;            // 물가·벼랑에 바짝 붙지 않는다

                    var pt = new Vector2(x + 0.5f, y + 0.5f);
                    bool blocked = false;
                    foreach (var (c, r) in circles)
                        if ((c - pt).sqrMagnitude < r * r) { blocked = true; break; }
                    if (blocked) continue;

                    float need = treeSpacing
                               + (TreeHash(x, y, treeSeed + 31) % 1000 / 1000f - 0.5f) * 2f * treeSpacingJitter;
                    need = Mathf.Max(0.5f, need);
                    if (TooClose(pt, need)) continue;

                    m.trees.Add(new Vector2Int(x, y));
                    var b = new Vector2Int(Mathf.FloorToInt(pt.x / bucket), Mathf.FloorToInt(pt.y / bucket));
                    if (!buckets.TryGetValue(b, out var lst)) buckets[b] = lst = new List<Vector2>();
                    lst.Add(pt);
                }

            return m.trees.Count;
        }

        /// <summary>그 칸의 나무를 넣거나 뺀다.</summary>
        void ToggleTree(GMap m, int cx, int cy)
        {
            int at = m.trees.FindIndex(t => t.x == cx && t.y == cy);
            if (at >= 0) m.trees.RemoveAt(at);
            else m.trees.Add(new Vector2Int(cx, cy));
        }

        void PaintAt(GMap m, int cx, int cy)
        {
            int h = brushSize >> 1;
            for (int y = cy - h; y <= cy + h; y++)
                for (int x = cx - h; x <= cx + h; x++)
                    if (InB(m, x, y)) m.tiles[Idx(m, x, y)] = (byte)paintTile;
            UpdateTileCells(m, cx - h, cy - h, cx + h, cy + h);
            overlay.MarkDirtyRepaint();
        }

        void EraseAt(GMap m, int cx, int cy)
        {
            int h = brushSize >> 1;
            for (int y = cy - h; y <= cy + h; y++)
                for (int x = cx - h; x <= cx + h; x++)
                    if (InB(m, x, y)) m.tiles[Idx(m, x, y)] = 0;   // 지면으로 되돌린다
            UpdateTileCells(m, cx - h, cy - h, cx + h, cy + h);
            overlay.MarkDirtyRepaint();
        }
    }
}
#endif
