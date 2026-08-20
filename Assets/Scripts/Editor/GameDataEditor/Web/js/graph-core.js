// ═══════════════════════════════════════════════════════════
//  아이템·레시피 그래프 — 데이터 모델 · 레이아웃 · 렌더 · 히스토리
// ═══════════════════════════════════════════════════════════
const ITEM_TYPES = window.EdEnum.ITEM_TYPES;


// ───────────────────────── 데이터 모델 ─────────────────────────
// node: {id(내부), kind:'item'|'recipe', x, y, data:{...}}
// edge: {id, from(nodeId), to(nodeId), amount}  // from=item→to=recipe(입력) 또는 from=recipe→to=item(출력)
// enum ItemType (ItemDataSO.cs) 와 1:1. 순서 = enum 정수값이므로 함부로 재배열하지 말 것.
// 색은 지정하지 않는다 — 색은 ItemLine 전용. type은 묶음·정렬·필터로만 표현한다.
const TYPE_SET = new Set(ITEM_TYPES.map(t => t.v));

// enum ItemLine — 계통(어느 생산 라인 소속인가). type과 직교하는 별개 축이다.
const ITEM_LINES = [
  { v:"None",    ko:"미지정", c:"#8FA3C0" },
  { v:"Iron",    ko:"구조",   c:"#E8A54B" },
  { v:"Copper",  ko:"전자",   c:"#4FD8E0" },
  { v:"Crystal", ko:"동력",   c:"#B48CFF" },
  { v:"Beast",   ko:"괴수",   c:"#FF5D73" },
];
const LINE_SET = new Set(ITEM_LINES.map(l => l.v));
const lineOf = v => ITEM_LINES.find(l => l.v === v);
const lineKo = v => (lineOf(v) || {}).ko || "?";
const lineColor = v => (lineOf(v) || {}).c || "#8FA3C0";
const typeOf = v => ITEM_TYPES.find(t => t.v === v);
const typeKo = v => (typeOf(v) || {}).ko || "?";
let nodes = new Map(), edges = new Map(), seq = 1;
let selection = null; // {type:'node'|'edge', id}
let layoutBands = null;   // 정렬 격자의 티어 구획·타입 행

// ── 실행 취소 / 다시 실행 ──
// 스냅샷 방식: 그래프가 작아(수십 노드) 통째로 복사해도 부담이 없고,
// 변경 종류마다 역연산을 짜는 것보다 어긋날 여지가 없다.
const history = { past: [], future: [], last: null };
function snapshot(){
  return JSON.stringify({
    nodes: [...nodes.values()].map(n => ({...n, _model: undefined})),
    edges: [...edges.values()], seq,
    bands: layoutBands,          // 티어 구획·타입 라벨. 빠뜨리면 노드만 돌아가고 격자는 남는다
  });
}
function pushHistory(){
  const cur = snapshot();
  if(cur === history.last) return;      // 실제로 바뀐 게 없으면 쌓지 않는다
  if(history.last !== null) history.past.push(history.last);
  if(history.past.length > 80) history.past.shift();
  history.future.length = 0;            // 새 변경이 생기면 redo 는 버린다
  history.last = cur;
}
function restore(snap){
  const o = JSON.parse(snap);
  nodes = new Map(o.nodes.map(n => [n.id, n]));
  edges = new Map(o.edges.map(e => [e.id, e]));
  seq = o.seq;
  layoutBands = o.bands ?? null;
  if(selection && !(selection.type === "node" ? nodes.has(selection.id) : edges.has(selection.id)))
    selection = null;
  history.last = snap;
  render(); renderSide();
}
function undo(){
  if(!history.past.length) return;
  history.future.push(history.last);
  restore(history.past.pop());
}
function redo(){
  if(!history.future.length) return;
  history.past.push(history.last);
  restore(history.future.pop());
}
let view = {x:60, y:40, k:1};

const { esc, $, $$, field } = window.EdUtil;
const worldEl = $("#world"), nodesEl = $("#nodes"), edgesEl = $("#edges"), wrap = $("#canvas-wrap");

function uid(){ return "n" + (seq++); }
function sanitize(name){ return (name||"").replace(/[^A-Za-z0-9_가-힣]/g,""); }

