// ═══════════════════════════════════════════════════════════
//  통합 셸 — 탭 전환 · 공용 GameData 입출력
// ═══════════════════════════════════════════════════════════


document.querySelectorAll("#tabs .tab").forEach(btn => btn.onclick = () => {
  document.querySelectorAll("#tabs .tab").forEach(b => b.classList.toggle("on", b === btn));
  document.querySelectorAll(".pane").forEach(p => p.classList.toggle("on", p.id === "pane-" + btn.dataset.pane));
  if(btn.dataset.pane === "building") window.BuildingEditor?.refresh();
  if(btn.dataset.pane === "combat") window.CombatData?.refresh();
  if(btn.dataset.pane === "map") window.MapData?.refresh();
  if(btn.dataset.pane === "wave") window.WaveTab?.refresh();
  window.dispatchEvent(new Event("resize"));
});

function collectAll(){
  const o = {};
  const ef = window.CombatData?.getEffects() || [];
  const gn = window.CombatData?.getGuns() || [];
  const wv = window.CombatData?.getWaves() || [];
  if(ef.length) o.effects = ef;
  if(gn.length) o.guns = gn;
  o.items = window.GameData?.getItems() || [];
  o.recipes = window.GameData?.getRecipes() || [];
  o.buildings = window.BuildingEditor?.getBuildings() || [];
  if(wv.length) o.waves = wv;
  return o;
}
function updateSharedStat(){
  const d = collectAll(), el = document.getElementById("shared-stat");
  if(el) el.textContent = `아이템 ${d.items.length} · 레시피 ${d.recipes.length} · 건물 ${d.buildings.length}`
    + ((d.effects||d.guns||d.waves) ? ` · 효과 ${(d.effects||[]).length} · 화기 ${(d.guns||[]).length} · 웨이브 ${(d.waves||[]).length}` : "");
}
setInterval(updateSharedStat, 900);

const sModal = document.createElement("div");
sModal.id = "s-modal";
sModal.style.cssText = "position:fixed;inset:0;background:rgba(4,8,16,.72);display:none;align-items:center;justify-content:center;z-index:90";
sModal.innerHTML = `
  <div style="width:min(820px,92vw);max-height:86vh;background:#111B2E;border:1px solid #2E4266;border-radius:12px;
              padding:18px;display:flex;flex-direction:column;gap:12px">
    <div id="s-title" style="font-size:12px;color:#8FA3C0;letter-spacing:.08em;text-transform:uppercase">GameData.json</div>
    <textarea id="s-text" spellcheck="false" style="flex:1;min-height:420px;background:#0E1727;border:1px solid #2E4266;
      color:#D9E4F5;border-radius:5px;padding:10px;font-family:Consolas,monospace;font-size:12px;
      white-space:pre;resize:none"></textarea>
    <div style="display:flex;gap:8px;justify-content:flex-end;align-items:center">
      <span id="s-msg" style="flex:1;font-size:12px;color:#5C6E8C"></span>
      <button id="s-copy">복사</button><button id="s-dl">파일 다운로드</button>
      <button id="s-apply">적용</button><button id="s-close">닫기</button>
    </div>
  </div>`;
document.body.appendChild(sModal);
sModal.querySelectorAll("button").forEach(b => b.style.cssText =
  "background:#111B2E;border:1px solid #2E4266;color:#D9E4F5;border-radius:6px;padding:6px 12px;cursor:pointer;font-size:13px;font-family:inherit");
sModal.querySelector("#s-apply").style.cssText += ";border-color:#4FD8E0;color:#4FD8E0";

let sKind = "gamedata";     // 어느 파일을 다루는 창인지 — 적용·다운로드 대상이 갈린다
const openS = (title, text, showApply, kind = "gamedata") => {
  sKind = kind;
  document.getElementById("s-title").textContent = title;
  document.getElementById("s-text").value = text;
  document.getElementById("s-apply").style.display = showApply ? "" : "none";
  // 불러오기(showApply)에는 복사·다운로드가 필요 없다 — 내보낼 내용이 아직 없다
  document.getElementById("s-copy").style.display = showApply ? "none" : "";
  document.getElementById("s-dl").style.display   = showApply ? "none" : "";
  const t = document.getElementById("s-text");
  t.readOnly = !showApply;
  t.placeholder = showApply ? (kind === "map" ? "MapData.json" : kind === "wave" ? "WaveData.json" : "GameData.json") + " 내용을 붙여넣으세요" : "";
  document.getElementById("s-msg").textContent = "";
  sModal.style.display = "flex";
};
document.getElementById("s-export").onclick = () =>
  openS("GameData.json — 내보내기", JSON.stringify(collectAll(), null, 2), false);
document.getElementById("s-import").onclick = () =>
  openS("GameData.json — 붙여넣고 적용", "", true);
document.getElementById("s-apply").onclick = () => {
  try {
    const obj = JSON.parse(document.getElementById("s-text").value);
    if(sKind === "map"){
      window.MapData.load(obj);
      updateSharedStat(); sModal.style.display = "none";
      return;
    }
    if(sKind === "wave"){
      // {"waves":[...]} 와 맨 배열 [...] 둘 다 받는다 — 임포터는 waves 섹션만 읽는다
      const wv = Array.isArray(obj) ? obj : obj.waves;
      if(!Array.isArray(wv)) throw new Error("waves 배열이 없습니다");
      window.CombatData?.load({ waves: wv });
      window.WaveTab?.refresh();
      updateSharedStat(); sModal.style.display = "none";
      return;
    }
    if(obj.items || obj.recipes) window.GameData.loadGraph({ items:obj.items||[], recipes:obj.recipes||[] });
    if(obj.buildings) window.BuildingEditor.loadBuildings({ buildings:obj.buildings });
    if(obj.effects || obj.guns || obj.waves)
      window.CombatData?.load({ effects:obj.effects, guns:obj.guns, waves:obj.waves });
    window.BuildingEditor?.refresh();
    window.WaveTab?.refresh();
    updateSharedStat(); sModal.style.display = "none";
  } catch(e){ document.getElementById("s-msg").textContent = "JSON 파싱 실패: " + e.message; }
};
document.getElementById("s-copy").onclick = async () => {
  const t = document.getElementById("s-text");
  try { await navigator.clipboard.writeText(t.value); } catch { t.select(); document.execCommand("copy"); }
  document.getElementById("s-msg").textContent = "복사됨";
};
document.getElementById("s-dl").onclick = () => {
  const blob = new Blob([document.getElementById("s-text").value], {type:"application/json"});
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob); a.download = sKind === "map" ? "MapData.json" : sKind === "wave" ? "WaveData.json" : "GameData.json";
  a.click(); URL.revokeObjectURL(a.href);
};
document.getElementById("s-close").onclick = () => sModal.style.display = "none";
window.openDataModal = openS;   // 맵 탭이 같은 창을 쓴다
updateSharedStat();

