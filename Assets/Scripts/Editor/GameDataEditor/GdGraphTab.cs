#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// ═══════════════════════════════════════════════════════════
//  아이템·레시피 그래프 탭 (Web/js/graph-core.js + graph-panel.js 대응)
//
//  원본과 같은 커스텀 캔버스다 — GraphView를 쓰지 않는 이유: 접힘·포트 표시·
//  컨텍스트 메뉴가 원본과 다르게 움직였고, 우리 모델(좌표·펼침이 데이터에 있고
//  히스토리 스냅샷에 포함 → 이동도 언두)이 GraphView와 맞지 않았다.
//
//  구조: wrap(클리핑) > world(팬·줌 변환) > [bands, edgeLayer(Painter2D), nodes, labels]
//  스냅샷 언두: nodes(x·y·expanded 포함) + edges + seq + bands — 원본 snapshot()과 동일.
// ═══════════════════════════════════════════════════════════

// ── enum 상수 (Web/js/enums.js 대응) ──
static class GdEnum
{
    public class TypeInfo
    {
        public readonly string v, ko, desc;
        public TypeInfo(string v, string ko, string desc) { this.v = v; this.ko = ko; this.desc = desc; }
    }
    public static readonly TypeInfo[] ItemTypes =
    {
        new("Ore", "원광", "채굴로만 얻는 원자재"),
        new("Ingot", "소재", "제련 산출물"),
        new("Part", "부품", "제작·조립 중간재"),
        new("RepairPart", "수리 부품", "게이트 납품 전용"),
        new("Ammo", "탄약", "무기·포탑 소모품"),
        new("Weapon", "무기", "손에 드는 것"),
        new("Armor", "방어구", "착용 장비 (부위 구분 없음)"),
        new("Salvage", "회수물", "수집으로만 얻고 제작 불가"),
    };

    public class LineInfo
    {
        public readonly string v, ko;
        public readonly Color c;
        public LineInfo(string v, string ko, Color c) { this.v = v; this.ko = ko; this.c = c; }
    }
    public static readonly LineInfo[] ItemLines =
    {
        new("None", "미지정", FromHex("#8FA3C0")),
        new("Iron", "구조", FromHex("#E8A54B")),
        new("Copper", "전자", FromHex("#4FD8E0")),
        new("Crystal", "동력", FromHex("#B48CFF")),
        new("Beast", "괴수", FromHex("#FF5D73")),
    };

    public static Color FromHex(string h)
    { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    public static Color LineColor(string v) => (ItemLines.FirstOrDefault(l => l.v == v) ?? ItemLines[0]).c;
    public static string LineKo(string v) => (ItemLines.FirstOrDefault(l => l.v == v)?.ko) ?? "?";
    public static string TypeKo(string v) => (ItemTypes.FirstOrDefault(t => t.v == v)?.ko) ?? "?";

    // 팔레트 (Web/css 대응 — 최종 폴리시 전의 기본색)
    public static readonly Color Bg = FromHex("#0B1220");
    public static readonly Color Panel = FromHex("#111B2E");
    public static readonly Color Panel2 = FromHex("#0E1727");
    public static readonly Color Line = FromHex("#223350");
    public static readonly Color Border = FromHex("#2E4266");   // --line2
    public static readonly Color ItemC = FromHex("#E8A54B");
    public static readonly Color RecipeC = FromHex("#4FD8E0");
    public static readonly Color Text = FromHex("#D9E4F5");
    public static readonly Color Muted = FromHex("#8FA3C0");
    public static readonly Color Faint = FromHex("#5C6E8C");
    public static readonly Color Accent = FromHex("#4FD8E0");
    public static readonly Color Warn = FromHex("#FF5D73");
    public static readonly Color Ok = FromHex("#5DD39E");
    public static readonly Color Sel = FromHex("#B48CFF");
    public static readonly Color EdgeIn = FromHex("#E8A54B");   // 아이템→레시피(재료)
    public static readonly Color EdgeOut = FromHex("#4FD8E0");  // 레시피→아이템(산출)
}

// ── 데이터 모델 (graph-core 상단 대응) ──
class GEff { public string effect; public float value; }

class GNodeData
{
    public string name = "", displayName = "", description = "", type = "Part", line = "None", icon = "", gun = "";
    // 아이콘 에셋의 guid — 이쪽이 파일을 특정하고 icon(이름)은 아틀라스 안에서 어느 스프라이트인지 고른다
    public string iconGuid = "";
    public List<GEff> attackEffects = new();
    public float speed = 50, gravity, explosionRadius, lifetime = 3;
    public int pierce;
    public int maxStack = 64;   // 한 슬롯 최대 개수 (ItemDataSO.maxStack)
    public int tier = 1;
    public float craftTime = 2f;
}

class GNode
{
    public string id;
    public string kind;   // "item" | "recipe"
    public float x, y;
    public bool expanded;
    public GNodeData data = new();
}

class GEdge { public string id, from, to; public int amount = 1; }

class GBands
{
    public List<float> seps = new();
    public List<(float x, float w, string label)> tiers = new();
    public List<(float x, string label)> steps = new();
    public List<(float y, float h, float width, string label)> rows = new();
}

class GdGraphTab : GdTab
{
    public override string Title => "아이템 · 레시피";
    public GdGraphTab(GameDataEditorWindow win) : base(win) { }

    // ── 상태 ──
    readonly Dictionary<string, GNode> nodes = new();
    readonly Dictionary<string, GEdge> edges = new();
    int seq = 1;
    (string type, string id)? selection;   // ("node"|"edge", id)
    GBands bands;
    string hoverNode;
    Vector2 viewPos = new(60, 40);
    float viewK = 1f;

    // 스키마 밖 필드 보존 — 그래프 모델을 거치면 unknownJson이 사라지므로 따로 들고 있다가 되붙인다
    readonly Dictionary<string, IDictionary<string, object>> itemExtra = new();
    readonly Dictionary<string, IDictionary<string, object>> recipeExtra = new();

    GdHistory history;

    // ── UI 참조 ──
    VisualElement wrap, world, bandsHost, nodesHost, labelHost, side;
    VisualElement edgeLayer;
    VisualElement stickyCols, stickyRows;
    Label statLabel, sideTitle;
    VisualElement sideBody;
    VisualElement warnBox;
    Button expandBtn;
    readonly Dictionary<string, VisualElement> nodeEls = new();

    string Uid() => "n" + seq++;
    static string Sanitize(string s) =>
        new(string.IsNullOrEmpty(s) ? "" : s.Where(c => char.IsLetterOrDigit(c) || c == '_' || (c >= '가' && c <= '힣')).ToArray());

    // ═════════════ 데이터 ↔ root (exportJson / importJson) ═════════════

    public override void OnDataLoaded()
    {
        ImportFromRoot();
        history = new GdHistory(Snapshot, Restore, 80);
        history.Reset();
    }

    void ImportFromRoot()
    {
        nodes.Clear(); edges.Clear(); itemExtra.Clear(); recipeExtra.Clear();
        selection = null; seq = 1;
        var itemNode = new Dictionary<string, string>();   // "Item:X" → nodeId

        foreach (var it in win.root.items ?? Array.Empty<GameDataImporter.ItemDto>())
        {
            string name = (it.id ?? "").StartsWith("Item:") ? it.id.Substring(5) : (it.id ?? it.displayName ?? "");
            var id = Uid();
            nodes[id] = new GNode
            {
                id = id, kind = "item",
                data = new GNodeData
                {
                    name = name, displayName = it.displayName ?? name, description = it.description ?? "",
                    type = string.IsNullOrEmpty(it.type) ? "Part" : it.type,
                    line = string.IsNullOrEmpty(it.line) ? "None" : it.line,
                    icon = it.icon ?? "", iconGuid = it.iconGuid ?? "",
                    maxStack = it.maxStack > 0 ? it.maxStack : 64,
                    attackEffects = it.attackEffects != null
                        ? it.attackEffects.Select(e => new GEff { effect = e.effect, value = e.value }).ToList()
                        : (it.damage > 0 ? new List<GEff> { new() { effect = "Effect:Damage", value = it.damage } } : new List<GEff>()),
                    speed = it.speed >= 0 ? it.speed : 50,
                    gravity = Mathf.Max(0, it.gravity),
                    explosionRadius = Mathf.Max(0, it.explosionRadius),
                    lifetime = it.lifetime >= 0 ? it.lifetime : 3,
                    pierce = Mathf.Max(0, it.pierce),
                    gun = it.gun ?? "",
                },
            };
            itemNode["Item:" + name] = id;
            if (it.unknownJson is { Count: > 0 }) itemExtra[name] = it.unknownJson;
        }

        string EnsureGhost(string itemId)
        {
            // 정의 안 된 재료 참조 → 자리 아이템 생성 (임포터의 미해석 스킵 방지용 시각화)
            string name = itemId.StartsWith("Item:") ? itemId.Substring(5) : itemId;
            var id = Uid();
            nodes[id] = new GNode { id = id, kind = "item",
                data = new GNodeData { name = name, displayName = name + " (미정의)", type = "Part", line = "None" } };
            itemNode[itemId] = id;
            return id;
        }

        foreach (var r in win.root.recipes ?? Array.Empty<GameDataImporter.RecipeDto>())
        {
            string name = (r.id ?? "").StartsWith("Recipe:") ? r.id.Substring(7) : (r.id ?? r.displayName ?? "");
            var id = Uid();
            nodes[id] = new GNode
            {
                id = id, kind = "recipe",
                data = new GNodeData
                {
                    name = name, displayName = r.displayName ?? name, description = r.description ?? "",
                    tier = r.tier, craftTime = r.craftTime,
                },
            };
            if (r.unknownJson is { Count: > 0 }) recipeExtra[name] = r.unknownJson;
            foreach (var p in r.inputs ?? Array.Empty<GameDataImporter.SlotDto>())
            {
                if (p.item == null) continue;
                if (!itemNode.TryGetValue(p.item, out var src)) src = EnsureGhost(p.item);
                var eid = Uid();
                edges[eid] = new GEdge { id = eid, from = src, to = id, amount = Math.Max(1, p.amount) };
            }
            foreach (var p in r.outputs ?? Array.Empty<GameDataImporter.SlotDto>())
            {
                if (p.item == null) continue;
                if (!itemNode.TryGetValue(p.item, out var dst)) dst = EnsureGhost(p.item);
                var eid = Uid();
                edges[eid] = new GEdge { id = eid, from = id, to = dst, amount = Math.Max(1, p.amount) };
            }
        }
        AutoLayout();
    }