function addNode(kind, x, y, data){
  const id = uid();
  const base = kind === "item"
    ? { name:"NewItem"+id, displayName:"새 아이템", description:"", type:"Part", line:"None", icon:"", attackEffects:[], speed:50, gravity:0, explosionRadius:0, lifetime:3, pierce:0, gun:"" }
    : { name:"NewRecipe"+id, displayName:"새 레시피", description:"", tier:1, craftTime:2.0 };
  nodes.set(id, { id, kind, x, y, data: Object.assign(base, data||{}) });
  render(); select({type:"node", id});
  pushHistory();
  return id;
}
function addEdge(fromId, toId, amount){
  const a = nodes.get(fromId), b = nodes.get(toId);
  if(!a || !b || a.kind === b.kind) return null;           // item↔recipe만 허용
  for(const e of edges.values()) if(e.from===fromId && e.to===toId) return null; // 중복 금지
  const id = uid();
  edges.set(id, { id, from: fromId, to: toId, amount: amount ?? 1 });
  render();
  pushHistory();
  return id;
}
function removeSelection(){
  if(!selection) return;
  if(selection.type === "edge") edges.delete(selection.id);
  else {
    for(const e of [...edges.values()]) if(e.from===selection.id || e.to===selection.id) edges.delete(e.id);
    nodes.delete(selection.id);
  }
  selection = null; render(); renderSide();
  pushHistory();
}

// ───────────────────────── 내보내기 / 불러오기 ─────────────────────────
function exportJson(){
  const items = [], recipes = [];
  for(const n of nodes.values()){
    if(n.kind === "item"){
      const it = { id:"Item:"+sanitize(n.data.name), displayName:n.data.displayName||n.data.name,
        description:n.data.description||"", type:n.data.type||"Part", line:n.data.line||"None", icon:n.data.icon||"" };
      if(n.data.type === "Ammo"){
        if((n.data.attackEffects||[]).length)
          it.attackEffects = n.data.attackEffects.map(e => ({ effect:e.effect, value:+e.value||0 }));
        // 탄도 — 0 이 정당한 값(직선·무폭발)이라 항상 내보낸다. 임포터는 음수를 "유지"로 본다
        it.speed = n.data.speed ?? 50;
        it.gravity = n.data.gravity ?? 0;
        it.explosionRadius = n.data.explosionRadius ?? 0;
        it.lifetime = n.data.lifetime ?? 3;
        it.pierce = n.data.pierce ?? 0;
      }
      if(n.data.type === "Weapon" && n.data.gun) it.gun = n.data.gun;
      items.push(it);
    }
  }
  for(const n of nodes.values()){
    if(n.kind !== "recipe") continue;
    const inputs = [], outputs = [];
    for(const e of edges.values()){
      if(e.to === n.id){ const it = nodes.get(e.from); if(it) inputs.push({ item:"Item:"+sanitize(it.data.name), amount:e.amount }); }
      if(e.from === n.id){ const it = nodes.get(e.to); if(it) outputs.push({ item:"Item:"+sanitize(it.data.name), amount:e.amount }); }
    }
    recipes.push({ id:"Recipe:"+sanitize(n.data.name), displayName:n.data.displayName||n.data.name,
      description:n.data.description||"", tier:+n.data.tier||0,
      craftTime:+n.data.craftTime||0, inputs, outputs });
  }
  return { items, recipes };
}
function importJson(obj){
  nodes.clear(); edges.clear(); selection = null;
  const itemNode = {}; // "Item:X" → nodeId
  (obj.items||[]).forEach(it => {
    const name = (it.id||"").replace(/^Item:/,"") || it.displayName;
    const id = uid();
    nodes.set(id, { id, kind:"item", x:0, y:0, data:{ name, displayName:it.displayName||name,
      description:it.description||"", type:it.type||"Part", line:it.line||"None", icon:it.icon||"",
      attackEffects: it.attackEffects || (it.damage>0 ? [{effect:"Effect:Damage", value:it.damage}] : []),
      speed: it.speed ?? 50, gravity: it.gravity ?? 0,
      explosionRadius: it.explosionRadius ?? 0, lifetime: it.lifetime ?? 3, pierce: it.pierce ?? 0,
      gun: it.gun || "" } });
    itemNode["Item:"+name] = id;
  });
  (obj.recipes||[]).forEach(r => {
    const name = (r.id||"").replace(/^Recipe:/,"") || r.displayName;
    const id = uid();
    nodes.set(id, { id, kind:"recipe", x:0, y:0, data:{ name, displayName:r.displayName||name,
      description:r.description||"", tier:r.tier ?? r.requiredCoreTier ?? 0, craftTime:r.craftTime??0 } });
    (r.inputs||[]).forEach(p => { let src = itemNode[p.item]; if(src===undefined){ src = ensureGhostItem(p.item, itemNode); }
      const eid = uid(); edges.set(eid, { id:eid, from:src, to:id, amount:p.amount??1 }); });
    (r.outputs||[]).forEach(p => { let dst = itemNode[p.item]; if(dst===undefined){ dst = ensureGhostItem(p.item, itemNode); }
      const eid = uid(); edges.set(eid, { id:eid, from:id, to:dst, amount:p.amount??1 }); });
  });
  autoLayout(); render(); renderSide();
}
function ensureGhostItem(itemId, map){ // 정의 안 된 재료 참조 → 자리 아이템 생성 (임포터의 미해석 스킵 방지용 시각화)
  const name = itemId.replace(/^Item:/,"");
  const id = uid();
  nodes.set(id, { id, kind:"item", x:0, y:0, data:{ name, displayName:name+" (미정의)", description:"", type:"Part", line:"None", icon:"" } });
  map[itemId] = id; return id;
}

