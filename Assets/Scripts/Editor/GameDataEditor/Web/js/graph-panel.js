// ═══════════════════════════════════════════════════════════
//  아이템·레시피 그래프 — 사이드 패널 · 입력 바인딩
// ═══════════════════════════════════════════════════════════


// ───────────────────────── 사이드 패널 ─────────────────────────
function select(sel){ selection = sel; render(); renderSide(); }
// ── 아이템 역할 모듈 (Feature_Damage: ItemDataSO + AmmoModuleSO/WeaponModuleSO) ──
// 상속이 아니라 조합이므로, type 이 Ammo/Weapon 이면 해당 모듈 필드가 붙는다.
const combatEffects = () => { try { return window.CombatData?.getEffects() || []; } catch { return []; } };
const combatGuns    = () => { try { return window.CombatData?.getGuns()    || []; } catch { return []; } };

function moduleSection(d){
  if(d.type === "Ammo"){
    const list = combatEffects();
    const rows = (d.attackEffects||[]).map((e,i)=>`
      <div class="mrow">
        <select data-aeff="${i}">
          ${list.map(x=>`<option value="${esc(x.id)}" ${x.id===e.effect?"selected":""}>${esc(x.id)}</option>`).join("")}
          ${e.effect && !list.some(x=>x.id===e.effect) ? `<option value="${esc(e.effect)}" selected>${esc(e.effect)} (없음)</option>` : ""}
        </select>
        <input type="number" step="0.5" value="${e.value}" data-aval="${i}">
        <span class="x" data-adel="${i}">✕</span>
      </div>`).join("");
    const dmg = (d.attackEffects||[]).reduce((sum,e)=>{
      const ef = list.find(x=>x.id===e.effect);
      return sum + (ef && ef.kind==="Damage" ? (+e.value||0) : 0); }, 0);
    const bal = (id, label, val, step, title) =>
      `<label class="bcell" title="${esc(title||"")}"><span>${label}</span>
        <input type="number" min="0" step="${step}" value="${val}" data-bal="${id}"></label>`;
    return `<div class="field modblock">
      <label>AmmoModule <span style="color:var(--muted)">· 1발 명중 효과</span></label>
      ${rows || `<div class="costempty">효과 없음 — 명중해도 아무 일도 일어나지 않습니다</div>`}
      <div class="mfoot">
        <button class="mini" id="btn-addeff">+ 효과</button>
        ${dmg>0 ? `<span class="mnote">1발 피해 <b>${dmg}</b></span>` : ""}
      </div>
      <div class="msub">탄도 <span style="color:var(--faint)">· 발사기가 아니라 탄이 갖는다</span></div>
      <div class="balgrid">
        ${bal("speed","속도",           d.speed ?? 50,  1,   "m/s. 0 이면 Projectile 총이 못 쏜다")}
        ${bal("gravity","중력",         d.gravity ?? 0, 0.1, "0 = 직선. 유탄은 9.8 로 포물선")}
        ${bal("explosionRadius","폭발 R", d.explosionRadius ?? 0, 0.5, "0 = 단일 대상")}
        ${bal("lifetime","수명",         d.lifetime ?? 3, 0.5, "초. 이 시간이 지나면 사라진다")}
        ${bal("pierce","관통",           d.pierce ?? 0, 1,   "첫 대상 뒤로 더 뚫는 수. 0 = 맞으면 멈춤")}
      </div></div>`;
  }
  if(d.type === "Weapon"){
    const gl = combatGuns();
    return `<div class="field modblock">
      <label>WeaponModule <span style="color:var(--muted)">· 장착할 총</span></label>
      <select id="f-gun">
        <option value="">(없음)</option>
        ${gl.map(g=>`<option value="${esc(g.id)}" ${g.id===d.gun?"selected":""}>${esc(g.id)}</option>`).join("")}
        ${d.gun && !gl.some(g=>g.id===d.gun) ? `<option value="${esc(d.gun)}" selected>${esc(d.gun)} (없음)</option>` : ""}
      </select>
      ${gl.length ? "" : `<div class="costempty" style="margin-top:5px">전투 탭에서 총을 먼저 만드세요</div>`}
      </div>`;
  }
  return "";
}