    public override void SyncToRoot()
    {
        if (win.root == null || nodes.Count == 0 && (win.root.items?.Length ?? 0) > 0) { }
        var items = new List<GameDataImporter.ItemDto>();
        var recipes = new List<GameDataImporter.RecipeDto>();

        foreach (var n in nodes.Values.Where(n => n.kind == "item"))
        {
            var d = n.data;
            var it = new GameDataImporter.ItemDto
            {
                id = "Item:" + Sanitize(d.name),
                displayName = string.IsNullOrEmpty(d.displayName) ? d.name : d.displayName,
                description = d.description ?? "",
                type = string.IsNullOrEmpty(d.type) ? "Part" : d.type,
                line = string.IsNullOrEmpty(d.line) ? "None" : d.line,
                icon = d.icon ?? "", iconGuid = d.iconGuid ?? "",
                maxStack = Mathf.Max(1, d.maxStack),
            };
            if (d.type == "Ammo")
            {
                if (d.attackEffects.Count > 0)
                    it.attackEffects = d.attackEffects.Select(e => new GameDataImporter.EffectEntryDto { effect = e.effect, value = e.value }).ToArray();
                // 탄도 — 0 이 정당한 값(직선·무폭발)이라 항상 내보낸다. 임포터는 음수를 "유지"로 본다
                it.speed = d.speed; it.gravity = d.gravity; it.explosionRadius = d.explosionRadius;
                it.lifetime = d.lifetime; it.pierce = d.pierce;
            }
            if (d.type == "Weapon" && !string.IsNullOrEmpty(d.gun)) it.gun = d.gun;
            if (itemExtra.TryGetValue(d.name, out var ex)) it.unknownJson = ex;
            items.Add(it);
        }
        foreach (var n in nodes.Values.Where(n => n.kind == "recipe"))
        {
            var d = n.data;
            var inputs = new List<GameDataImporter.SlotDto>();
            var outputs = new List<GameDataImporter.SlotDto>();
            foreach (var e in edges.Values)
            {
                if (e.to == n.id && nodes.TryGetValue(e.from, out var src))
                    inputs.Add(new GameDataImporter.SlotDto { item = "Item:" + Sanitize(src.data.name), amount = e.amount });
                if (e.from == n.id && nodes.TryGetValue(e.to, out var dst))
                    outputs.Add(new GameDataImporter.SlotDto { item = "Item:" + Sanitize(dst.data.name), amount = e.amount });
            }
            var r = new GameDataImporter.RecipeDto
            {
                id = "Recipe:" + Sanitize(d.name),
                displayName = string.IsNullOrEmpty(d.displayName) ? d.name : d.displayName,
                description = d.description ?? "",
                tier = d.tier, craftTime = d.craftTime,
                inputs = inputs.Count > 0 ? inputs.ToArray() : null,
                outputs = outputs.Count > 0 ? outputs.ToArray() : null,
            };
            if (recipeExtra.TryGetValue(d.name, out var ex)) r.unknownJson = ex;
            recipes.Add(r);
        }
        win.root.items = items.ToArray();
        win.root.recipes = recipes.ToArray();
    }

    // ═════════════ 히스토리 (snapshot / restore) ═════════════

    string Snapshot() => JsonConvert.SerializeObject(new
    {
        nodes = nodes.Values, edges = edges.Values, seq,
        bands = bands == null ? null : new
        {
            seps = bands.seps,
            tiers = bands.tiers.Select(t => new { t.x, t.w, t.label }),
            steps = bands.steps.Select(s => new { s.x, s.label }),
            rows = bands.rows.Select(r => new { r.y, r.h, r.width, r.label }),
        },
    });

    void Restore(string snap)
    {
        var o = JsonConvert.DeserializeAnonymousType(snap, new
        {
            nodes = new List<GNode>(), edges = new List<GEdge>(), seq = 0,
            bands = new
            {
                seps = new List<float>(),
                tiers = new List<Dictionary<string, object>>(),
                steps = new List<Dictionary<string, object>>(),
                rows = new List<Dictionary<string, object>>(),
            },
        });
        nodes.Clear(); foreach (var n in o.nodes) nodes[n.id] = n;
        edges.Clear(); foreach (var e in o.edges) edges[e.id] = e;
        seq = o.seq;
        bands = null;
        if (o.bands != null)
        {
            float F(object v) => Convert.ToSingle(v);
            bands = new GBands { seps = o.bands.seps };
            foreach (var t in o.bands.tiers) bands.tiers.Add((F(t["x"]), F(t["w"]), (string)t["label"]));
            foreach (var s in o.bands.steps) bands.steps.Add((F(s["x"]), (string)s["label"]));
            foreach (var r in o.bands.rows) bands.rows.Add((F(r["y"]), F(r["h"]), F(r["width"]), (string)r["label"]));
        }
        if (selection != null && !(selection.Value.type == "node" ? nodes.ContainsKey(selection.Value.id) : edges.ContainsKey(selection.Value.id)))
            selection = null;
        Render(); RenderSide();
        win.MarkDirty();
    }

    public override void Undo() { history?.Undo(); }
    public override void Redo() { history?.Redo(); }

    void Push() { history?.Push(); win.MarkDirty(); }

    // ═════════════ 편집 연산 (addNode / addEdge / removeSelection) ═════════════

    string AddNode(string kind, float x, float y)
    {
        var id = Uid();
        var data = kind == "item"
            ? new GNodeData { name = "NewItem" + id, displayName = "새 아이템" }
            : new GNodeData { name = "NewRecipe" + id, displayName = "새 레시피" };
        nodes[id] = new GNode { id = id, kind = kind, x = x, y = y, data = data };
        Render();
        Select(("node", id));
        Push();
        return id;
    }

    string AddEdge(string fromId, string toId, int amount = 1)
    {
        if (!nodes.TryGetValue(fromId, out var a) || !nodes.TryGetValue(toId, out var b) || a.kind == b.kind) return null;
        foreach (var e in edges.Values) if (e.from == fromId && e.to == toId) return null;   // 중복 금지
        var id = Uid();
        edges[id] = new GEdge { id = id, from = fromId, to = toId, amount = amount };
        measuredH.Remove(fromId);
        measuredH.Remove(toId);
        Render();
        Push();
        return id;
    }

    void RemoveSelection()
    {
        if (selection == null) return;
        var (type, id) = selection.Value;
        if (type == "edge")
        {
            if (edges.TryGetValue(id, out var de)) { measuredH.Remove(de.from); measuredH.Remove(de.to); }
            edges.Remove(id);
        }
        else
        {
            foreach (var e in edges.Values.Where(e => e.from == id || e.to == id).ToList())
            { measuredH.Remove(e.from); measuredH.Remove(e.to); edges.Remove(e.id); }
            nodes.Remove(id);
            measuredH.Remove(id);
        }
        selection = null;
        Render(); RenderSide();
        Push();
    }

    public override bool DeleteSelection()
    {
        if (selection == null) return false;
        RemoveSelection();
        return true;
    }

    // ═════════════ 자동 정렬 (TIER_OF / DEPTH_OF / layoutGrid) ═════════════

    int TierOf(string id)
    {
        var n = nodes[id];
        if (n.kind == "recipe") return n.data.tier;
        int best = int.MaxValue;
        foreach (var e in edges.Values)
            if (e.to == id && nodes.TryGetValue(e.from, out var f) && f.kind == "recipe")
                best = Math.Min(best, f.data.tier);
        return best == int.MaxValue ? 0 : best;
    }