// ───────────────────────── 자동 정렬 ─────────────────────────
// 하나의 격자에 세 축을 모두 반영한다.
//   가로 = 티어(1순위) → 생산 흐름 깊이(2순위)
//   세로 = 아이템 타입
// 빈 칸이 곧 정보다 — "이 티어엔 이 종류가 없다"가 눈에 보인다.

const TIER_OF = id => {
  const n = nodes.get(id);
  if(n.kind === "recipe") return +n.data.tier || 0;
  // 아이템의 티어 = 그것을 만드는 레시피 중 가장 이른 것. 원광은 0
  let best = Infinity;
  for(const e of edges.values()){
    if(e.to !== id) continue;
    const f = nodes.get(e.from);
    if(f && f.kind === "recipe") best = Math.min(best, +f.data.tier || 0);
  }
  return best === Infinity ? 0 : best;
};

function autoLayout(){
  _depthCache = null;
  layoutGrid();
}

// 생산 흐름 깊이 — 티어 안에서만 센다.
// 이전 티어에서 이미 만들어둔 재료는 "가진 것"이므로 깊이 0 으로 보고,
// 그 티어에서 새로 열리는 레시피만 단계로 친다. 티어를 가로질러 세면
// 재료 하나가 깊어질 때마다 열이 늘어 격자가 성기게 벌어진다.
let _depthCache = null;
function DEPTH_OF(id){
  if(!_depthCache) _depthCache = computeDepth();
  return _depthCache.get(id) ?? 0;
}
function computeDepth(){
  const producers = new Map();          // itemId → [recipeNodeId]
  for(const e of edges.values()){
    const f = nodes.get(e.from);
    if(f && f.kind === "recipe"){
      if(!producers.has(e.to)) producers.set(e.to, []);
      producers.get(e.to).push(e.from);
    }
  }
  const memo = new Map();
  const itemDepth = (id, t, seen) => {
    const key = id + "@" + t;
    if(memo.has(key)) return memo.get(key);
    if(seen.has(id)) return 0;
    if(TIER_OF(id) < t) return 0;                       // 이전 티어 = 이미 가진 재료
    const rs = (producers.get(id) || []).filter(r => (+nodes.get(r).data.tier || 0) === t);
    if(!rs.length) return 0;                            // 원광·드롭
    const s2 = new Set(seen); s2.add(id);
    let best = 0;
    for(const rid of rs){
      let deepest = 0;
      for(const e of edges.values())
        if(e.to === rid) deepest = Math.max(deepest, itemDepth(e.from, t, s2));
      best = Math.max(best, deepest + 1);
    }
    memo.set(key, best);
    return best;
  };
  const out = new Map();
  for(const [id, n] of nodes){
    if(n.kind === "item"){ out.set(id, itemDepth(id, TIER_OF(id), new Set())); }
  }
  // 레시피는 결과물과 같은 열
  for(const [id, n] of nodes){
    if(n.kind !== "recipe") continue;
    let d = 0;
    for(const e of edges.values())
      if(e.from === id && nodes.get(e.to)?.kind === "item") d = Math.max(d, out.get(e.to) ?? 0);
    out.set(id, d);
  }
  return out;
}