function bindModule(d){
  // type 이 바뀌면 모듈 섹션이 즉시 나타나거나 사라져야 한다
  const t = document.getElementById("f-type");
  if(t) t.addEventListener("change", () => setTimeout(renderSide, 0));
  const add = document.getElementById("btn-addeff");
  if(add) add.onclick = () => {
    d.attackEffects = d.attackEffects || [];
    d.attackEffects.push({ effect: combatEffects()[0]?.id || "Effect:Damage", value: 10 });
    renderSide(); render();
  };
  document.querySelectorAll("[data-aeff]").forEach(el => el.onchange = e => {
    d.attackEffects[+el.dataset.aeff].effect = e.target.value; renderSide(); render(); });
  document.querySelectorAll("[data-aval]").forEach(el => el.oninput = e => {
    d.attackEffects[+el.dataset.aval].value = +e.target.value || 0; renderSide(); render(); });
  document.querySelectorAll("[data-adel]").forEach(el => el.onclick = () => {
    d.attackEffects.splice(+el.dataset.adel,1); renderSide(); render(); });
  document.querySelectorAll("[data-bal]").forEach(el => el.oninput = e => {
    d[el.dataset.bal] = Math.max(0, +e.target.value || 0);
  });
  const g = document.getElementById("f-gun");
  if(g) g.onchange = e => { d.gun = e.target.value; render(); };
}

function renderSide(){
  const body = $("#side-body"), title = $("#side-title");
  if(!selection){ title.textContent = "선택 없음"; body.innerHTML = ""; return; }
  if(selection.type === "edge"){
    const e = edges.get(selection.id); if(!e){ selection=null; return renderSide(); }
    const f = nodes.get(e.from), t = nodes.get(e.to);
    const isIn = f.kind === "item";
    title.textContent = isIn ? "재료 연결 (input)" : "결과물 연결 (output)";
    body.innerHTML = `
      <div class="field"><label>${isIn?"아이템 → 레시피":"레시피 → 아이템"}</label>
        <div style="font-size:12.5px;color:var(--muted)">${esc(f.data.displayName)} → ${esc(t.data.displayName)}</div></div>
      <div class="field"><label>amount (수량)</label><input id="f-amount" type="number" min="1" step="1" value="${e.amount}"></div>
      <button class="warn" id="btn-del">연결 삭제 (Delete)</button>`;
    $("#f-amount").oninput = ev => { e.amount = Math.max(1, +ev.target.value||1); render(); };
    $("#btn-del").onclick = removeSelection;
    return;
  }
  const n = nodes.get(selection.id); if(!n){ selection=null; return renderSide(); }
  title.textContent = n.kind === "item" ? "아이템 속성" : "레시피 속성";
  const common = `
    <div class="field"><label>id 이름 (영문/숫자/_/한글 — "${n.kind==="item"?"Item":"Recipe"}:" 자동 접두)</label>
      <input id="f-name" value="${esc(n.data.name)}"></div>
    <div class="field"><label>displayName</label><input id="f-disp" value="${esc(n.data.displayName)}"></div>
    <div class="field"><label>description</label><textarea id="f-desc" rows="2">${esc(n.data.description)}</textarea></div>`;
  body.innerHTML = n.kind === "item" ? common + `
    <div class="field"><label>type — enum ItemType (분류 태그)</label>
      <select id="f-type">${ITEM_TYPES.map(t=>`<option value="${t.v}" ${t.v===n.data.type?"selected":""}>${t.v} — ${t.ko} · ${t.desc}</option>`).join("")}${TYPE_SET.has(n.data.type)?"":`<option value="${esc(n.data.type)}" selected>${esc(n.data.type)} — 알 수 없음</option>`}</select></div>
    <div class="field"><label>line — enum ItemLine (계통)</label>
      <select id="f-line">${ITEM_LINES.map(l=>`<option value="${l.v}" ${l.v===(n.data.line||"None")?"selected":""}>${l.v} — ${l.ko}</option>`).join("")}${LINE_SET.has(n.data.line||"None")?"":`<option value="${esc(n.data.line)}" selected>${esc(n.data.line)} — 알 수 없음</option>`}</select></div>
    <div class="field"><label>icon (스프라이트 이름 검색 — 비우면 기존 유지)</label><input id="f-icon" value="${esc(n.data.icon)}"></div>
    ${moduleSection(n.data)}
    <button class="warn" id="btn-del">노드 삭제 (Delete)</button>`
  : common + `
    <div class="field"><label>tier (해금되는 코어 수리 단계)</label><input id="f-tier" type="number" min="0" step="1" value="${n.data.tier}"></div>
    <div class="field"><label>craftTime (초)</label><input id="f-time" type="number" min="0" step="0.1" value="${n.data.craftTime}"></div>
    <button class="warn" id="btn-del">노드 삭제 (Delete)</button>`;
  const bind = (id, key, num) => { const el = $("#"+id); if(el) el.oninput = ev => { n.data[key] = num ? +ev.target.value : ev.target.value; render(); }; };
  bind("f-name","name"); bind("f-disp","displayName"); bind("f-desc","description");
  bind("f-type","type"); bind("f-line","line"); bind("f-icon","icon");
  bindModule(n.data);
  bind("f-tier","tier",1); bind("f-time","craftTime",1);
  $("#btn-del").onclick = removeSelection;
}