    void AutoLayout()
    {
        // 생산 흐름 깊이 — 티어 안에서만 센다. 이전 티어 재료는 "가진 것"(깊이 0)
        var producers = new Dictionary<string, List<string>>();   // itemNodeId → recipeNodeIds
        foreach (var e in edges.Values)
            if (nodes.TryGetValue(e.from, out var f) && f.kind == "recipe")
            {
                if (!producers.TryGetValue(e.to, out var l)) producers[e.to] = l = new();
                l.Add(e.from);
            }
        var memo = new Dictionary<string, int>();
        int ItemDepth(string id, int t, HashSet<string> seen)
        {
            string key = id + "@" + t;
            if (memo.TryGetValue(key, out int m)) return m;
            if (seen.Contains(id)) return 0;
            if (TierOf(id) < t) return 0;
            if (!producers.TryGetValue(id, out var rs)) return 0;
            var mine = rs.Where(r => nodes[r].data.tier == t).ToList();
            if (mine.Count == 0) return 0;
            var s2 = new HashSet<string>(seen) { id };
            int best = 0;
            foreach (var rid in mine)
            {
                int deepest = 0;
                foreach (var e in edges.Values)
                    if (e.to == rid) deepest = Math.Max(deepest, ItemDepth(e.from, t, s2));
                best = Math.Max(best, deepest + 1);
            }
            memo[key] = best;
            return best;
        }
        var depth = new Dictionary<string, int>();
        foreach (var (id, n) in nodes)
            if (n.kind == "item") depth[id] = ItemDepth(id, TierOf(id), new HashSet<string>());

        // 2축 격자 — 가로: 티어→깊이 · 세로: 타입 밴드 (원본 layoutGrid)
        const float ROW = 170, COL = 620, BAND_GAP = 70, TIER_GAP = 80, RECIPE_DX = 300;

        string TypeOfNode(string id)
        {
            var n = nodes[id];
            if (n.kind == "item") return string.IsNullOrEmpty(n.data.type) ? "Part" : n.data.type;
            foreach (var e in edges.Values)
                if (e.from == id && nodes.TryGetValue(e.to, out var t) && t.kind == "item")
                    return string.IsNullOrEmpty(t.data.type) ? "Part" : t.data.type;
            return "Part";
        }

        var cells = new Dictionary<(int tier, int depth, string type), List<string>>();
        var colKeys = new List<(int tier, int depth)>();
        var typeSet = new HashSet<string>();
        foreach (var (id, n) in nodes)
        {
            if (n.kind != "item") continue;
            int tier = TierOf(id), dp = depth.GetValueOrDefault(id);
            string type = TypeOfNode(id);
            typeSet.Add(type);
            if (!colKeys.Contains((tier, dp))) colKeys.Add((tier, dp));
            var k = (tier, dp, type);
            if (!cells.TryGetValue(k, out var l)) cells[k] = l = new();
            l.Add(id);
        }
        colKeys.Sort((a, b) => a.tier != b.tier ? a.tier - b.tier : a.depth - b.depth);

        // 깊이를 티어 안에서 다시 매긴다 — 절대 깊이는 열을 잘게 쪼갠다
        var seqByTier = new Dictionary<int, List<int>>();
        var cols = colKeys.Select(c =>
        {
            if (!seqByTier.TryGetValue(c.tier, out var arr)) seqByTier[c.tier] = arr = new();
            if (!arr.Contains(c.depth)) arr.Add(c.depth);
            return (c.tier, c.depth, step: arr.IndexOf(c.depth));
        }).ToList();

        var order = GdEnum.ItemTypes.Select(t => t.v).ToList();
        var types = typeSet.OrderBy(t => { int i = order.IndexOf(t); return i < 0 ? 99 : i; }).ToList();

        var bandRows = types.Select(t =>
            Math.Max(1, cols.Count == 0 ? 1 : cols.Max(c => cells.TryGetValue((c.tier, c.depth, t), out var l) ? l.Count : 0))).ToList();
        var bandY = new List<float>();
        {
            float y = 110;
            for (int i = 0; i < types.Count; i++) { bandY.Add(y); y += bandRows[i] * ROW + BAND_GAP; }
        }
        var colX = new List<float>();
        {
            float x = 40;
            for (int i = 0; i < cols.Count; i++)
            {
                if (i > 0 && cols[i - 1].tier != cols[i].tier) x += TIER_GAP;
                colX.Add(x); x += COL;
            }
        }

        for (int ci = 0; ci < cols.Count; ci++)
            for (int ti = 0; ti < types.Count; ti++)
            {
                if (!cells.TryGetValue((cols[ci].tier, cols[ci].depth, types[ti]), out var l)) continue;
                var sorted = l.OrderBy(id => nodes[id].data.displayName ?? "", StringComparer.Ordinal).ToList();
                for (int i = 0; i < sorted.Count; i++)
                { nodes[sorted[i]].x = colX[ci]; nodes[sorted[i]].y = bandY[ti] + i * ROW; }
            }

        // 레시피는 결과물 왼쪽 반 칸 — 같은 자리면 아래로 비킨다
        var usedY = new Dictionary<int, List<float>>();
        foreach (var (id, n) in nodes)
        {
            if (n.kind != "recipe") continue;
            var outs = edges.Values.Where(e => e.from == id && nodes.TryGetValue(e.to, out var t) && t.kind == "item")
                .Select(e => nodes[e.to]).ToList();
            float ax, ay;
            if (outs.Count > 0)
            {
                ax = outs.Min(o => o.x) - RECIPE_DX;
                ay = outs.Average(o => o.y);
            }
            else { ax = 40 - RECIPE_DX; ay = 110; }
            int key = Mathf.RoundToInt(ax);
            if (!usedY.TryGetValue(key, out var taken)) usedY[key] = taken = new();
            while (taken.Any(v => Mathf.Abs(v - ay) < 150)) ay += 150;
            taken.Add(ay);
            n.x = ax; n.y = ay;
        }

        // 구획 밴드 (tierSpans / seps / rows / steps)
        bands = new GBands();
        for (int i = 0; i < cols.Count; i++)
        {
            if (bands.tiers.Count > 0 && bands.tiers[^1].label == TierLabel(cols[i].tier))
            {
                var last = bands.tiers[^1];
                bands.tiers[^1] = (last.x, colX[i] + 250 - last.x, last.label);
            }
            else
                bands.tiers.Add((Mathf.Max(0, colX[i] - RECIPE_DX - 20), colX[i] + 250 - Mathf.Max(0, colX[i] - RECIPE_DX - 20), TierLabel(cols[i].tier)));
            if (i > 0 && cols[i - 1].tier == cols[i].tier) bands.seps.Add(colX[i] - RECIPE_DX - 30);
            bands.steps.Add((colX[i], $"{cols[i].step + 1}단계"));
        }
        float right = (colX.Count > 0 ? colX[^1] : 40) + 280;
        for (int ti = 0; ti < types.Count; ti++)
        {
            var info = GdEnum.ItemTypes.FirstOrDefault(x => x.v == types[ti]);
            bands.rows.Add((bandY[ti], bandRows[ti] * ROW, right,
                info != null ? $"{info.v} · {info.ko}" : types[ti]));
        }

        static string TierLabel(int t) => t == 0 ? "티어 0 · 시작" : $"티어 {t}";
    }

    // ═════════════ pane 구성 (index.html pane-graph 대응) ═════════════

    public override void Build(VisualElement host)
    {
        host.style.backgroundColor = GdEnum.Bg;

        // ── topbar ──
        var top = new VisualElement();
        top.AddToClassList("gd-topbar");
        host.Add(top);
        var title = new Label("GameData 노드 에디터");
        title.AddToClassList("gd-topbar-title");
        top.Add(title);
        var small = new Label("아이템 · 레시피");
        small.AddToClassList("gd-topbar-small");
        top.Add(small);
        top.Add(TopBtn("↶", () => Undo(), "실행 취소 (Ctrl+Z)"));
        top.Add(TopBtn("↷", () => Redo(), "다시 실행 (Ctrl+Y)"));
        top.Add(new VisualElement { style = { flexGrow = 1 } });
        expandBtn = TopBtn("재료 펼치기", ToggleExpandAll, "레시피 재료를 모두 펼치거나 접는다");
        top.Add(expandBtn);
        top.Add(TopBtn("자동 정렬", () => { AutoLayout(); Render(); Push(); }, null));
        top.Add(new VisualElement { style = { flexGrow = 1 } });
        statLabel = new Label();
        statLabel.AddToClassList("gd-stat");
        top.Add(statLabel);

        // ── main = 캔버스 + 사이드 ──
        var main = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
        host.Add(main);

        wrap = new VisualElement { style = { flexGrow = 1, overflow = Overflow.Hidden, backgroundColor = GdEnum.Bg } };
        main.Add(wrap);

        world = new VisualElement { style = { position = Position.Absolute, left = 0, top = 0,
            transformOrigin = new TransformOrigin(0, 0) } };
        wrap.Add(world);

        bandsHost = new VisualElement { pickingMode = PickingMode.Ignore,
            style = { position = Position.Absolute, left = 0, top = 0 } };
        world.Add(bandsHost);

        edgeLayer = new VisualElement { pickingMode = PickingMode.Ignore,
            style = { position = Position.Absolute, left = 0, top = 0, width = 4000, height = 3000 } };
        edgeLayer.generateVisualContent += DrawEdges;
        world.Add(edgeLayer);

        nodesHost = new VisualElement { style = { position = Position.Absolute, left = 0, top = 0 } };
        world.Add(nodesHost);

        labelHost = new VisualElement { style = { position = Position.Absolute, left = 0, top = 0 } };
        world.Add(labelHost);

        stickyCols = new VisualElement { pickingMode = PickingMode.Ignore,
            style = { position = Position.Absolute, left = 0, top = 0, right = 0, height = 24 } };
        wrap.Add(stickyCols);
        stickyRows = new VisualElement { pickingMode = PickingMode.Ignore,
            style = { position = Position.Absolute, left = 0, top = 24, bottom = 0, width = 170 } };
        wrap.Add(stickyRows);

        // #side — width 300 · border-left --line · bg --panel2 · overflow-y:auto · padding 14.
        // 원본은 패널 전체가 하나의 스크롤 컬럼이다(제목·폼·검증·힌트가 한 흐름) —
        // 스크롤을 쪼개면 폼 마지막 요소가 경계에서 잘려 겹쳐 보인다.
        side = new VisualElement { style = { width = 300, borderLeftWidth = 1, borderLeftColor = GdEnum.Line,
            backgroundColor = GdEnum.Panel2 } };
        main.Add(side);
        var sideScroll = new ScrollView { style = { flexGrow = 1 } };
        sideScroll.contentContainer.style.paddingLeft = 14;
        sideScroll.contentContainer.style.paddingRight = 14;
        sideScroll.contentContainer.style.paddingTop = 14;
        sideScroll.contentContainer.style.paddingBottom = 14;
        side.Add(sideScroll);
        sideTitle = new Label("선택 없음") { style = { unityFontStyleAndWeight = FontStyle.Bold,
            fontSize = 13, color = GdEnum.Faint, letterSpacing = 1, marginBottom = 10 } };
        sideScroll.Add(sideTitle);
        sideBody = new VisualElement();
        sideScroll.Add(sideBody);
        warnBox = new VisualElement { style = { paddingBottom = 8, marginTop = 8 } };
        sideScroll.Add(warnBox);
        sideScroll.Add(Hint("조작 — 휠클릭 드래그: 이동 · 휠: 줌 · 노드 헤더 드래그: 노드 이동 · " +
            "간선 라벨 클릭(더블클릭: 수량 편집) · Delete: 선택 삭제 · 우클릭: 추가 메뉴\n" +
            "노드에 커서를 올리면 그 노드에 연결된 간선만 남고 나머지는 흐려진다.\n\n" +
            "id 규칙 — \"Item:이름\" / \"Recipe:이름\" 자동 접두. id가 기본 키(멱등 재임포트)이므로 임포트 후에는 바꾸지 말 것.\n\n" +
            "line — 계통. type과 직교하는 축이라 Part × Copper(구리 전선) 같은 조합이 나온다. 색은 line만 쓴다.\n" +
            "type — 분류 태그. enum에 없는 값을 쓰면 임포터가 에러를 내고 그 아이템을 건너뛴다."));

        RegisterCanvasInput();
        Render();
        RenderSide();
    }

