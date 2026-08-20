// ═══════════════════════════════════════════════════════════
//  건물 에디터 — BuildingDataSO · 3D 포트 편집
// ═══════════════════════════════════════════════════════════
const TIER_MARK = window.EdEnum.TIER_MARK;
const DVEC = window.EdEnum.DVEC;
const DIRS = window.EdEnum.DIRS;
const CATEGORIES = window.EdEnum.CATEGORIES;
const KINDS = window.EdEnum.KINDS;
const LINE_COLOR = window.EdEnum.LINE_COLOR;

/* 노드 에디터와 전역 이름이 겹치지 않도록 즉시실행 함수로 감싼다.
   three.js 는 module 스크립트로 받으므로 classic 인 이 블록보다 늦게 끝난다 — 기다렸다 시작한다. */
(async function(){

await (window.__THREE_READY__ || Promise.resolve(false));

if(!window.__THREE__){
  // 로컬 번들도 CDN 도 실패. 건물 탭의 3D 뷰만 포기하고 나머지는 그대로 쓴다.
  const box = document.getElementById("b-view");
  if(box) box.innerHTML = '<div style="position:absolute;inset:0;display:flex;align-items:center;'
    + 'justify-content:center;text-align:center;color:#8FA3C0;font-size:13px;padding:24px;line-height:1.7">'
    + '<div><b style="color:#FF5D73">three.js 를 불러오지 못했습니다</b><br>'
    + '인터넷 연결(cdn.jsdelivr.net)을 확인하세요.<br>'
    + '<span style="color:#5C6E8C">데이터 편집은 그대로 사용할 수 있습니다.</span></div></div>';
  window.__THREE__ = null;
}
let { THREE, OrbitControls, GLTFLoader, OBJLoader, FBXLoader } = window.__THREE__ || {};
if(!THREE){
  // three.js 가 없어도 건물 데이터 편집은 그대로 돌아가야 한다.
  // 3D 호출은 전부 빈 객체로 흘려보낸다 — 화면만 안내 문구로 대체된다.
  const nop = function(){ return new Proxy(this, stub); };
  const stub = {
    get: (t, k) => {
      if(k === Symbol.toPrimitive || k === "toString") return () => "";
      if(k === "children") return [];
      if(k === "isMesh" || k === "isGroup") return false;
      return dummy;
    },
    set: () => true,
    apply: () => dummy,
    construct: () => dummy,
  };
  const dummy = new Proxy(function(){}, stub);
  THREE = dummy; OrbitControls = dummy; GLTFLoader = dummy; OBJLoader = dummy; FBXLoader = dummy;
}

// ═══════════════════════════ 상수 ═══════════════════════════
        // enum Direction 순서 = 정수값
// BuildingDataSO 서브클래스 — 임포터가 이 값으로 CreateInstance 할 타입을 고른다
const COL_IN = 0x4FD8E0, COL_OUT = 0xFF9E4A, COL_SEL = 0xB48CFF;
// GameData.json 의 items 를 같이 불러오면 채워진다 — 비용 드롭다운과 검증에 쓴다
let knownItems = [];
function syncItems(){
  try {
    knownItems = (window.GameData?.getItems() || []).map(i => ({
      id:i.id, displayName:i.displayName||i.id, line:i.line||"None", type:i.type||"" }));
    knownRecipes = (window.GameData?.getRecipes() || []).map(r => ({
      id:r.id, displayName:r.displayName||r.id, inputs:(r.inputs||[]).length, tier:r.tier ?? 0 }));
  } catch { knownItems = []; knownRecipes = []; }
}
let knownRecipes = [];   // {id, displayName, inputs, tier}
const itemById = id => knownItems.find(i => i.id === id);
const recipeById = id => knownRecipes.find(r => r.id === id);

// ═══════════════════════════ 데이터 ═══════════════════════════
let buildings = [];
let curIdx = 0, selPort = -1, rot = 0;
const cur = () => buildings[curIdx];

function newBuilding(kind="Miner"){
  const k = KINDS.find(x=>x.v===kind);
  return {
    id:"", _idLocked:false, kind, displayName:"새 건물", description:"", category:k.cat,
    model:"", size:{x:1,y:1}, ports:[],
    maxHp:200, buildCost:[], inputSlots:1, outputSlots:1, bufferStackCap:0,
    requiredCoreTier:0, hideFromBuildMenu:false,
    speedMultiplier:1, speedTilesPerSec:2, availableRecipes:[],
    damageMultiplier:1, range:8, fireRate:1, ammoFilter:[],
    tiers:[],
    droneRange:40, carryCapacity:20, travelSpeed:8,
    _model:null, _modelName:"",
  };
}
const slug = s => (s||"").replace(/[^A-Za-z0-9_가-힣]/g,"");
const ID_PREFIX = "Building:";
const idSuffix = b => (b.id || "").replace(/^Building:/, "");
const bid = b => ID_PREFIX + (slug(idSuffix(b)) || slug(b.displayName) || "Unnamed");

// 회전: Dir.RotateCellCW 와 동일 — (x,y) → (y, w-1-x)
function rotCell(p, w){ return { x:p.y, y:w-1-p.x }; }
function rotDir(d, steps){ return DIRS[(DIRS.indexOf(d)+steps)%4]; }
function rotatedSize(b, steps){ return steps%2===0 ? {...b.size} : {x:b.size.y, y:b.size.x}; }
function rotatedPorts(b, steps){
  let ports = b.ports.map(p=>({...p}));
  let w = b.size.x;
  for(let s=0;s<steps;s++){
    ports = ports.map(p=>{ const c = rotCell(p, w); return { ...p, x:c.x, y:c.y, dir:rotDir(p.dir,1) }; });
    w = (s%2===0) ? b.size.y : b.size.x;   // 다음 스텝의 회전 전 가로 크기
  }
  return ports;
}

// ═══════════════════════════ 3D ═══════════════════════════
// three.js 는 이 섹션 안에서만 쓴다 (scene/camera/renderer/그룹 3개).
// 밖에서는 refresh3D() · resize() · loadModelFile() 만 부른다.
// 공유 상태는 selPort(선택된 포트)·rot(회전 단계) 둘뿐 — 늘리지 말 것.
const viewEl = document.getElementById("b-view");

// three.js 나 WebGL 이 없어도 데이터 편집은 계속 되어야 한다.
// Scene 생성부터 THREE 를 쓰므로 전체를 한 번에 감싼다.
let scene = null, camera = null, renderer = null, controls = null;
try {
  if(!window.__THREE__) throw new Error("three.js 미로드");
  scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0B1220);
  scene.fog = new THREE.Fog(0x0B1220, 14, 34);
  camera = new THREE.PerspectiveCamera(45, 1, .1, 200);
  camera.position.set(3.6, 3.4, 4.6);
  renderer = new THREE.WebGLRenderer({ antialias:true });
  renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
  viewEl.appendChild(renderer.domElement);
} catch(e) {
  console.warn("[3D] WebGL 사용 불가 —", e && e.message);
  viewEl.innerHTML = '<div style="position:absolute;inset:0;display:flex;align-items:center;justify-content:center;'
    + 'color:var(--faint);font-size:13px;text-align:center;padding:20px">'
    + '3D 미리보기를 켤 수 없습니다<br>(WebGL 사용 불가) — 데이터 편집은 그대로 됩니다</div>';
}

if(renderer && camera){
  controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true; controls.dampingFactor = .08;
  controls.target.set(0, .3, 0);
}

let gFootprint = null, gPorts = null, gModel = null;
if(scene){
  scene.add(new THREE.HemisphereLight(0x9FC4FF, 0x121A2A, 1.1));
  const key = new THREE.DirectionalLight(0xffffff, 1.5);
  key.position.set(4, 7, 3); scene.add(key);
  const rim = new THREE.DirectionalLight(0x4FD8E0, .5);
  rim.position.set(-4, 2, -3); scene.add(rim);
  
  // 바닥 격자
  const grid = new THREE.GridHelper(24, 24, 0x2E4266, 0x1A2740);
  grid.material.opacity = .5; grid.material.transparent = true;
  scene.add(grid);
  
  gFootprint = new THREE.Group(); scene.add(gFootprint);   // 풋프린트 셀
  gPorts = new THREE.Group(); scene.add(gPorts);       // 포트 화살표
  gModel = new THREE.Group(); scene.add(gModel);       // 불러온 모델 / 대체 박스
}

// 그리드 좌표 → 월드 (셀 중앙). y(격자) 는 -z 로 매핑해 North 가 화면 안쪽.
function cellToWorld(x, y, size){
  return new THREE.Vector3(x - (size.x-1)/2, 0, -(y - (size.y-1)/2));
}

function buildFootprint(size){
  gFootprint.clear();
  const mat  = new THREE.MeshBasicMaterial({ color:0x4FD8E0, transparent:true, opacity:.055 });
  const line = new THREE.LineBasicMaterial({ color:0x2E4266 });
  for(let x=0;x<size.x;x++) for(let y=0;y<size.y;y++){
    const p = cellToWorld(x,y,size);
    const plane = new THREE.Mesh(new THREE.PlaneGeometry(.96,.96), mat);
    plane.rotation.x = -Math.PI/2; plane.position.set(p.x, .002, p.z);
    plane.userData.cell = {x,y};
    gFootprint.add(plane);
    const eg = new THREE.EdgesGeometry(new THREE.PlaneGeometry(.96,.96));
    const ln = new THREE.LineSegments(eg, line);
    ln.rotation.x = -Math.PI/2; ln.position.set(p.x, .004, p.z);
    gFootprint.add(ln);
  }
  // origin 표시 (0,0) — 회전 기준점
  const o = cellToWorld(0,0,size);
  const dot = new THREE.Mesh(new THREE.SphereGeometry(.055,12,12),
              new THREE.MeshBasicMaterial({ color:0x5DD39E }));
  dot.position.set(o.x, .05, o.z); gFootprint.add(dot);
}

// 포트 표시 — 화살표 대신 면에서 퍼져 나가는 반투명 그라데이션.
// 바닥에 깔리는 판 + 세워진 지느러미 두 장으로 어느 각도에서 봐도 방향이 읽힌다.
const FLOW_LEN = 0.58, FLOW_W = 0.78, FLOW_H = 0.36;

function flowMaterial(color, selected, vertical){
  return new THREE.ShaderMaterial({
    uniforms: {
      uColor:  { value: new THREE.Color(color) },
      uPower:  { value: selected ? 1.5 : 1.0 },
      uVert:   { value: vertical ? 1.0 : 0.0 },
    },
    vertexShader: `
      varying vec2 vUv;
      void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }`,
    fragmentShader: `
      uniform vec3 uColor; uniform float uPower; uniform float uVert;
      varying vec2 vUv;
      void main(){
        float along = 1.0 - vUv.x;              // 면에서 멀어질수록 옅어진다
        float fade  = pow(along, 1.7);
        // 가장자리 부드럽게: 바닥판은 좌우로, 지느러미는 위로 흐려진다
        float across = mix(smoothstep(0.0, 0.22, vUv.y) * smoothstep(1.0, 0.78, vUv.y),
                           pow(1.0 - vUv.y, 1.2), uVert);
        float a = fade * across * 0.55 * uPower;
        if(a < 0.004) discard;
        gl_FragColor = vec4(uColor, a);
      }`,
    transparent: true, depthWrite: false, side: THREE.DoubleSide,
    blending: THREE.AdditiveBlending,
  });
}

function makePortFlow(color, selected){
  const g = new THREE.Group();

  // 바닥판 — 스케치의 번지는 자국
  const floorGeo = new THREE.PlaneGeometry(FLOW_LEN, FLOW_W, 24, 1);
  floorGeo.rotateX(-Math.PI/2);
  floorGeo.translate(FLOW_LEN/2, 0.012, 0);
  g.add(new THREE.Mesh(floorGeo, flowMaterial(color, selected, false)));

  // 세운 지느러미 — 위에서만 보면 납작해 보이므로 하나 세운다
  const finGeo = new THREE.PlaneGeometry(FLOW_LEN, FLOW_H, 24, 1);
  finGeo.translate(FLOW_LEN/2, FLOW_H/2, 0);
  g.add(new THREE.Mesh(finGeo, flowMaterial(color, selected, true)));

  // 면에 붙는 발광 띠 — 포트의 정확한 위치를 찍어준다
  const lipGeo = new THREE.PlaneGeometry(0.05, FLOW_W * 0.8);
  lipGeo.rotateX(-Math.PI/2); lipGeo.translate(0.02, 0.02, 0);
  g.add(new THREE.Mesh(lipGeo, new THREE.MeshBasicMaterial({
    color, transparent:true, opacity: selected ? 0.95 : 0.6, depthWrite:false })));

  if(selected){
    const ring = new THREE.Mesh(new THREE.TorusGeometry(0.17, 0.014, 8, 24),
                 new THREE.MeshBasicMaterial({ color: COL_SEL, transparent:true, opacity:.9, depthWrite:false }));
    ring.rotation.x = -Math.PI/2; ring.position.set(0, 0.03, 0);
    g.add(ring);
  }
  return g;
}

function buildPorts(){
  gPorts.clear();
  const b = cur(); if(!b) return;
  const size = rotatedSize(b, rot);
  const ports = rotatedPorts(b, rot);
  ports.forEach((p, i) => {
    const c = cellToWorld(p.x, p.y, size);
    const v = DVEC[p.dir];
    const flow = makePortFlow(p.isInput ? COL_IN : COL_OUT, i === selPort);
    // 입출력 모두 건물 면에 붙여 바깥으로 번지게 한다 — 방향은 색으로 구분한다.
    flow.position.set(c.x + v[0]*0.5, 0, c.z - v[1]*0.5);
    flow.rotation.y = Math.atan2(v[1], v[0]);
    flow.traverse(o => o.userData.portIndex = i);
    flow.userData.portIndex = i;
    gPorts.add(flow);
  });
}

function placeholderBox(size){
  gModel.clear();
  const geo = new THREE.BoxGeometry(size.x*0.86, 0.55, size.y*0.86);
  const mat = new THREE.MeshStandardMaterial({ color:0x223350, metalness:.25, roughness:.7,
                                               transparent:true, opacity:.9 });
  const m = new THREE.Mesh(geo, mat); m.position.y = 0.275; gModel.add(m);
  const eg = new THREE.EdgesGeometry(geo);
  gModel.add(new THREE.LineSegments(eg, new THREE.LineBasicMaterial({ color:0x2E4266 }))
            .translateY(0.275));
}

// 모델을 풋프린트 크기에 맞춰 축소·확대만 한다.
// 위치는 건드리지 않는다 — 모델이 가진 원점(피벗)이 곧 건물의 원점이다.
// 바운딩 박스 바닥에 맞추면 드릴처럼 아래로 뻗은 부품 때문에 본체가 떠 보인다.
function fitModel(obj, size){
  const box = new THREE.Box3().setFromObject(obj);
  const s = new THREE.Vector3(); box.getSize(s);
  const target = Math.max(size.x, size.y) * 0.9;
  const scale = target / Math.max(s.x, s.z, 0.0001);
  obj.scale.setScalar(scale);
  obj.position.set(0, 0, 0);
}

// ═══════════════════════════ 렌더 갱신 ═══════════════════════════
function refresh3D(){
  if(!scene) return;
  const b = cur(); if(!b) return;
  const size = rotatedSize(b, rot);
  buildFootprint(size);
  buildPorts();
  if(b._model){
    gModel.clear();
    const clone = b._model.clone(true);
    fitModel(clone, size);
    clone.rotation.y = -rot * Math.PI/2;
    gModel.add(clone);
  } else placeholderBox(size);
}

function resize(){
  if(!renderer || !camera) return;
  const w = viewEl.clientWidth, h = viewEl.clientHeight;
  if(!w || !h) return;
  camera.aspect = w/h; camera.updateProjectionMatrix();
  renderer.setSize(w, h);   // CSS 크기도 함께 갱신해야 캔버스가 영역을 넘지 않는다
}
if(typeof ResizeObserver !== "undefined") new ResizeObserver(resize).observe(viewEl);
else window.addEventListener("resize", resize);

if(renderer){
  (function loop(){
    requestAnimationFrame(loop);
    controls.update();
    renderer.render(scene, camera);
  })();
}

// ═══════════════════════════ 피킹 ═══════════════════════════
const ray = new THREE.Raycaster(), ptr = new THREE.Vector2();
let downPos = null;
if(renderer) renderer.domElement.addEventListener("pointerdown", e => { downPos = { x:e.clientX, y:e.clientY }; });
if(renderer) renderer.domElement.addEventListener("pointerup", e => {
  if(!downPos) return;
  const moved = Math.hypot(e.clientX-downPos.x, e.clientY-downPos.y);
  downPos = null;
  if(moved > 4 || e.button !== 0) return;   // 드래그(회전)와 구분
  if(!renderer) return;
  const r = renderer.domElement.getBoundingClientRect();
  ptr.x = ((e.clientX-r.left)/r.width)*2 - 1;
  ptr.y = -((e.clientY-r.top)/r.height)*2 + 1;
  ray.setFromCamera(ptr, camera);

  // 1) 포트 먼저
  const hitPort = ray.intersectObjects(gPorts.children, true)[0];
  if(hitPort){
    const i = hitPort.object.userData.portIndex;
    if(i !== undefined){ selPort = i; renderPorts(); refresh3D(); return; }
  }
  // 2) 격자 셀 → 그 자리에 포트 추가 (바깥을 향하는 방향 자동 선택)
  const hitCell = ray.intersectObjects(gFootprint.children, false)
                     .find(h => h.object.userData.cell);
  if(hitCell && rot === 0){
    const {x,y} = hitCell.object.userData.cell;
    addPortAt(x, y);
  }
});

// 셀에서 바깥으로 나가는 방향 중 비어 있는 것을 고른다
function outwardDir(b, x, y){
  for(const d of DIRS){
    const v = DVEC[d], nx = x+v[0], ny = y+v[1];
    const outside = nx<0 || ny<0 || nx>=b.size.x || ny>=b.size.y;
    if(outside && !b.ports.some(p => p.x===x && p.y===y && p.dir===d)) return d;
  }
  return null;
}
function addPortAt(x, y, isInput){
  const b = cur(); if(!b) return;
  const d = outwardDir(b, x, y);
  if(!d){ flash("이 칸에는 더 놓을 바깥 방향이 없습니다"); return; }
  if(isInput === undefined) isInput = b.ports.filter(p=>p.isInput).length <= b.ports.filter(p=>!p.isInput).length;
  b.ports.push({ x, y, dir:d, isInput });
  selPort = b.ports.length-1;
  renderAll();
}

// ═══════════════════════════ UI ═══════════════════════════
const { esc, $, $$, field } = window.EdUtil;

function renderList(){
  $("#b-blist").innerHTML = buildings.map((b,i)=>`
    <div class="bitem ${i===curIdx?"sel":""}" data-i="${i}">
      <span class="nm">${esc(b.displayName)}</span><span class="kd">${b.kind}</span>
    </div>`).join("");
  $("#b-blist").querySelectorAll(".bitem").forEach(el => el.onclick = () => {
    curIdx = +el.dataset.i; selPort = -1; rot = 0; setRotButtons(); renderAll();
  });
}

// kind 별 추가 숫자 필드 — 라벨·입력 id·범위를 한자리에 모았다.
// 이전에는 if 분기 9개가 같은 마크업을 반복했다.
const NUM_FIELDS = {
  speedMultiplier:  { id:"b-f-speed-mult", label:"Speed Multiplier", min:0.1, step:0.1,
                      title:"채굴 시간 = 광맥의 extractInterval ÷ 이 배율" },
  speedTilesPerSec: { id:"b-f-speed",      label:"Speed (tiles/s)",  min:0,   step:0.1 },
  damageMultiplier: { id:"b-f-dmgx",       label:"Damage ×",         min:0,   step:0.1,
                      title:"실제 피해 = 탄약의 기본 피해 × 이 배수. 0 = 공격하지 않음" },
  range:            { id:"b-f-range",      label:"Range (타일)",      min:0,   step:0.5 },
  fireRate:         { id:"b-f-rate",       label:"Fire Rate (회/초)",  min:0.1, step:0.1 },
  droneRange:       { id:"f-drange",       label:"Range (타일)",      min:1,   step:5,
                      title:"짝지을 수 있는 다른 스테이션까지의 최대 거리" },
  carryCapacity:    { id:"f-dcarry",       label:"Carry",            min:1,
                      title:"드론 1회 운반량" },
  travelSpeed:      { id:"f-dspeed",       label:"Speed (타일/초)",   min:0.5, step:0.5 },
};


// 함께 다니는 갱신 묶음 — 하나만 부르고 다른 걸 빠뜨리는 실수를 막는다.
// (배칭하지 않는다. 입력 중 재렌더 타이밍이 바뀌면 드롭다운이 닫힌다)
const refreshProps = () => { refreshProps(); };
const refreshList  = () => { renderList();  renderWarn(); };
function renderProps(){
  const b = cur();
  if(!b){ $("#b-props").innerHTML = ""; return; }
  const k = KINDS.find(x=>x.v===b.kind);
  const extra = k.extra.map(f => {
    const spec = NUM_FIELDS[f];
    if(spec) return field(spec.label,
      `<input id="${spec.id}" type="number"${spec.min!=null?` min="${spec.min}"`:""}` +
      `${spec.step!=null?` step="${spec.step}"`:""} value="${b[f]}"` +
      `${spec.title?` title="${esc(spec.title)}"`:""}>`);
    if(f === "ammoFilter") return ammoSection(b);
    if(f === "tiers")      return tierSection(b);
    if(f === "availableRecipes") return recipeSection(b);
    if(f === "curveLPrefab")     return modelRow("Curve L", "modelCurveL", b);
    if(f === "curveRPrefab")     return modelRow("Curve R", "modelCurveR", b);
    return "";
  }).join("");

  $("#b-props").innerHTML =
    field("Kind",
      `<select id="b-f-kind">${KINDS.map(x=>`<option value="${x.v}" ${x.v===b.kind?"selected":""}>${x.v} — ${x.ko}</option>`).join("")}</select>`) +
    field("Display Name", `<input id="b-f-name" value="${esc(b.displayName)}">`) +
    field("Id", `<div class="idrow">
       <span class="pfx mono">Building:</span>
       <input id="b-f-id" class="mono" value="${esc(idSuffix(b))}" placeholder="Assembler" spellcheck="false">
     </div>`) +
    field("Description", `<textarea id="b-f-desc" class="autogrow" rows="1">${esc(b.description)}</textarea>`, "top") +
    field("Category",
      `<select id="b-f-cat">${CATEGORIES.map(c=>`<option value="${c.v}" ${c.v===b.category?"selected":""}>${c.v} — ${c.ko}</option>`).join("")}</select>`) +
    field("Model", `<div style="display:flex;gap:5px;align-items:center">
       <span style="flex:1;min-width:0;font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;
                    color:${b.model?"var(--text)":"var(--faint)"}" class="mono">${esc(b.model || "(없음)")}</span>
       <button class="mini" id="b-f-pickmodel">지정</button>
       ${b.model?`<span class="x" id="b-f-clearmodel" style="color:var(--faint);cursor:pointer;padding:0 3px">✕</span>`:""}
     </div>`) +
    `<div class="field"><label></label><div style="font-size:11px;color:var(--faint);line-height:1.45">
       ${b.model ? "프리팹은 임포트할 때 이 모델로 자동 생성된다"
                 : "모델이 없으면 풋프린트 크기의 큐브로 만들어진다"}</div></div>` +
    field("Size (X, Y)", `<div class="row2">
       <input id="b-f-sx" type="number" min="1" max="8" value="${b.size.x}">
       <input id="b-f-sy" type="number" min="1" max="8" value="${b.size.y}"></div>`) +
    field("Buffer Slots", `<div class="row3">
       <input id="b-f-in"  type="number" min="0" value="${b.inputSlots}" title="입력 슬롯">
       <input id="b-f-out" type="number" min="0" value="${b.outputSlots}" title="출력 슬롯">
       <input id="b-f-cap" type="number" min="0" value="${b.bufferStackCap}" title="슬롯 하나에 쌓이는 최대 개수 (0 = 아이템 기본 스택). 벨트류는 1"></div>`) +
    field("Max HP", `<input id="b-f-hp" type="number" min="1" step="10" value="${b.maxHp}" title="밤 웨이브에 몬스터가 때릴 때 버티는 내구도">`) +
    field("Required Tier", `<input id="b-f-tier" type="number" min="0" value="${b.requiredCoreTier}">`) +
    costSection(b) +
    field("Hide In Menu", `<input type="checkbox" id="b-f-hide" ${b.hideFromBuildMenu?"checked":""}>`) +
    extra;

  const bindV = (id, key, num) => { const el = $("#"+id); if(el) el.oninput = e => {
    b[key] = num ? (+e.target.value||0) : e.target.value; refreshList(); }; };
  $("#b-f-kind").onchange = e => {
    b.kind = e.target.value;
    const kk = KINDS.find(x=>x.v===b.kind); if(kk) b.category = kk.cat;
    renderAll();
  };
  bindV("f-name","displayName"); bindV("f-desc","description");
  $("#b-f-pickmodel").onclick = () => { pickTarget = "model"; $("#b-file").click(); };
  const cm = $("#b-f-clearmodel");
  if(cm) cm.onclick = () => { b.model=""; b._model=null; refreshProps(); refresh3D(); };
  $("#b-f-id").oninput = e => {
    const cleaned = slug(e.target.value);            // 접두는 고정, 접미만 편집한다
    if(cleaned !== e.target.value) e.target.value = cleaned;
    b.id = cleaned ? ID_PREFIX + cleaned : "";
    b._idLocked = !!cleaned;
    refreshList();
  };
  // 이름을 처음 지을 때만 비어 있는 id 를 채워준다 — 한 번 정한 id 는 따라 바뀌지 않는다
  $("#b-f-name").addEventListener("input", () => {
    if(!b._idLocked && !b.id){
      const suf = slug(b.displayName);
      b.id = suf ? ID_PREFIX + suf : "";
      const el = $("#b-f-id"); if(el) el.value = suf;
    }
  });
  bindV("f-tier","requiredCoreTier",1); bindV("f-hp","maxHp",1);
  bindV("f-speed-mult","speedMultiplier",1); bindV("f-speed","speedTilesPerSec",1);
  document.querySelectorAll("[data-pick]").forEach(el => el.onclick = () => {
    pickTarget = el.dataset.pick; $("#b-file").click(); });
  $("#b-f-cat").onchange = e => { b.category = e.target.value; };
  // 코어 티어
  const pair = v => v.split(":").map(Number);
  const addTier = $("#btn-addtier");
  if(addTier) addTier.onclick = () => {
    b.tiers = b.tiers || [];
    b.tiers.push({ name:"새 단계", description:"", requirements:[], unlocks:[], maxHpBonus:0, isFinal:false });
    refreshProps();
  };
  document.querySelectorAll("[data-tname]").forEach(el => el.oninput = e => {
    b.tiers[+el.dataset.tname].name = e.target.value; renderWarn(); });
  document.querySelectorAll("[data-tdesc]").forEach(el => el.oninput = e => {
    b.tiers[+el.dataset.tdesc].description = e.target.value; });
  document.querySelectorAll("[data-tunlock]").forEach(el => el.oninput = e => {
    b.tiers[+el.dataset.tunlock].unlocks = e.target.value.split("\n").map(x=>x.trim()).filter(Boolean); });
  document.querySelectorAll("[data-thp]").forEach(el => el.oninput = e => {
    b.tiers[+el.dataset.thp].maxHpBonus = Math.max(0, +e.target.value||0);
    refreshProps(); });
  document.querySelectorAll("[data-tfinal]").forEach(el => el.onchange = e => {
    b.tiers.forEach((t,i) => t.isFinal = (i === +el.dataset.tfinal) ? e.target.checked : false);
    refreshProps(); });
  document.querySelectorAll("[data-tdel]").forEach(el => el.onclick = () => {
    b.tiers.splice(+el.dataset.tdel,1); refreshProps(); });
  document.querySelectorAll("[data-treqadd]").forEach(el => el.onclick = () => {
    const t = b.tiers[+el.dataset.treqadd];
    (t.requirements ||= []).push({ item: knownItems[0]?.id || "", amount: 1 });
    refreshProps(); });
  document.querySelectorAll("[data-treq]").forEach(el => el.onchange = e => {
    const [ti,ri] = pair(el.dataset.treq); b.tiers[ti].requirements[ri].item = e.target.value;
    refreshProps(); });
  document.querySelectorAll("[data-tamt]").forEach(el => el.oninput = e => {
    const [ti,ri] = pair(el.dataset.tamt);
    b.tiers[ti].requirements[ri].amount = Math.max(1, +e.target.value||1); renderWarn(); });
  document.querySelectorAll("[data-treqdel]").forEach(el => el.onclick = () => {
    const [ti,ri] = pair(el.dataset.treqdel); b.tiers[ti].requirements.splice(ri,1);
    refreshProps(); });

  document.querySelectorAll("[data-recipe]").forEach(el => el.onchange = e => {
    const id = el.dataset.recipe;
    b.availableRecipes = b.availableRecipes || [];
    if(e.target.checked){ if(!b.availableRecipes.includes(id)) b.availableRecipes.push(id); }
    else b.availableRecipes = b.availableRecipes.filter(x => x !== id);
    el.closest(".ammorow").classList.toggle("on", e.target.checked);
    renderWarn();
  });
  ["b-f-in","b-f-out","b-f-cap"].forEach((id,i) => {
    $("#"+id).oninput = e => { b[["inputSlots","outputSlots","bufferStackCap"][i]] = +e.target.value||0; renderWarn(); };
  });
  ["b-f-sx","b-f-sy"].forEach((id,i) => {
    $("#"+id).oninput = e => {
      const v = Math.max(1, Math.min(8, +e.target.value||1));
      b.size[i===0?"x":"y"] = v; renderAll();
    };
  });
  $("#b-f-hide").onchange = e => { b.hideFromBuildMenu = e.target.checked; };
  bindAutoGrow($("#b-props"));
  const addCost = $("#b-btn-addcost");
  if(addCost) addCost.onclick = () => {
    b.buildCost = b.buildCost || [];
    b.buildCost.push({ item: knownItems[0]?.id || "Item:IronPlate", amount: 1 });
    refreshProps();
  };
  document.querySelectorAll("[data-cost]").forEach(el => el.onchange = e => {
    b.buildCost[+el.dataset.cost].item = e.target.value; refreshProps(); });
  document.querySelectorAll("[data-camt]").forEach(el => el.oninput = e => {
    b.buildCost[+el.dataset.camt].amount = Math.max(1, +e.target.value||1); renderWarn(); });
  document.querySelectorAll("[data-cdel]").forEach(el => el.onclick = () => {
    b.buildCost.splice(+el.dataset.cdel,1); refreshProps(); });
}
// 코어 수리 단계 — 게이트마다 요구 부품(BOM)과 해금 내용을 갖는다
function tierSection(b){
  const tiers = b.tiers || [];
  const opts = sel => knownItems.length
    ? knownItems.map(i=>`<option value="${esc(i.id)}" ${i.id===sel?"selected":""}>${esc(i.id)}</option>`).join("")
      + (sel && !itemById(sel) ? `<option value="${esc(sel)}" selected>${esc(sel)} (알 수 없음)</option>` : "")
    : `<option value="${esc(sel||"")}" selected>${esc(sel||"(아이템 목록 없음)")}</option>`;

  const cards = tiers.map((t,ti)=>`
    <div class="tiercard ${t.isFinal?"final":""}">
      <div class="tiercard__hd">
        <span class="tiercard__n">${TIER_MARK[ti]||ti}</span>
        <input value="${esc(t.name||"")}" data-tname="${ti}" placeholder="단계 이름">
        <span class="x" data-tdel="${ti}">✕</span>
      </div>
      <div class="tiercard__bd">
        <textarea class="autogrow" rows="1" data-tdesc="${ti}" placeholder="게임에 보일 한 줄 설명">${esc(t.description||"")}</textarea>

        <div class="tiersub">요구 부품</div>
        ${(t.requirements||[]).map((r,ri)=>`
          <div class="costrow">
            <span class="bar" style="background:${LINE_COLOR[(itemById(r.item)||{}).line]||"#2E4266"}"></span>
            <select data-treq="${ti}:${ri}">${opts(r.item)}</select>
            <input type="number" min="1" value="${r.amount}" data-tamt="${ti}:${ri}">
            <span class="del" data-treqdel="${ti}:${ri}">✕</span>
          </div>`).join("") || `<div class="costempty">요구 부품 없음</div>`}
        <button class="mini" data-treqadd="${ti}">+ 부품</button>

        <div class="tiersub">해금 <span style="color:var(--faint);font-weight:400">· 확인 창에 그대로 표시된다</span></div>
        <textarea class="autogrow" rows="1" data-tunlock="${ti}" placeholder="줄바꿈으로 구분">${esc((t.unlocks||[]).join("\n"))}</textarea>
        ${(t.maxHpBonus||0) > 0 ? `<div class="tierauto">+ 코어 내구도 +${(t.maxHpBonus).toLocaleString()} <span>자동 — 아래 값에서 생성</span></div>` : ""}

        <div class="tierfoot">
          <label title="0보다 크면 해금 목록에 자동으로 한 줄 추가된다">최대 HP +<input type="number" min="0" step="100" value="${t.maxHpBonus||0}" data-thp="${ti}"></label>
          <label><input type="checkbox" data-tfinal="${ti}" ${t.isFinal?"checked":""}> 최종 단계 (경고 표시)</label>
        </div>
      </div>
    </div>`).join("");

  return `<div class="field wide tierblock">
    <label>Repair Tiers <span style="color:var(--faint)">· 게이트별 납품 목록</span></label>
    <div>${cards || `<div class="costempty">단계가 없습니다</div>`}
      <button class="mini primary" id="btn-addtier" style="margin-top:6px">+ 단계</button></div></div>`;
}

// 이 설비가 돌릴 수 있는 레시피. 재료 종류가 입력 슬롯보다 많으면 넣을 수 없다.
function recipeSection(b){
  const sel = new Set(b.availableRecipes || []);
  const rows = knownRecipes.map(r => {
    const over = r.inputs > b.inputSlots;
    return `<label class="ammorow ${sel.has(r.id)?"on":""} ${over?"over":""}" title="${over?`재료 ${r.inputs}종 > 입력 슬롯 ${b.inputSlots}`:""}">
      <input type="checkbox" data-recipe="${esc(r.id)}" ${sel.has(r.id)?"checked":""} ${over?"disabled":""}>
      <span class="bar" style="background:${over?"#3B4B66":"#4FD8E0"}"></span>
      <span class="nm">${esc(r.id)}</span>
      <span class="ty">${r.inputs}입력 · T${r.tier}</span>
    </label>`;
  }).join("");
  const unknown = [...sel].filter(id => !recipeById(id)).map(id =>
    `<label class="ammorow on"><input type="checkbox" data-recipe="${esc(id)}" checked>
      <span class="bar" style="background:#2E4266"></span><span class="nm">${esc(id)}</span>
      <span class="ty" style="color:var(--err)">없음</span></label>`).join("");
  const body = unknown + rows;
  return `<div class="field wide ammoblock">
    <label>Recipes <span style="color:var(--faint)">· 이 설비가 돌릴 레시피</span></label>
    <div><div class="ammolist">${body || `<div class="costempty">레시피 목록이 없습니다 — GameData.json 을 불러오세요</div>`}</div></div></div>`;
}

// 이 타워가 받을 수 있는 탄약/연료 목록. 비우면 아무것도 소비하지 않는다.
function ammoSection(b){
  const sel = new Set(b.ammoFilter || []);
  // 탄약 + 이미 지정된 것만
  const cand = knownItems.filter(i => i.type === "Ammo" || sel.has(i.id));
  const rows = cand.map(i => `
    <label class="ammorow ${sel.has(i.id)?"on":""}">
      <input type="checkbox" data-ammo="${esc(i.id)}" ${sel.has(i.id)?"checked":""}>
      <span class="bar" style="background:${LINE_COLOR[i.line]||"#2E4266"}"></span>
      <span class="nm">${esc(i.id)}</span>
      <span class="ty">${esc(i.type)}</span>
    </label>`).join("");
  const unknown = [...sel].filter(id => !knownItems.some(i => i.id === id))
    .map(id => `<label class="ammorow on"><input type="checkbox" data-ammo="${esc(id)}" checked>
       <span class="bar" style="background:#2E4266"></span><span class="nm">${esc(id)}</span>
       <span class="ty" style="color:var(--err)">없음</span></label>`).join("");
  return `<div class="field wide ammoblock">
    <label>Ammo Filter <span style="color:var(--faint)">· 받을 수 있는 탄약</span></label>
    <div><div class="ammolist">${unknown}${rows || `<div class="costempty">탄약이 없습니다</div>`}</div></div></div>`;
}

function modelRow(label, key, b){
  return field(label, `<div style="display:flex;gap:5px;align-items:center">
     <span style="flex:1;min-width:0;font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;
                  color:${b[key]?"var(--text)":"var(--faint)"}" class="mono">${esc(b[key] || "(없음)")}</span>
     <button class="mini" data-pick="${key}">지정</button></div>`);
}
// 내용만큼 늘어나는 textarea — 스크롤바 대신 높이가 따라온다
function autoGrow(el){
  if(!el) return;
  el.style.height = "auto";
  el.style.height = Math.max(el.scrollHeight, 30) + "px";
}
function bindAutoGrow(root){
  (root || document).querySelectorAll("textarea.autogrow").forEach(el => {
    autoGrow(el);
    el.addEventListener("input", () => autoGrow(el));
  });
}


// 건설 비용 — 배치 시 인벤토리에서 차감되고 철거 시 전액 환급된다
function costSection(b){
  const opts = sel => knownItems.length
    ? knownItems.map(i=>`<option value="${esc(i.id)}" ${i.id===sel?"selected":""}>${esc(i.id)}</option>`).join("")
      + (sel && !itemById(sel) ? `<option value="${esc(sel)}" selected>${esc(sel)} (알 수 없음)</option>` : "")
    : `<option value="${esc(sel||"")}" selected>${esc(sel||"(아이템 목록 없음)")}</option>`;
  const rows = (b.buildCost||[]).map((c,i)=>`
    <div class="costrow">
      <span class="bar" style="background:${LINE_COLOR[(itemById(c.item)||{}).line]||"#2E4266"}"></span>
      <select data-cost="${i}">${opts(c.item)}</select>
      <input type="number" min="1" value="${c.amount}" data-camt="${i}">
      <span class="del" data-cdel="${i}">✕</span>
    </div>`).join("");
  return `<div class="field wide costblock">
    <label>Build Cost <span style="color:var(--faint)">· 철거 시 전액 환급</span></label>
    <div>${rows || `<div class="costempty">비용 없음 — 무료로 지어집니다</div>`}
      <button class="mini" id="b-btn-addcost">+ 재료</button></div></div>`;
}


function renderPorts(){
  const b = cur();
  if(!b){ $("#b-ports").innerHTML = ""; return; }
  $("#b-ports").innerHTML = b.ports.map((p,i)=>`
    <div class="port ${p.isInput?"in":"out"} ${i===selPort?"sel":""}" data-i="${i}">
      <div class="hd">
        <span class="pill ${p.isInput?"in":"out"}">${p.isInput?"IN":"OUT"}</span>
        <b>(${p.x}, ${p.y})</b> <span style="color:var(--faint)">${p.dir}</span>
        <span class="x" data-del="${i}">✕</span>
      </div>
      <div class="row2" style="margin-bottom:6px">
        <input type="number" min="0" value="${p.x}" data-f="x" data-i="${i}">
        <input type="number" min="0" value="${p.y}" data-f="y" data-i="${i}">
      </div>
      <div class="dirgrid">
        <span class="sp"></span><button data-dir="North" data-i="${i}" class="${p.dir==="North"?"act":""}">N</button><span class="sp"></span>
        <button data-dir="West" data-i="${i}" class="${p.dir==="West"?"act":""}">W</button>
        <button data-flip="${i}" title="입출력 전환">⇄</button>
        <button data-dir="East" data-i="${i}" class="${p.dir==="East"?"act":""}">E</button>
        <span class="sp"></span><button data-dir="South" data-i="${i}" class="${p.dir==="South"?"act":""}">S</button><span class="sp"></span>
      </div>
    </div>`).join("") || `<div style="font-size:12px;color:var(--faint)">포트가 없습니다. 3D 격자를 클릭하거나 위 버튼으로 추가하세요.</div>`;

  const P = $("#b-ports");
  P.querySelectorAll(".port").forEach(el => el.onclick = ev => {
    if(ev.target.closest("[data-del],[data-dir],[data-flip],input")) return;
    selPort = +el.dataset.i; renderPorts(); refresh3D();
  });
  P.querySelectorAll("[data-del]").forEach(el => el.onclick = () => {
    b.ports.splice(+el.dataset.del,1); selPort = -1; renderAll();
  });
  P.querySelectorAll("[data-dir]").forEach(el => el.onclick = () => {
    b.ports[+el.dataset.i].dir = el.dataset.dir; selPort = +el.dataset.i; renderAll();
  });
  P.querySelectorAll("[data-flip]").forEach(el => el.onclick = () => {
    const p = b.ports[+el.dataset.flip]; p.isInput = !p.isInput; selPort = +el.dataset.flip; renderAll();
  });
  P.querySelectorAll("input[data-f]").forEach(el => el.oninput = () => {
    const p = b.ports[+el.dataset.i];
    p[el.dataset.f] = Math.max(0, +el.value||0);
    renderWarn(); refresh3D();
  });
}

// ═══════════════════════════ 검증 ═══════════════════════════
function validate(){
  const b = cur(); const out = [];
  if(!b) return out;
  out.push(...window.EdValid.identity({ ...b, id: bid(b) }, buildings.map(x => ({ ...x, id: bid(x) })), "building"));

  const seen = new Set();
  b.ports.forEach((p,i) => {
    const tag = `포트 ${i+1} (${p.x},${p.y},${p.dir})`;
    if(p.x >= b.size.x || p.y >= b.size.y)
      out.push(`${tag} — LocalOffset 이 풋프린트(${b.size.x}×${b.size.y}) 밖입니다`);
    const v = DVEC[p.dir], nx = p.x+v[0], ny = p.y+v[1];
    const inside = nx>=0 && ny>=0 && nx<b.size.x && ny<b.size.y;
    if(inside) out.push(`${tag} — 건물 안쪽을 향합니다. 이웃 칸이 자기 자신이라 연결되지 않습니다`);
    const key = `${p.x},${p.y},${p.dir}`;
    if(seen.has(key)) out.push(`${tag} — 같은 칸·같은 방향에 포트가 중복됩니다`);
    seen.add(key);
  });

  const nIn  = b.ports.filter(p=>p.isInput).length;
  const nOut = b.ports.length - nIn;
  // 물류(벨트·분배·합류)는 통과만 하므로 출력 버퍼를 두지 않는다
  const PASS_THROUGH = ["Belt","Splitter","Merger"];
  if(nIn > 0 && b.inputSlots  < 1) out.push("입력 포트가 있는데 inputSlots 가 0 입니다");
  if(nOut > 0 && b.outputSlots < 1 && !PASS_THROUGH.includes(b.kind))
    out.push("출력 포트가 있는데 outputSlots 가 0 입니다");
  if(PASS_THROUGH.includes(b.kind)){
    if(b.outputSlots !== 0) out.push(`${b.kind} 은 통과형이라 outputSlots 가 0 이어야 합니다`);
    if(b.bufferStackCap !== 1) out.push(`${b.kind} 은 한 번에 하나만 물므로 bufferStackCap 이 1 이어야 합니다`);
  }
  if(b.kind === "Miner" && b.inputSlots !== 0) out.push("채굴기는 입력 슬롯이 0 이어야 합니다");
  if(b.kind === "Belt" && (nIn !== 1 || nOut !== 1))
    out.push("벨트는 입력 1 · 출력 1 이어야 합니다 (모양별 포트는 런타임에 BuildPorts 가 계산)");
  if(b.kind === "Miner" && nIn > 0) out.push("채굴기는 입력 포트를 갖지 않습니다");
  if(b.kind === "Assembler"){
    const rs = b.availableRecipes || [];
    if(rs.length === 0) out.push("돌릴 레시피가 없습니다 — Recipes 에서 하나 이상 고르세요");
    rs.forEach(id => {
      const r = recipeById(id);
      if(knownRecipes.length && !r) out.push(`Recipes — 레시피 id "${id}" 를 찾을 수 없습니다`);
      else if(r && r.inputs > b.inputSlots)
        out.push(`레시피 ${id} 는 재료 ${r.inputs}종인데 입력 슬롯이 ${b.inputSlots}개입니다`);
    });
  }
  const costSeen = new Set();
  (b.buildCost||[]).forEach((c,i) => {
    if(!c.item) out.push(`건설 비용 ${i+1} — 아이템이 비어 있습니다`);
    else if(knownItems.length && !itemById(c.item))
      out.push(`건설 비용 — 아이템 id "${c.item}" 을 찾을 수 없습니다`);
    if(c.amount < 1) out.push(`건설 비용 — 수량은 1 이상이어야 합니다`);
    if(costSeen.has(c.item)) out.push(`건설 비용 — 같은 아이템이 중복됩니다 (${c.item})`);
    costSeen.add(c.item);
  });
  if((b.buildCost||[]).length === 0 && !b.hideFromBuildMenu)
    out.push("건설 비용이 없습니다 — 무료 건물이 의도한 것인지 확인하세요");
  if(!(b.maxHp > 0)) out.push("Max HP 는 1 이상이어야 합니다 — 밤 웨이브에 즉시 파괴됩니다");
  if(b.kind === "Miner" && (!(b.speedMultiplier > 0)))
    out.push("Speed Multiplier 는 0보다 커야 합니다 (채굴 시간 = 광맥 extractInterval ÷ 배율)");
  const NEEDS_PORT = ["Miner","Assembler","Belt","Splitter","Merger","Storage","Core","DronePort"];
  if(b.ports.length === 0 && NEEDS_PORT.includes(b.kind) && !b.hideFromBuildMenu)
    out.push("포트가 하나도 없습니다 — 벨트·기계와 연결될 수 없습니다");
  if(b.kind === "DronePort"){
    if(!(b.droneRange > 0))    out.push("Range 는 0보다 커야 합니다 — 짝지을 스테이션을 찾지 못합니다");
    if(!(b.carryCapacity > 0)) out.push("Carry 는 1 이상이어야 합니다");
    if(!(b.travelSpeed > 0))   out.push("Speed 는 0보다 커야 합니다");
    if(b.ports.filter(p=>p.isInput).length === 0)  out.push("입력 포트가 없습니다 — 보낼 물건을 받을 수 없습니다");
    if(b.ports.filter(p=>!p.isInput).length === 0) out.push("출력 포트가 없습니다 — 받은 물건을 내보낼 수 없습니다");
  }
  if(b.kind === "Core"){
    const ts = b.tiers || [];
    if(ts.length === 0) out.push("수리 단계가 없습니다 — 게이트를 하나 이상 만드세요");
    if(ts.filter(t=>t.isFinal).length > 1) out.push("최종 단계는 하나만 지정할 수 있습니다");
    if(ts.length && !ts.some(t=>t.isFinal)) out.push("최종 단계가 지정되지 않았습니다 — 엔딩 조건이 없습니다");
    ts.forEach((t,i) => {
      const tag = `${TIER_MARK[i]||i} ${t.name||"이름 없음"}`;
      if(!t.name) out.push(`${tag} — 단계 이름이 비어 있습니다`);
      const reqs = t.requirements || [];
      if(reqs.length === 0) out.push(`${tag} — 요구 부품이 없습니다`);
      const seen2 = new Set();
      reqs.forEach(r => {
        if(!r.item) out.push(`${tag} — 부품이 비어 있습니다`);
        else if(knownItems.length && !itemById(r.item)) out.push(`${tag} — 아이템 id "${r.item}" 을 찾을 수 없습니다`);
        if(r.amount < 1) out.push(`${tag} — 수량은 1 이상이어야 합니다`);
        if(seen2.has(r.item)) out.push(`${tag} — 같은 부품이 중복됩니다`);
        seen2.add(r.item);
      });
      if((t.unlocks||[]).length === 0) out.push(`${tag} — 해금 내용이 없습니다 (확인 창이 비어 보입니다)`);
    });
  }
  if(b.kind === "Tower"){
    const atk = b.damageMultiplier > 0;
    if(!(b.range >= 0))   out.push("Range 는 0 이상이어야 합니다");
    if(!(b.fireRate > 0)) out.push("Fire Rate 는 0보다 커야 합니다");
    const ammo = b.ammoFilter || [];
    if(atk && ammo.length === 0)
      out.push("공격 타워인데 받을 수 있는 탄약이 없습니다 — Ammo Filter 를 지정하세요 (피해 = 탄약 피해 × 배수)");
    // 건설 비용에 그 탄약이 들어 있으면 "설치할 때 장전되는 일회용"으로 본다 (지뢰)
    const oneShot = ammo.length > 0 && ammo.every(id => (b.buildCost||[]).some(c => c.item === id));
    if(ammo.length && !oneShot && b.ports.filter(p=>p.isInput).length === 0)
      out.push("탄약을 쓰는데 입력 포트가 없습니다 — 벨트로 보급하거나 건설 비용에 포함하세요");
    ammo.forEach(id => { if(knownItems.length && !itemById(id))
      out.push(`Ammo Filter — 아이템 id "${id}" 을 찾을 수 없습니다`); });
  }
  return out;
}
function renderWarn(){
  const ws = validate();
  $("#b-warn").innerHTML = ws.length
    ? "<h3>검증</h3>" + ws.map(w=>`<div class="w">${esc(w)}</div>`).join("")
    : `<div class="okmsg">✓ 검증 통과</div>`;
  const b = cur();
  $("#b-stat").textContent = b
    ? `건물 ${buildings.length} · 포트 ${b.ports.length} (입력 ${b.ports.filter(p=>p.isInput).length} / 출력 ${b.ports.filter(p=>!p.isInput).length})`
    : `건물 ${buildings.length}`;
}
function renderAll(){ renderList(); renderProps(); renderPorts(); renderWarn(); refresh3D(); }
let flashT = null;
function flash(msg){
  const el = $("#b-hint"); const old = el.innerHTML;
  el.innerHTML = `<span style="color:var(--err)">${esc(msg)}</span>`;
  clearTimeout(flashT); flashT = setTimeout(()=>{ el.innerHTML = old; }, 1800);
}

// ═══════════════════════════ 모델 로딩 ═══════════════════════════
let pickTarget = "model";   // "model" | "modelCurveL" | "modelCurveR"
function loadModelFile(file){
  const b = cur(); if(!b) return;
  if(pickTarget !== "model"){          // 커브는 파일명만 기록 (미리보기는 본체 모델만)
    b[pickTarget] = file.name;
    pickTarget = "model"; refreshProps(); return;
  }
  const ext = file.name.split(".").pop().toLowerCase();
  const done = obj => {
    let meshes = 0;
    // 미리보기는 형태만 본다 — 텍스처가 빠져 검게 나오는 걸 막으려고 재질을 통일한다
    const preview = new THREE.MeshStandardMaterial({ color:0x9FB2CC, metalness:.15, roughness:.65 });
    obj.traverse(o => {
      if(o.isMesh){ meshes++; o.material = preview; }
    });
    if(meshes === 0){                     // 빈 씬을 내보낸 경우가 잦다
      flash(`${file.name} 에 메시가 없습니다 — 내보낼 때 오브젝트가 선택됐는지 확인하세요`);
      return;
    }
    b._model = obj; b.model = file.name;
    refreshProps(); refresh3D();
  };
  const fail = e => {
    console.error("[model]", e);
    const m = (e && e.message) || "";
    // three 의 FBX 파서는 Objects/Connections 가 비면 forEach 에서 죽는다 = 빈 씬
    const msg = /forEach|Connections|undefined/.test(m)
      ? `${file.name} 을 읽지 못했습니다 — 모델이 들어 있지 않거나 지원하지 않는 형식입니다`
      : `불러오지 못했습니다 — ${m || ext}`;
    flash(msg);
  };

  // blob URL 대신 파일을 직접 읽어 parse 한다.
  // FBX 는 로더가 확장자로 형식을 판단하는데 blob URL 에는 확장자가 없어 실패하는 경우가 있다.
  const reader = new FileReader();
  reader.onerror = () => flash("파일을 읽지 못했습니다");
  reader.onload = () => {
    try {
      if(ext === "fbx"){
        done(new FBXLoader().parse(reader.result, ""));
      } else if(ext === "obj"){
        done(new OBJLoader().parse(reader.result));
      } else if(ext === "glb" || ext === "gltf"){
        new GLTFLoader().parse(reader.result, "", g => done(g.scene), fail);
      } else {
        flash("지원하지 않는 형식입니다 (.glb/.gltf/.obj/.fbx)");
      }
    } catch(e){ fail(e); }
  };
  if(ext === "obj") reader.readAsText(file);
  else if(ext === "gltf") reader.readAsText(file);   // .gltf 는 JSON — parse 가 문자열도 받는다
  else reader.readAsArrayBuffer(file);
}
$("#b-btn-model").onclick = () => { pickTarget = "model"; $("#b-file").click(); };
$("#b-file").onchange = e => { if(e.target.files[0]) loadModelFile(e.target.files[0]); e.target.value = ""; };
$("#b-btn-clearmodel").onclick = () => { const b = cur(); if(b){ b._model = null; b.model = ""; refreshProps(); refresh3D(); } };

const dropEl = $("#b-drop"), centerEl = document.getElementById("b-center");
["dragenter","dragover"].forEach(t => centerEl.addEventListener(t, e => { e.preventDefault(); dropEl.classList.add("on"); }));
["dragleave","drop"].forEach(t => centerEl.addEventListener(t, e => { e.preventDefault(); dropEl.classList.remove("on"); }));
centerEl.addEventListener("drop", e => { const f = e.dataTransfer.files[0]; if(f) loadModelFile(f); });

// ═══════════════════════════ 툴바 ═══════════════════════════
$("#b-btn-add").onclick = () => { buildings.push(newBuilding()); curIdx = buildings.length-1; selPort=-1; rot=0; setRotButtons(); renderAll(); };
$("#b-btn-del").onclick = () => {
  if(!buildings.length) return;
  buildings.splice(curIdx,1);
  if(!buildings.length) buildings.push(newBuilding());
  curIdx = Math.max(0, curIdx-1); selPort = -1; renderAll();
};
$("#b-btn-addin").onclick  = () => addFirstFree(true);
$("#b-btn-addout").onclick = () => addFirstFree(false);
function addFirstFree(isInput){
  const b = cur(); if(!b) return;
  for(let x=0;x<b.size.x;x++) for(let y=0;y<b.size.y;y++)
    if(outwardDir(b,x,y)){ addPortAt(x,y,isInput); return; }
  flash("빈 바깥 방향이 없습니다");
}
document.querySelectorAll("#b-rotbar button").forEach(el => el.onclick = () => {
  rot = +el.dataset.rot; setRotButtons(); selPort = -1; renderPorts(); refresh3D();
});
function setRotButtons(){
  document.querySelectorAll("#b-rotbar button").forEach(el =>
    el.classList.toggle("act", +el.dataset.rot === rot));
}

// ═══════════════════════════ JSON ═══════════════════════════
function exportJson(){
  return { buildings: buildings.map(b => {
    const o = {
      id: bid(b), kind: b.kind, displayName: b.displayName, description: b.description||"",
      category: b.category, model: b.model||"",
      size: { x:b.size.x, y:b.size.y },
      ports: b.ports.map(p => ({ x:p.x, y:p.y, dir:p.dir, isInput:!!p.isInput })),
      buildCost: (b.buildCost||[]).map(c => ({ item:c.item, amount:c.amount })),
      inputSlots: b.inputSlots, outputSlots: b.outputSlots, bufferStackCap: b.bufferStackCap,
      maxHp: b.maxHp, requiredCoreTier: b.requiredCoreTier, hideFromBuildMenu: !!b.hideFromBuildMenu,
    };
    if(b.kind === "Miner")     o.speedMultiplier  = b.speedMultiplier;
    if(b.kind === "Belt")     { o.speedTilesPerSec = b.speedTilesPerSec;
                                if(b.modelCurveL) o.modelCurveL = b.modelCurveL;
                                if(b.modelCurveR) o.modelCurveR = b.modelCurveR; }
    if(b.kind === "Assembler") o.availableRecipes = b.availableRecipes||[];
    if(b.kind === "DronePort"){ o.droneRange = b.droneRange; o.carryCapacity = b.carryCapacity;
                                o.travelSpeed = b.travelSpeed; }
    if(b.kind === "Core")      o.tiers = (b.tiers||[]).map(t => ({
      name:t.name||"", description:t.description||"",
      requirements:(t.requirements||[]).map(r=>({item:r.item, amount:r.amount})),
      unlocks:t.unlocks||[], maxHpBonus:t.maxHpBonus||0, isFinal:!!t.isFinal }));
    if(b.kind === "Tower")    { o.damageMultiplier = b.damageMultiplier; o.range = b.range;
                                o.fireRate = b.fireRate; o.ammoFilter = b.ammoFilter || []; }
    return o;
  })};
}
function importJson(obj){
  if(Array.isArray(obj.recipes) && obj.recipes.length)
    knownRecipes = obj.recipes.map(r => ({ id:r.id, displayName:r.displayName||r.id,
                                           inputs:(r.inputs||[]).length, tier:r.tier ?? 0 }));
  if(Array.isArray(obj.items) && obj.items.length)
    knownItems = obj.items.map(i => ({ id:i.id, displayName:i.displayName||i.id, line:i.line||"None", type:i.type||"" }));
  const list = (obj.buildings||[]).map(o => {
    const b = newBuilding(o.kind || "Miner");
    Object.assign(b, {
      id: o.id || "", _idLocked: !!o.id,
      displayName: o.displayName || (o.id||"").replace(/^Building:/,""),
      description: o.description||"", category: o.category||b.category, model: o.model||o.prefab||"",
      size: { x:(o.size?.x)||1, y:(o.size?.y)||1 },
      ports: (o.ports||[]).map(p => ({ x:p.x|0, y:p.y|0, dir:p.dir||"East", isInput:!!p.isInput })),
      inputSlots: o.inputSlots ?? 1, outputSlots: o.outputSlots ?? 1, bufferStackCap: o.bufferStackCap ?? 0,
      maxHp: o.maxHp ?? 200, requiredCoreTier: o.requiredCoreTier ?? 0, hideFromBuildMenu: !!o.hideFromBuildMenu,
      buildCost: (o.buildCost||[]).map(c => ({ item:c.item, amount:c.amount||1 })),
      speedMultiplier: o.speedMultiplier ?? 1, speedTilesPerSec: o.speedTilesPerSec ?? 2,
      damageMultiplier: o.damageMultiplier ?? 1, range: o.range ?? 8, fireRate: o.fireRate ?? 1,
      ammoFilter: o.ammoFilter || (o.ammoItem ? [o.ammoItem] : []),
      droneRange: o.droneRange ?? 40, carryCapacity: o.carryCapacity ?? 20, travelSpeed: o.travelSpeed ?? 8,
      tiers: (o.tiers||[]).map(t => ({ name:t.name||"", description:t.description||"",
        requirements:(t.requirements||[]).map(r=>({item:r.item, amount:r.amount||1})),
        unlocks:t.unlocks||[], maxHpBonus:t.maxHpBonus||0, isFinal:!!t.isFinal })),
      availableRecipes: o.availableRecipes || [],
    });
    if(o.modelCurveL || o.curveLPrefab) b.modelCurveL = o.modelCurveL || o.curveLPrefab;
    if(o.modelCurveR || o.curveRPrefab) b.modelCurveR = o.modelCurveR || o.curveRPrefab;
    return b;
  });
  if(list.length){ buildings = list; curIdx = 0; selPort = -1; rot = 0; setRotButtons(); renderAll(); }
}
const modal = $("#b-modal");
$("#b-btn-export").onclick = () => {
  $("#b-modal-title").textContent = "JSON 내보내기 — buildings";
  $("#b-modal-text").value = JSON.stringify(exportJson(), null, 2);
  $("#b-modal-apply").style.display = "none"; $("#b-modal-msg").textContent = "";
  modal.classList.add("on");
};
$("#b-btn-import").onclick = () => {
  $("#b-modal-title").textContent = "JSON 불러오기 — buildings 배열이 있는 JSON";
  $("#b-modal-text").value = ""; $("#b-modal-apply").style.display = ""; $("#b-modal-msg").textContent = "";
  modal.classList.add("on");
};
$("#b-modal-apply").onclick = () => {
  try { importJson(JSON.parse($("#b-modal-text").value)); modal.classList.remove("on"); }
  catch(e){ $("#b-modal-msg").textContent = "JSON 파싱 실패: " + e.message; }
};
$("#b-modal-copy").onclick = async () => {
  try { await navigator.clipboard.writeText($("#b-modal-text").value); $("#b-modal-msg").textContent = "복사됨"; }
  catch { $("#b-modal-text").select(); document.execCommand("copy"); $("#b-modal-msg").textContent = "복사됨"; }
};
$("#b-modal-dl").onclick = () => {
  const blob = new Blob([$("#b-modal-text").value], {type:"application/json"});
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob); a.download = "BuildingData.json"; a.click();
  URL.revokeObjectURL(a.href);
};
$("#b-modal-close").onclick = () => modal.classList.remove("on");