// 2축 격자 — 가로: 티어(또는 생산 흐름 깊이) · 세로: 타입 밴드
// 같은 티어라도 타입이 다르면 다른 줄에 놓여, 빈 칸 자체가 "이 티어엔 이 종류가 없다"를 말한다.
function layoutGrid(){
  const ROW = 170, COL = 620, BAND_GAP = 70, TIER_GAP = 80, RECIPE_DX = 300;

  const typeOf = id => {
    const n = nodes.get(id);
    if(n.kind === "item") return n.data.type || "Part";
    for(const e of edges.values()){
      if(e.from !== id) continue;
      const t = nodes.get(e.to);
      if(t && t.kind === "item") return t.data.type || "Part";
    }
    return "Part";
  };
  const outputsOf = id => [...edges.values()]
    .filter(e => e.from === id && nodes.get(e.to)?.kind === "item").map(e => e.to);

  // ── 1) 아이템만으로 격자를 짠다. 레시피는 나중에 결과물 왼쪽에 붙인다 ──
  const cells = new Map();                 // "tier|step|type" → [itemId]
  const colKeys = new Map();
  const typeSet = new Set();
  for(const [id, n] of nodes){
    if(n.kind !== "item") continue;
    const tier = TIER_OF(id), depth = DEPTH_OF(id), type = typeOf(id);
    typeSet.add(type);
    const ck = tier + "|" + depth;
    if(!colKeys.has(ck)) colKeys.set(ck, { tier, depth });
    const k = ck + "|" + type;
    if(!cells.has(k)) cells.set(k, []);
    cells.get(k).push(id);
  }

  // 깊이를 티어 안에서 다시 매긴다 — 절대 깊이는 열을 잘게 쪼갠다
  const raw = [...colKeys.values()].sort((a,b) => a.tier - b.tier || a.depth - b.depth);
  const seqByTier = new Map();
  const cols = raw.map(c => {
    if(!seqByTier.has(c.tier)) seqByTier.set(c.tier, []);
    const arr = seqByTier.get(c.tier);
    if(!arr.includes(c.depth)) arr.push(c.depth);
    return { ...c, step: arr.indexOf(c.depth) };
  });

  const order = ITEM_TYPES.map(t=>t.v);
  const types = [...typeSet].sort((a,b)=>{
    const ia = order.indexOf(a), ib = order.indexOf(b);
    return (ia<0?99:ia) - (ib<0?99:ib);
  });

  const bandRows = types.map(t =>
    Math.max(1, ...cols.map(c => (cells.get(c.tier+"|"+c.depth+"|"+t)||[]).length)));
  const bandY = []; let y = 110;
  bandRows.forEach((r, i) => { bandY[i] = y; y += r*ROW + BAND_GAP; });

  const colX = []; let x = 40;
  cols.forEach((c, i) => {
    if(i > 0 && cols[i-1].tier !== c.tier) x += TIER_GAP;
    colX[i] = x; x += COL;
  });

  cols.forEach((c, ci) => {
    types.forEach((t, ti) => {
      const L = (cells.get(c.tier+"|"+c.depth+"|"+t) || [])
        .sort((a,b)=>(nodes.get(a).data.displayName||"").localeCompare(nodes.get(b).data.displayName||""));
      L.forEach((id, i) => { const n = nodes.get(id); n.x = colX[ci]; n.y = bandY[ti] + i*ROW; });
    });
  });

  // ── 2) 레시피는 결과물 왼쪽 반 칸 — 재료 → 레시피 → 결과물이 한 방향으로 흐른다 ──
  const usedY = new Map();                 // x좌표 → 이미 쓴 y들 (겹침 방지)
  for(const [id, n] of nodes){
    if(n.kind !== "recipe") continue;
    const outs = outputsOf(id).map(o => nodes.get(o)).filter(Boolean);
    let ax, ay;
    if(outs.length){
      ax = Math.min(...outs.map(o => o.x)) - RECIPE_DX;
      ay = outs.reduce((s,o)=>s+o.y, 0) / outs.length;
    } else {                                // 결과물이 없는 레시피 — 맨 왼쪽에 모아둔다
      ax = 40 - RECIPE_DX; ay = 110;
    }
    const key = Math.round(ax);
    if(!usedY.has(key)) usedY.set(key, []);
    const taken = usedY.get(key);
    while(taken.some(v => Math.abs(v - ay) < 150)) ay += 150;   // 같은 자리면 아래로 비킨다
    taken.push(ay);
    n.x = ax; n.y = ay;
  }

  const tierSpans = [];
  cols.forEach((c, i) => {
    const last = tierSpans[tierSpans.length-1];
    if(last && last.tier === c.tier) last.x2 = colX[i] + 250;
    else tierSpans.push({ tier:c.tier, x1:Math.max(0, colX[i] - RECIPE_DX - 20), x2:colX[i] + 250 });
  });

  const right = colX[colX.length-1] + 280;
  // 같은 티어 안에서 열이 바뀌는 지점 (티어 경계는 제외 — 그건 구획 띠가 이미 나눈다)
  const seps = [];
  cols.forEach((c, i) => {
    if(i > 0 && cols[i-1].tier === c.tier) seps.push(colX[i] - RECIPE_DX - 30);
  });

  layoutBands = {
    seps,
    tiers: tierSpans.map(t => ({
      x: t.x1, w: t.x2 - t.x1,
      label: t.tier === 0 ? "티어 0 · 시작" : `티어 ${t.tier}` })),
    steps: cols.map((c, i) => ({ x: colX[i], label: `${c.step + 1}단계` })),
    rows: types.map((t, ti) => {
      const info = ITEM_TYPES.find(x=>x.v===t);
      return { y: bandY[ti], h: bandRows[ti]*ROW, width: right,
               label: info ? `${info.v} · ${info.ko}` : t };
    }),
  };
}