    Button TopBtn(string text, Action act, string tip)
    {
        var b = new Button(act) { text = text, tooltip = tip ?? "" };
        b.style.backgroundColor = GdEnum.Panel;
        b.style.color = GdEnum.Text;
        b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 1;
        b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = GdEnum.Border;
        return b;
    }

    void ToggleExpandAll()
    {
        bool any = nodes.Values.Any(n => n.kind == "recipe" && !n.expanded);
        foreach (var n in nodes.Values) if (n.kind == "recipe") n.expanded = any;
        expandBtn.text = any ? "재료 접기" : "재료 펼치기";
        Render();
        Push();
    }

    // ═════════════ 렌더 ═════════════

    void ApplyViewTransform()
    {
        world.style.translate = new Translate(viewPos.x, viewPos.y);
        world.style.scale = new Scale(new Vector3(viewK, viewK, 1f));
        RenderSticky();
    }

    const float NODE_W = 240, HEAD_H = 30, BODY_PAD = 14, ROW_H = 21;

    float EstimateH(GNode n)
    {
        int rows;
        if (n.kind == "item") rows = 3;
        else if (n.expanded)
        {
            int ins = edges.Values.Count(e => e.to == n.id), outs = edges.Values.Count(e => e.from == n.id);
            rows = ins + outs + (ins > 0 && outs > 0 ? 1 : 0) + 1;
        }
        else rows = 3;
        return HEAD_H + BODY_PAD + rows * ROW_H;
    }

    // 실측 높이 캐시 — 리렌더 직후엔 요소 높이가 0이라 추정치로 그렸다가 실측으로
    // 보정하면 선이 한 번 출렁인다. 같은 노드의 높이는 내용이 바뀌기 전까지 유효하므로
    // 캐시해 두면 리렌더 직후에도 정확한 자리에 그린다.
    readonly Dictionary<string, float> measuredH = new();

    Rect NodeBox(string id)
    {
        var n = nodes[id];
        float h, w = NODE_W;
        if (nodeEls.TryGetValue(id, out var el) && el.resolvedStyle.height > 0)
        { h = el.resolvedStyle.height; w = el.resolvedStyle.width; measuredH[id] = h; }
        else if (!measuredH.TryGetValue(id, out h)) h = EstimateH(n);
        return new Rect(n.x, n.y, w, h);
    }

    // 간선별 접점 — 상대 노드 y 순서대로 노드 변을 따라 분산 (computeAnchors)
    Dictionary<string, (Vector2 a, Vector2 b)> anchors = new();

    void ComputeAnchors()
    {
        anchors = new();
        var outMap = new Dictionary<string, List<GEdge>>();
        var inMap = new Dictionary<string, List<GEdge>>();
        foreach (var e in edges.Values)
        {
            if (!nodes.ContainsKey(e.from) || !nodes.ContainsKey(e.to)) continue;
            if (!outMap.TryGetValue(e.from, out var lo)) outMap[e.from] = lo = new();
            lo.Add(e);
            if (!inMap.TryGetValue(e.to, out var li)) inMap[e.to] = li = new();
            li.Add(e);
        }
        float CenterY(string id) { var b = NodeBox(id); return b.y + b.height / 2; }
        void Spread(List<GEdge> list, string id, bool outSide)
        {
            var b = NodeBox(id);
            list.Sort((p, q) => CenterY(outSide ? p.to : p.from).CompareTo(CenterY(outSide ? q.to : q.from)));
            float usable = Mathf.Max(0, b.height - 24);
            float step = list.Count > 1 ? Mathf.Min(22, usable / (list.Count - 1)) : 0;
            float start = b.y + b.height / 2 - step * (list.Count - 1) / 2;
            for (int i = 0; i < list.Count; i++)
            {
                var pt = new Vector2(b.x + (outSide ? b.width : 0), start + step * i);
                var cur = anchors.GetValueOrDefault(list[i].id);
                if (outSide) cur.a = pt; else cur.b = pt;
                anchors[list[i].id] = cur;
            }
        }
        foreach (var (id, l) in outMap) Spread(l, id, true);
        foreach (var (id, l) in inMap) Spread(l, id, false);
    }

    (Vector2 a, Vector2 b, Vector2 c1, Vector2 c2) EdgeGeom(GEdge e)
    {
        var cur = anchors.GetValueOrDefault(e.id);
        var a = cur.a == default ? PortPos(e.from, true) : cur.a;
        var b = cur.b == default ? PortPos(e.to, false) : cur.b;
        float dx = Mathf.Max(50, Mathf.Abs(b.x - a.x) / 2);
        return (a, b, new Vector2(a.x + dx, a.y), new Vector2(b.x - dx, b.y));
    }

    Vector2 PortPos(string nodeId, bool outSide)
    {
        var b = NodeBox(nodeId);
        return new Vector2(b.x + (outSide ? b.width : 0), b.y + b.height / 2);
    }

    static Vector2 BezierAt(Vector2 a, Vector2 c1, Vector2 c2, Vector2 b, float t)
    {
        float u = 1 - t;
        return u * u * u * a + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * b;
    }

    // 와이어 드래그 상태
    (string node, bool outSide, Vector2 cur)? tempWire;

    void DrawEdges(MeshGenerationContext mgc)
    {
        var p = mgc.painter2D;
        foreach (var e in edges.Values)
        {
            if (!nodes.ContainsKey(e.from) || !nodes.ContainsKey(e.to)) continue;
            bool isIn = nodes[e.from].kind == "item";
            bool selc = selection is { type: "edge" } s && s.id == e.id;
            bool rel = hoverNode == null || e.from == hoverNode || e.to == hoverNode;
            var col = selc ? GdEnum.Sel : isIn ? GdEnum.EdgeIn : GdEnum.EdgeOut;
            col.a = selc ? 1f : rel ? 0.85f : 0.12f;
            var g = EdgeGeom(e);
            p.BeginPath();
            p.MoveTo(g.a);
            p.BezierCurveTo(g.c1, g.c2, g.b);
            p.strokeColor = col;
            p.lineWidth = selc ? 3 : (hoverNode != null && rel ? 2.6f : 2f);
            p.Stroke();
            // 접점 표시
            foreach (var pt in new[] { g.a, g.b })
            {
                p.BeginPath();
                p.Arc(pt, 3, 0, 360);
                p.fillColor = col;
                p.Fill();
            }
        }
        if (tempWire != null)
        {
            var a = PortPos(tempWire.Value.node, tempWire.Value.outSide);
            var b = tempWire.Value.cur;
            float dx = Mathf.Max(50, Mathf.Abs(b.x - a.x) / 2);
            var c1 = new Vector2(a.x + dx, a.y);
            var c2 = new Vector2(b.x - dx, b.y);
            // Painter2D 에 점선이 없다 — 짧은 구간을 번갈아 그린다
            p.strokeColor = GdEnum.Sel;
            p.lineWidth = 2;
            const int SEG = 22;
            for (int i = 0; i < SEG; i += 2)
            {
                p.BeginPath();
                p.MoveTo(BezierAt(a, c1, c2, b, i / (float)SEG));
                p.LineTo(BezierAt(a, c1, c2, b, (i + 1) / (float)SEG));
                p.Stroke();
            }
        }
    }

    void Render()
    {
        ApplyViewTransform();
        RenderBands();
        RenderNodes();
        ComputeAnchors();
        RenderEdgeLabels();
        FitLayers();
        edgeLayer.MarkDirtyRepaint();
        if (statLabel != null)
            statLabel.text = $"아이템 {nodes.Values.Count(n => n.kind == "item")} · 레시피 {nodes.Values.Count(n => n.kind == "recipe")} · 연결 {edges.Count}";
        RenderWarnings();
        win.RefreshSharedStat();
    }

    void FitLayers()
    {
        float mx = 1200, my = 800;
        foreach (var n in nodes.Values) { mx = Mathf.Max(mx, n.x + 400); my = Mathf.Max(my, n.y + 300); }
        edgeLayer.style.width = mx;
        edgeLayer.style.height = my;
    }