// ───────────────────────── 인터랙션 ─────────────────────────
let drag = null; // {type:'node'|'pan'|'wire', ...}
let lastEdgeClick = { id:null, t:0 };
let hoverNode = null;   // 커서가 올라간 노드 — 관련 간선만 남기고 나머지를 흐리게
function toWorld(ev){ const r = wrap.getBoundingClientRect(); return { x:(ev.clientX-r.left-view.x)/view.k, y:(ev.clientY-r.top-view.y)/view.k }; }

wrap.addEventListener("mousedown", ev => {
  // 펼치기 토글 — 헤더 드래그보다 먼저 잡는다
  const exp = ev.target.closest("[data-exp]");
  if(exp){
    const n = nodes.get(exp.dataset.exp);
    if(n){ n.expanded = !n.expanded; render(); pushHistory(); }
    ev.preventDefault(); ev.stopPropagation(); return;
  }
  const port = ev.target.closest(".port");
  if(port){
    drag = { type:"wire", node: port.dataset.node, side: port.dataset.side, cur: toWorld(ev) };
    ev.preventDefault(); return;
  }
  const head = ev.target.closest(".node .head");
  if(head){
    const id = head.parentElement.id.slice(3);
    const n = nodes.get(id), p = toWorld(ev);
    drag = { type:"node", id, ox: p.x-n.x, oy: p.y-n.y };
    select({type:"node", id});
    ev.preventDefault(); return;
  }
  const nodeEl = ev.target.closest(".node");
  if(nodeEl){ select({type:"node", id: nodeEl.id.slice(3)}); return; }
  const eEl = ev.target.closest("[data-edge]");
  if(eEl){
    const id = eEl.dataset.edge, now = Date.now();
    const isDbl = lastEdgeClick.id === id && (now - lastEdgeClick.t) < 400;
    lastEdgeClick = { id, t: now };
    select({type:"edge", id});
    if(isDbl){ ev.preventDefault(); const f = $("#f-amount"); if(f){ f.focus(); f.select(); } }
    return;
  }
  drag = { type:"pan", sx: ev.clientX, sy: ev.clientY, vx: view.x, vy: view.y };
  wrap.classList.add("panning");
  select(null);
});
window.addEventListener("mousemove", ev => {
  if(!drag) return;
  if(drag.type === "pan"){ view.x = drag.vx + ev.clientX - drag.sx; view.y = drag.vy + ev.clientY - drag.sy; worldEl.style.transform = `translate(${view.x}px,${view.y}px) scale(${view.k})`; renderSticky(); }
  else if(drag.type === "node"){ const n = nodes.get(drag.id), p = toWorld(ev); n.x = p.x-drag.ox; n.y = p.y-drag.oy; render(); }
  else if(drag.type === "wire"){ drag.cur = toWorld(ev); render(); drawTempWire(); }
});
window.addEventListener("mouseup", ev => {
  if(drag && drag.type === "wire"){
    const port = document.elementFromPoint(ev.clientX, ev.clientY)?.closest?.(".port");
    if(port && port.dataset.node !== drag.node){
      // 방향 정규화: out → in
      let from = drag.node, to = port.dataset.node;
      if(drag.side === "in"){ [from, to] = [to, from]; }
      const created = addEdge(from, to);
      if(created) select({type:"edge", id:created});
    }
  }
  const wasEdit = drag && (drag.type === "node" || drag.type === "wire");
  wrap.classList.remove("panning");
  drag = null; render();
  if(wasEdit) pushHistory();
});
function drawTempWire(){
  const a = portPos(drag.node, drag.side === "in" ? "in" : "out");
  const b = drag.cur;
  const dx = Math.max(50, Math.abs(b.x-a.x)/2);
  edgesEl.innerHTML += `<path d="M ${a.x} ${a.y} C ${a.x+dx} ${a.y}, ${b.x-dx} ${b.y}, ${b.x} ${b.y}" fill="none" stroke="#B48CFF" stroke-width="2" stroke-dasharray="5 4" opacity=".8"/>`;
}
wrap.addEventListener("wheel", ev => {
  ev.preventDefault();
  const r = wrap.getBoundingClientRect(), mx = ev.clientX-r.left, my = ev.clientY-r.top;
  const k2 = Math.min(2, Math.max(.3, view.k * (ev.deltaY < 0 ? 1.1 : 0.9)));
  view.x = mx - (mx-view.x) * k2/view.k; view.y = my - (my-view.y) * k2/view.k; view.k = k2;
  worldEl.style.transform = `translate(${view.x}px,${view.y}px) scale(${view.k})`;
  renderSticky();
}, {passive:false});
window.addEventListener("keydown", ev => {
  const typing = /INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName);
  if((ev.ctrlKey || ev.metaKey) && !typing){
    const k = ev.key.toLowerCase();
    if(k === "z" && !ev.shiftKey){ ev.preventDefault(); undo(); return; }
    if(k === "y" || (k === "z" && ev.shiftKey)){ ev.preventDefault(); redo(); return; }
  }
  if((ev.key === "Delete" || ev.key === "Backspace") && selection && !/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)){
    ev.preventDefault(); removeSelection();
  }
});