// ───────────────────────── 렌더 ─────────────────────────
// 접힌 상태 — 예전과 같은 크기
function recipeSummary(n){
  let ins = 0, outs = 0;
  for(const e of edges.values()){ if(e.to===n.id) ins++; if(e.from===n.id) outs++; }
  return `<div class="row"><span>재료</span><b>${ins}종 → ${outs}</b></div>
          <div class="row"><span>tier / time</span><b>${n.data.tier} · ${n.data.craftTime}s</b></div>`;
}

// 펼친 상태 — 재료 → 결과물. 수량과 계통색을 함께 보여준다.
function recipeBody(n){
  const rows = [];
  const line = id => { const it = nodes.get(id); return it ? lineColor(it.data.line||"None") : "#5C6E8C"; };
  const name = id => { const it = nodes.get(id); return it ? (it.data.displayName || it.data.name) : "?"; };
  const ins = [], outs = [];
  for(const e of edges.values()){
    if(e.to === n.id)   ins.push(e);
    if(e.from === n.id) outs.push(e);
  }
  ins.forEach(e => rows.push(
    `<div class="mat"><i style="background:${line(e.from)}"></i>
       <span class="mname">${esc(name(e.from))}</span><b>${e.amount}</b></div>`));
  if(ins.length && outs.length) rows.push(`<div class="matsep">↓ ${n.data.craftTime}s</div>`);
  outs.forEach(e => rows.push(
    `<div class="mat out"><i style="background:${line(e.to)}"></i>
       <span class="mname">${esc(name(e.to))}</span><b>${e.amount}</b></div>`));
  if(!rows.length) rows.push(`<div class="matsep">재료·결과물 없음 · ${n.data.craftTime}s</div>`);
  return rows.join("");
}

// 노드 크기 — DOM 이 아직 없거나 레이아웃 전이면(첫 렌더) 내용으로 추정한다.
// offsetHeight 0 을 그대로 쓰면 앵커가 노드 위쪽 모서리에 몰려 선이 엉킨다.
const NODE_W = 240, HEAD_H = 34, BODY_PAD = 16, ROW_H = 22;
function estimateH(n){
  let rows;
  if(n.kind === "item") rows = 3;                       // type, line, id
  else if(n.expanded){
    let ins = 0, outs = 0;
    for(const e of edges.values()){ if(e.to===n.id) ins++; if(e.from===n.id) outs++; }
    rows = ins + outs + (ins&&outs ? 1 : 0) + 1;        // 재료 + 구분줄 + id
  } else rows = 3;                                      // 재료 요약, tier/time, id
  return HEAD_H + BODY_PAD + rows*ROW_H;
}
function nodeBox(id){
  const n = nodes.get(id), el = document.getElementById("nd-"+id);
  const h = el && el.offsetHeight > 0 ? el.offsetHeight : estimateH(n);
  const w = el && el.offsetWidth  > 0 ? el.offsetWidth  : NODE_W;
  return { x:n.x, y:n.y, w, h };
}
function portPos(nodeId, side){ // 노드 중앙 (임시 연결선용)
  const b = nodeBox(nodeId);
  return { x: b.x + (side==="out" ? b.w : 0), y: b.y + b.h/2 };
}