    // ── 밴드 (티어 구획·타입 행) ──
    void RenderBands()
    {
        bandsHost.Clear();
        if (bands == null || bands.rows.Count == 0) return;
        float top = bands.rows[0].y - 24;
        var bot = bands.rows[^1];
        float colH = bot.y + bot.h + 8 - top;

        for (int i = 0; i < bands.tiers.Count; i++)
        {
            var t = bands.tiers[i];
            bandsHost.Add(new VisualElement { pickingMode = PickingMode.Ignore, style = {
                position = Position.Absolute, left = t.x, top = top, width = t.w, height = colH,
                backgroundColor = new Color(1, 1, 1, i % 2 == 1 ? 0.028f : 0.012f) } });
        }
        foreach (var x in bands.seps)
            bandsHost.Add(new VisualElement { pickingMode = PickingMode.Ignore, style = {
                position = Position.Absolute, left = x, top = top, width = 1, height = colH,
                backgroundColor = GdEnum.FromHex("#16233A") } });
        for (int i = 0; i < bands.rows.Count; i++)
        {
            var r = bands.rows[i];
            bandsHost.Add(new VisualElement { pickingMode = PickingMode.Ignore, style = {
                position = Position.Absolute, left = -184, top = r.y - 24, width = r.width + 184, height = r.h + 8,
                backgroundColor = new Color(1, 1, 1, i % 2 == 1 ? 0.02f : 0f),
                borderTopWidth = 1, borderTopColor = new Color(1, 1, 1, 0.05f) } });
            bandsHost.Add(new Label(r.label) { pickingMode = PickingMode.Ignore, style = {
                position = Position.Absolute, left = -176, top = r.y - 22, color = GdEnum.Faint, fontSize = 11 } });
        }
        foreach (var t in bands.tiers)
            bandsHost.Add(new Label(t.label) { pickingMode = PickingMode.Ignore, style = {
                position = Position.Absolute, left = t.x, top = top - 26, width = t.w, color = GdEnum.Muted,
                fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, unityTextAlign = TextAnchor.MiddleCenter } });
        foreach (var s in bands.steps)
            bandsHost.Add(new Label(s.label) { pickingMode = PickingMode.Ignore, style = {
                position = Position.Absolute, left = s.x, top = top - 4, color = GdEnum.Faint, fontSize = 10 } });
    }

    // 스크롤해도 어느 티어·타입인지 보이게 화면에 고정하는 머리글
    void RenderSticky()
    {
        if (stickyCols == null) return;
        stickyCols.Clear();
        stickyRows.Clear();
        if (bands == null) return;
        float k = viewK, W = wrap.resolvedStyle.width, H = wrap.resolvedStyle.height;
        foreach (var t in bands.tiers)
        {
            float x = t.x * k + viewPos.x, w = t.w * k;
            if (x + w < 0 || x > W) continue;
            float cx = Mathf.Max(6, Mathf.Min(x, W - 6));
            float cw = Mathf.Min(x + w, W - 6) - cx;
            if (cw < 40) continue;
            stickyCols.Add(new Label(t.label) { pickingMode = PickingMode.Ignore, style = {
                position = Position.Absolute, left = cx, width = cw, top = 2,
                color = GdEnum.Muted, fontSize = 11, unityTextAlign = TextAnchor.MiddleCenter,
                backgroundColor = new Color(0.05f, 0.09f, 0.15f, 0.85f) } });
        }
        foreach (var r in bands.rows)
        {
            float y = (r.y - 24) * k + viewPos.y, h = (r.h + 8) * k;
            if (y + h < 0 || y > H) continue;
            float cy = Mathf.Max(2, Mathf.Min(y, H - 22));
            stickyRows.Add(new Label(r.label) { pickingMode = PickingMode.Ignore, style = {
                position = Position.Absolute, left = 4, top = cy, color = GdEnum.Faint, fontSize = 11,
                backgroundColor = new Color(0.05f, 0.09f, 0.15f, 0.85f) } });
        }
    }

    // ── 노드 ──
    void RenderNodes()
    {
        nodesHost.Clear();
        nodeEls.Clear();
        foreach (var n in nodes.Values)
        {
            var el = BuildNodeElement(n);
            nodesHost.Add(el);
            nodeEls[n.id] = el;
        }
        // 첫 프레임에는 요소 높이가 아직 없다(추정치 사용) — 레이아웃이 끝난 뒤
        // 실측 높이로 접점·라벨·선을 다시 잡는다. 원본의 rAF x2 재렌더에 해당.
        nodesHost.schedule.Execute(RefreshEdgeGeometry).ExecuteLater(0);
        nodesHost.schedule.Execute(RefreshEdgeGeometry).ExecuteLater(40);
    }

    void RefreshEdgeGeometry()
    {
        foreach (var (id, el) in nodeEls)
            if (el.resolvedStyle.height > 0) measuredH[id] = el.resolvedStyle.height;
        ComputeAnchors();
        RepositionLabels();
        edgeLayer.MarkDirtyRepaint();
    }

    VisualElement BuildNodeElement(GNode n)
    {
        bool sel = selection is { type: "node" } s && s.id == n.id;
        var lineC = GdEnum.LineColor(n.data.line ?? "None");
        // 원본: 아이템 = 주황 .65 / 레시피 = 시안 .65 — 테두리에 계통색을 쓰지 않는다(색은 line 전용)
        var kindC = n.kind == "item" ? GdEnum.ItemC : GdEnum.RecipeC;
        var borderC = sel ? GdEnum.Sel : new Color(kindC.r, kindC.g, kindC.b, 0.65f);

        var el = new VisualElement { userData = n.id, style = {
            position = Position.Absolute, left = n.x, top = n.y, width = NODE_W,
            backgroundColor = GdEnum.Panel,
            borderTopWidth = sel ? 2 : 1.5f, borderBottomWidth = sel ? 2 : 1.5f,
            borderLeftWidth = sel ? 2 : 1.5f, borderRightWidth = sel ? 2 : 1.5f,
            borderTopColor = borderC, borderBottomColor = borderC,
            borderLeftColor = borderC, borderRightColor = borderC,
            borderTopLeftRadius = 9, borderTopRightRadius = 9,
            borderBottomLeftRadius = 9, borderBottomRightRadius = 9 } };
        el.AddToClassList("gd-node");

        // head — 드래그 핸들. kind색 텍스트 + 옅은 kind색 배경 (원본 .head)
        var head = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center,
            height = HEAD_H, paddingLeft = 10, paddingRight = 8,
            backgroundColor = new Color(kindC.r, kindC.g, kindC.b, 0.08f),
            borderBottomWidth = 1, borderBottomColor = GdEnum.Line,
            borderTopLeftRadius = 8, borderTopRightRadius = 8 } };
        head.AddToClassList("gd-head");
        el.Add(head);
        head.Add(new Label(n.kind == "item" ? "ITEM" : "RECIPE") { pickingMode = PickingMode.Ignore, style = {
            fontSize = 10, color = kindC, marginRight = 6, paddingLeft = 5, paddingRight = 5, opacity = 0.8f,
            borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
            borderTopColor = kindC, borderBottomColor = kindC, borderLeftColor = kindC, borderRightColor = kindC,
            borderTopLeftRadius = 3, borderTopRightRadius = 3, borderBottomLeftRadius = 3, borderBottomRightRadius = 3 } });
        head.Add(new Label(n.data.displayName) { pickingMode = PickingMode.Ignore, style = {
            color = kindC, fontSize = 12.5f, unityFontStyleAndWeight = FontStyle.Bold,
            flexGrow = 1, overflow = Overflow.Hidden } });
        if (n.kind == "recipe")
        {
            var exp = new Label(n.expanded ? "▾" : "▸") { style = { color = GdEnum.Muted, fontSize = 12,
                paddingLeft = 4, paddingRight = 2 }, tooltip = "재료 펼치기" };
            exp.RegisterCallback<PointerDownEvent>(e =>
            {
                n.expanded = !n.expanded;
                measuredH.Remove(n.id);
                Render();
                Push();
                e.StopPropagation();
            });
            head.Add(exp);
        }

        // body
        var body = new VisualElement { pickingMode = PickingMode.Ignore, style = { paddingLeft = 8, paddingRight = 8, paddingTop = 5, paddingBottom = 3 } };
        el.Add(body);
        void Row(string key, string val, Color? valColor = null, string suffix = null)
        {
            var r = new VisualElement { pickingMode = PickingMode.Ignore, style = { flexDirection = FlexDirection.Row, height = ROW_H } };
            r.Add(new Label(key) { pickingMode = PickingMode.Ignore, style = { color = GdEnum.Faint, fontSize = 11, width = 52 } });
            r.Add(new Label(val) { pickingMode = PickingMode.Ignore, style = { color = valColor ?? GdEnum.Text, fontSize = 11,
                unityFontStyleAndWeight = FontStyle.Bold } });
            if (suffix != null)
                r.Add(new Label(suffix) { pickingMode = PickingMode.Ignore, style = { color = GdEnum.Muted, fontSize = 11, marginLeft = 4 } });
            body.Add(r);
        }
        if (n.kind == "item")
        {
            Row("type", n.data.type, null, GdEnum.TypeKo(n.data.type));
            Row("line", n.data.line ?? "None", lineC, GdEnum.LineKo(n.data.line ?? "None"));
        }
        else if (!n.expanded)
        {
            int ins = edges.Values.Count(e => e.to == n.id), outs = edges.Values.Count(e => e.from == n.id);
            Row("재료", $"{ins}종 → {outs}");
            Row("tier / time", $"{n.data.tier} · {n.data.craftTime}s");
        }
        else
        {
            // 펼친 본문 — 재료 → 결과물, 수량·계통색
            var ins = edges.Values.Where(e => e.to == n.id).ToList();
            var outs = edges.Values.Where(e => e.from == n.id).ToList();
            void Mat(GEdge e, string other, bool strong)
            {
                var it = nodes.GetValueOrDefault(other);
                var r = new VisualElement { pickingMode = PickingMode.Ignore, style = { flexDirection = FlexDirection.Row,
                    alignItems = Align.Center, height = ROW_H } };
                r.Add(new VisualElement { pickingMode = PickingMode.Ignore, style = { width = 8, height = 8,
                    backgroundColor = it != null ? GdEnum.LineColor(it.data.line ?? "None") : GdEnum.Faint, marginRight = 5 } });
                r.Add(new Label(it?.data.displayName ?? "?") { pickingMode = PickingMode.Ignore, style = {
                    color = GdEnum.Text, fontSize = 11, flexGrow = 1,
                    unityFontStyleAndWeight = strong ? FontStyle.Bold : FontStyle.Normal } });
                var amtL = new Label(e.amount.ToString()) { pickingMode = PickingMode.Ignore,
                    style = { color = GdEnum.Text, fontSize = 11.5f } };
                Mono(amtL);
                r.Add(amtL);
                body.Add(r);
            }
            foreach (var e in ins) Mat(e, e.from, false);
            if (ins.Count > 0 && outs.Count > 0)
                body.Add(new Label($"↓ {n.data.craftTime}s") { pickingMode = PickingMode.Ignore, style = {
                    color = GdEnum.Faint, fontSize = 10, unityTextAlign = TextAnchor.MiddleCenter, height = ROW_H } });
            foreach (var e in outs) Mat(e, e.to, true);
            if (ins.Count == 0 && outs.Count == 0)
                body.Add(new Label($"재료·결과물 없음 · {n.data.craftTime}s") { pickingMode = PickingMode.Ignore, style = {
                    color = GdEnum.Faint, fontSize = 10, height = ROW_H } });
        }