// ───────────────────────── 툴바 / 모달 ─────────────────────────
$("#btn-undo").onclick = undo;
$("#btn-redo").onclick = redo;
$("#btn-layout").onclick = () => { autoLayout(); render(); pushHistory(); };
$("#btn-expand").onclick = e => {
  const any = [...nodes.values()].some(n => n.kind === "recipe" && !n.expanded);
  for(const n of nodes.values()) if(n.kind === "recipe") n.expanded = any;
  e.target.textContent = any ? "재료 접기" : "재료 펼치기";
  render(); pushHistory();
};

// ───────────────────────── 우클릭 메뉴 ─────────────────────────
const ctx = $("#ctx");
let ctxWorld = {x:0,y:0};
wrap.addEventListener("mouseover", ev => {
  const el = ev.target.closest(".node");
  const id = el ? el.id.slice(3) : null;
  if(id !== hoverNode){ hoverNode = id; render(); }
});
wrap.addEventListener("mouseleave", () => { if(hoverNode){ hoverNode = null; render(); } });

wrap.addEventListener("contextmenu", ev => {
  ev.preventDefault();
  ctxWorld = toWorld(ev);
  const nodeEl = ev.target.closest(".node");
  const eEl = ev.target.closest("[data-edge]");
  let html = "";
  if(nodeEl){
    select({type:"node", id: nodeEl.id.slice(3)});
    html = `<button class="danger" data-act="del">이 노드 삭제</button>`;
  } else if(eEl){
    select({type:"edge", id: eEl.dataset.edge});
    html = `<button class="danger" data-act="del">이 연결 삭제</button>`;
  } else {
    html = `<button class="item" data-act="add-item">＋ 아이템 추가</button>
            <button class="recipe" data-act="add-recipe">＋ 레시피 추가</button>
            <div class="sep"></div>
            <button data-act="layout">자동 정렬</button>`;
  }
  ctx.innerHTML = html;
  ctx.style.left = Math.min(ev.clientX, innerWidth-190) + "px";
  ctx.style.top = Math.min(ev.clientY, innerHeight-160) + "px";
  ctx.classList.add("show");
});
ctx.addEventListener("click", ev => {
  const act = ev.target.dataset.act;
  ctx.classList.remove("show");
  if(act === "add-item") addNode("item", ctxWorld.x, ctxWorld.y);
  else if(act === "add-recipe") addNode("recipe", ctxWorld.x, ctxWorld.y);
  else if(act === "layout"){ autoLayout(); render(); }
  else if(act === "del") removeSelection();
});
window.addEventListener("mousedown", ev => { if(!ev.target.closest("#ctx")) ctx.classList.remove("show"); });


const modal = $("#modal");
let modalMode = "export";
$("#btn-export").onclick = () => {
  modalMode = "export";
  $("#modal-title").textContent = "JSON 내보내기 — GameData.json";
  $("#modal-text").value = JSON.stringify(exportJson(), null, 2);
  $("#modal-apply").style.display = "none"; $("#modal-msg").textContent = "";
  modal.classList.add("show");
};
$("#btn-import").onclick = () => {
  modalMode = "import";
  $("#modal-title").textContent = "JSON 불러오기 — GameData.json 내용을 붙여넣으세요";
  $("#modal-text").value = "";
  $("#modal-apply").style.display = ""; $("#modal-msg").textContent = "";
  modal.classList.add("show");
};
$("#modal-apply").onclick = () => {
  try{ importJson(JSON.parse($("#modal-text").value)); modal.classList.remove("show"); }
  catch(e){ $("#modal-msg").textContent = "JSON 파싱 실패: " + e.message; }
};
$("#modal-copy").onclick = async () => {
  try{ await navigator.clipboard.writeText($("#modal-text").value); $("#modal-msg").textContent = "복사됨"; }
  catch(e){ $("#modal-text").select(); document.execCommand("copy"); $("#modal-msg").textContent = "복사됨"; }
};
$("#modal-download").onclick = () => {
  const blob = new Blob([$("#modal-text").value], {type:"application/json"});
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob); a.download = "GameData.json"; a.click();
  URL.revokeObjectURL(a.href);
};
$("#modal-close").onclick = () => modal.classList.remove("show");