// 간선별 접점 — 상대 노드의 y 순서대로 노드 변을 따라 분산시킨다.
// 한 점에서 부챗살처럼 나가지 않으므로 교차가 눈에 띄게 줄어든다.
let anchors = new Map(); // edgeId → {a:{x,y}, b:{x,y}}
function computeAnchors(){
  anchors = new Map();
  const out = {}, inn = {};
  for(const e of edges.values()){
    if(!nodes.has(e.from) || !nodes.has(e.to)) continue;
    (out[e.from] ||= []).push(e); (inn[e.to] ||= []).push(e);
  }
  const centerY = id => { const b = nodeBox(id); return b.y + b.h/2; };
  const spread = (list, id, side) => {
    const b = nodeBox(id);
    list.sort((p,q) => centerY(side==="out" ? p.to : q.from === undefined ? q.to : q.from)
                     - centerY(side==="out" ? q.to : p.from));
    // 위 정렬이 애매한 경우를 피해 명시적으로 재정렬
    list.sort((p,q) => centerY(side==="out" ? p.to : p.from) - centerY(side==="out" ? q.to : q.from));
    const usable = Math.max(0, b.h - 24);
    const step = list.length > 1 ? Math.min(22, usable / (list.length - 1)) : 0;
    const start = b.y + b.h/2 - step * (list.length - 1) / 2;
    list.forEach((e, i) => {
      const pt = { x: b.x + (side==="out" ? b.w : 0), y: start + step * i };
      const cur = anchors.get(e.id) || {};
      if(side==="out") cur.a = pt; else cur.b = pt;
      anchors.set(e.id, cur);
    });
  };
  for(const id in out) spread(out[id], id, "out");
  for(const id in inn) spread(inn[id], id, "in");
}
function edgeEnds(e){
  const cur = anchors.get(e.id);
  return { a: (cur && cur.a) || portPos(e.from,"out"), b: (cur && cur.b) || portPos(e.to,"in") };
}
// 3차 베지어 위의 점 (라벨 위치용)
function bezierAt(a, c1, c2, b, t){
  const u = 1-t;
  return { x: u*u*u*a.x + 3*u*u*t*c1.x + 3*u*t*t*c2.x + t*t*t*b.x,
           y: u*u*u*a.y + 3*u*u*t*c1.y + 3*u*t*t*c2.y + t*t*t*b.y };
}
function edgeGeom(e){
  const {a, b} = edgeEnds(e);
  const dx = Math.max(50, Math.abs(b.x-a.x)/2);
  return { a, b, c1:{x:a.x+dx, y:a.y}, c2:{x:b.x-dx, y:b.y} };
}
function edgePath(e){
  const f = nodes.get(e.from), t = nodes.get(e.to);
  if(!f || !t) return "";
  const g = edgeGeom(e);
  return `M ${g.a.x} ${g.a.y} C ${g.c1.x} ${g.c1.y}, ${g.c2.x} ${g.c2.y}, ${g.b.x} ${g.b.y}`;
}
// 스크롤해도 어느 티어·어느 타입인지 알 수 있게 머리글을 화면에 고정한다.
// world 밖 레이어라 팬·줌의 영향을 받지 않으므로 좌표를 직접 환산한다.
function renderSticky(){
  const ce = document.getElementById("stick-cols"), re_ = document.getElementById("stick-rows");
  if(!ce || !re_) return;
  if(!layoutBands){ ce.innerHTML = ""; re_.innerHTML = ""; return; }
  const k = view.k, W = wrap.clientWidth, H = wrap.clientHeight;

  ce.innerHTML = layoutBands.tiers.map(t => {
    const x = t.x * k + view.x, w = t.w * k;
    if(x + w < 0 || x > W) return "";              // 화면 밖은 그리지 않는다
    const cx = Math.max(6, Math.min(x, W - 6));    // 일부만 보여도 라벨은 화면 안에
    const cw = Math.min(x + w, W - 6) - cx;
    if(cw < 40) return "";
    return `<div class="stick-tier" style="left:${cx}px;width:${cw}px">${esc(t.label)}</div>`;
  }).join("");

  re_.innerHTML = layoutBands.rows.map(r => {
    const y = (r.y - 24) * k + view.y, h = (r.h + 8) * k;
    if(y + h < 0 || y > H) return "";
    const cy = Math.max(2, Math.min(y, H - 22));
    return `<div class="stick-row" style="top:${cy}px">${esc(r.label)}</div>`;
  }).join("");
}