// ═══════════════════════════ 초기 데이터 ═══════════════════════════
// 저장소의 프리팹·SO 주석에 나온 구성을 그대로 옮긴 것
importJson({"buildings": [{"id": "Building:Miner", "kind": "Miner", "displayName": "채굴기", "description": "광맥 위에 놓아 자원을 캔다. 광맥이 어려울수록 느리다.", "category": "Production", "model": "", "size": {"x": 1, "y": 1}, "ports": [{"x": 0, "y": 0, "dir": "East", "isInput": false}], "buildCost": [{"item": "Item:IronPlate", "amount": 6}, {"item": "Item:IronGear", "amount": 4}], "inputSlots": 0, "outputSlots": 1, "bufferStackCap": 10, "maxHp": 150, "requiredCoreTier": 1, "hideFromBuildMenu": false, "speedMultiplier": 1}, {"id": "Building:MinerMk2", "kind": "Miner", "displayName": "채굴기 Mk.2", "description": "두 배 빠르게 캔다. 광맥이 감당하는 만큼만 나온다.", "category": "Production", "model": "", "size": {"x": 2, "y": 2}, "ports": [{"x": 1, "y": 0, "dir": "East", "isInput": false}], "buildCost": [{"item": "Item:IronPlate", "amount": 12}, {"item": "Item:IronGear", "amount": 8}, {"item": "Item:CircuitBoard", "amount": 4}], "inputSlots": 0, "outputSlots": 1, "bufferStackCap": 20, "maxHp": 220, "requiredCoreTier": 2, "hideFromBuildMenu": false, "speedMultiplier": 2}, {"id": "Building:Smelter", "kind": "Assembler", "displayName": "제련로", "description": "원광을 녹여 소재로 만든다.", "category": "Production", "model": "", "size": {"x": 1, "y": 1}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}, {"x": 0, "y": 0, "dir": "East", "isInput": false}], "buildCost": [{"item": "Item:IronPlate", "amount": 5}, {"item": "Item:IronGear", "amount": 3}], "inputSlots": 1, "outputSlots": 1, "bufferStackCap": 10, "maxHp": 180, "requiredCoreTier": 1, "hideFromBuildMenu": false, "availableRecipes": ["Recipe:Recipe_IronIngot", "Recipe:Recipe_CopperIngot", "Recipe:Recipe_RefinedCrystal"]}, {"id": "Building:Constructor", "kind": "Assembler", "displayName": "제작기", "description": "소재 한 종을 가공해 기본 부품을 만든다.", "category": "Production", "model": "", "size": {"x": 1, "y": 1}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}, {"x": 0, "y": 0, "dir": "East", "isInput": false}], "buildCost": [{"item": "Item:IronPlate", "amount": 6}, {"item": "Item:IronGear", "amount": 4}], "inputSlots": 1, "outputSlots": 1, "bufferStackCap": 10, "maxHp": 180, "requiredCoreTier": 1, "hideFromBuildMenu": false, "availableRecipes": ["Recipe:Recipe_IronPlate", "Recipe:Recipe_IronGear", "Recipe:Recipe_BasicAmmo", "Recipe:Recipe_CopperWire", "Recipe:Recipe_EnergyCellAmmo"]}, {"id": "Building:Assembler", "kind": "Assembler", "displayName": "조립기", "description": "부품 두 종을 합쳐 조립 부품을 만든다.", "category": "Production", "model": "", "size": {"x": 2, "y": 2}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}, {"x": 0, "y": 1, "dir": "West", "isInput": true}, {"x": 1, "y": 0, "dir": "East", "isInput": false}], "buildCost": [{"item": "Item:IronPlate", "amount": 10}, {"item": "Item:IronGear", "amount": 6}, {"item": "Item:CopperWire", "amount": 8}], "inputSlots": 2, "outputSlots": 1, "bufferStackCap": 10, "maxHp": 250, "requiredCoreTier": 2, "hideFromBuildMenu": false, "availableRecipes": ["Recipe:Recipe_CircuitBoard", "Recipe:Recipe_CrystalAmmo", "Recipe:Recipe_DenseAmmo", "Recipe:Recipe_EnergyCell", "Recipe:Recipe_Grenade", "Recipe:Recipe_GrenadeLauncher", "Recipe:Recipe_HullPanel", "Recipe:Recipe_NavControlUnit", "Recipe:Recipe_Pistol", "Recipe:Recipe_Rifle", "Recipe:Recipe_Shotgun", "Recipe:Recipe_Sniper"]}, {"id": "Building:Manufacturer", "kind": "Assembler", "displayName": "제조기", "description": "부품 네 종을 한 번에 조립한다. 동력 모듈 전용.", "category": "Production", "model": "", "size": {"x": 3, "y": 3}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}, {"x": 0, "y": 1, "dir": "West", "isInput": true}, {"x": 0, "y": 2, "dir": "West", "isInput": true}, {"x": 1, "y": 2, "dir": "North", "isInput": true}, {"x": 2, "y": 1, "dir": "East", "isInput": false}], "buildCost": [{"item": "Item:HullPanel", "amount": 6}, {"item": "Item:CircuitBoard", "amount": 8}, {"item": "Item:CopperWire", "amount": 20}, {"item": "Item:IronPlate", "amount": 15}], "inputSlots": 4, "outputSlots": 1, "bufferStackCap": 10, "maxHp": 320, "requiredCoreTier": 3, "hideFromBuildMenu": false, "availableRecipes": ["Recipe:Recipe_PowerModule", "Recipe:Recipe_ControlModule", "Recipe:Recipe_EnergyRifle"]}, {"id": "Building:DronePort", "kind": "DronePort", "displayName": "드론 스테이션", "description": "벨트 없이 다른 스테이션으로 물건을 실어 나른다. 멀리 떨어진 광맥을 잇는 용도.", "category": "Logistics", "model": "", "size": {"x": 2, "y": 2}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}, {"x": 1, "y": 1, "dir": "East", "isInput": false}], "buildCost": [{"item": "Item:ControlModule", "amount": 3}, {"item": "Item:IronPlate", "amount": 16}], "inputSlots": 1, "outputSlots": 1, "bufferStackCap": 30, "maxHp": 260, "requiredCoreTier": 3, "hideFromBuildMenu": false, "droneRange": 60, "carryCapacity": 20, "travelSpeed": 8}, {"id": "Building:Belt", "kind": "Belt", "displayName": "컨베이어 벨트", "description": "설비 사이로 아이템을 옮긴다. 배치 중 T로 모양을 바꾼다.", "category": "Logistics", "model": "", "size": {"x": 1, "y": 1}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}, {"x": 0, "y": 0, "dir": "East", "isInput": false}], "buildCost": [{"item": "Item:IronPlate", "amount": 2}, {"item": "Item:IronGear", "amount": 1}], "inputSlots": 1, "outputSlots": 0, "bufferStackCap": 1, "maxHp": 60, "requiredCoreTier": 1, "hideFromBuildMenu": false, "speedTilesPerSec": 2}, {"id": "Building:BeltMk2", "kind": "Belt", "displayName": "컨베이어 벨트 Mk.2", "description": "두 배 빠른 벨트.", "category": "Logistics", "model": "", "size": {"x": 1, "y": 1}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}, {"x": 0, "y": 0, "dir": "East", "isInput": false}], "buildCost": [{"item": "Item:IronPlate", "amount": 3}, {"item": "Item:CopperWire", "amount": 2}], "inputSlots": 1, "outputSlots": 0, "bufferStackCap": 1, "maxHp": 60, "requiredCoreTier": 2, "hideFromBuildMenu": false, "speedTilesPerSec": 4}, {"id": "Building:Splitter", "kind": "Splitter", "displayName": "분배기", "description": "입력 하나를 여러 출구로 나눈다. 출구별 필터를 걸 수 있다.", "category": "Logistics", "model": "", "size": {"x": 1, "y": 1}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}, {"x": 0, "y": 0, "dir": "North", "isInput": false}, {"x": 0, "y": 0, "dir": "East", "isInput": false}, {"x": 0, "y": 0, "dir": "South", "isInput": false}], "buildCost": [{"item": "Item:IronPlate", "amount": 3}, {"item": "Item:IronGear", "amount": 2}], "inputSlots": 1, "outputSlots": 0, "bufferStackCap": 1, "maxHp": 80, "requiredCoreTier": 1, "hideFromBuildMenu": false}, {"id": "Building:Merger", "kind": "Merger", "displayName": "합류기", "description": "여러 입력을 하나로 모은다.", "category": "Logistics", "model": "", "size": {"x": 1, "y": 1}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}, {"x": 0, "y": 0, "dir": "North", "isInput": true}, {"x": 0, "y": 0, "dir": "South", "isInput": true}, {"x": 0, "y": 0, "dir": "East", "isInput": false}], "buildCost": [{"item": "Item:IronPlate", "amount": 3}, {"item": "Item:IronGear", "amount": 2}], "inputSlots": 1, "outputSlots": 0, "bufferStackCap": 1, "maxHp": 80, "requiredCoreTier": 1, "hideFromBuildMenu": false}, {"id": "Building:Storage", "kind": "Storage", "displayName": "보관소", "description": "아이템을 보관한다. 어느 면이든 왼쪽으로 받고 오른쪽으로 내보낸다.", "category": "Storage", "model": "", "size": {"x": 2, "y": 2}, "ports": [{"x": 0, "y": 1, "dir": "West", "isInput": true}, {"x": 0, "y": 0, "dir": "West", "isInput": false}, {"x": 1, "y": 1, "dir": "North", "isInput": true}, {"x": 0, "y": 1, "dir": "North", "isInput": false}, {"x": 1, "y": 0, "dir": "East", "isInput": true}, {"x": 1, "y": 1, "dir": "East", "isInput": false}, {"x": 0, "y": 0, "dir": "South", "isInput": true}, {"x": 1, "y": 0, "dir": "South", "isInput": false}], "buildCost": [{"item": "Item:IronPlate", "amount": 10}, {"item": "Item:IronGear", "amount": 4}], "inputSlots": 12, "outputSlots": 1, "bufferStackCap": 0, "maxHp": 300, "requiredCoreTier": 1, "hideFromBuildMenu": false}, {"id": "Building:Core", "kind": "Core", "displayName": "코어", "description": "불시착한 우주선. 어느 면으로든 부품을 받아 수리한다.", "category": "Production", "model": "", "size": {"x": 3, "y": 3}, "ports": [{"x": 0, "y": 2, "dir": "North", "isInput": true}, {"x": 0, "y": 0, "dir": "South", "isInput": true}, {"x": 1, "y": 2, "dir": "North", "isInput": true}, {"x": 1, "y": 0, "dir": "South", "isInput": true}, {"x": 2, "y": 2, "dir": "North", "isInput": true}, {"x": 2, "y": 0, "dir": "South", "isInput": true}, {"x": 0, "y": 0, "dir": "West", "isInput": true}, {"x": 2, "y": 0, "dir": "East", "isInput": true}, {"x": 0, "y": 1, "dir": "West", "isInput": true}, {"x": 2, "y": 1, "dir": "East", "isInput": true}, {"x": 0, "y": 2, "dir": "West", "isInput": true}, {"x": 2, "y": 2, "dir": "East", "isInput": true}], "buildCost": [], "inputSlots": 4, "outputSlots": 0, "bufferStackCap": 0, "maxHp": 5000, "requiredCoreTier": 0, "hideFromBuildMenu": true, "tiers": [{"name": "잔해 회수", "description": "흩어진 파편을 모아 코어를 깨운다.", "requirements": [{"item": "Item:ShipDebris", "amount": 12}], "unlocks": ["작업대 — 손으로 제련·제작", "채굴기 · 제련로 · 컨베이어 벨트"], "maxHpBonus": 0, "isFinal": false}, {"name": "선체 봉합", "description": "찢어진 동체를 덮어 내부 정비 구역을 연다.", "requirements": [{"item": "Item:HullPanel", "amount": 20}, {"item": "Item:IronPlate", "amount": 40}, {"item": "Item:IronGear", "amount": 30}], "unlocks": ["조립기 — 부품 2종을 합쳐 조립", "채굴기 Mk.2 · 벨트 Mk.2", "중기관 포탑 · 박격포 타워"], "maxHpBonus": 2000, "isFinal": false}, {"name": "항법·제어 복구", "description": "조종 콘솔과 선내 배선을 되살린다.", "requirements": [{"item": "Item:NavControlUnit", "amount": 8}, {"item": "Item:CircuitBoard", "amount": 20}, {"item": "Item:CopperWire", "amount": 50}], "unlocks": ["레이더 — 다음 웨이브의 진입 방위와 규모", "제조기 — 부품 4종을 한 번에 조립", "레이저 타워 · 감속 필드 타워"], "maxHpBonus": 1500, "isFinal": false}, {"name": "동력 재점화", "description": "엔진에 동력 모듈을 장착한다.", "requirements": [{"item": "Item:PowerModule", "amount": 3}, {"item": "Item:EnergyCell", "amount": 15}, {"item": "Item:CopperWire", "amount": 30}, {"item": "Item:HullPanel", "amount": 10}], "unlocks": ["엔진 예열 개시", "이륙 시퀀스"], "maxHpBonus": 1500, "isFinal": true}]}, {"id": "Building:Fence", "kind": "Tower", "displayName": "울타리", "description": "몬스터의 진로를 막아 동선을 유도한다. 공격하지 않는다.", "category": "Defense", "model": "", "size": {"x": 1, "y": 1}, "ports": [], "buildCost": [{"item": "Item:IronPlate", "amount": 2}], "inputSlots": 0, "outputSlots": 0, "bufferStackCap": 0, "maxHp": 250, "requiredCoreTier": 1, "hideFromBuildMenu": false, "damageMultiplier": 0, "range": 0, "fireRate": 0, "ammoFilter": [], "fireMode": "None"}, {"id": "Building:Mine", "kind": "Tower", "displayName": "지뢰", "description": "밟으면 터진다. 건설 비용의 유탄이 그대로 폭약이며 한 번 쓰면 사라진다.", "category": "Defense", "model": "", "size": {"x": 1, "y": 1}, "ports": [], "buildCost": [{"item": "Item:Grenade", "amount": 1}], "inputSlots": 0, "outputSlots": 0, "bufferStackCap": 0, "maxHp": 10, "requiredCoreTier": 2, "hideFromBuildMenu": false, "damageMultiplier": 6, "range": 2, "fireRate": 1, "ammoFilter": ["Item:Grenade"]}, {"id": "Building:BasicTurret", "kind": "Tower", "displayName": "기본 포탑", "description": "기본 탄약을 소비해 사격한다. 벨트로 보급할 수 있다.", "category": "Defense", "model": "", "size": {"x": 1, "y": 1}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}], "buildCost": [{"item": "Item:IronPlate", "amount": 8}, {"item": "Item:IronGear", "amount": 6}], "inputSlots": 1, "outputSlots": 0, "bufferStackCap": 20, "maxHp": 200, "requiredCoreTier": 1, "hideFromBuildMenu": false, "damageMultiplier": 1, "range": 9, "fireRate": 2, "ammoFilter": ["Item:BasicAmmo", "Item:DenseAmmo", "Item:CrystalAmmo"], "fireMode": "Projectile", "defaultAmmo": "Item:BasicAmmo"}, {"id": "Building:HeavyTurret", "kind": "Tower", "displayName": "중기관 포탑", "description": "고밀도 탄약을 쓰는 연사 포탑.", "category": "Defense", "model": "", "size": {"x": 2, "y": 2}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}], "buildCost": [{"item": "Item:IronPlate", "amount": 14}, {"item": "Item:IronGear", "amount": 10}, {"item": "Item:CircuitBoard", "amount": 4}], "inputSlots": 1, "outputSlots": 0, "bufferStackCap": 30, "maxHp": 320, "requiredCoreTier": 2, "hideFromBuildMenu": false, "damageMultiplier": 2.2, "range": 11, "fireRate": 4, "ammoFilter": ["Item:DenseAmmo", "Item:CrystalAmmo"], "fireMode": "Projectile", "defaultAmmo": "Item:DenseAmmo"}, {"id": "Building:MortarTower", "kind": "Tower", "displayName": "박격포 타워", "description": "유탄을 곡사로 날려 범위 피해를 준다. 근접한 적은 때리지 못한다.", "category": "Defense", "model": "", "size": {"x": 2, "y": 2}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}], "buildCost": [{"item": "Item:IronPlate", "amount": 16}, {"item": "Item:IronGear", "amount": 12}, {"item": "Item:CircuitBoard", "amount": 6}], "inputSlots": 1, "outputSlots": 0, "bufferStackCap": 20, "maxHp": 300, "requiredCoreTier": 2, "hideFromBuildMenu": false, "damageMultiplier": 3.5, "range": 18, "fireRate": 0.5, "ammoFilter": ["Item:Grenade"], "fireMode": "Projectile", "preferHighArc": true, "defaultAmmo": "Item:Grenade", "muzzleHeight": 1.8, "minRange": 5}, {"id": "Building:LaserTower", "kind": "Tower", "displayName": "레이저 타워", "description": "에너지 셀 탄을 소비해 관통 사격한다.", "category": "Defense", "model": "", "size": {"x": 2, "y": 2}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}], "buildCost": [{"item": "Item:ControlModule", "amount": 2}, {"item": "Item:RefinedCrystal", "amount": 4}, {"item": "Item:IronPlate", "amount": 12}], "inputSlots": 1, "outputSlots": 0, "bufferStackCap": 20, "maxHp": 280, "requiredCoreTier": 3, "hideFromBuildMenu": false, "damageMultiplier": 2.8, "range": 14, "fireRate": 3, "ammoFilter": ["Item:EnergyCellAmmo"], "fireMode": "Hitscan", "defaultAmmo": "Item:EnergyCellAmmo"}, {"id": "Building:SlowFieldTower", "kind": "Tower", "displayName": "감속 필드 타워", "description": "범위 안의 몬스터를 느리게 만든다. 5초마다 에너지 셀 1개를 태운다.", "category": "Defense", "model": "", "size": {"x": 1, "y": 1}, "ports": [{"x": 0, "y": 0, "dir": "West", "isInput": true}], "buildCost": [{"item": "Item:ControlModule", "amount": 2}, {"item": "Item:CopperWire", "amount": 10}], "inputSlots": 1, "outputSlots": 0, "bufferStackCap": 10, "maxHp": 180, "requiredCoreTier": 3, "hideFromBuildMenu": false, "damageMultiplier": 0, "range": 7, "fireRate": 0.2, "ammoFilter": ["Item:EnergyCellAmmo"], "fireMode": "Aura", "defaultAmmo": "Item:EnergyCellAmmo"}]});
syncItems();
window.GameData.onGraphChange = () => {
  syncItems();
  const pane = document.getElementById("pane-building");
  if(!pane || !pane.classList.contains("on")) return;      // 안 보이면 나중에 탭 전환 때 갱신된다
  if(document.activeElement && document.activeElement.closest("#b-props")) return;  // 편집 중이면 건드리지 않는다
  refreshProps();
};
window.BuildingEditor = {
  getBuildings: () => exportJson().buildings,
  loadBuildings: obj => { syncItems(); importJson(obj); },
  refresh: () => { syncItems(); renderAll(); resize(); },
};
resize();

})();
