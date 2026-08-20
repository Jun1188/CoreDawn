// ══════════════════════════════════════════════════════════════
//  전투 탭 — Effect · Gun · 아이템 모듈(Ammo/Weapon)
//  Feature_Damage 브랜치의 GameDataImporter DTO 와 1:1
// ══════════════════════════════════════════════════════════════
(function(){
const { esc, $, $$, field, bareId, fullId, autogrow } = window.EdUtil;
const { EFFECT_KINDS, FIRE_MODES, STACKING } = window.EdEnum;


// EffectKindMap (GameDataImporter.cs) 와 동일
const kindOf = v => EFFECT_KINDS.find(k => k.v === v) || EFFECT_KINDS[0];
let effects = [], guns = [], waves = [];

// 실행 취소 — 효과·화기·웨이브를 통째로 스냅샷한다
let hist = null;
const pushHistoryC = () => hist && hist.push();
let curTab = "effects", curE = 0, curG = 0, curW = 0;

// ── 공유 스토어 등록 ──────────────────────────────────────────
window.CombatData = {
  getEffects: () => effects.map(e => {
    const k = kindOf(e.kind);
    const o = { id:e.id, displayName:e.displayName, kind:e.kind };
    if(e.description) o.description = e.description;
    if(k.dur){ if(e.duration > 0) o.duration = e.duration; if(e.stacking) o.stacking = e.stacking; }
    if(k.tick && e.tickInterval > 0) o.tickInterval = e.tickInterval;
    if(k.affects && (e.affects||[]).length) o.affects = e.affects;
    return o;
  }),
  getGuns: () => guns.map(g => {
    const o = { id:g.id, displayName:g.displayName, isAutomatic:!!g.isAutomatic, fireMode:g.fireMode };
    if(g.description) o.description = g.description;
    for(const f of ["fireRate","range","reloadTime","zoomFOV",
                    "xRecoil","yRecoil","zRecoil","visualKickbackZ",
                    "baseSpread","maxSpread","spreadIncreasePerShot","spreadRecoveryRate"])
      if(g[f] > 0) o[f] = g[f];
    if(g.magSize > 0) o.magSize = g.magSize;
    if(g.pellets > 1) o.pellets = g.pellets;      // 1 은 기본값 — 샷건만 8
    if(g.ammo) o.ammo = g.ammo;
    if((g.ammoFilter||[]).length) o.ammoFilter = g.ammoFilter;
    if(g.damageMultiplier >= 0) o.damageMultiplier = g.damageMultiplier;
    return o;
  }),
  getWaves: () => waves.map(w => ({
    id:w.id, displayName:w.displayName, description:w.description||"",
    day:w.day, requiredCoreTier:w.requiredCoreTier,
    baseAmount:w.baseAmount, maxAliveAmount:w.maxAliveAmount, spawnInterval:w.spawnInterval,
    monsterMaxHp:w.monsterMaxHp||0,   // 0 = wave_settings.json → 프리팹 폴백 (임포터가 무조건 덮으므로 항상 내보낸다)
  })),
  load: obj => {
    if(Array.isArray(obj.waves)) waves = obj.waves.map(w => ({
      id:w.id||"", displayName:w.displayName||"", description:w.description||"",
      day:w.day??1, requiredCoreTier:w.requiredCoreTier??0,
      baseAmount:w.baseAmount??4, maxAliveAmount:w.maxAliveAmount??4,
      spawnInterval:w.spawnInterval??2, monsterMaxHp:w.monsterMaxHp??0 })).sort((a,b)=>a.day-b.day);
    if(Array.isArray(obj.effects)) effects = obj.effects.map(e => ({
      id:e.id||"", displayName:e.displayName||"", description:e.description||"",
      kind:e.kind||"Damage", duration:e.duration??0, stacking:e.stacking||"Refresh",
      tickInterval:e.tickInterval??0, affects:e.affects||[] }));
    if(Array.isArray(obj.guns)) guns = obj.guns.map(g => ({
      id:g.id||"", displayName:g.displayName||"", description:g.description||"",
      isAutomatic:!!g.isAutomatic, fireMode:g.fireMode||"Projectile",
      fireRate:g.fireRate??0.2, range:g.range??0, pellets:g.pellets??1,
      magSize:g.magSize??30, reloadTime:g.reloadTime??1.5, zoomFOV:g.zoomFOV??50,
      ammo:g.ammo||"", ammoFilter:g.ammoFilter||(g.ammo?[g.ammo]:[]),
      damageMultiplier:g.damageMultiplier??1,
      xRecoil:g.xRecoil??3, yRecoil:g.yRecoil??2, zRecoil:g.zRecoil??1,
      visualKickbackZ:g.visualKickbackZ??1,
      baseSpread:g.baseSpread??0.5, maxSpread:g.maxSpread??5,
      spreadIncreasePerShot:g.spreadIncreasePerShot??1, spreadRecoveryRate:g.spreadRecoveryRate??5 }));
    curE = 0; curG = 0; curW = 0; render();
  },
  refresh: () => render(),
};

hist = window.EdHistory(
  () => ({ effects: window.CombatData.getEffects(), guns: window.CombatData.getGuns(),
           waves: window.CombatData.getWaves() }),
  o => {
    // 보던 탭과 선택은 유지한다 — 되돌리기가 화면을 옮기면 어디를 고쳤는지 놓친다
    const t = curTab, e = curE, g = curG, wv = curW;
    window.CombatData.load({ effects:o.effects, guns:o.guns, waves:o.waves });
    curTab = t;
    curE = Math.min(e, Math.max(0, effects.length-1));
    curG = Math.min(g, Math.max(0, guns.length-1));
    curW = Math.min(wv, Math.max(0, waves.length-1));
    $$("#c-subtabs button").forEach(b => b.classList.toggle("on", b.dataset.sub === curTab));
    render();
  });

// ── 아이템 목록 (그래프 탭에서 가져온다) ──────────────────────
const items      = () => { try { return window.GameData?.getItems() || []; } catch { return []; } };
const ammoItems  = () => items().filter(i => i.type === "Ammo");
const effectById = id => effects.find(e => e.id === id);

// ── 렌더 ──────────────────────────────────────────────────────
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
  renderList(); renderDetail(); renderWarn();
  const st = $("#c-stat");
  if(st) st.textContent = `효과 ${effects.length} · 화기 ${guns.length} · 웨이브 ${waves.length}`;
}

function renderList(){
  const arr = curTab === "effects" ? effects : curTab === "guns" ? guns : waves;
  const cur = curTab === "effects" ? curE : curTab === "guns" ? curG : curW;
  const sub = x => curTab === "effects" ? x.kind : curTab === "guns" ? x.fireMode : `Day ${x.day}`;
  $("#c-list").innerHTML = arr.map((x, i) => `
    <div class="bitem ${i===cur?"sel":""}" data-i="${i}">
      <span class="nm">${esc(x.displayName || "(이름 없음)")}</span>
      <span class="kd">${esc(sub(x))}</span>
    </div>`).join("") || `<div class="costempty">항목이 없습니다</div>`;
  $$("#c-list .bitem").forEach(el => el.onclick = () => {
    const i = +el.dataset.i;
    if(curTab === "effects") curE = i; else if(curTab === "guns") curG = i; else curW = i;
    render();
  });
}


function renderDetail(){
  const box = $("#c-detail");
  if(curTab === "effects"){
    const e = effects[curE];
    if(!e){ box.innerHTML = ""; return; }
    const k = kindOf(e.kind);
    box.innerHTML =
      field("Kind", `<select id="c-kind">${EFFECT_KINDS.map(x=>
        `<option value="${x.v}" ${x.v===e.kind?"selected":""}>${x.v} — ${x.ko}</option>`).join("")}</select>`) +
      `<div class="field"><label></label><div style="font-size:11px;color:var(--faint);line-height:1.45">${k.desc}</div></div>` +
      field("Id", `<div class="idrow"><span class="pfx mono">Effect:</span>
        <input id="c-id" class="mono" value="${esc((e.id||"").replace(/^Effect:/,""))}" placeholder="Damage"></div>`) +
      field("Display Name", `<input id="c-name" value="${esc(e.displayName)}">`) +
      field("Description", `<textarea id="c-desc" class="autogrow" rows="1">${esc(e.description)}</textarea>`, "top") +
      (k.dur ? field("Duration (초)", `<input id="c-dur" type="number" min="0" step="0.5" value="${e.duration}">`) +
               field("Stacking", `<select id="c-stack">${STACKING.map(x=>
                 `<option ${x===e.stacking?"selected":""}>${x}</option>`).join("")}</select>`) : "") +
      (k.tick ? field("Tick (초)", `<input id="c-tick" type="number" min="0" step="0.1" value="${e.tickInterval}">`) : "") +
      (k.affects ? affectsSection(e) : "");

    $("#c-kind").onchange = ev => { e.kind = ev.target.value; render(); };
    bindText("c-id", v => { e.id = v ? "Effect:" + v.replace(/[^A-Za-z0-9_가-힣]/g,"") : ""; }, true);
    bindText("c-name", v => { e.displayName = v; renderList(); });
    bindText("c-desc", v => { e.description = v; });
    bindNum("c-dur",  v => e.duration = v);
    bindNum("c-tick", v => e.tickInterval = v);
    const st = $("#c-stack"); if(st) st.onchange = ev => { e.stacking = ev.target.value; };
    $$("[data-aff]").forEach(el => el.onchange = ev => {
      const id = el.dataset.aff;
      e.affects = e.affects || [];
      if(ev.target.checked){ if(!e.affects.includes(id)) e.affects.push(id); }
      else e.affects = e.affects.filter(x => x !== id);
      renderWarn();
    });
  } else if(curTab === "waves"){
    const w = waves[curW];
    if(!w){ box.innerHTML = ""; return; }
    const dur = w.spawnInterval > 0 ? (w.baseAmount * w.spawnInterval) : 0;
    box.innerHTML =
      field("Id", `<div class="idrow"><span class="pfx mono">Wave:</span>
        <input id="c-id" class="mono" value="${esc((w.id||"").replace(/^Wave:/,""))}" placeholder="Day1"></div>`) +
      field("Display Name", `<input id="c-name" value="${esc(w.displayName)}">`) +
      field("Description", `<textarea id="c-desc" class="autogrow" rows="1">${esc(w.description)}</textarea>`, "top") +
      field("Day", `<input id="w-day" type="number" min="1" step="1" value="${w.day}" title="이 웨이브가 오는 일차">`) +
      field("Core Tier", `<input id="w-tier" type="number" min="0" step="1" value="${w.requiredCoreTier}" title="코어 티어가 낮으면 이전 웨이브가 반복된다">`) +
      `<div class="field wide"><label>규모</label><div class="gungrid">
        ${numCell("총 마리수", "w-base", w.baseAmount, 1, "둥지 파괴 전 기준 총량")}
        ${numCell("동시 상한", "w-alive", w.maxAliveAmount, 1, "한 번에 살아 있을 수 있는 수")}
        ${numCell("스폰 간격", "w-int", w.spawnInterval, .1, "초")}
        ${numCell("몬스터 HP", "w-hp", w.monsterMaxHp, 1, "0 = wave_settings.json → 프리팹 기본값 폴백")}
      </div></div>` +
      `<div class="dpsbox">
        <span>최소 소요 <b>${dur.toFixed(0)}s</b></span>
        <span>동시 압력 <b>${w.maxAliveAmount}</b></span>
        <span>웨이브 <b>${w.baseAmount}</b>마리</span>
      </div>` +
      (w.maxAliveAmount > w.baseAmount
        ? `<div class="costempty" style="margin-top:8px">동시 상한이 총량보다 큽니다 — 상한이 의미 없습니다</div>` : "");

    bindText("c-id", v => { w.id = v ? "Wave:" + v.replace(/[^A-Za-z0-9_가-힣]/g,"") : ""; }, true);
    bindText("c-name", v => { w.displayName = v; renderList(); });
    bindText("c-desc", v => { w.description = v; });
    for(const [id, key] of Object.entries({ "w-day":"day","w-tier":"requiredCoreTier",
        "w-base":"baseAmount","w-alive":"maxAliveAmount","w-int":"spawnInterval","w-hp":"monsterMaxHp" }))
      bindNum(id, v => { w[key] = v; renderDetail(); renderList(); });
  } else {
    const g = guns[curG];
    if(!g){ box.innerHTML = ""; return; }
    box.innerHTML =
      field("Id", `<div class="idrow"><span class="pfx mono">Gun:</span>
        <input id="c-id" class="mono" value="${esc((g.id||"").replace(/^Gun:/,""))}" placeholder="Rifle"></div>`) +
      field("Display Name", `<input id="c-name" value="${esc(g.displayName)}">`) +
      field("Description", `<textarea id="c-desc" class="autogrow" rows="1">${esc(g.description)}</textarea>`, "top") +
      field("Fire Mode", `<select id="g-mode">${FIRE_MODES.map(m=>
        `<option ${m===g.fireMode?"selected":""}>${m}</option>`).join("")}</select>`) +
      field("Automatic", `<input type="checkbox" id="g-auto" ${g.isAutomatic?"checked":""}>`) +
      field("Damage ×", `<input id="g-dmgx" type="number" min="0" step="0.1" value="${g.damageMultiplier}" title="탄약의 피해형 항목에 곱해진다">`) +
      gunAmmoFilter(g) +
      `<div class="field wide"><label>사격</label><div class="gungrid">
        ${numCell("Fire Rate", "g-rate", g.fireRate, .01, "발 간격(초). 작을수록 빠르다")}
        ${numCell("Mag Size", "g-mag", g.magSize, 1)}
        ${numCell("Reload", "g-reload", g.reloadTime, .1)}
        ${numCell("Range", "g-range", g.range, 1)}
        ${numCell("Pellets", "g-pellets", g.pellets, 1, "방아쇠당 발사 수. 샷건 8, 나머지 1")}
        ${numCell("Zoom FOV", "g-fov", g.zoomFOV, 1)}
      </div></div>` +
      `<div class="field wide"><label>반동</label><div class="gungrid">
        ${numCell("X", "g-rx", g.xRecoil, .01)}
        ${numCell("Y", "g-ry", g.yRecoil, .01)}
        ${numCell("Z", "g-rz", g.zRecoil, .01)}
        ${numCell("Kickback Z", "g-kick", g.visualKickbackZ, .01)}
      </div></div>` +
      `<div class="field wide"><label>탄퍼짐</label><div class="gungrid">
        ${numCell("Base", "g-sbase", g.baseSpread, .1)}
        ${numCell("Max", "g-smax", g.maxSpread, .1)}
        ${numCell("+/발", "g-sinc", g.spreadIncreasePerShot, .1)}
        ${numCell("회복", "g-srec", g.spreadRecoveryRate, .1)}
      </div></div>` +
      dpsBox(g);

    bindText("c-id", v => { g.id = v ? "Gun:" + v.replace(/[^A-Za-z0-9_가-힣]/g,"") : ""; }, true);
    bindText("c-name", v => { g.displayName = v; renderList(); });
    bindText("c-desc", v => { g.description = v; });
    $("#g-mode").onchange = ev => { g.fireMode = ev.target.value; refreshList(); };
    $("#g-auto").onchange = ev => { g.isAutomatic = ev.target.checked; };
    $$("[data-gammo]").forEach(el => el.onchange = e => {
      const id = el.dataset.gammo;
      g.ammoFilter = g.ammoFilter || [];
      if(e.target.checked){
        if(!g.ammoFilter.includes(id)) g.ammoFilter.push(id);
        if(!g.ammo) g.ammo = id;                    // 첫 탄종이 기본이 된다
      } else {
        g.ammoFilter = g.ammoFilter.filter(x => x !== id);
        if(g.ammo === id) g.ammo = g.ammoFilter[0] || "";   // 기본을 빼면 남은 것 중 하나가 기본
      }
      renderDetail(); renderWarn(); pushHistoryC();
    });
    $$("[data-gdef]").forEach(el => el.onclick = ev => {
      ev.preventDefault(); ev.stopPropagation();
      g.ammo = el.dataset.gdef;
      renderDetail(); renderWarn(); pushHistoryC();
    });
    const nums = { "g-dmgx":"damageMultiplier","g-rate":"fireRate","g-mag":"magSize","g-reload":"reloadTime",
      "g-range":"range","g-pellets":"pellets","g-fov":"zoomFOV","g-rx":"xRecoil","g-ry":"yRecoil",
      "g-rz":"zRecoil","g-kick":"visualKickbackZ","g-sbase":"baseSpread","g-smax":"maxSpread",
      "g-sinc":"spreadIncreasePerShot","g-srec":"spreadRecoveryRate" };
    for(const [id, key] of Object.entries(nums))
      bindNum(id, v => { g[key] = v; if(id==="g-rate"||id==="g-dmgx") renderDetail(); });
  }
  autogrow(box);
  // 입력이 확정될 때 한 번만 기록한다 — 타이핑마다 쌓으면 되돌리기가 쓸모없어진다
  box.querySelectorAll("input,select,textarea").forEach(el =>
    el.addEventListener("change", pushHistoryC));
}

function numCell(label, id, val, step, title){
  return `<label class="gcell" ${title?`title="${esc(title)}"`:""}>
    <span>${label}</span><input id="${id}" type="number" step="${step}" value="${val}"></label>`;
}
function bindText(id, fn, rerenderList){
  const el = $("#"+id); if(!el) return;
  el.oninput = e => { fn(e.target.value); if(rerenderList) refreshList(); };
}
function bindNum(id, fn){
  const el = $("#"+id); if(!el) return;
  el.oninput = e => { fn(+e.target.value || 0); renderWarn(); };
}

// AttackModifier 가 증폭할 효과 선택
function affectsSection(e){
  const sel = new Set(e.affects || []);
  const rows = effects.filter(x => x.id !== e.id).map(x => `
    <label class="ammorow ${sel.has(x.id)?"on":""}">
      <input type="checkbox" data-aff="${esc(x.id)}" ${sel.has(x.id)?"checked":""}>
      <span class="bar" style="background:${x.kind==="Damage"?"#FF5D73":"#4FD8E0"}"></span>
      <span class="nm">${esc(x.id)}</span><span class="ty">${esc(x.kind)}</span>
    </label>`).join("");
  return `<div class="field wide ammoblock"><label>Affects <span style="color:var(--faint)">· 증폭할 효과</span></label>
    <div><div class="ammolist">${rows || `<div class="costempty">다른 효과가 없습니다</div>`}</div></div></div>`;
}

// 장전 가능한 탄종 — 게임에서 V 로 돌려 쓴다. 기본 탄약(ammo)은 항상 포함된다.
function gunAmmoFilter(g){
  const sel = new Set(g.ammoFilter || []);
  const rows = ammoItems().map(i => {
    const on = sel.has(i.id), isDefault = i.id === g.ammo;
    return `<label class="ammorow ${on?"on":""}">
      <input type="checkbox" data-gammo="${esc(i.id)}" ${on?"checked":""}>
      <span class="bar" style="background:${isDefault?"#4FD8E0":(on?"#2E4266":"#1A2740")}"></span>
      <span class="nm">${esc(i.id)}</span>
      ${on ? (isDefault
        ? `<span class="ty" style="color:#4FD8E0">기본</span>`
        : `<span class="ty setdef" data-gdef="${esc(i.id)}" title="기본 탄약으로 지정">기본으로</span>`)
        : `<span class="ty"></span>`}
    </label>`;
  }).join("");
  return `<div class="field wide ammoblock">
    <label>탄종 <span style="color:var(--faint)">· 장전해 돌려 쓸 수 있는 탄약 (게임에서 V 로 전환)</span></label>
    <div><div class="ammolist">${rows || `<div class="costempty">Ammo 타입 아이템이 없습니다</div>`}</div></div></div>`;
}

// 총의 실제 DPS — 탄약의 피해 합 × 배율 ÷ 발 간격
function dpsBox(g){
  const ammo = items().find(i => i.id === g.ammo);
  const dmg = ammo ? (ammo.attackEffects || []).reduce((s, x) => {
    const ef = effectById(x.effect);
    return s + (ef && ef.kind === "Damage" ? (x.value||0) : 0);
  }, 0) : 0;
  if(!ammo) return `<div class="dpsbox off">탄약을 지정하면 DPS 가 계산됩니다</div>`;
  if(dmg <= 0) return `<div class="dpsbox off">${esc(g.ammo)} 에 피해 효과가 없습니다</div>`;
  const pellets = Math.max(1, g.pellets || 1);
  const perShot = dmg * (g.damageMultiplier||0) * pellets;   // 샷건은 방아쇠당 여러 발
  const dps = g.fireRate > 0 ? perShot / g.fireRate : 0;
  // 탄창은 발 수 기준이라, 방아쇠를 몇 번 당길 수 있는지로 나눈다
  const trigger = Math.floor((g.magSize||0) / pellets);
  return `<div class="dpsbox">
    <span>1발 피해 <b>${perShot.toFixed(1)}</b>${pellets>1?` <span style="color:var(--faint)">(${pellets}발)</span>`:""}</span>
    <span>초당 피해 <b>${dps.toFixed(1)}</b></span>
    <span>탄창 총합 <b>${(perShot*trigger).toFixed(1)}</b></span>
  </div>`;
}

// ── 검증 ──────────────────────────────────────────────────────
function validate(){
  const out = [];
  const arr = curTab === "effects" ? effects : curTab === "guns" ? guns : waves;
  const x = curTab === "effects" ? effects[curE] : curTab === "guns" ? guns[curG] : waves[curW];
  if(!x) return out;
  const kind = curTab === "effects" ? "effect" : curTab === "guns" ? "gun" : "wave";
  out.push(...window.EdValid.identity(x, arr, kind));

  if(curTab === "effects"){
    const k = kindOf(x.kind);
    if(k.dur && !(x.duration > 0)) out.push("지속 효과인데 duration 이 0 입니다 — 즉시 사라집니다");
    if(k.tick && !(x.tickInterval > 0)) out.push("DamageOverTime 인데 tickInterval 이 0 입니다");
    if(k.affects && !(x.affects||[]).length) out.push("AttackModifier 인데 증폭할 효과가 없습니다");
    (x.affects||[]).forEach(id => { if(!effectById(id)) out.push(`Affects — "${id}" 를 찾을 수 없습니다`); });
    const used = guns.some(g => {
      const a = items().find(i => i.id === g.ammo);
      return (a?.attackEffects||[]).some(e => e.effect === x.id);
    }) || items().some(i => (i.attackEffects||[]).some(e => e.effect === x.id));
    if(!used && x.id) out.push("어떤 탄약도 이 효과를 쓰지 않습니다");
  } else if(curTab === "waves"){
    if(!(x.day > 0)) out.push("Day 는 1 이상이어야 합니다");
    if(x.requiredCoreTier < 0) out.push("Core Tier 는 0 이상이어야 합니다");
    if(!(x.baseAmount > 0)) out.push("총 마리수가 0 입니다 — 아무도 오지 않습니다");
    if(!(x.maxAliveAmount > 0)) out.push("동시 상한이 0 입니다 — 스폰이 막힙니다");
    if(!(x.spawnInterval > 0)) out.push("스폰 간격은 0보다 커야 합니다");
    if(waves.filter(w => w.day === x.day).length > 1) out.push(`Day ${x.day} 웨이브가 둘 이상입니다`);
    const prev = waves.filter(w => w.day < x.day).sort((a,b)=>b.day-a.day)[0];
    if(prev && x.baseAmount < prev.baseAmount)
      out.push(`Day ${prev.day}(${prev.baseAmount}마리) 보다 규모가 작습니다 — 난이도가 내려갑니다`);
    if(prev && x.requiredCoreTier < prev.requiredCoreTier)
      out.push(`Day ${prev.day} 보다 요구 코어 티어가 낮습니다`);
  } else {
    if(!(x.fireRate > 0)) out.push("Fire Rate 는 0보다 커야 합니다 (발 간격 초)");
    if(!x.ammo) out.push("탄약이 지정되지 않았습니다 — 사격해도 아무 일도 일어나지 않습니다");
    else {
      const a = items().find(i => i.id === x.ammo);
      if(!a) out.push(`탄약 "${x.ammo}" 를 아이템 목록에서 찾을 수 없습니다`);
      else if(a.type !== "Ammo") out.push(`"${x.ammo}" 는 Ammo 타입이 아닙니다`);
      else if(!(a.attackEffects||[]).length) out.push(`"${x.ammo}" 에 명중 효과가 없습니다`);
    }
    if(x.pellets < 1) out.push("Pellets 는 1 이상이어야 합니다");
    if(x.ammo && !(x.ammoFilter||[]).includes(x.ammo))
      out.push("기본 탄약이 탄종 목록에 없습니다 — 장전할 수 없습니다");
    (x.ammoFilter||[]).forEach(id => {
      const a = items().find(i => i.id === id);
      if(items().length && !a) out.push(`탄종 — "${id}" 를 찾을 수 없습니다`);
      else if(a && a.type !== "Ammo") out.push(`탄종 — "${id}" 는 Ammo 타입이 아닙니다`);
    });
    if(x.fireMode === "Projectile" && x.ammo){
      const a = items().find(i => i.id === x.ammo);
      if(a && !(a.speed > 0)) out.push(`Projectile 인데 탄약 "${x.ammo}" 의 speed 가 0 입니다`);
    }
    if(!(x.magSize > 0)) out.push("Mag Size 는 1 이상이어야 합니다");
    if(x.maxSpread < x.baseSpread) out.push("Max Spread 가 Base Spread 보다 작습니다");
    if(!(x.damageMultiplier > 0)) out.push("Damage × 가 0 입니다 — 피해를 주지 않습니다");
  }
  return out;
}
function renderWarn(){
  const w = validate();
  $("#c-warn").innerHTML = w.length
    ? "<h3>검증</h3>" + w.map(m=>`<div class="w">${esc(m)}</div>`).join("")
    : `<div class="okmsg">✓ 검증 통과</div>`;
}

// ── 툴바 ──────────────────────────────────────────────────────
function boot(){
  $$("#c-subtabs button").forEach(b => b.onclick = () => {
    curTab = b.dataset.sub;
    $$("#c-subtabs button").forEach(x => x.classList.toggle("on", x === b));
    render();
  });
  $("#c-add").onclick = () => {
    if(curTab === "effects"){
      effects.push({ id:"", displayName:"새 효과", description:"", kind:"Damage",
                     duration:3, stacking:"Refresh", tickInterval:0.5, affects:[] });
      curE = effects.length - 1;
    } else if(curTab === "waves"){
      const last = waves[waves.length-1];
      waves.push({ id:"", displayName:"새 웨이브", description:"",
        day: last ? last.day + 1 : 1, requiredCoreTier: last ? last.requiredCoreTier : 0,
        baseAmount: last ? last.baseAmount + 2 : 4,
        maxAliveAmount: last ? last.maxAliveAmount + 2 : 4,
        spawnInterval: last ? last.spawnInterval : 2,
        monsterMaxHp: last ? last.monsterMaxHp : 0 });
      curW = waves.length - 1;
    } else {
      guns.push({ id:"", displayName:"새 화기", description:"", isAutomatic:true, fireMode:"Projectile",
                  fireRate:0.2, range:99, pellets:1, magSize:30, reloadTime:1.5, zoomFOV:50,
                  ammo:"", ammoFilter:[], damageMultiplier:1, xRecoil:3, yRecoil:2, zRecoil:1, visualKickbackZ:1,
                  baseSpread:.5, maxSpread:5, spreadIncreasePerShot:1, spreadRecoveryRate:5 });
      curG = guns.length - 1;
    }
    render(); pushHistoryC();
  };
  $("#c-del").onclick = () => {
    if(curTab === "effects"){ effects.splice(curE,1); curE = Math.max(0, curE-1); }
    else if(curTab === "waves"){ waves.splice(curW,1); curW = Math.max(0, curW-1); }
    else { guns.splice(curG,1); curG = Math.max(0, curG-1); }
    render(); pushHistoryC();
  };
  window.addEventListener("keydown", ev => {
    if(!document.getElementById("pane-combat")?.classList.contains("on")) return;
    if(/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)) return;
    if(!(ev.ctrlKey || ev.metaKey)) return;
    const k = ev.key.toLowerCase();
    if(k === "z" && !ev.shiftKey){ ev.preventDefault(); hist.undo(); }
    else if(k === "y" || (k === "z" && ev.shiftKey)){ ev.preventDefault(); hist.redo(); }
  });
  $("#c-undo").onclick = () => hist.undo();
  $("#c-redo").onclick = () => hist.redo();
  render();
  pushHistoryC();
}
if(document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
else boot();

// 기본 전투 데이터
window.CombatData.load({"effects": [{"id": "Effect:Damage", "displayName": "피해", "kind": "Damage", "description": "value 만큼 체력을 깎는다. 모든 공격의 기본 항목."}, {"id": "Effect:Knockback", "displayName": "넉백", "kind": "Knockback", "description": "value = 밀어내는 거리(타일). 유탄·샷건이 쓴다."}, {"id": "Effect:Burn", "displayName": "발화", "kind": "DamageOverTime", "description": "value = 틱당 피해. 크리스탈 탄약의 잔여 피해.", "duration": 4, "stacking": "Refresh", "tickInterval": 0.5}, {"id": "Effect:SlowField", "displayName": "감속 필드", "kind": "MoveSpeed", "description": "value = 이동속도 배율 (0.5 = 절반). 감속 필드 타워의 오라.", "duration": 6, "stacking": "Refresh"}], "guns": [{"id": "Gun:Pistol", "displayName": "기본 권총", "isAutomatic": false, "fireMode": "Projectile", "description": "시작 화기. 느리지만 탄약이 싸다.", "fireRate": 0.33, "range": 60, "reloadTime": 1.2, "zoomFOV": 40, "xRecoil": 2, "yRecoil": 1.5, "zRecoil": 1, "visualKickbackZ": 0.25, "baseSpread": 0.6, "maxSpread": 4, "spreadIncreasePerShot": 0.8, "spreadRecoveryRate": 5, "magSize": 12, "ammo": "Item:BasicAmmo", "damageMultiplier": 1, "ammoFilter": ["Item:BasicAmmo"]}, {"id": "Gun:Rifle", "displayName": "라이플", "isAutomatic": true, "fireMode": "Projectile", "description": "주력 화기. 탄약 소모가 빠르다.", "fireRate": 0.1, "range": 99, "reloadTime": 2, "zoomFOV": 40, "xRecoil": 3, "yRecoil": 2, "zRecoil": 1, "visualKickbackZ": 0.3, "baseSpread": 0.5, "maxSpread": 5, "spreadIncreasePerShot": 1, "spreadRecoveryRate": 5, "magSize": 30, "ammo": "Item:BasicAmmo", "damageMultiplier": 1.4, "ammoFilter": ["Item:BasicAmmo"]}, {"id": "Gun:Shotgun", "displayName": "샷건", "isAutomatic": false, "fireMode": "Hitscan", "description": "근접 다수. 한 번에 8발을 소비한다.", "fireRate": 0.83, "range": 25, "reloadTime": 2.5, "zoomFOV": 40, "xRecoil": 8, "yRecoil": 5, "zRecoil": 2, "visualKickbackZ": 0.6, "baseSpread": 4, "maxSpread": 8, "spreadIncreasePerShot": 1.5, "spreadRecoveryRate": 4, "magSize": 8, "ammo": "Item:DenseAmmo", "damageMultiplier": 0.5, "pellets": 8, "ammoFilter": ["Item:DenseAmmo"]}, {"id": "Gun:Sniper", "displayName": "스나이퍼", "isAutomatic": false, "fireMode": "Hitscan", "description": "원거리 단일 대상. 한 발의 무게가 크다.", "fireRate": 1.25, "range": 150, "reloadTime": 3, "zoomFOV": 30, "xRecoil": 10, "yRecoil": 6, "zRecoil": 3, "visualKickbackZ": 0.8, "baseSpread": 0.05, "maxSpread": 3, "spreadIncreasePerShot": 2, "spreadRecoveryRate": 3, "magSize": 5, "ammo": "Item:DenseAmmo", "damageMultiplier": 4, "ammoFilter": ["Item:DenseAmmo", "Item:CrystalAmmo"]}, {"id": "Gun:GrenadeLauncher", "displayName": "유탄 발사기", "isAutomatic": false, "fireMode": "Projectile", "description": "범위. 박격포 타워와 탄약을 공유한다.", "fireRate": 2, "range": 50, "reloadTime": 2.5, "zoomFOV": 40, "xRecoil": 6, "yRecoil": 4, "zRecoil": 2, "visualKickbackZ": 0.5, "baseSpread": 1, "maxSpread": 4, "spreadIncreasePerShot": 1, "spreadRecoveryRate": 4, "magSize": 4, "ammo": "Item:Grenade", "damageMultiplier": 1, "ammoFilter": ["Item:Grenade"]}, {"id": "Gun:EnergyRifle", "displayName": "에너지 소총", "isAutomatic": true, "fireMode": "Hitscan", "description": "관통. 좁은 길목에서 최강.", "fireRate": 0.25, "range": 100, "reloadTime": 2, "zoomFOV": 40, "xRecoil": 1, "yRecoil": 0.8, "zRecoil": 0.5, "visualKickbackZ": 0.15, "baseSpread": 0.2, "maxSpread": 2, "spreadIncreasePerShot": 0.4, "spreadRecoveryRate": 6, "magSize": 20, "ammo": "Item:EnergyCellAmmo", "damageMultiplier": 1.2, "ammoFilter": ["Item:EnergyCellAmmo"]}], "waves": [{"id": "Wave:Day1", "displayName": "Day 1 Wave", "description": "First wave", "day": 1, "requiredCoreTier": 0, "baseAmount": 4, "maxAliveAmount": 4, "spawnInterval": 2}, {"id": "Wave:Day2", "displayName": "Day 2 Wave", "description": "Second wave", "day": 2, "requiredCoreTier": 0, "baseAmount": 6, "maxAliveAmount": 6, "spawnInterval": 2}, {"id": "Wave:Day3", "displayName": "Day 3 Wave", "description": "Third wave", "day": 3, "requiredCoreTier": 1, "baseAmount": 8, "maxAliveAmount": 8, "spawnInterval": 1.5}, {"id": "Wave:Day4", "displayName": "Day 4 Wave", "description": "Fourth wave", "day": 4, "requiredCoreTier": 1, "baseAmount": 10, "maxAliveAmount": 10, "spawnInterval": 1.5}, {"id": "Wave:Day5", "displayName": "Day 5 Wave", "description": "Fifth wave", "day": 5, "requiredCoreTier": 2, "baseAmount": 15, "maxAliveAmount": 12, "spawnInterval": 1}]});

// 기본 전투 데이터 — Feature_Damage 브랜치의 GameDataCombat.json


})();