function render(){
  worldEl.style.transform = `translate(${view.x}px,${view.y}px) scale(${view.k})`;
  // nodes
  nodesEl.innerHTML = "";
  for(const n of nodes.values()){
    const el = document.createElement("div");
    el.className = "node " + n.kind + (selection && selection.type==="node" && selection.id===n.id ? " selected":"");
    el.id = "nd-"+n.id;
    el.style.left = n.x+"px"; el.style.top = n.y+"px";
    if(n.kind === "item" && n.data.line && n.data.line !== "None"){
      el.style.borderColor = lineColor(n.data.line);
      el.style.boxShadow = `0 0 0 1px ${lineColor(n.data.line)}22, 0 4px 16px rgba(0,0,0,.35)`;
    }
    const warn = nodeWarning(n);
    el.innerHTML = n.kind === "item"
      ? `<div class="head"><span class="kind">ITEM</span>${esc(n.data.displayName)}</div>
         <div class="body">
           <div class="row"><span>type</span>
             <b>${esc(n.data.type)} <span style="color:var(--muted);font-weight:400">${typeKo(n.data.type)}</span></b></div>
           <div class="row"><span>line</span>
             <b style="color:${lineColor(n.data.line)}">${esc(n.data.line||"None")} <span style="color:var(--muted);font-weight:400">${lineKo(n.data.line||"None")}</span></b></div>
         </div>
         <div class="idline">Item:${esc(sanitize(n.data.name))}</div>${warn?`<div class="warnmark">⚠ ${warn}</div>`:""}
         <div class="port in" data-node="${n.id}" data-side="in" title="레시피 출력 연결"></div>
         <div class="port out" data-node="${n.id}" data-side="out" title="레시피 입력으로 드래그"></div>`
      : `<div class="head"><span class="kind">RECIPE</span><span class="ttl">${esc(n.data.displayName)}</span>
           <span class="exp" data-exp="${n.id}" title="재료 펼치기">${n.expanded ? "▾" : "▸"}</span></div>
         <div class="body">${n.expanded ? recipeBody(n) : recipeSummary(n)}</div>
         <div class="idline">Recipe:${esc(sanitize(n.data.name))}</div>${warn?`<div class="warnmark">⚠ ${warn}</div>`:""}
         <div class="port in" data-node="${n.id}" data-side="in" title="재료(inputs)"></div>
         <div class="port out" data-node="${n.id}" data-side="out" title="결과물(outputs)"></div>`;
    nodesEl.appendChild(el);
  }
  // 열 머리글 (티어/타입 정렬에서만)
  let bandHtml = "";
  if(layoutBands){
    // 티어 구획 — 세로 배경 띠. 홀짝으로 음영을 달리해 경계가 보인다
    const top = layoutBands.rows[0].y - 24;
    const bot = layoutBands.rows[layoutBands.rows.length-1];
    const colH = (bot.y + bot.h + 8) - top;
    bandHtml += layoutBands.tiers.map((t, i) =>
      `<div class="bandcol ${i%2?"alt":""}" style="left:${t.x}px;top:${top}px;width:${t.w}px;height:${colH}px"></div>`).join("");
    // 단계 구분선 — 티어 안의 열 사이
    bandHtml += (layoutBands.seps||[]).map(x =>
      `<div class="bandsep" style="left:${x}px;top:${top}px;height:${colH}px"></div>`).join("");
    // 타입 행 — 배경 띠 + 왼쪽 라벨
    bandHtml += layoutBands.rows.map((r, i) =>
      `<div class="bandrow ${i%2?"alt":""}" style="left:-184px;top:${r.y-24}px;height:${r.h+8}px;width:${r.width+184}px"></div>
       <div class="bandlabel" style="top:${r.y-22}px">${esc(r.label)}</div>`).join("");
    // 티어 머리글 — 같은 티어의 열을 하나로 묶는다
    bandHtml += layoutBands.tiers.map(t =>
      `<div class="bandtier" style="left:${t.x}px;width:${t.w}px">${esc(t.label)}</div>`).join("");
    // 단계 머리글 — 티어 안의 생산 흐름 순서
    bandHtml += layoutBands.steps.map(c =>
      `<div class="band" style="left:${c.x}px;top:66px">${esc(c.label)}</div>`).join("");
  }
  const bandEl = document.getElementById("bands");
  if(bandEl) bandEl.innerHTML = bandHtml;
  renderSticky();

  // edges
  computeAnchors();
  let svg = "", svgTop = "", lbl = "";
  const edgeList = [...edges.values()];
  for(const e of edgeList){
    const isIn = nodes.get(e.from)?.kind === "item";
    const col = isIn ? "#E8A54B" : "#4FD8E0";
    const selc = selection && selection.type==="edge" && selection.id===e.id;
    const f = nodes.get(e.from), t = nodes.get(e.to);
    if(!f||!t) continue;
    const rel = !hoverNode || e.from===hoverNode || e.to===hoverNode;
    const g = edgeGeom(e);
    const a = g.a, b = g.b;
    // 라벨을 중앙이 아니라 출발 쪽 38% 지점에 둔다 — 교차 지점에서 라벨끼리 겹치지 않는다
    const lp = bezierAt(g.a, g.c1, g.c2, g.b, 0.38);
    const mx = lp.x, my = lp.y;
    const d = edgePath(e);
    let piece = "";
    const op = selc ? 1 : (rel ? .85 : .12);
    piece += `<path d="${d}" fill="none" stroke="${selc?'#B48CFF':col}" stroke-width="${selc?3:(hoverNode&&rel?2.6:2)}" opacity="${op}" style="pointer-events:none"/>`;
    piece += `<path d="${d}" fill="none" stroke="rgba(0,0,0,0.001)" stroke-width="18" data-edge="${e.id}" style="cursor:pointer"/>`;
    // 접점 표시 — 어느 선이 어디에 붙었는지 눈으로 따라갈 수 있다
    piece += `<circle cx="${a.x}" cy="${a.y}" r="3" fill="${selc?'#B48CFF':col}" opacity="${op}" style="pointer-events:none"/>`;
    piece += `<circle cx="${b.x}" cy="${b.y}" r="3" fill="${selc?'#B48CFF':col}" opacity="${op}" style="pointer-events:none"/>`;
    lbl += `<g data-edge="${e.id}" style="cursor:pointer;opacity:${selc?1:(rel?.97:.12)}"><rect x="${mx-18}" y="${my-13}" width="36" height="24" rx="6" fill="#0E1727" stroke="${selc?'#B48CFF':col}"/>
      <text x="${mx}" y="${my+4}" text-anchor="middle" font-size="11" fill="#D9E4F5" style="pointer-events:none">×${e.amount}</text></g>`;
    if(selc || (hoverNode && rel)) svgTop += piece; else svg += piece;
  }
  edgesEl.innerHTML = svg + svgTop;
  const lblEl = document.getElementById("edge-labels");
  if(lblEl){
    lblEl.innerHTML = lbl;
    lblEl.setAttribute("width", edgesEl.getAttribute("width"));
    lblEl.setAttribute("height", edgesEl.getAttribute("height"));
  }
  fitSvg();
  $("#stat").textContent = `아이템 ${[...nodes.values()].filter(n=>n.kind==="item").length} · 레시피 ${[...nodes.values()].filter(n=>n.kind==="recipe").length} · 연결 ${edges.size}`;
  renderWarnings();
}
function fitSvg(){
  let mx=1200,my=800;
  for(const n of nodes.values()){ mx=Math.max(mx,n.x+400); my=Math.max(my,n.y+300); }
  edgesEl.setAttribute("width",mx); edgesEl.setAttribute("height",my);
  const le = document.getElementById("edge-labels");
  if(le){ le.setAttribute("width",mx); le.setAttribute("height",my); }
}

