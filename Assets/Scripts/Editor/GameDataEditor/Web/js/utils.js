// ═══════════════════════════════════════════════════════════
//  공용 유틸 — 세 에디터 블록이 함께 쓴다
//  (이전에는 블록마다 같은 함수를 따로 정의해 미묘하게 어긋났다)
// ═══════════════════════════════════════════════════════════
window.EdUtil = (() => {
  const esc = v => (v ?? "").toString()
    .replace(/[&<>"]/g, c => ({ "&":"&amp;", "<":"&lt;", ">":"&gt;", '"':"&quot;" }[c]));
  const $  = s => document.querySelector(s);
  const $$ = s => [...document.querySelectorAll(s)];

  // <label> + 입력칸 한 줄. cls: "top"(라벨 상단 정렬) / "wide"(그룹 제목)
  const field = (label, inner, cls = "") =>
    `<div class="field ${cls}"><label>${label}</label><div>${inner}</div></div>`;

  // id 접두 — 임포터가 "Item:" 같은 접두로 종류를 가른다
  const PREFIX = { item:"Item:", recipe:"Recipe:", building:"Building:",
                   effect:"Effect:", gun:"Gun:", wave:"Wave:", map:"Map:" };
  const bareId = (id, kind) => (id || "").replace(new RegExp("^" + PREFIX[kind]), "");
  const fullId = (bare, kind) => bare ? PREFIX[kind] + bare.replace(/[^A-Za-z0-9_가-힣]/g, "") : "";

  // 내용에 맞춰 늘어나는 textarea
  const autogrow = (root = document) => {
    root.querySelectorAll("textarea.autogrow").forEach(el => {
      const grow = () => { el.style.height = "auto"; el.style.height = Math.max(el.scrollHeight, 30) + "px"; };
      grow(); el.addEventListener("input", grow);
    });
  };

  return { esc, $, $$, field, PREFIX, bareId, fullId, autogrow };
})();

// ═══════════════════════════════════════════════════════════
//  공용 검증 — 임포터(GameDataImporter.cs)가 거부하는 조건들.
//  건물·전투 탭이 각자 구현하던 것을 모았다. 규칙이 바뀌면 여기만 고친다.
// ═══════════════════════════════════════════════════════════
window.EdValid = (() => {
  // 모든 엔티티가 공통으로 지켜야 하는 것 — id 와 표시 이름
  const identity = (x, all, kind, idOf = e => e.id) => {
    const out = [];
    const id = idOf(x) || "";
    const bare = window.EdUtil.bareId(id, kind);
    if(!bare) out.push("id 가 비어 있습니다 — 임포트의 기본 키입니다");
    else if(all.filter(y => idOf(y) === id).length > 1) out.push(`id 중복 — ${id}`);
    if(!(x.displayName || "").trim()) out.push("displayName 이 비어 있습니다 — 임포터가 거부합니다");
    return out;
  };

  // 참조가 실제로 존재하는지. 목록이 비어 있으면(아직 안 불러옴) 검사하지 않는다.
  const refs = (ids, pool, label) => {
    if(!pool.length) return [];
    const have = new Set(pool.map(p => p.id));
    return ids.filter(id => id && !have.has(id)).map(id => `${label} — id "${id}" 를 찾을 수 없습니다`);
  };

  // 같은 값이 두 번 들어갔는지
  const dup = (values, label) => {
    const seen = new Set(), out = [];
    for(const v of values){
      if(v && seen.has(v)) out.push(`${label} — 같은 항목이 중복됩니다 (${v})`);
      seen.add(v);
    }
    return out;
  };

  // 0 이하면 임포터가 무시하거나 런타임이 멈추는 값들
  const positive = (obj, fields) =>
    fields.filter(([k]) => !(obj[k] > 0)).map(([k, label]) => `${label} 는 0보다 커야 합니다`);

  return { identity, refs, dup, positive };
})();

// ═══════════════════════════════════════════════════════════
//  실행 취소 / 다시 실행 — 탭마다 같은 스택을 따로 만들어 쓴다.
//  스냅샷 방식: 데이터가 작아 통째로 복사해도 부담이 없고,
//  변경 종류마다 역연산을 짜는 것보다 어긋날 여지가 없다.
// ═══════════════════════════════════════════════════════════
window.EdHistory = (take, apply, limit = 60) => {
  const past = [], future = [];
  let last = null;
  const push = () => {
    const cur = JSON.stringify(take());
    if(cur === last) return;                 // 실제로 바뀐 게 없으면 쌓지 않는다
    if(last !== null) past.push(last);
    if(past.length > limit) past.shift();
    future.length = 0;                       // 새 변경이 생기면 redo 는 버린다
    last = cur;
  };
  const restore = str => { apply(JSON.parse(str)); last = str; };
  return {
    push,
    undo(){ if(!past.length) return false; future.push(last); restore(past.pop()); return true; },
    redo(){ if(!future.length) return false; past.push(last); restore(future.pop()); return true; },
    get canUndo(){ return past.length > 0; },
    get canRedo(){ return future.length > 0; },
  };
};
