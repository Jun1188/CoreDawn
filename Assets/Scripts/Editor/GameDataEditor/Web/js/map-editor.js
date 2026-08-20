// ═══════════════════════════════════════════════════════════
//  맵 에디터 — 고정 맵. 타일(지면·강·절벽) + 코어 · 자원 노드 · 둥지
//  GameData 와 달리 맵은 여러 개라 MapData.json 으로 따로 나간다.
// ═══════════════════════════════════════════════════════════
(function(){
const { esc, $, $$, field, autogrow } = window.EdUtil;

// 타일 — 통행과 건설을 따로 본다. 강은 지나가지만 짓지 못한다.
const TILES = [
  { v:0, key:"ground", ko:"지면",  color:"#3E6B45", walk:true,  build:true,  desc:"통행 · 건설 모두 가능" },
  { v:1, key:"river",  ko:"강",    color:"#1B4A6B", walk:true,  build:false, desc:"지나갈 수 있지만 짓지 못한다" },
  { v:2, key:"cliff",  ko:"절벽",  color:"#3A2A2A", walk:false, build:false, desc:"통행 불가 — 몬스터 동선을 가른다" },
];
const tileOf = v => TILES[v] || TILES[0];

const NODE_KINDS = [
  { item:"Item:IronOre",    ko:"철광석",       color:"#E8A54B" },
  { item:"Item:CopperOre",  ko:"구리광석",     color:"#4FD8E0" },
  { item:"Item:CrystalOre", ko:"크리스탈 광석", color:"#B48CFF" },
];

let maps = [], curMap = 0;
let tool = "paint", paintTile = 2, brush = 3;
let sel = null;                       // {type:"node"|"nest", i}
let view = { x:0, y:0, k:4 };         // k = 타일당 픽셀
let drag = null, hoverCell = null;
let showRings = true, showHalo = true, showGrid = true;

const M = () => maps[curMap];

// 거리 눈금 — 코어에서 맵 가장자리까지를 3등분한다.
// 게임에는 Ring 개념이 없어서 저장하지 않는다. 배치 감각을 잡는 가이드일 뿐이다.
const R = m => Math.min(m.width, m.height) / 2;          // 코어에서 가장자리까지
const guideRings = m => [R(m)/3, R(m)*2/3, R(m)].map(v => Math.round(v));
const items = () => { try { return window.GameData?.getItems() || []; } catch { return []; } };

// ── 맵 생성 ────────────────────────────────────────────────
function blankMap(n = 1){
  const w = 121, h = 121;   // 홀수 — 코어 3×3 이 정확히 중앙에 온다
  return {
    id: "Map:New" + n, displayName: "새 맵", description: "",
    width: w, height: h,
    core: { x: (w>>1) - 1, y: (h>>1) - 1 },     // 3×3 의 왼쪽 아래
    tiles: new Uint8Array(w * h),
    nodes: [], nests: [], nightSpawnPoints: [],
  };
}
// 맵 크기는 홀수만 쓴다 — 코어 3×3 의 중심 칸이 정확히 하나여야 Ring 도 반듯하게 그려진다
const odd = v => { v = Math.max(21, Math.min(401, v|0)); return v % 2 ? v : v + 1; };
const idx = (m, x, y) => y * m.width + x;
const inBounds = (m, x, y) => x >= 0 && y >= 0 && x < m.width && y < m.height;

// ── 입출력 ────────────────────────────────────────────────
// 타일은 행마다 한 줄씩 문자열로 쓴다.
//   "0000222000"  ← 지면 4 · 절벽 3 · 지면 3
// JSON 에서 맵 모양이 그대로 보여서 diff 를 읽거나 손으로 고치기 쉽다.
function encodeTiles(tiles, w, h){
  const rows = [];
  for(let y = 0; y < h; y++){
    let line = "";
    for(let x = 0; x < w; x++) line += tiles[y*w + x];
    rows.push(line);
  }
  return rows;
}
function decodeTiles(src, w, h){
  const t = new Uint8Array(w * h);
  if(!Array.isArray(src)) return t;
  for(let y = 0; y < Math.min(h, src.length); y++){
    const line = src[y] || "";
    for(let x = 0; x < Math.min(w, line.length); x++) t[y*w + x] = +line[x] || 0;
  }
  return t;
}

window.MapData = {
  getMaps: () => maps.map(m => ({
    id: m.id, displayName: m.displayName, description: m.description || "",
    width: m.width, height: m.height,
    core: { x: m.core.x, y: m.core.y },
    tiles: encodeTiles(m.tiles, m.width, m.height),
    nodes: m.nodes.map(n => ({ item:n.item, x:n.x, y:n.y, size:n.size,
                               extractInterval:n.extractInterval, maxStock:n.maxStock })),
    nests: m.nests.map(n => ({ x:n.x, y:n.y,
      warningRange:n.warningRange, triggerRange:n.triggerRange,
      defenseSpawnAmount:n.defenseSpawnAmount, defenseSpawnCooldown:n.defenseSpawnCooldown,
      spawnPoints:n.spawnPoints.map(p => ({ x:p.x, y:p.y, hasBoss:!!p.hasBoss })),
      engageMinRange:n.engageMinRange, engageMaxRange:n.engageMaxRange,
      chaseRange:n.chaseRange, leashRange:n.leashRange, engageDayOnly:!!n.engageDayOnly,
      bossRecoveryDays:n.bossRecoveryDays, nestRecoveryDays:n.nestRecoveryDays })),
    nightSpawnPoints: m.nightSpawnPoints.map(p => ({ x:p.x, y:p.y })),
  })),
  load: obj => {
    const arr = Array.isArray(obj?.maps) ? obj.maps : [];
    maps = arr.map(m => ({
      id: m.id || "", displayName: m.displayName || "", description: m.description || "",
      width: odd(m.width || 121), height: odd(m.height || 121),
      core: m.core || { x:59, y:59 },
      tiles: decodeTiles(m.tiles, odd(m.width||121), odd(m.height||121)),
      nodes: (m.nodes||[]).map(n => ({ item:n.item||"Item:IronOre", x:n.x|0, y:n.y|0,
        size:n.size||1, extractInterval:n.extractInterval??1, maxStock:n.maxStock??20 })),
      nests: (m.nests||[]).map(n => ({ x:n.x|0, y:n.y|0,
        warningRange:n.warningRange??25, triggerRange:n.triggerRange??15,
        defenseSpawnAmount:n.defenseSpawnAmount??3, defenseSpawnCooldown:n.defenseSpawnCooldown??10,
        spawnPoints:(n.spawnPoints||[{x:0,y:0,hasBoss:false}]).map(p=>({x:p.x|0,y:p.y|0,hasBoss:!!p.hasBoss})),
        engageMinRange:n.engageMinRange??4, engageMaxRange:n.engageMaxRange??18,
        chaseRange:n.chaseRange??24, leashRange:n.leashRange??32, engageDayOnly:n.engageDayOnly!==false,
        bossRecoveryDays:n.bossRecoveryDays??3, nestRecoveryDays:n.nestRecoveryDays??5 })),
      nightSpawnPoints: (m.nightSpawnPoints||[]).map(p=>({x:p.x|0, y:p.y|0})),
    }));
    if(!maps.length) maps = [blankMap()];
    curMap = 0; sel = null; fitView(); render();
  },
  // 탭이 숨겨져 있는 동안에는 캔버스 크기가 0 이라 그릴 수 없다.
  // 탭을 열 때 크기를 다시 잡고, 첫 표시에만 화면에 맞춘다.
  refresh: () => { if(_resize) _resize(); if(!_fitted){ fitView(); _fitted = true; } render(); },
};
let _resize = null, _fitted = false;


// ── 실행 취소 / 다시 실행 ──
// 되돌릴 때 화면(view)은 건드리지 않는다 — 작업하던 자리가 튀면 흐름이 끊긴다
const hist = window.EdHistory(
  () => ({ maps: window.MapData.getMaps(), cur: curMap }),
  o => {
    const v = { ...view };
    window.MapData.load({ maps: o.maps });        // load 안의 fitView 로 바뀐 값을 되돌린다
    curMap = Math.min(o.cur, maps.length - 1);
    view = v; sel = null; render();
  });
const pushHistory = () => hist.push();
const undo = () => hist.undo();
const redo = () => hist.redo();


// ── 캔버스 ────────────────────────────────────────────────
let cv, ctx;
function fitView(){
  const m = M(); if(!m || !cv) return;
  const k = Math.min(cv.width / m.width, cv.height / m.height) * 0.92;
  view.k = k;
  view.x = (cv.width  - m.width  * k) / 2;
  view.y = (cv.height - m.height * k) / 2;
}
const toCell = (px, py) => ({
  x: Math.floor((px - view.x) / view.k),
  y: Math.floor((py - view.y) / view.k),
});
const toPx = (x, y) => ({ x: view.x + x * view.k, y: view.y + y * view.k });

function draw(){
  const m = M(); if(!m || !ctx) return;
  const W = cv.width, H = cv.height, k = view.k;
  ctx.fillStyle = "#080D16"; ctx.fillRect(0, 0, W, H);
  const cx = m.core.x + 1.5, cy = m.core.y + 1.5;   // 코어 중심 (3×3)

  drawTiles(m, W, H, k);
  if(showRings) drawRings(m, cx, cy, k);
  if(showHalo)  drawNests(m, k, true);
  drawNodes(m, k);
  drawNests(m, k, false);
  drawNightSpawns(m, k);
  drawCore(m, k);
  drawBrush(k);
}

// 보이는 범위만 그린다 — 401×401 이면 16만 칸이라 전부 그리면 느려진다
function drawTiles(m, W, H, k){
  const x0 = Math.max(0, Math.floor(-view.x / k)), x1 = Math.min(m.width,  Math.ceil((W - view.x) / k));
  const y0 = Math.max(0, Math.floor(-view.y / k)), y1 = Math.min(m.height, Math.ceil((H - view.y) / k));
  for(let y = y0; y < y1; y++)
    for(let x = x0; x < x1; x++){
      ctx.fillStyle = tileOf(m.tiles[idx(m, x, y)]).color;
      ctx.fillRect(view.x + x*k, view.y + y*k, k + 0.5, k + 0.5);
    }
  if(showGrid && k >= 6){
    ctx.strokeStyle = "rgba(255,255,255,.05)"; ctx.lineWidth = 1;
    ctx.beginPath();
    for(let x = x0; x <= x1; x++){ ctx.moveTo(view.x + x*k, view.y + y0*k); ctx.lineTo(view.x + x*k, view.y + y1*k); }
    for(let y = y0; y <= y1; y++){ ctx.moveTo(view.x + x0*k, view.y + y*k); ctx.lineTo(view.x + x1*k, view.y + y*k); }
    ctx.stroke();
  }
}

function drawRings(m, cx, cy, k){
  ctx.setLineDash([6, 6]); ctx.lineWidth = 1.5;
  guideRings(m).forEach((r, i) => {
    ctx.strokeStyle = ["rgba(93,211,158,.35)","rgba(79,216,224,.3)","rgba(180,140,255,.3)"][i] || "rgba(255,255,255,.2)";
    ctx.beginPath(); ctx.arc(view.x + cx*k, view.y + cy*k, r*k, 0, Math.PI*2); ctx.stroke();
  });
  ctx.setLineDash([]);
}

function drawNodes(m, k){
  m.nodes.forEach((n, i) => {
    const c = NODE_KINDS.find(q => q.item === n.item) || NODE_KINDS[0];
    ctx.fillStyle = c.color;
    ctx.fillRect(view.x + n.x*k, view.y + n.y*k, n.size*k, n.size*k);
    if(sel && sel.type === "node" && sel.i === i){
      ctx.strokeStyle = "#fff"; ctx.lineWidth = 2;
      ctx.strokeRect(view.x + n.x*k - 1, view.y + n.y*k - 1, n.size*k + 2, n.size*k + 2);
    }
  });
}

// halo=true 면 반경만, false 면 둥지 본체만 그린다 (반경이 노드 아래 깔려야 한다)
function drawNests(m, k, halo){
  m.nests.forEach((n, i) => {
    const px = view.x + n.x*k, py = view.y + n.y*k;
    if(halo){
      const cx2 = px + k/2, cy2 = py + k/2;
      ctx.fillStyle = "rgba(255,93,115,.07)";
      ctx.beginPath(); ctx.arc(cx2, cy2, n.warningRange*k, 0, Math.PI*2); ctx.fill();
      ctx.fillStyle = "rgba(255,93,115,.13)";      // 진입 반경 — 여기 들어가면 튀어나온다
      ctx.beginPath(); ctx.arc(cx2, cy2, n.triggerRange*k, 0, Math.PI*2); ctx.fill();
      n.spawnPoints.forEach(p => {
        ctx.fillStyle = p.hasBoss ? "rgba(255,51,85,.85)" : "rgba(255,176,188,.8)";
        ctx.fillRect(view.x + (n.x+p.x)*k + k*.2, view.y + (n.y+p.y)*k + k*.2, k*.6, k*.6);
      });
      return;
    }
    const boss = n.spawnPoints.some(p => p.hasBoss);
    ctx.fillStyle = boss ? "#FF3355" : "#FF5D73";
    ctx.fillRect(px, py, k, k);
    if(k < 8){                                     // 축소하면 한 칸이 작아 놓치기 쉽다
      ctx.strokeStyle = boss ? "#FF3355" : "#FF5D73"; ctx.lineWidth = 1.5;
      ctx.strokeRect(px - 2.5, py - 2.5, k + 5, k + 5);
    }
    if(sel && sel.type === "nest" && sel.i === i){
      ctx.strokeStyle = "#fff"; ctx.lineWidth = 2;
      ctx.strokeRect(px - 1.5, py - 1.5, k + 3, k + 3);
    }
  });
}

// 밤 진입로 — 맵 가장자리 쪽에 두는 대문. 둥지와 구분되게 노란 테두리 칸으로 그린다
function drawNightSpawns(m, k){
  m.nightSpawnPoints.forEach(p => {
    const px = view.x + p.x*k, py = view.y + p.y*k;
    ctx.fillStyle = "rgba(232,165,75,.35)";
    ctx.fillRect(px, py, k, k);
    ctx.strokeStyle = "#E8A54B"; ctx.lineWidth = Math.max(1.5, k*.12);
    ctx.strokeRect(px + .5, py + .5, k - 1, k - 1);
  });
}

function drawCore(m, k){
  ctx.fillStyle = "#5DD39E";
  ctx.fillRect(view.x + m.core.x*k, view.y + m.core.y*k, 3*k, 3*k);
  ctx.strokeStyle = "#0B1220"; ctx.lineWidth = 1.5;
  ctx.strokeRect(view.x + m.core.x*k, view.y + m.core.y*k, 3*k, 3*k);
}

function drawBrush(k){
  if(!hoverCell || tool !== "paint") return;
  const h = brush >> 1;
  ctx.strokeStyle = "rgba(255,255,255,.6)"; ctx.lineWidth = 1.5;
  ctx.strokeRect(view.x + (hoverCell.x-h)*k, view.y + (hoverCell.y-h)*k, brush*k, brush*k);
}

// ── 편집 ─────────────────────────────────────────────────
function paintAt(cx, cy){
  const m = M(); const h = brush >> 1;
  for(let y = cy-h; y <= cy+h; y++)
    for(let x = cx-h; x <= cx+h; x++)
      if(inBounds(m, x, y)) m.tiles[idx(m, x, y)] = paintTile;
}
function eraseAt(cx, cy){
  const m = M(); const h = brush >> 1;
  for(let y = cy-h; y <= cy+h; y++)
    for(let x = cx-h; x <= cx+h; x++)
      if(inBounds(m, x, y)) m.tiles[idx(m, x, y)] = 0;   // 지면으로 되돌린다
}
function hitTest(cx, cy){
  const m = M();
  for(let i = m.nests.length-1; i >= 0; i--){
    const n = m.nests[i];
    if(n.x === cx && n.y === cy) return { type:"nest", i };
  }
  for(let i = m.nodes.length-1; i >= 0; i--){
    const n = m.nodes[i];
    if(cx >= n.x && cx < n.x + n.size && cy >= n.y && cy < n.y + n.size) return { type:"node", i };
  }
  return null;
}

// ── 검증 ─────────────────────────────────────────────────
// 고정 맵에서 가장 무서운 건 "갈 수 없는 곳"이다.
// 플로우필드는 도달 불가 목표를 처리하지 못하고, 벽 뒤 광맥은 없는 것과 같다.
function reachable(m){
  const seen = new Uint8Array(m.width * m.height);
  const q = [];
  for(let y = m.core.y; y < m.core.y+3; y++)
    for(let x = m.core.x; x < m.core.x+3; x++)
      if(inBounds(m, x, y)){ seen[idx(m,x,y)] = 1; q.push(x, y); }
  for(let p = 0; p < q.length; p += 2){
    const x = q[p], y = q[p+1];
    for(const [dx,dy] of [[1,0],[-1,0],[0,1],[0,-1]]){
      const nx = x+dx, ny = y+dy;
      if(!inBounds(m, nx, ny)) continue;
      const id = idx(m, nx, ny);
      if(seen[id] || !tileOf(m.tiles[id]).walk) continue;
      seen[id] = 1; q.push(nx, ny);
    }
  }
  return seen;
}
function validate(){
  const m = M(); const out = [];
  if(!m) return out;
  out.push(...window.EdValid.identity(m, maps, "map"));

  const tileAt = (x, y) => inBounds(m, x, y) ? tileOf(m.tiles[idx(m,x,y)]) : null;
  const cx = m.core.x + 1.5, cy = m.core.y + 1.5;
  const dist = (x,y) => Math.hypot(x - cx, y - cy);

  // 코어 — 3×3 이 전부 건설 가능해야 한다. 강도 안 된다
  let coreBad = "";
  for(let y = m.core.y; y < m.core.y+3 && !coreBad; y++)
    for(let x = m.core.x; x < m.core.x+3 && !coreBad; x++){
      const t = tileAt(x, y);
      if(!t) coreBad = "코어가 맵 밖으로 나갔습니다";
      else if(!t.build) coreBad = `코어 자리에 ${t.ko}이(가) 있습니다 — 지을 수 없습니다`;
    }
  if(coreBad) out.push(coreBad);

  const seen = reachable(m);

  // 자원 노드 — 겹침·타일 종류·도달성을 모두 본다
  const nodeCells = new Map();
  m.nodes.forEach((n, i) => {
    const tag = `자원 #${i+1}`;
    if(!inBounds(m, n.x, n.y) || !inBounds(m, n.x+n.size-1, n.y+n.size-1)){
      out.push(`${tag} 이(가) 맵 밖에 있습니다`); return;
    }
    if(!seen[idx(m,n.x,n.y)]) out.push(`${tag} 이(가) 절벽에 갇혔습니다 — 캘 수 없습니다`);
    let onBad = "";
    for(let y = n.y; y < n.y+n.size; y++)
      for(let x = n.x; x < n.x+n.size; x++){
        const t = tileAt(x, y);
        if(t && !t.build && !onBad) onBad = t.ko;
        const key = x + "," + y;
        if(nodeCells.has(key)) out.push(`${tag} 이(가) 자원 #${nodeCells.get(key)+1} 과 겹칩니다`);
        else nodeCells.set(key, i);
      }
    if(onBad) out.push(`${tag} 이(가) ${onBad} 위에 있습니다 — 채굴기를 놓을 수 없습니다`);
    if(dist(n.x, n.y) > R(m)) out.push(`${tag} 이(가) 맵 모서리 쪽에 치우쳐 있습니다`);
  });

  // 둥지 — 스폰 지점까지 확인한다. 막힌 자리면 웨이브가 통째로 안 나온다
  const nestCells = new Set();
  m.nests.forEach((n, i) => {
    const tag = `둥지 #${i+1}`;
    if(!inBounds(m, n.x, n.y)){ out.push(`${tag} 이(가) 맵 밖에 있습니다`); return; }
    if(!seen[idx(m,n.x,n.y)]) out.push(`${tag} 에서 코어로 갈 수 없습니다 — 몬스터가 영영 오지 못합니다`);
    const t = tileAt(n.x, n.y);
    if(t && !t.walk) out.push(`${tag} 이(가) ${t.ko} 위에 있습니다`);
    const key = n.x + "," + n.y;
    if(nestCells.has(key)) out.push(`${tag} 이(가) 다른 둥지와 같은 칸에 있습니다`);
    nestCells.add(key);
    if(dist(n.x, n.y) <= R(m)/3) out.push(`${tag} 이(가) 코어에 너무 가깝습니다 — 시작하자마자 몰립니다`);
    (n.spawnPoints||[]).forEach((p, j) => {
      const sx = n.x + p.x, sy = n.y + p.y;
      const st = tileAt(sx, sy);
      if(!st) out.push(`${tag} 스폰 #${j+1} 이(가) 맵 밖입니다`);
      else if(!st.walk) out.push(`${tag} 스폰 #${j+1} 이(가) ${st.ko} 위입니다 — 나오지 못합니다`);
      else if(!seen[idx(m,sx,sy)]) out.push(`${tag} 스폰 #${j+1} 에서 코어로 갈 수 없습니다`);
    });
    if(n.triggerRange > n.warningRange)
      out.push(`${tag} — 진입 반경이 경고 반경보다 큽니다. 경고 없이 튀어나옵니다`);
    // 교전 구역은 안쪽부터 넓어져야 한다: 최소 < 최대 < 추격 < 귀환
    if(n.engageMaxRange > 0){
      if(n.engageMinRange >= n.engageMaxRange)
        out.push(`${tag} — 교전 최소가 최대보다 큽니다`);
      if(n.chaseRange < n.engageMaxRange)
        out.push(`${tag} — 추격 범위가 교전 최대보다 좁습니다. 붙자마자 돌아섭니다`);
      if(n.leashRange < n.chaseRange)
        out.push(`${tag} — 귀환 거리가 추격보다 짧습니다`);
    }
  });

  // 밤 진입로 — 웨이브가 들어오는 대문. 없으면 밤이 오지 않는다
  const nightSeen = new Set();
  m.nightSpawnPoints.forEach((p, i) => {
    const tag = `밤 진입로 #${i+1}`;
    const t = tileAt(p.x, p.y);
    if(!t){ out.push(`${tag} 이(가) 맵 밖입니다`); return; }
    if(!t.walk) out.push(`${tag} 이(가) ${t.ko} 위입니다 — 들어오지 못합니다`);
    else if(!seen[idx(m,p.x,p.y)]) out.push(`${tag} 에서 코어로 갈 수 없습니다`);
    const key = p.x + "," + p.y;
    if(nightSeen.has(key)) out.push(`${tag} 이(가) 다른 진입로와 겹칩니다`);
    nightSeen.add(key);
    if(dist(p.x, p.y) < R(m) * 0.6)
      out.push(`${tag} 이(가) 코어에 가깝습니다 — 진입로는 가장자리에 둔다`);
  });
  if(!m.nightSpawnPoints.length)
    out.push("밤 진입로가 없습니다 — 웨이브가 맵으로 들어올 자리가 없습니다");

  if(!m.nests.length) out.push("둥지가 없습니다 — 낮에 칠 대상이 없습니다");
  if(!m.nodes.length) out.push("자원 노드가 없습니다");

  // 계통별 Ring 분포 — 철은 안쪽, 크리스탈은 바깥이어야 테크트리가 성립한다
  const iron = m.nodes.filter(n => n.item === "Item:IronOre");
  const crys = m.nodes.filter(n => n.item === "Item:CrystalOre");
  // 초반 자원은 가까이, 후반 자원은 멀리 — 테크트리 진행과 확장 동기가 여기서 나온다
  if(iron.length && Math.min(...iron.map(n => dist(n.x,n.y))) > R(m)/3)
    out.push("코어 근처에 철광석이 없습니다 — 초반에 아무것도 못 만듭니다");
  if(crys.length && Math.max(...crys.map(n => dist(n.x,n.y))) < R(m)/3)
    out.push("크리스탈이 전부 코어 근처에 있습니다 — 확장할 이유가 사라집니다");

  // 방위 균형 — 한쪽만 위험하면 그쪽에만 포탑을 몰아 짓고 끝난다
  if(m.nests.length >= 4){
    const q = [0,0,0,0];
    m.nests.forEach(n => {
      const a = Math.atan2(n.y - cy, n.x - cx);
      q[Math.floor(((a + Math.PI) / (Math.PI/2)) % 4)]++;
    });
    if(q.some(v => v === 0)) out.push("둥지가 없는 방향이 있습니다");
  }
  return out;
}

// ── 렌더 ─────────────────────────────────────────────────
function render(){ renderList(); renderProps(); renderWarn(); draw(); renderStat(); }

function renderStat(){
  const m = M(); const el = $("#m-stat");
  if(el) el.textContent = m ? `${m.width}×${m.height} · 노드 ${m.nodes.length} · 둥지 ${m.nests.length}` : "";
}
function renderList(){
  $("#m-list").innerHTML = maps.map((m, i) => `
    <div class="bitem ${i===curMap?"sel":""}" data-i="${i}">
      <span class="nm">${esc(m.displayName || "(이름 없음)")}</span>
      <span class="kd">${m.width}×${m.height}</span>
    </div>`).join("") || `<div class="costempty">맵이 없습니다</div>`;
  $$("#m-list .bitem").forEach(el => el.onclick = () => {
    curMap = +el.dataset.i; sel = null; fitView(); render();
  });
}

// 입력칸이 확정될 때(포커스 아웃·엔터·스피너) 한 번만 기록한다.
// oninput 마다 쌓으면 글자 하나에 한 단계씩 밀려 되돌리기가 쓸모없어진다.
function commitOn(root){
  root.querySelectorAll("input,select,textarea").forEach(el =>
    el.addEventListener("change", () => pushHistory()));
}

function renderProps(){
  const m = M(); const box = $("#m-props");
  if(!m){ box.innerHTML = ""; return; }

  // 선택된 배치물이 있으면 그 속성을, 없으면 맵 자체 속성을
  if(sel && sel.type === "node"){
    const n = m.nodes[sel.i];
    box.innerHTML =
      `<div class="mtitle">자원 노드</div>` +
      field("종류", `<select id="n-item">${NODE_KINDS.map(k=>
        `<option value="${k.item}" ${k.item===n.item?"selected":""}>${k.ko}</option>`).join("")}</select>`) +
      field("크기", `<select id="n-size">${[1,2,3].map(s=>
        `<option value="${s}" ${s===n.size?"selected":""}>${s}×${s}</option>`).join("")}</select>`) +
      field("채굴 간격", `<input id="n-int" type="number" min="0.1" step="0.1" value="${n.extractInterval}" title="배율 1 기준 1개당 초. 값이 클수록 캐기 어려운 광맥">`) +
      field("최대 재고", `<input id="n-stock" type="number" min="1" value="${n.maxStock}">`) +
      `<div class="field"><label>위치</label><div class="xyrow">
        <input id="n-x" type="number" value="${n.x}"><input id="n-y" type="number" value="${n.y}">
        <span class="mono" title="코어에서의 거리">${Math.round(Math.hypot(n.x-(m.core.x+1.5), n.y-(m.core.y+1.5)))}칸</span>
      </div></div>` +
      `<button class="mini warn" id="n-del" style="margin-top:10px">삭제</button>`;
    $("#n-x").oninput = e => { n.x = Math.max(0,Math.min(m.width-1, +e.target.value|0)); draw(); };
    $("#n-y").oninput = e => { n.y = Math.max(0,Math.min(m.height-1, +e.target.value|0)); draw(); };
    $("#n-item").onchange = e => { n.item = e.target.value; render(); };
    $("#n-size").onchange = e => { n.size = +e.target.value; render(); };
    $("#n-int").oninput = e => { n.extractInterval = Math.max(0.1, +e.target.value||1); };
    $("#n-stock").oninput = e => { n.maxStock = Math.max(1, +e.target.value||1); };
    $("#n-del").onclick = () => { m.nodes.splice(sel.i,1); sel = null; pushHistory(); render(); };
    commitOn(box);
    return;
  }
  if(sel && sel.type === "nest"){
    const n = m.nests[sel.i];
    box.innerHTML =
      `<div class="mtitle">몬스터 둥지</div>` +
      `<div class="field"><label>위치</label><div class="xyrow">
        <input id="s-x" type="number" value="${n.x}"><input id="s-y" type="number" value="${n.y}">
        <span class="mono" title="코어에서의 거리">${Math.round(Math.hypot(n.x-(m.core.x+1.5), n.y-(m.core.y+1.5)))}칸</span>
      </div></div>` +
      `<div class="field wide"><label>방어 반응 <span style="color:var(--faint)">· 낮에 접근했을 때</span></label>
        <div class="gungrid">
        <label class="gcell" title="플레이어가 이 안에 들어오면 경고가 뜬다"><span>경고 반경</span>
          <input id="s-warn" type="number" min="1" value="${n.warningRange}"></label>
        <label class="gcell" title="이 안으로 들어오면 방어 몬스터가 튀어나온다"><span>진입 반경</span>
          <input id="s-trig" type="number" min="1" value="${n.triggerRange}"></label>
        </div>
        <div class="gungrid" style="margin-top:6px">
        <label class="gcell" title="한 번에 나오는 방어 몬스터 수"><span>스폰 수</span>
          <input id="s-amt" type="number" min="1" value="${n.defenseSpawnAmount}"></label>
        <label class="gcell" title="다시 나오기까지 걸리는 시간(초)"><span>쿨타임</span>
          <input id="s-cd" type="number" min="1" step="0.5" value="${n.defenseSpawnCooldown}"></label>
        </div></div>` +
      `<div class="field wide"><label>스폰 지점 <span style="color:var(--faint)">· 밤 웨이브가 나오는 자리</span></label>
        <div>${n.spawnPoints.map((p,i)=>`
          <div class="sprow">
            <span class="mono">#${i+1}</span>
            <input type="number" value="${p.x}" data-spx="${i}" title="둥지 기준 상대 좌표">
            <input type="number" value="${p.y}" data-spy="${i}">
            <label class="chk" title="이 지점에 보스가 붙는다"><input type="checkbox" data-spb="${i}" ${p.hasBoss?"checked":""}>보스</label>
            <span class="x" data-spd="${i}">✕</span>
          </div>`).join("")}
          <button class="mini" id="s-addsp" style="margin-top:4px">+ 지점</button>
        </div></div>` +
      `<div class="field wide"><label>교전 <span style="color:var(--faint)">· 언제 얼마나 달려드는가</span></label>
        <div class="gungrid">
        <label class="gcell" title="이보다 가까우면 추가 스폰을 멈춘다 — 코앞에서 무한히 쏟아지지 않게"><span>최소</span>
          <input id="e-min" type="number" min="0" step="0.5" value="${n.engageMinRange}"></label>
        <label class="gcell" title="이 밖이면 아예 반응하지 않는다"><span>최대</span>
          <input id="e-max" type="number" min="0" step="0.5" value="${n.engageMaxRange}"></label>
        <label class="gcell" title="이미 교전한 몬스터가 쫓아오는 한계"><span>추격</span>
          <input id="e-chase" type="number" min="0" step="0.5" value="${n.chaseRange}"></label>
        </div>
        <div class="gungrid" style="margin-top:6px">
        <label class="gcell" title="이보다 멀어지면 둥지로 돌아간다"><span>귀환</span>
          <input id="e-leash" type="number" min="0" step="0.5" value="${n.leashRange}"></label>
        <label class="gcell" style="grid-column:span 2"><span>낮에만</span>
          <label class="chk" style="margin-top:3px"><input type="checkbox" id="e-day" ${n.engageDayOnly?"checked":""}>
            밤에는 웨이브가 주도</label></label>
        </div></div>` +
      `<div class="field wide"><label>복구 <span style="color:var(--faint)">· 부순 뒤 다시 서기까지</span></label>
        <div class="gungrid">
        <label class="gcell"><span>보스(일)</span>
          <input id="r-boss" type="number" min="0" value="${n.bossRecoveryDays}"></label>
        <label class="gcell"><span>둥지(일)</span>
          <input id="r-nest" type="number" min="0" value="${n.nestRecoveryDays}"></label>
        </div></div>` +
      `<button class="mini warn" id="s-del" style="margin-top:14px">둥지 삭제</button>`;
    $("#s-x").oninput = e => { n.x = Math.max(0,Math.min(m.width-1, +e.target.value|0)); draw(); };
    $("#s-y").oninput = e => { n.y = Math.max(0,Math.min(m.height-1, +e.target.value|0)); draw(); };
    const bindN = (id, key, min) => { const el = $("#"+id); if(el) el.oninput = e => {
      n[key] = Math.max(min, +e.target.value||min); draw(); }; };
    bindN("s-warn","warningRange",1); bindN("s-trig","triggerRange",1);
    bindN("s-amt","defenseSpawnAmount",1); bindN("s-cd","defenseSpawnCooldown",1);
    bindN("e-min","engageMinRange",0); bindN("e-max","engageMaxRange",0);
    bindN("e-chase","chaseRange",0); bindN("e-leash","leashRange",0);
    bindN("r-boss","bossRecoveryDays",0); bindN("r-nest","nestRecoveryDays",0);
    $("#e-day").onchange = e => { n.engageDayOnly = e.target.checked; };
    $$("[data-spx]").forEach(el => el.oninput = e => { n.spawnPoints[+el.dataset.spx].x = +e.target.value|0; draw(); });
    $$("[data-spy]").forEach(el => el.oninput = e => { n.spawnPoints[+el.dataset.spy].y = +e.target.value|0; draw(); });
    $$("[data-spb]").forEach(el => el.onchange = e => { n.spawnPoints[+el.dataset.spb].hasBoss = e.target.checked; draw(); });
    $$("[data-spd]").forEach(el => el.onclick = () => {
      if(n.spawnPoints.length < 2) return;
      n.spawnPoints.splice(+el.dataset.spd,1); pushHistory(); render(); });
    $("#s-addsp").onclick = () => { n.spawnPoints.push({x:2,y:2,hasBoss:false}); pushHistory(); render(); };
    $("#s-del").onclick = () => { m.nests.splice(sel.i,1); sel = null; pushHistory(); render(); };
    commitOn(box);
    return;
  }

  box.innerHTML =
    `<div class="mtitle">맵</div>` +
    field("Id", `<div class="idrow"><span class="pfx mono">Map:</span>
      <input id="m-id" class="mono" value="${esc((m.id||"").replace(/^Map:/,""))}" placeholder="Plains01"></div>`) +
    field("이름", `<input id="m-name" value="${esc(m.displayName)}">`) +
    field("설명", `<textarea id="m-desc" class="autogrow" rows="1">${esc(m.description)}</textarea>`, "top") +
    `<div class="field wide"><label>크기</label><div class="gungrid">
      <label class="gcell" title="홀수만 — 코어 3×3 이 정확히 가운데 오려면 중심 칸이 하나여야 한다"><span>가로</span>
        <input id="m-w" type="number" min="21" max="401" step="2" value="${m.width}"></label>
      <label class="gcell" title="홀수만"><span>세로</span>
        <input id="m-h" type="number" min="21" max="401" step="2" value="${m.height}"></label>
    </div></div>` +
    ringStat(m);

  $("#m-id").oninput = e => { m.id = e.target.value ? "Map:" + e.target.value.replace(/[^A-Za-z0-9_가-힣]/g,"") : ""; renderWarn(); };
  $("#m-name").oninput = e => { m.displayName = e.target.value; renderList(); renderWarn(); };
  $("#m-desc").oninput = e => { m.description = e.target.value; };
  const resize = () => {
    const w = odd(+$("#m-w").value || 121);
    const h = odd(+$("#m-h").value || 121);
    if(w !== +$("#m-w").value) $("#m-w").value = w;   // 짝수를 넣으면 다음 홀수로 올린다
    if(h !== +$("#m-h").value) $("#m-h").value = h;
    if(w === m.width && h === m.height) return;
    const t = new Uint8Array(w*h);           // 크기를 바꿔도 겹치는 부분은 남긴다
    for(let y = 0; y < Math.min(h, m.height); y++)
      for(let x = 0; x < Math.min(w, m.width); x++) t[y*w+x] = m.tiles[y*m.width+x];
    // 코어가 가운데 있었다면 새 가운데로 옮긴다 — Ring 중심이 코어라 안 옮기면 원이 한쪽으로 쏠린다.
    // 일부러 치우쳐 둔 코어는 그대로 두고 범위만 맞춘다.
    const wasCentered = m.core.x === (m.width>>1)-1 && m.core.y === (m.height>>1)-1;
    m.width = w; m.height = h; m.tiles = t;
    if(wasCentered) m.core = { x:(w>>1)-1, y:(h>>1)-1 };
    else m.core = { x:Math.max(0,Math.min(w-3,m.core.x)), y:Math.max(0,Math.min(h-3,m.core.y)) };
    fitView(); render(); pushHistory();
  };
  $("#m-w").onchange = resize; $("#m-h").onchange = resize;
  autogrow(box);
  commitOn(box);
}

// Ring 별 자원 분포 — 테크트리 진행과 직결되므로 눈으로 확인한다
function ringStat(m){
  const cx = m.core.x + 1.5, cy = m.core.y + 1.5;
  const g = guideRings(m);
  const ringOf = (x,y) => {
    const d = Math.hypot(x-cx, y-cy);
    return d <= g[0] ? 0 : d <= g[1] ? 1 : d <= g[2] ? 2 : 3;
  };
  const names = ["안쪽","중간","바깥","모서리"];
  const rows = names.map((nm, r) => {
    const cells = NODE_KINDS.map(k => {
      const n = m.nodes.filter(x => x.item === k.item && ringOf(x.x,x.y) === r).length;
      return `<td style="color:${n?k.color:"var(--faint)"}">${n||"·"}</td>`;
    }).join("");
    const nest = m.nests.filter(x => ringOf(x.x,x.y) === r).length;
    return `<tr><td style="color:var(--muted)">${nm}</td>${cells}<td style="color:${nest?"#FF5D73":"var(--faint)"}">${nest||"·"}</td></tr>`;
  }).join("");
  return `<div class="field wide"><label>분포</label><div>
    <table class="rstat"><tr><th></th><th>철</th><th>구리</th><th>크리</th><th>둥지</th></tr>${rows}</table>
  </div></div>`;
}

function renderWarn(){
  const w = validate();
  $("#m-warn").innerHTML = w.length
    ? "<h3>검증</h3>" + w.map(x=>`<div class="w">${esc(x)}</div>`).join("")
    : `<div class="okmsg">✓ 검증 통과</div>`;
}

// ── 도구 바인딩 ───────────────────────────────────────────
function boot(){
  cv = $("#m-canvas"); if(!cv) return;
  ctx = cv.getContext("2d");
  maps = [blankMap()];

  const resizeCanvas = () => {
    const box = cv.parentElement;
    const w = box.clientWidth, h = box.clientHeight;
    if(!w || !h) return;                       // 숨겨진 상태 — 크기를 0 으로 만들면 내용이 사라진다
    cv.width = w; cv.height = h;
    if(!_fitted){ fitView(); _fitted = true; } // 처음 보일 때 한 번만 맞춘다
    draw();
  };
  _resize = resizeCanvas;
  if(typeof ResizeObserver !== "undefined") new ResizeObserver(resizeCanvas).observe(cv.parentElement);
  else window.addEventListener("resize", resizeCanvas);

  $$("#m-tools [data-tool]").forEach(b => b.onclick = () => {
    tool = b.dataset.tool;
    $$("#m-tools [data-tool]").forEach(x => x.classList.toggle("on", x === b));
    $("#m-paintrow").style.display = tool === "paint" ? "" : "none";
    sel = null; render();
  });
  $$("#m-tiles [data-tile]").forEach(b => b.onclick = () => {
    paintTile = +b.dataset.tile;
    $$("#m-tiles [data-tile]").forEach(x => x.classList.toggle("on", x === b));
  });
  $("#m-brush").oninput = e => { brush = +e.target.value; $("#m-brushv").textContent = brush; draw(); };
  ["rings","halo","grid"].forEach(k => {
    const el = $("#m-show-"+k);
    if(el) el.onchange = e => {
      if(k==="rings") showRings = e.target.checked;
      if(k==="halo")  showHalo  = e.target.checked;
      if(k==="grid")  showGrid  = e.target.checked;
      draw();
    };
  });
  $("#m-add").onclick = () => { maps.push(blankMap(maps.length+1)); curMap = maps.length-1; sel = null; fitView(); render(); pushHistory(); };
  $("#m-del").onclick = () => { if(maps.length<2) return; maps.splice(curMap,1); curMap = Math.max(0,curMap-1); sel = null; fitView(); render(); pushHistory(); };
  $("#m-fit").onclick = () => { fitView(); draw(); };

  // 맵은 GameData 와 따로 나가지만 창은 같은 것을 쓴다
  $("#m-export").onclick = () =>
    window.openDataModal("MapData.json — 내보내기",
      JSON.stringify({ maps: window.MapData.getMaps() }, null, 2), false, "map");
  $("#m-import").onclick = () =>
    window.openDataModal("MapData.json — 붙여넣고 적용", "", true, "map");
  $("#m-fill").onclick = () => {
    const m = M();
    if(!confirm(`맵 전체를 ${tileOf(paintTile).ko}(으)로 채웁니다.`)) return;
    m.tiles.fill(paintTile); pushHistory(); render();
  };

  // 캔버스 조작
  cv.addEventListener("mousedown", ev => {
    const r = cv.getBoundingClientRect();
    const px = ev.clientX - r.left, py = ev.clientY - r.top;
    const c = toCell(px, py);
    if(ev.button === 1 || ev.shiftKey){ drag = { type:"pan", sx:px, sy:py, vx:view.x, vy:view.y }; return; }
    const m = M();

    // 우클릭 = 지우기. 배치물이 있으면 그것을, 없으면 지형을 지면으로 되돌린다
    if(ev.button === 2){
      const hit = hitTest(c.x, c.y);
      if(hit){
        (hit.type === "node" ? m.nodes : m.nests).splice(hit.i, 1);
        sel = null; pushHistory(); render(); return;
      }
      const ni = m.nightSpawnPoints.findIndex(p => p.x === c.x && p.y === c.y);
      if(ni >= 0){ m.nightSpawnPoints.splice(ni,1); pushHistory(); render(); return; }
      if(tool === "paint"){ drag = { type:"erase" }; eraseAt(c.x, c.y); draw(); }
      return;
    }
    if(tool === "paint"){ drag = { type:"paint" }; paintAt(c.x, c.y); draw(); return; }
    // 배치 도구여도 이미 있는 것을 누르면 새로 놓지 않고 고른다 —
    // 겹쳐 쌓이면 지우기도 어렵고 실수인 경우가 대부분이다
    if(tool === "node" || tool === "nest"){
      if(!inBounds(m, c.x, c.y)) return;
      const hit = hitTest(c.x, c.y);
      if(hit){ sel = hit; drag = { type:"move", ...hit, ox:c.x, oy:c.y }; render(); return; }
      if(tool === "node"){
        m.nodes.push({ item:NODE_KINDS[0].item, x:c.x, y:c.y, size:1, extractInterval:1, maxStock:20 });
        sel = { type:"node", i:m.nodes.length-1 };
      } else {
        m.nests.push({ x:c.x, y:c.y, warningRange:25, triggerRange:15,
                     defenseSpawnAmount:3, defenseSpawnCooldown:10,
                     spawnPoints:[{ x:0, y:0, hasBoss:false }],
                     engageMinRange:4, engageMaxRange:18, chaseRange:24, leashRange:32,
                     engageDayOnly:true, bossRecoveryDays:3, nestRecoveryDays:5 });
        sel = { type:"nest", i:m.nests.length-1 };
      }
      pushHistory(); render(); return;
    }
    // 밤 웨이브가 맵으로 들어오는 자리 — 둥지 스폰과 다르다.
    // 둥지 것은 낮에 다가갔을 때 튀어나오는 자리, 이쪽은 밤에 코어로 밀려드는 대문이다.
    if(tool === "night"){
      if(!inBounds(m, c.x, c.y)) return;
      const at = m.nightSpawnPoints.findIndex(p => p.x === c.x && p.y === c.y);
      if(at >= 0) m.nightSpawnPoints.splice(at, 1);
      else m.nightSpawnPoints.push({ x:c.x, y:c.y });
      pushHistory(); render(); return;
    }
    if(tool === "core"){
      if(!inBounds(m, c.x, c.y)) return;
      m.core = { x:Math.max(0,Math.min(m.width-3,c.x-1)), y:Math.max(0,Math.min(m.height-3,c.y-1)) };
      pushHistory(); render(); return;
    }
    if(tool === "select"){
      const hit = hitTest(c.x, c.y);
      sel = hit;
      if(hit) drag = { type:"move", ...hit, ox:c.x, oy:c.y };
      render(); return;
    }
  });
  cv.addEventListener("mousemove", ev => {
    const r = cv.getBoundingClientRect();
    const px = ev.clientX - r.left, py = ev.clientY - r.top;
    const c = toCell(px, py);
    hoverCell = c;
    const hint = $("#m-hint");
    const m = M();
    if(hint && inBounds(m, c.x, c.y)){
      const t = tileOf(m.tiles[idx(m,c.x,c.y)]);
      hint.textContent = `${c.x}, ${c.y} · ${t.ko}`;
    }
    if(!drag){ if(tool === "paint") draw(); return; }
    if(drag.type === "pan"){ view.x = drag.vx + px - drag.sx; view.y = drag.vy + py - drag.sy; draw(); return; }
    if(drag.type === "paint"){ paintAt(c.x, c.y); draw(); return; }
    if(drag.type === "erase"){ eraseAt(c.x, c.y); draw(); return; }
    if(drag.type === "move"){
      const arr = drag.type === "move" && drag.i != null
        ? (sel.type === "node" ? m.nodes : m.nests) : null;
      if(!arr) return;
      const o = arr[sel.i];
      o.x += c.x - drag.ox; o.y += c.y - drag.oy;
      o.x = Math.max(0, Math.min(m.width-1, o.x));
      o.y = Math.max(0, Math.min(m.height-1, o.y));
      drag.ox = c.x; drag.oy = c.y;
      draw();
    }
  });
  window.addEventListener("mouseup", () => {
    if(drag && ["paint","erase","move"].includes(drag.type)){ renderWarn(); pushHistory(); }
    drag = null;
  });
  cv.addEventListener("wheel", ev => {
    ev.preventDefault();
    const r = cv.getBoundingClientRect();
    const px = ev.clientX - r.left, py = ev.clientY - r.top;
    const k2 = Math.max(1, Math.min(40, view.k * (ev.deltaY < 0 ? 1.15 : 1/1.15)));
    view.x = px - (px - view.x) * k2/view.k;
    view.y = py - (py - view.y) * k2/view.k;
    view.k = k2; draw();
  }, { passive:false });
  cv.addEventListener("contextmenu", ev => ev.preventDefault());

  window.addEventListener("keydown", ev => {
    if(!document.getElementById("pane-map")?.classList.contains("on")) return;
    if(/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)) return;
    if(!(ev.ctrlKey || ev.metaKey)) return;
    const k = ev.key.toLowerCase();
    if(k === "z" && !ev.shiftKey){ ev.preventDefault(); undo(); }
    else if(k === "y" || (k === "z" && ev.shiftKey)){ ev.preventDefault(); redo(); }
  });
  $("#m-undo").onclick = undo;
  $("#m-redo").onclick = redo;

  setTimeout(() => { resizeCanvas(); render(); pushHistory(); }, 0);
}
if(document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
else boot();
})();