// ───────────────────────── 검증 ─────────────────────────
function nodeWarning(n){
  if(!sanitize(n.data.name)) return "id 이름이 비어 있음";
  const dup = [...nodes.values()].filter(o=>o.kind===n.kind && sanitize(o.data.name)===sanitize(n.data.name));
  if(dup.length>1) return "id 중복";
  if(n.kind==="recipe"){
    const hasIn = [...edges.values()].some(e=>e.to===n.id);
    const hasOut = [...edges.values()].some(e=>e.from===n.id);
    if(!hasIn) return "재료(inputs) 없음 — 임포터가 스킵함";
    if(!hasOut) return "결과물(outputs) 없음 — 임포터가 스킵함";
  }
  if(n.kind==="item" && !TYPE_SET.has(n.data.type)) return `ItemType에 없는 값 "${n.data.type}" — 임포터가 에러로 거부함`;
  if(n.kind==="item" && n.data.type==="Ammo"){
    if(!(n.data.attackEffects||[]).length) return "탄약인데 명중 효과가 없습니다 — AmmoModule 을 채우세요";
    if(!(n.data.speed > 0)) return "탄약 속도가 0 입니다 — Projectile 총이 쏘지 못합니다";
    if(!(n.data.lifetime > 0)) return "탄약 수명이 0 입니다 — 발사되자마자 사라집니다";
  }
  if(n.kind==="item" && n.data.type==="Weapon" && !n.data.gun)
    return "무기인데 연결된 총이 없습니다 — WeaponModule 을 지정하세요";
  if(n.kind==="item" && !LINE_SET.has(n.data.line||"None")) return `ItemLine에 없는 값 "${n.data.line}" — 임포터가 에러로 거부함`;
  if(n.kind==="item" && /\(미정의\)$/.test(n.data.displayName)) return "임포트 시 미정의였던 아이템 — 속성 확인";
  return "";
}
function renderWarnings(){
  const box = $("#warnbox"); const ws = [];
  for(const n of nodes.values()){ const w = nodeWarning(n); if(w) ws.push(`[${n.kind==="item"?"아이템":"레시피"}] ${n.data.displayName}: ${w}`); }
  box.innerHTML = ws.length ? "<h3>경고</h3>" + ws.map(w=>`<div class="w">${esc(w)}</div>`).join("")
                            : `<div class="allok">✓ 검증 통과 — 임포터 안전장치에 걸릴 항목 없음</div>`;
}