        // idline + 경고
        var idLbl = new Label((n.kind == "item" ? "Item:" : "Recipe:") + Sanitize(n.data.name)) { pickingMode = PickingMode.Ignore,
            style = { color = GdEnum.Faint, fontSize = 11, paddingLeft = 10, paddingRight = 10, paddingBottom = 8 } };
        Mono(idLbl);
        el.Add(idLbl);
        var warn = NodeWarning(n);
        if (!string.IsNullOrEmpty(warn))
            el.Add(new Label("⚠ " + warn) { pickingMode = PickingMode.Ignore, style = { color = GdEnum.Warn, fontSize = 9,
                paddingLeft = 8, paddingBottom = 4, whiteSpace = WhiteSpace.Normal } });

        // 포트
        VisualElement Port(bool outSide)
        {
            var portC = new Color(kindC.r, kindC.g, kindC.b, 0.8f);
            var pt = new VisualElement { style = { position = Position.Absolute, width = 18, height = 18,
                top = Length.Percent(50), marginTop = -9,
                backgroundColor = GdEnum.Panel2,
                borderTopLeftRadius = 9, borderTopRightRadius = 9, borderBottomLeftRadius = 9, borderBottomRightRadius = 9,
                borderTopWidth = 2, borderBottomWidth = 2, borderLeftWidth = 2, borderRightWidth = 2,
                borderTopColor = portC, borderBottomColor = portC,
                borderLeftColor = portC, borderRightColor = portC },
                tooltip = outSide ? (n.kind == "item" ? "레시피 입력으로 드래그" : "결과물(outputs)") : (n.kind == "item" ? "레시피 출력 연결" : "재료(inputs)") };
            if (outSide) pt.style.right = -11; else pt.style.left = -11;
            pt.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                tempWire = (n.id, outSide, ToWorld(e.position));
                wrap.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            return pt;
        }
        el.Add(Port(false));
        el.Add(Port(true));

