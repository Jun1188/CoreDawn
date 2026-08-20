// ═══════════════════════════════════════════════════════════
//  웨이브 탭 — Day별 웨이브 옵션을 표 하나로 편집한다.
//  데이터 정본은 전투 탭(CombatData)의 waves 배열 — 여기는 같은 배열을
//  getWaves/load 통로로 읽고 쓰는 또 하나의 뷰다 (저장소를 둘로 쪼개지 않는다).
// ═══════════════════════════════════════════════════════════
(function(){
const esc = s => String(s ?? "").replace(/[&<>"']/g,
  c => ({ "&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;" }[c]));

const getWaves = () => { try { return window.CombatData?.getWaves() || []; } catch { return []; } };
const setWaves = wv => { window.CombatData?.load({ waves: wv }); };   // load()가 day 순 정렬까지 한다

const COLS = [
  { key:"day",            th:"Day",       num:true, step:1,  title:"이 웨이브가 적용되기 시작하는 일차" },
  { key:"requiredCoreTier",th:"코어 티어", num:true, step:1,  title:"코어 티어가 낮으면 이전 웨이브가 반복된다" },
  { key:"baseAmount",     th:"총 마리수", num:true, step:1,  title:"밤에 전멸시켜야 하는 총량 (물량제 밤의 길이)" },
  { key:"maxAliveAmount", th:"동시 상한", num:true, step:1,  title:"한 번에 살아 있을 수 있는 수" },
  { key:"spawnInterval",  th:"스폰 간격", num:true, step:.1, title:"스폰 시도 간격(초)" },
  { key:"monsterMaxHp",   th:"몬스터 HP", num:true, step:1,  title:"0 = wave_settings.json → 프리팹 기본값 폴백" },
];

function render(){
  const waves = getWaves();
  const table = document.getElementById("wv-table");
  const empty = document.getElementById("wv-empty");
  if(!table) return;

  empty.style.display = waves.length ? "none" : "";
  table.innerHTML = !waves.length ? "" :
    `<tr><th>Id</th><th>이름</th>${COLS.map(c=>`<th title="${esc(c.title)}">${c.th}</th>`).join("")}<th></th></tr>` +
    waves.map((w, i) => `<tr>
      <td style="min-width:130px"><input class="mono" data-i="${i}" data-k="id"
        value="${esc((w.id||"").replace(/^Wave:/,""))}" placeholder="Day${w.day}" title="Wave: 접두는 자동으로 붙는다"></td>
      <td style="min-width:130px"><input data-i="${i}" data-k="displayName" value="${esc(w.displayName)}"></td>
      ${COLS.map(c=>`<td class="num"><input type="number" step="${c.step}" min="0"
        data-i="${i}" data-k="${c.key}" value="${w[c.key] ?? 0}" title="${esc(c.title)}"></td>`).join("")}
      <td><button class="mini warn" data-del="${i}">삭제</button></td>
    </tr>`).join("");

  table.querySelectorAll("input").forEach(el => el.onchange = () => {
    const rows = getWaves();
    const w = rows[+el.dataset.i];
    if(!w) return;
    const k = el.dataset.k;
    if(k === "id") w.id = el.value ? "Wave:" + el.value.replace(/^Wave:/,"").replace(/[^A-Za-z0-9_가-힣]/g,"") : "";
    else if(k === "displayName") w.displayName = el.value;
    else w[k] = Math.max(0, parseFloat(el.value) || 0);
    setWaves(rows);
    render();
  });
  table.querySelectorAll("[data-del]").forEach(el => el.onclick = () => {
    const rows = getWaves();
    rows.splice(+el.dataset.del, 1);
    setWaves(rows);
    render();
  });

  const stat = document.getElementById("wv-stat");
  if(stat) stat.textContent = waves.length
    ? `웨이브 ${waves.length}개 · Day ${waves[0].day}~${waves[waves.length-1].day}`
    : "";

  const warn = [];
  const seen = new Set();
  for(const w of waves){
    if(!w.id) warn.push(`Day ${w.day}: id가 비어 있습니다 — 임포트에서 스킵됩니다`);
    const key = w.day + "/" + w.requiredCoreTier;
    if(seen.has(key)) warn.push(`Day ${w.day} · 티어 ${w.requiredCoreTier} 조합이 중복입니다 — 뒤 항목만 적용됩니다`);
    seen.add(key);
    if(w.maxAliveAmount > w.baseAmount) warn.push(`Day ${w.day}: 동시 상한이 총량보다 큽니다 — 상한이 의미 없습니다`);
    if(!(w.spawnInterval > 0)) warn.push(`Day ${w.day}: 스폰 간격은 0보다 커야 합니다`);
  }
  document.getElementById("wv-warn").textContent = warn.join("\n");
}

function boot(){
  document.getElementById("wv-add").onclick = () => {
    const rows = getWaves();
    const last = rows[rows.length-1];
    rows.push({ id:"", displayName:"새 웨이브", description:"",
      day: last ? last.day + 1 : 1, requiredCoreTier: last ? last.requiredCoreTier : 0,
      baseAmount: last ? last.baseAmount + 2 : 4,
      maxAliveAmount: last ? last.maxAliveAmount + 2 : 4,
      spawnInterval: last ? last.spawnInterval : 2,
      monsterMaxHp: last ? last.monsterMaxHp : 0 });
    setWaves(rows);
    render();
  };
  document.getElementById("wv-export").onclick = () =>
    window.openDataModal("WaveData.json — 내보내기", JSON.stringify({ waves: getWaves() }, null, 2), false, "wave");
  document.getElementById("wv-import").onclick = () =>
    window.openDataModal("WaveData.json — 붙여넣고 적용", "", true, "wave");
  render();
}
if(document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
else boot();

window.WaveTab = { refresh: render };
})();