// ───────────────────────── 초기 데이터 ─────────────────────────

window.GameData = {
  seed: {"items": [{"id": "Item:ShipDebris", "displayName": "우주선 잔해", "description": "추락하며 흩어진 파편. 게이트 ⓪ 납품 및 초반 수동 제작 재료.", "type": "Salvage", "line": "None", "icon": ""}, {"id": "Item:IronOre", "displayName": "철광석", "description": "구조재의 원광. Ring 1 광맥에서 채굴.", "type": "Ore", "line": "Iron", "icon": ""}, {"id": "Item:IronIngot", "displayName": "철 주괴", "description": "철광석을 제련한 기본 소재.", "type": "Ingot", "line": "Iron", "icon": ""}, {"id": "Item:IronPlate", "displayName": "철판", "description": "선체 봉합과 회로 함체에 쓰이는 판재.", "type": "Part", "line": "Iron", "icon": ""}, {"id": "Item:IronGear", "displayName": "철 기어", "description": "체결용 부품. 선체 패널 조립에 사용.", "type": "Part", "line": "Iron", "icon": ""}, {"id": "Item:BasicAmmo", "displayName": "기본 탄약", "description": "권총·라이플·기본 포탑 공용 탄약.", "type": "Ammo", "line": "Iron", "icon": "", "attackEffects": [{"effect": "Effect:Damage", "value": 10}], "speed": 70, "gravity": 0, "explosionRadius": 0, "lifetime": 3}, {"id": "Item:CopperOre", "displayName": "구리광석", "description": "전자 계통의 원광. Ring 1 외곽~Ring 2.", "type": "Ore", "line": "Copper", "icon": ""}, {"id": "Item:CopperIngot", "displayName": "구리 주괴", "description": "구리광석을 제련한 소재.", "type": "Ingot", "line": "Copper", "icon": ""}, {"id": "Item:CopperWire", "displayName": "구리 전선", "description": "배선과 회로의 기초 부품.", "type": "Part", "line": "Copper", "icon": ""}, {"id": "Item:CrystalOre", "displayName": "크리스탈 광석", "description": "동력 계통의 원광. Ring 2~3 위험 지역.", "type": "Ore", "line": "Crystal", "icon": ""}, {"id": "Item:RefinedCrystal", "displayName": "정제 크리스탈", "description": "에너지를 담는 매질.", "type": "Ingot", "line": "Crystal", "icon": ""}, {"id": "Item:HullPanel", "displayName": "선체 패널", "description": "게이트 ① 납품 부품. 동체를 물리적으로 봉합한다.", "type": "RepairPart", "line": "Iron", "icon": ""}, {"id": "Item:CircuitBoard", "displayName": "회로 기판", "description": "배전반·제어 유닛의 기초 회로.", "type": "Part", "line": "Copper", "icon": ""}, {"id": "Item:Grenade", "displayName": "유탄", "description": "유탄 발사기와 박격포 타워가 공유하는 폭발물.", "type": "Ammo", "line": "Iron", "icon": "", "attackEffects": [{"effect": "Effect:Damage", "value": 70}, {"effect": "Effect:Knockback", "value": 2}], "speed": 25, "gravity": 9.8, "explosionRadius": 3, "lifetime": 6}, {"id": "Item:DenseAmmo", "displayName": "고밀도 탄약", "description": "구리 코팅 강화 탄약. 상위 총기·중기관 포탑용.", "type": "Ammo", "line": "Iron", "icon": "", "attackEffects": [{"effect": "Effect:Damage", "value": 24}], "speed": 100, "gravity": 0, "explosionRadius": 0, "lifetime": 3}, {"id": "Item:EnergyCell", "displayName": "에너지 셀", "description": "에너지 그릇. 동력 모듈과 T3 방어 시설이 소비.", "type": "Part", "line": "Crystal", "icon": ""}, {"id": "Item:EnergyCellAmmo", "displayName": "에너지 셀 탄", "description": "에너지 소총·레이저 타워용 탄.", "type": "Ammo", "line": "Crystal", "icon": "", "attackEffects": [{"effect": "Effect:Damage", "value": 30}, {"effect": "Effect:SlowField", "value": 0.5}], "speed": 60, "gravity": 0, "explosionRadius": 0, "lifetime": 3, "pierce": 4}, {"id": "Item:NavControlUnit", "displayName": "항법 제어 유닛", "description": "게이트 ② 납품 부품. 패널로 차폐된 함체에 장착된 제어 모듈.", "type": "RepairPart", "line": "Copper", "icon": ""}, {"id": "Item:BeastCore", "displayName": "괴수 핵", "description": "코어와 동일 계열의 응축 에너지. 낮 둥지 레이드로 획득, 게이트 납품 전용.", "type": "Salvage", "line": "Beast", "icon": ""}, {"id": "Item:ControlModule", "displayName": "제어 모듈", "description": "연산·배선·전원을 한 덩어리로 묶은 복합 부품. 제조기에서만 만들 수 있고, 티어 3 설비의 뼈대가 된다.", "type": "Part", "line": "Copper", "icon": ""}, {"id": "Item:PowerModule", "displayName": "동력 모듈", "description": "게이트 ③ 납품 부품. 엔진에 꽂는 완성형 연료·제어 통합 모듈.", "type": "RepairPart", "line": "Crystal", "icon": ""}, {"id": "Item:CrystalAmmo", "displayName": "크리스탈 탄약", "description": "크리스탈 첨두 관통탄. T3 물리 계열 최상위 탄약 — 저격소총·중기관 포탑 상위 호환.", "type": "Ammo", "line": "Crystal", "icon": "", "attackEffects": [{"effect": "Effect:Damage", "value": 30}, {"effect": "Effect:Burn", "value": 4}], "speed": 90, "gravity": 0, "explosionRadius": 0, "lifetime": 3}, {"id": "Item:Pistol", "displayName": "기본 권총", "description": "잔해로 만든 호신용 권총.", "type": "Weapon", "line": "Iron", "icon": "", "gun": "Gun:Pistol"}, {"id": "Item:Rifle", "displayName": "라이플", "description": "연사가 가능한 주력 화기.", "type": "Weapon", "line": "Iron", "icon": "", "gun": "Gun:Rifle"}, {"id": "Item:Shotgun", "displayName": "샷건", "description": "근접에서 한 번에 쓸어낸다.", "type": "Weapon", "line": "Iron", "icon": "", "gun": "Gun:Shotgun"}, {"id": "Item:Sniper", "displayName": "스나이퍼", "description": "먼 거리의 단일 대상을 정확히 노린다.", "type": "Weapon", "line": "Copper", "icon": "", "gun": "Gun:Sniper"}, {"id": "Item:GrenadeLauncher", "displayName": "유탄 발사기", "description": "유탄을 곡사로 날린다.", "type": "Weapon", "line": "Iron", "icon": "", "gun": "Gun:GrenadeLauncher"}, {"id": "Item:EnergyRifle", "displayName": "에너지 소총", "description": "일직선의 적을 관통한다.", "type": "Weapon", "line": "Crystal", "icon": "", "gun": "Gun:EnergyRifle"}], "recipes": [{"id": "Recipe:Recipe_IronIngot", "displayName": "철 주괴 제련", "description": "제련로 1입력.", "tier": 1, "craftTime": 2, "inputs": [{"item": "Item:IronOre", "amount": 2}], "outputs": [{"item": "Item:IronIngot", "amount": 1}]}, {"id": "Recipe:Recipe_IronPlate", "displayName": "철판 제작", "description": "제작기 1입력.", "tier": 1, "craftTime": 3, "inputs": [{"item": "Item:IronIngot", "amount": 2}], "outputs": [{"item": "Item:IronPlate", "amount": 1}]}, {"id": "Recipe:Recipe_IronGear", "displayName": "철 기어 제작", "description": "제작기 1입력.", "tier": 1, "craftTime": 3, "inputs": [{"item": "Item:IronIngot", "amount": 2}], "outputs": [{"item": "Item:IronGear", "amount": 1}]}, {"id": "Recipe:Recipe_BasicAmmo", "displayName": "기본 탄약 제작", "description": "제작기 1입력. 총기·기본 포탑 공용.", "tier": 1, "craftTime": 2, "inputs": [{"item": "Item:IronIngot", "amount": 1}], "outputs": [{"item": "Item:BasicAmmo", "amount": 4}]}, {"id": "Recipe:Recipe_HullPanel", "displayName": "선체 패널 조립", "description": "게이트 ① 납품 부품. 티어 1에서는 작업대 수동 조립.", "tier": 1, "craftTime": 4, "inputs": [{"item": "Item:IronPlate", "amount": 2}, {"item": "Item:IronGear", "amount": 1}], "outputs": [{"item": "Item:HullPanel", "amount": 1}]}, {"id": "Recipe:Recipe_CopperIngot", "displayName": "구리 주괴 제련", "description": "제련로 1입력. 구리 라인 개방.", "tier": 2, "craftTime": 2, "inputs": [{"item": "Item:CopperOre", "amount": 2}], "outputs": [{"item": "Item:CopperIngot", "amount": 1}]}, {"id": "Recipe:Recipe_CopperWire", "displayName": "구리 전선 제작", "description": "제작기 1입력.", "tier": 2, "craftTime": 2, "inputs": [{"item": "Item:CopperIngot", "amount": 1}], "outputs": [{"item": "Item:CopperWire", "amount": 2}]}, {"id": "Recipe:Recipe_CircuitBoard", "displayName": "회로 기판 조립", "description": "조립기 2입력.", "tier": 2, "craftTime": 4, "inputs": [{"item": "Item:CopperWire", "amount": 3}, {"item": "Item:IronPlate", "amount": 1}], "outputs": [{"item": "Item:CircuitBoard", "amount": 1}]}, {"id": "Recipe:Recipe_Grenade", "displayName": "유탄 조립", "description": "조립기 2입력. 유탄 발사기·박격포 타워 공용.", "tier": 2, "craftTime": 3, "inputs": [{"item": "Item:BasicAmmo", "amount": 2}, {"item": "Item:IronPlate", "amount": 1}], "outputs": [{"item": "Item:Grenade", "amount": 1}]}, {"id": "Recipe:Recipe_NavControlUnit", "displayName": "항법 제어 유닛 조립", "description": "게이트 ② 납품 부품. 조립기 2입력 — 게이트① 부품(선체 패널) 재소비.", "tier": 2, "craftTime": 6, "inputs": [{"item": "Item:CircuitBoard", "amount": 1}, {"item": "Item:HullPanel", "amount": 1}], "outputs": [{"item": "Item:NavControlUnit", "amount": 1}]}, {"id": "Recipe:Recipe_RefinedCrystal", "displayName": "크리스탈 정제", "description": "제련로 1입력. Ring 2~3 원정 자원.", "tier": 3, "craftTime": 3, "inputs": [{"item": "Item:CrystalOre", "amount": 2}], "outputs": [{"item": "Item:RefinedCrystal", "amount": 1}]}, {"id": "Recipe:Recipe_DenseAmmo", "displayName": "고밀도 탄약 조립", "description": "조립기 2입력. 구리 코팅 강화 탄약.", "tier": 2, "craftTime": 3, "inputs": [{"item": "Item:BasicAmmo", "amount": 2}, {"item": "Item:CopperIngot", "amount": 1}], "outputs": [{"item": "Item:DenseAmmo", "amount": 2}]}, {"id": "Recipe:Recipe_EnergyCell", "displayName": "에너지 셀 조립", "description": "조립기 2입력.", "tier": 3, "craftTime": 4, "inputs": [{"item": "Item:RefinedCrystal", "amount": 1}, {"item": "Item:CopperWire", "amount": 2}], "outputs": [{"item": "Item:EnergyCell", "amount": 1}]}, {"id": "Recipe:Recipe_EnergyCellAmmo", "displayName": "에너지 셀 탄 제작", "description": "제작기 1입력.", "tier": 3, "craftTime": 2, "inputs": [{"item": "Item:EnergyCell", "amount": 1}], "outputs": [{"item": "Item:EnergyCellAmmo", "amount": 2}]}, {"id": "Recipe:Recipe_PowerModule", "displayName": "동력 모듈 제조", "description": "게이트 ③ 납품 부품. 제조기 4입력 — 괴수 핵이 공장 라인에 합류하는 유일한 지점.", "tier": 3, "craftTime": 10, "inputs": [{"item": "Item:NavControlUnit", "amount": 1}, {"item": "Item:HullPanel", "amount": 1}, {"item": "Item:EnergyCell", "amount": 2}, {"item": "Item:BeastCore", "amount": 1}], "outputs": [{"item": "Item:PowerModule", "amount": 1}]}, {"id": "Recipe:Recipe_CrystalAmmo", "displayName": "크리스탈 탄약 조립", "description": "조립기 2입력. 고밀도 탄약에 크리스탈 첨두를 결합.", "tier": 3, "craftTime": 3, "inputs": [{"item": "Item:DenseAmmo", "amount": 2}, {"item": "Item:RefinedCrystal", "amount": 1}], "outputs": [{"item": "Item:CrystalAmmo", "amount": 2}]}, {"id": "Recipe:Recipe_Pistol", "displayName": "기본 권총 제작", "description": "잔해를 깎아 만든 호신용 권총.", "tier": 1, "craftTime": 6, "inputs": [{"item": "Item:ShipDebris", "amount": 4}, {"item": "Item:IronPlate", "amount": 3}], "outputs": [{"item": "Item:Pistol", "amount": 1}]}, {"id": "Recipe:Recipe_Rifle", "displayName": "라이플 제작", "description": "연사가 가능한 주력 화기.", "tier": 2, "craftTime": 10, "inputs": [{"item": "Item:IronPlate", "amount": 10}, {"item": "Item:IronGear", "amount": 8}], "outputs": [{"item": "Item:Rifle", "amount": 1}]}, {"id": "Recipe:Recipe_Shotgun", "displayName": "샷건 제작", "description": "근접에서 한 번에 쓸어낸다.", "tier": 2, "craftTime": 10, "inputs": [{"item": "Item:IronPlate", "amount": 10}, {"item": "Item:IronGear", "amount": 4}], "outputs": [{"item": "Item:Shotgun", "amount": 1}]}, {"id": "Recipe:Recipe_Sniper", "displayName": "스나이퍼 제작", "description": "먼 거리의 단일 대상을 정확히 노린다.", "tier": 2, "craftTime": 12, "inputs": [{"item": "Item:CircuitBoard", "amount": 4}, {"item": "Item:IronPlate", "amount": 8}], "outputs": [{"item": "Item:Sniper", "amount": 1}]}, {"id": "Recipe:Recipe_GrenadeLauncher", "displayName": "유탄 발사기 제작", "description": "유탄을 곡사로 날린다.", "tier": 2, "craftTime": 12, "inputs": [{"item": "Item:IronPlate", "amount": 12}, {"item": "Item:IronGear", "amount": 8}], "outputs": [{"item": "Item:GrenadeLauncher", "amount": 1}]}, {"id": "Recipe:Recipe_EnergyRifle", "displayName": "에너지 소총 제작", "description": "제어 모듈에 크리스탈 광학계를 얹는다. 제조기 전용.", "tier": 3, "craftTime": 20.0, "inputs": [{"item": "Item:ControlModule", "amount": 2}, {"item": "Item:RefinedCrystal", "amount": 6}, {"item": "Item:CopperWire", "amount": 12}, {"item": "Item:IronPlate", "amount": 8}], "outputs": [{"item": "Item:EnergyRifle", "amount": 1}]}, {"id": "Recipe:Recipe_ControlModule", "displayName": "제어 모듈 제조", "description": "기판·셀·배선·판을 한 번에 압착한다. 제조기 전용.", "tier": 3, "craftTime": 8.0, "inputs": [{"item": "Item:CircuitBoard", "amount": 2}, {"item": "Item:EnergyCell", "amount": 1}, {"item": "Item:CopperWire", "amount": 4}, {"item": "Item:IronPlate", "amount": 3}], "outputs": [{"item": "Item:ControlModule", "amount": 1}]}]},
  getItems:   () => exportJson().items,
  getRecipes: () => exportJson().recipes,
  loadGraph:  obj => { importJson(obj); pushHistory(); },
  onGraphChange: null,
};
importJson(window.GameData.seed);
// 첫 프레임에는 노드 DOM 이 아직 측정되지 않는다 — 레이아웃이 잡힌 뒤 선을 다시 그린다
requestAnimationFrame(() => requestAnimationFrame(() => render()));

// render() 는 마우스업·호버마다 호출된다. 그때마다 건물 탭을 다시 그리면
// 열려 있던 드롭다운이 닫히므로, 아이템 목록이 실제로 바뀐 경우에만 알린다.
const _renderOrig = render;
pushHistory();
const _itemSigOf = () => {
  const d = exportJson();
  return JSON.stringify([d.items.map(i => [i.id, i.displayName, i.line, i.type]),
                         d.recipes.map(r => [r.id, r.displayName, r.inputs.length])]);
};
let _itemSig = _itemSigOf();   // 초기값을 채워 첫 호출이 헛되이 알리지 않게 한다
render = function(){
  _renderOrig();
  const sig = _itemSigOf();
  if(sig !== _itemSig){
    _itemSig = sig;
    if(window.GameData.onGraphChange) window.GameData.onGraphChange();
    const p = document.getElementById("pane-combat");
    if(p && p.classList.contains("on") && !document.activeElement?.closest("#c-detail"))
      window.CombatData?.refresh();
  }
};