        // 헤더 드래그 = 이동, 본체 클릭 = 선택, 호버 = 간선 강조
        head.RegisterCallback<PointerDownEvent>(e =>
        {
            if (e.button != 0) return;
            var p = ToWorld(e.position);
            dragNode = (n.id, p.x - n.x, p.y - n.y);
            Select(("node", n.id));
            wrap.CapturePointer(e.pointerId);
            e.StopPropagation();
        });
        el.RegisterCallback<PointerDownEvent>(e =>
        {
            if (e.button != 0) return;
            Select(("node", n.id));
            e.StopPropagation();
        });
        el.RegisterCallback<PointerEnterEvent>(_ => { if (hoverNode != n.id) { hoverNode = n.id; edgeLayer.MarkDirtyRepaint(); UpdateLabelDim(); } });
        el.RegisterCallback<PointerLeaveEvent>(_ => { if (hoverNode == n.id) { hoverNode = null; edgeLayer.MarkDirtyRepaint(); UpdateLabelDim(); } });
        return el;
    }

    // ── 간선 수량 라벨 (클릭 = 선택, 더블클릭 = 수량 편집 포커스) ──
    readonly Dictionary<string, Label> edgeLabels = new();
    (string id, double t) lastEdgeClick = (null, 0);

    void RenderEdgeLabels()
    {
        labelHost.Clear();
        edgeLabels.Clear();
        foreach (var e in edges.Values)
        {
            if (!nodes.ContainsKey(e.from) || !nodes.ContainsKey(e.to)) continue;
            bool isIn = nodes[e.from].kind == "item";
            bool selc = selection is { type: "edge" } s && s.id == e.id;
            var col = selc ? GdEnum.Sel : isIn ? GdEnum.EdgeIn : GdEnum.EdgeOut;
            var g = EdgeGeom(e);
            // 라벨을 출발 쪽 38% 지점에 — 교차 지점에서 라벨끼리 겹치지 않는다
            var lp = BezierAt(g.a, g.c1, g.c2, g.b, 0.38f);
            var lbl = new Label("×" + e.amount) { userData = e.id, style = {
                position = Position.Absolute, left = lp.x - 18, top = lp.y - 12, width = 36, height = 24,
                unityTextAlign = TextAnchor.MiddleCenter, fontSize = 11, color = GdEnum.Text,
                backgroundColor = GdEnum.Panel2,
                borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                borderTopColor = col, borderBottomColor = col, borderLeftColor = col, borderRightColor = col,
                borderTopLeftRadius = 6, borderTopRightRadius = 6, borderBottomLeftRadius = 6, borderBottomRightRadius = 6 } };
            string eid = e.id;
            lbl.RegisterCallback<PointerDownEvent>(ev =>
            {
                if (ev.button != 0) return;
                double now = EditorApplication.timeSinceStartup;
                bool dbl = lastEdgeClick.id == eid && now - lastEdgeClick.t < 0.4;
                lastEdgeClick = (eid, now);
                Select(("edge", eid));
                if (dbl) FocusAmountField();
                ev.StopPropagation();
            });
            labelHost.Add(lbl);
            edgeLabels[e.id] = lbl;
        }
        UpdateLabelDim();
    }

    void UpdateLabelDim()
    {
        foreach (var (id, lbl) in edgeLabels)
        {
            var e = edges.GetValueOrDefault(id);
            if (e == null) continue;
            bool selc = selection is { type: "edge" } s && s.id == id;
            bool rel = hoverNode == null || e.from == hoverNode || e.to == hoverNode;
            lbl.style.opacity = selc ? 1f : rel ? 0.97f : 0.12f;
            bool isIn = nodes.TryGetValue(e.from, out var f) && f.kind == "item";
            var col = selc ? GdEnum.Sel : isIn ? GdEnum.EdgeIn : GdEnum.EdgeOut;
            lbl.style.borderTopColor = lbl.style.borderBottomColor = lbl.style.borderLeftColor = lbl.style.borderRightColor = col;
        }
    }

    // ═════════════ 인터랙션 (graph-panel 하단 대응) ═════════════

    (string id, float ox, float oy)? dragNode;
    (Vector2 s, Vector2 v)? dragPan;

    Vector2 ToWorld(Vector2 panelPos)
    {
        var local = wrap.WorldToLocal(panelPos);
        return (local - viewPos) / viewK;
    }

    void RegisterCanvasInput()
    {
        wrap.focusable = true;   // 키 입력(Ctrl+Z/Y/S·Delete)이 창에 닿으려면 패널 안에 포커스가 있어야 한다
        wrap.RegisterCallback<PointerDownEvent>(e =>
        {
            wrap.Focus();
            if (e.button == 2)   // 휠클릭 = 화면 이동
            {
                dragPan = (e.position, viewPos);
                wrap.CapturePointer(e.pointerId);
                e.StopPropagation();
                return;
            }
            if (e.button == 0)
            {
                // 간선 곡선 근처 클릭 — 라벨 밖이어도 선을 집을 수 있게 거리 검사
                var wpt = ToWorld(e.position);
                var hit = EdgeNear(wpt, 9f / viewK);
                if (hit != null)
                {
                    double now = EditorApplication.timeSinceStartup;
                    bool dbl = lastEdgeClick.id == hit && now - lastEdgeClick.t < 0.4;
                    lastEdgeClick = (hit, now);
                    Select(("edge", hit));
                    if (dbl) FocusAmountField();
                    return;
                }
                Select(null);
            }
        });
        wrap.RegisterCallback<PointerMoveEvent>(e =>
        {
            if (tempWire != null)
            {
                tempWire = (tempWire.Value.node, tempWire.Value.outSide, ToWorld(e.position));
                edgeLayer.MarkDirtyRepaint();
            }
            else if (dragNode != null && nodes.TryGetValue(dragNode.Value.id, out var n))
            {
                var p = ToWorld(e.position);
                n.x = p.x - dragNode.Value.ox;
                n.y = p.y - dragNode.Value.oy;
                if (nodeEls.TryGetValue(n.id, out var el)) { el.style.left = n.x; el.style.top = n.y; }
                ComputeAnchors();
                RepositionLabels();
                edgeLayer.MarkDirtyRepaint();
            }
            else if (dragPan != null)
            {
                viewPos = dragPan.Value.v + ((Vector2)e.position - dragPan.Value.s);
                ApplyViewTransform();
            }
        });
        wrap.RegisterCallback<PointerUpEvent>(e =>
        {
            if (wrap.HasPointerCapture(e.pointerId)) wrap.ReleasePointer(e.pointerId);
            if (tempWire != null)
            {
                var drop = PortAt(ToWorld(e.position));
                if (drop != null && drop.Value.node != tempWire.Value.node)
                {
                    // 방향 정규화: out → in
                    string from = tempWire.Value.node, to = drop.Value.node;
                    if (!tempWire.Value.outSide) (from, to) = (to, from);
                    var created = AddEdge(from, to);
                    if (created != null) Select(("edge", created));
                }
                tempWire = null;
                Render();
            }
            if (dragNode != null)
            {
                dragNode = null;
                Render();
                Push();   // 이동도 언두 한 단위
            }
            dragPan = null;
        });
        wrap.RegisterCallback<WheelEvent>(e =>
        {
            var local = wrap.WorldToLocal(e.mousePosition);
            float k2 = Mathf.Clamp(viewK * (e.delta.y < 0 ? 1.1f : 0.9f), 0.3f, 2f);
            viewPos = local - (local - viewPos) * (k2 / viewK);
            viewK = k2;
            ApplyViewTransform();
            e.StopPropagation();
        });

        // 우클릭 메뉴 — 빈 곳: 추가/정렬 · 노드: 삭제 (간선은 라벨 우클릭 대신 선택 후 Delete)
        wrap.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            var wpt = ToWorld(evt.mousePosition);
            string nodeId = null;
            for (var ve = evt.target as VisualElement; ve != null; ve = ve.parent)
                if (ve.ClassListContains("gd-node")) { nodeId = (string)ve.userData; break; }
            if (nodeId != null)
            {
                Select(("node", nodeId));
                evt.menu.AppendAction("이 노드 삭제", _ => RemoveSelection());
            }
            else
            {
                var hit = EdgeNear(wpt, 9f / viewK);
                if (hit != null)
                {
                    Select(("edge", hit));
                    evt.menu.AppendAction("이 연결 삭제", _ => RemoveSelection());
                }
                else
                {
                    evt.menu.AppendAction("＋ 아이템 추가", _ => AddNode("item", wpt.x, wpt.y));
                    evt.menu.AppendAction("＋ 레시피 추가", _ => AddNode("recipe", wpt.x, wpt.y));
                    evt.menu.AppendSeparator();
                    evt.menu.AppendAction("자동 정렬", _ => { AutoLayout(); Render(); Push(); });
                }
            }
        }));
    }

    void RepositionLabels()
    {
        foreach (var (id, lbl) in edgeLabels)
        {
            var e = edges.GetValueOrDefault(id);
            if (e == null) continue;
            var g = EdgeGeom(e);
            var lp = BezierAt(g.a, g.c1, g.c2, g.b, 0.38f);
            lbl.style.left = lp.x - 18;
            lbl.style.top = lp.y - 12;
        }
    }

    string EdgeNear(Vector2 wpt, float maxDist)
    {
        string best = null;
        float bestD = maxDist;
        foreach (var e in edges.Values)
        {
            if (!nodes.ContainsKey(e.from) || !nodes.ContainsKey(e.to)) continue;
            var g = EdgeGeom(e);
            for (int i = 0; i <= 24; i++)
            {
                float d = Vector2.Distance(wpt, BezierAt(g.a, g.c1, g.c2, g.b, i / 24f));
                if (d < bestD) { bestD = d; best = e.id; }
            }
        }
        return best;
    }

    (string node, bool outSide)? PortAt(Vector2 wpt)
    {
        foreach (var n in nodes.Values)
        {
            var b = NodeBox(n.id);
            if (Vector2.Distance(wpt, new Vector2(b.x, b.y + b.height / 2)) < 14 / viewK + 6) return (n.id, false);
            if (Vector2.Distance(wpt, new Vector2(b.x + b.width, b.y + b.height / 2)) < 14 / viewK + 6) return (n.id, true);
        }
        return null;
    }

    // ═════════════ 사이드 패널 (renderSide) ═════════════

    void Select((string type, string id)? sel)
    {
        var old = selection;
        selection = sel;
        // 노드를 갈아치우지 않는다 — 리렌더는 높이 재측정을 부르고 선이 출렁인다
        void Restyle((string type, string id)? which)
        {
            if (which is not { type: "node" } w2) return;
            if (!nodes.TryGetValue(w2.id, out var n) || !nodeEls.TryGetValue(w2.id, out var el)) return;
            ApplyNodeBorder(el, n, selection is { type: "node" } cur && cur.id == w2.id);
        }
        Restyle(old);
        Restyle(sel);
        edgeLayer.MarkDirtyRepaint();
        UpdateLabelDim();
        RenderSide();
    }

    void ApplyNodeBorder(VisualElement el, GNode n, bool selected)
    {
        var kindC = n.kind == "item" ? GdEnum.ItemC : GdEnum.RecipeC;
        var c = selected ? GdEnum.Sel : new Color(kindC.r, kindC.g, kindC.b, 0.65f);
        float w = selected ? 2f : 1.5f;
        el.style.borderTopColor = el.style.borderBottomColor = el.style.borderLeftColor = el.style.borderRightColor = c;
        el.style.borderTopWidth = el.style.borderBottomWidth = el.style.borderLeftWidth = el.style.borderRightWidth = w;
    }

    FloatField amountField;
    void FocusAmountField() { amountField?.Focus(); amountField?.Q("unity-text-input")?.Focus(); }

    void RenderSide()
    {
        if (sideBody == null) return;
        sideBody.Clear();
        amountField = null;

        if (selection == null)
        {
            sideTitle.text = "선택 없음";
            sideBody.Add(new Label("노드를 클릭해 속성을 편집합니다.\n\n아이템 출력 포트 → 레시피 입력 포트로 드래그 = 재료(inputs)\n레시피 출력 포트 → 아이템 입력 포트로 드래그 = 결과물(outputs)")
            { style = { color = GdEnum.Faint, fontSize = 12, whiteSpace = WhiteSpace.Normal } });
            return;
        }

        var (type, id) = selection.Value;
        if (type == "edge")
        {
            var e = edges.GetValueOrDefault(id);
            if (e == null) { selection = null; RenderSide(); return; }
            var f = nodes[e.from];
            var t = nodes[e.to];
            bool isIn = f.kind == "item";
            sideTitle.text = isIn ? "재료 연결 (input)" : "결과물 연결 (output)";
            sideBody.Add(new Label(isIn ? "아이템 → 레시피" : "레시피 → 아이템") { style = { color = GdEnum.Faint, fontSize = 11 } });
            sideBody.Add(new Label($"{f.data.displayName} → {t.data.displayName}") { style = { color = GdEnum.Muted, fontSize = 12, marginBottom = 6 } });
            var amount = new FloatField { value = e.amount };
            amount.RegisterValueChangedCallback(ev => { e.amount = Mathf.Max(1, Mathf.RoundToInt(ev.newValue)); Render(); });
            sideBody.Add(Field("amount (수량)", amount));
            amountField = amount;
            var delE = new Button(RemoveSelection) { text = "연결 삭제 (Delete)" };
            delE.AddToClassList("gd-btn-warn");
            sideBody.Add(delE);
            return;
        }

        var n = nodes.GetValueOrDefault(id);
        if (n == null) { selection = null; RenderSide(); return; }
        sideTitle.text = n.kind == "item" ? "아이템 속성" : "레시피 속성";
        var d = n.data;

        sideBody.Add(Text($"id 이름 (\"{(n.kind == "item" ? "Item" : "Recipe")}:\" 자동 접두)", d.name, v => { d.name = v; Render(); }));
        sideBody.Add(Text("displayName", d.displayName, v => { d.displayName = v; Render(); }));
        sideBody.Add(Text("description", d.description, v => { d.description = v; }, multiline: true));

        if (n.kind == "item")
        {
            var typeChoices = GdEnum.ItemTypes.Select(t => $"{t.v} — {t.ko} · {t.desc}").ToList();
            int typeIdx = Array.FindIndex(GdEnum.ItemTypes, t => t.v == d.type);
            if (typeIdx < 0) { typeChoices.Add($"{d.type} — 알 수 없음"); typeIdx = typeChoices.Count - 1; }
            sideBody.Add(Drop("type — enum ItemType (분류 태그)", typeChoices, typeIdx, i =>
            {
                if (i < GdEnum.ItemTypes.Length) d.type = GdEnum.ItemTypes[i].v;
                Render();
                RenderSide();   // 모듈 섹션이 즉시 나타나거나 사라져야 한다
            }));

            var lineChoices = GdEnum.ItemLines.Select(l => $"{l.v} — {l.ko}").ToList();
            int lineIdx = Array.FindIndex(GdEnum.ItemLines, l => l.v == (d.line ?? "None"));
            if (lineIdx < 0) { lineChoices.Add($"{d.line} — 알 수 없음"); lineIdx = lineChoices.Count - 1; }
            sideBody.Add(Drop("line — enum ItemLine (계통)", lineChoices, lineIdx, i =>
            {
                if (i < GdEnum.ItemLines.Length) d.line = GdEnum.ItemLines[i].v;
                Render();
            }));

            sideBody.Add(Int("maxStack (한 슬롯 최대 개수 — 무기·설치물은 1)", d.maxStack,
                v => { d.maxStack = Mathf.Max(1, v); Render(); }));

            // icon — guid 가 파일을 특정하고 이름이 아틀라스 안의 스프라이트를 고른다
            var iconRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var iconPrev = new Image { style = { width = 34, height = 34, marginRight = 4 },
                sprite = FindSprite(d.iconGuid, d.icon) };
            var iconPick = new UnityEditor.UIElements.ObjectField { objectType = typeof(Sprite),
                allowSceneObjects = false, tooltip = "JSON 에는 guid 와 스프라이트 이름이 함께 저장된다",
                value = FindSprite(d.iconGuid, d.icon), style = { flexGrow = 1 } };
            iconPick.RegisterValueChangedCallback(ev =>
            {
                (d.icon, d.iconGuid) = IconRefOf(ev.newValue as Sprite);
                iconPrev.sprite = ev.newValue as Sprite;
                Render();
            });
            iconRow.Add(iconPrev);
            iconRow.Add(iconPick);
            sideBody.Add(new Label("icon (guid + 이름으로 저장 — 비우면 기존 유지)") { style = { color = GdEnum.Faint, fontSize = 11, marginTop = 4 } });
            sideBody.Add(iconRow);

            BuildModuleSection(d);
        }
        else
        {
            sideBody.Add(Int("tier (해금되는 코어 수리 단계)", d.tier, v => { d.tier = v; Render(); }));
            sideBody.Add(Num("craftTime (초)", d.craftTime, v => { d.craftTime = Mathf.Max(0, v); Render(); }));
        }

        var delN = new Button(RemoveSelection) { text = "노드 삭제 (Delete)", style = { marginTop = 8 } };
        delN.AddToClassList("gd-btn-warn");
        sideBody.Add(delN);
    }

    // 아이템 역할 모듈 — type 이 Ammo/Weapon 이면 해당 모듈 필드가 붙는다 (moduleSection)
    void BuildModuleSection(GNodeData d)
    {
        if (d.type == "Ammo")
        {
            var ammoTtl = new Label("AMMOMODULE · 1발 명중 효과");
            ammoTtl.AddToClassList("gd-groupttl");
            sideBody.Add(ammoTtl);
            var effectIds = (win.root.effects ?? Array.Empty<GameDataImporter.EffectDto>())
                .Select(e => e.id).Where(s => !string.IsNullOrEmpty(s)).ToList();
            var holder = new VisualElement();
            sideBody.Add(holder);
            void Rebuild()
            {
                holder.Clear();
                if (d.attackEffects.Count == 0)
                    holder.Add(new Label("효과 없음 — 명중해도 아무 일도 일어나지 않습니다") { style = { color = GdEnum.Faint, fontSize = 11 } });
                for (int i = 0; i < d.attackEffects.Count; i++)
                {
                    var eff = d.attackEffects[i];
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                    var choices = new List<string>(effectIds);
                    if (!string.IsNullOrEmpty(eff.effect) && !choices.Contains(eff.effect)) choices.Add(eff.effect + " (없음)");
                    var pick = new DropdownField(choices, Mathf.Max(0, choices.IndexOf(eff.effect ?? ""))) { style = { flexGrow = 1 } };
                    if (!string.IsNullOrEmpty(eff.effect) && choices.Contains(eff.effect)) pick.SetValueWithoutNotify(eff.effect);
                    pick.RegisterValueChangedCallback(ev => { eff.effect = ev.newValue.Replace(" (없음)", ""); Render(); });
                    row.Add(pick);
                    var val = new FloatField { value = eff.value, style = { width = 56 } };
                    val.RegisterValueChangedCallback(ev => { eff.value = ev.newValue; Render(); });
                    row.Add(val);
                    int idx = i;
                    row.Add(new Button(() => { d.attackEffects.RemoveAt(idx); Rebuild(); Render(); }) { text = "✕" });
                    holder.Add(row);
                }
                float dmg = d.attackEffects.Where(e =>
                {
                    var ef = (win.root.effects ?? Array.Empty<GameDataImporter.EffectDto>()).FirstOrDefault(x => x.id == e.effect);
                    return ef != null && string.Equals(ef.kind, "Damage", StringComparison.OrdinalIgnoreCase);
                }).Sum(e => e.value);
                var foot = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                foot.Add(new Button(() =>
                {
                    d.attackEffects.Add(new GEff { effect = effectIds.FirstOrDefault() ?? "Effect:Damage", value = 10 });
                    Rebuild(); Render();
                }) { text = "+ 효과" });
                if (dmg > 0) foot.Add(new Label($"1발 피해 {dmg}") { style = { color = GdEnum.Muted, fontSize = 11, marginLeft = 6 } });
                holder.Add(foot);
            }
            Rebuild();

            var balTtl = new Label("탄도 · 발사기가 아니라 탄이 갖는다");
            balTtl.AddToClassList("gd-groupttl");
            sideBody.Add(balTtl);
            var balGrid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            balGrid.Add(MiniCell("속도", d.speed, v => d.speed = Mathf.Max(0, v), "m/s. 0 이면 Projectile 총이 못 쏜다", 50f));
            balGrid.Add(MiniCell("중력", d.gravity, v => d.gravity = Mathf.Max(0, v), "0 = 직선. 유탄은 9.8 로 포물선", 50f));
            balGrid.Add(MiniCell("폭발 R", d.explosionRadius, v => d.explosionRadius = Mathf.Max(0, v), "0 = 단일 대상", 50f));
            balGrid.Add(MiniCell("수명", d.lifetime, v => d.lifetime = Mathf.Max(0, v), "초. 이 시간이 지나면 사라진다", 50f));
            balGrid.Add(MiniCell("관통", d.pierce, v => d.pierce = Mathf.Max(0, Mathf.RoundToInt(v)), "첫 대상 뒤로 더 뚫는 수. 0 = 맞으면 멈춤", 50f));
            sideBody.Add(balGrid);
        }
        if (d.type == "Weapon")
        {
            var wpnTtl = new Label("WEAPONMODULE · 장착할 총");
            wpnTtl.AddToClassList("gd-groupttl");
            sideBody.Add(wpnTtl);
            var guns = (win.root.guns ?? Array.Empty<GameDataImporter.GunDto>())
                .Select(g => g.id).Where(s => !string.IsNullOrEmpty(s)).ToList();
            var choices = new List<string> { "(없음)" };
            choices.AddRange(guns);
            if (!string.IsNullOrEmpty(d.gun) && !guns.Contains(d.gun)) choices.Add(d.gun + " (없음)");
            var pick = new DropdownField(choices, string.IsNullOrEmpty(d.gun) ? 0 : Mathf.Max(0, choices.IndexOf(d.gun)));
            pick.RegisterValueChangedCallback(ev =>
            {
                d.gun = ev.newValue == "(없음)" ? "" : ev.newValue.Replace(" (없음)", "");
                Render();
            });
            sideBody.Add(pick);
            if (guns.Count == 0)
                sideBody.Add(new Label("전투 탭에서 총을 먼저 만드세요") { style = { color = GdEnum.Faint, fontSize = 11 } });
        }
    }

    static readonly Dictionary<string, Sprite> spriteCache = new();

    /// <summary>
    /// guid 가 <b>파일</b>을, 이름이 그 안의 <b>어느 스프라이트</b>인지 고른다 — 스프라이트는
    /// 아틀라스의 하위 에셋일 수 있어 guid 하나로는 특정되지 않는다.
    /// 임포터의 FindSprite 와 같은 우선순위여야 에디터에 보이는 것과 임포트 결과가 어긋나지 않는다.
    /// </summary>
    static Sprite FindSprite(string guid, string name)
    {
        if (!string.IsNullOrEmpty(guid))
        {
            var key = guid + "|" + name;
            if (spriteCache.TryGetValue(key, out var hit) && hit != null) return hit;

            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path))
            {
                Sprite first = null;
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (sub is not Sprite sp) continue;
                    if (!string.IsNullOrEmpty(name) && sp.name == name) return spriteCache[key] = sp;
                    first ??= sp;
                }
                if (first != null) return spriteCache[key] = first;
            }
        }
        return FindSpriteByName(name);
    }

    static Sprite FindSpriteByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (spriteCache.TryGetValue(name, out var c) && c != null) return c;
        foreach (var guid in AssetDatabase.FindAssets($"{name} t:Sprite"))
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                if (sub is Sprite sp && sp.name == name) return spriteCache[name] = sp;
        return null;
    }

    /// <summary>픽커에서 고른 스프라이트 → (스프라이트 이름, 담긴 에셋의 guid).</summary>
    static (string name, string guid) IconRefOf(Sprite sprite)
    {
        if (sprite == null) return ("", "");
        return (sprite.name, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sprite)));
    }

    // ═════════════ 검증 (nodeWarning / renderWarnings) ═════════════

    string NodeWarning(GNode n)
    {
        if (string.IsNullOrEmpty(Sanitize(n.data.name))) return "id 이름이 비어 있음";
        if (nodes.Values.Count(o => o.kind == n.kind && Sanitize(o.data.name) == Sanitize(n.data.name)) > 1) return "id 중복";
        if (n.kind == "recipe")
        {
            if (!edges.Values.Any(e => e.to == n.id)) return "재료(inputs) 없음 — 임포터가 스킵함";
            if (!edges.Values.Any(e => e.from == n.id)) return "결과물(outputs) 없음 — 임포터가 스킵함";
        }
        if (n.kind == "item")
        {
            if (!GdEnum.ItemTypes.Any(t => t.v == n.data.type)) return $"ItemType에 없는 값 \"{n.data.type}\" — 임포터가 거부함";
            if (n.data.type == "Ammo")
            {
                if (n.data.attackEffects.Count == 0) return "탄약인데 명중 효과가 없습니다 — AmmoModule 을 채우세요";
                if (!(n.data.speed > 0)) return "탄약 속도가 0 입니다 — Projectile 총이 쏘지 못합니다";
                if (!(n.data.lifetime > 0)) return "탄약 수명이 0 입니다 — 발사되자마자 사라집니다";
            }
            if (n.data.type == "Weapon" && string.IsNullOrEmpty(n.data.gun))
                return "무기인데 연결된 총이 없습니다 — WeaponModule 을 지정하세요";
            if (!GdEnum.ItemLines.Any(l => l.v == (n.data.line ?? "None"))) return $"ItemLine에 없는 값 \"{n.data.line}\" — 임포터가 거부함";
            if ((n.data.displayName ?? "").EndsWith("(미정의)")) return "임포트 시 미정의였던 아이템 — 속성 확인";
        }
        return "";
    }

    void RenderWarnings()
    {
        if (warnBox == null) return;
        warnBox.Clear();
        var ws = new List<string>();
        foreach (var n in nodes.Values)
        {
            var w = NodeWarning(n);
            if (!string.IsNullOrEmpty(w)) ws.Add($"[{(n.kind == "item" ? "아이템" : "레시피")}] {n.data.displayName}: {w}");
        }
        if (ws.Count == 0)
            warnBox.Add(OkMsg("✓ 검증 통과 — 임포터 안전장치에 걸릴 항목 없음"));
        else
        {
            warnBox.Add(H3("경고"));
            foreach (var w in ws)
                warnBox.Add(WarnItem(w));
        }
    }
}
#endif
