import * as THREE from 'three';
import { CFG } from './config.js';
import { loadAssets, instantiate } from './assets.js';
import { BeltPath, N, rightOf, yawOf } from './path.js';
import { Items } from './items.js';
import { Wires } from './wires.js';
import { glitchOut, glitchIn, bindGlitchEnd, isAnimating, initSettingsPanel } from './menu.js';

// ── 렌더러 / 씬 ───────────────────────────────────────
const renderer = new THREE.WebGLRenderer({ canvas: document.getElementById('gl'), antialias: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping; renderer.toneMappingExposure = 1.1;
renderer.shadowMap.enabled = true;

const scene = new THREE.Scene();
scene.background = new THREE.Color(CFG.bg);
const camera = new THREE.PerspectiveCamera(38, 1, 0.1, 200);
scene.add(new THREE.HemisphereLight('#dff3ff', '#2a3a5c', 2.6), new THREE.AmbientLight('#8fb8d8', 0.6));
const sun = new THREE.DirectionalLight('#fff4e2', 3.6);
sun.castShadow = true; sun.shadow.mapSize.set(2048, 2048); sun.shadow.bias = -0.0005;
scene.add(sun, sun.target);

// ── 에셋 / 월드 ───────────────────────────────────────
const loadUI = { fill: document.getElementById('loadFill'), pct: document.getElementById('loadPct'), msg: document.getElementById('loadMsg') };
const { tiles, core: coreShip, TILE, beltTop } = await loadAssets((done, total, label) => {
  const p = Math.round(done / total * 100);
  loadUI.fill.style.width = p + '%'; loadUI.pct.textContent = p + '%'; loadUI.msg.textContent = label.toUpperCase();
});
loadUI.msg.textContent = 'READY';
await new Promise(r => setTimeout(r, 300));
scene.fog = new THREE.Fog(CFG.bg, TILE * 11, TILE * 24);
const path = new BeltPath(tiles, TILE); scene.add(path.group);
const items = new Items(scene, TILE, beltTop);

// ── DOM ───────────────────────────────────────────────
const $ = s => document.querySelector(s);
const btns = [...document.querySelectorAll('#ui .btn')];      // 게임 시작 / 설정 / 종료
const subBtns = [...document.querySelectorAll('#ui2 .btn')];  // 새 게임 / 불러오기 / 뒤로가기
const panel = $('#panel'), fadeEl = $('#fade');
const wires = new Wires($('#wires'), btns.length);
initSettingsPanel(panel);

// ── 상태 ──────────────────────────────────────────────
const links = btns.map(() => null);   // 버튼 i ↔ 아이템
const ui = { settings: false, busy: false };
const launch = { active: false, arrived: false, docked: false, returning: false, zoom: 1, item: null, piece: null, center: null, dir: null, len: 0, coreMixer: null, anchors: null, anchorsSorted: null };
let camS = TILE * 6;                  // 카메라 앵커의 경로 거리
const camPos = new THREE.Vector3(), camTgt = new THREE.Vector3();
let inited = false;

function resize() {
  renderer.setSize(innerWidth, innerHeight, false);
  camera.aspect = innerWidth / innerHeight; camera.updateProjectionMatrix();
  wires.resize(innerWidth, innerHeight);
}
addEventListener('resize', resize); resize();

// ── 프레임 ────────────────────────────────────────────
const clock = new THREE.Clock();
function frame() {
  const dt = Math.min(clock.getDelta(), 0.05), time = clock.elapsedTime;
  const speedK = launch.active ? CFG.launchSpeed : 1;
  const v = CFG.beltSpeed * TILE * speedK;
  const paused = launch.arrived;      // 도킹 중엔 벨트 흐름 정지
  if (!paused) camS += v * dt;

  // 경로
  path.ensure(camS + TILE * 22);
  path.prune(camS - TILE * 6, q => (launch.active || launch.docked) && 'BCX'.includes(q.type));
  path.update(dt * CFG.beltAnimSpeed * speedK);
  launch.coreMixer?.update(dt);

  // 아이템
  if (items.list.length === 0) items.seedRow(camS);
  for (const it of [...items.list]) {
    if (it.branch) {                  // 분기 벨트 위 (분배기 → 우주선)
      it.bs = Math.min(it.bs + v * dt, launch.len);
      items.place(it, path.toWorld(launch.center.clone().addScaledVector(launch.dir, it.bs)));
      if (it.bs >= launch.len && !launch.arrived) onArrive();
      continue;
    }
    if (!paused) it.s += v * dt;
    if (it.s > camS + TILE * 12 || it.s < camS - TILE * 8) { items.remove(it); continue; }
    if (launch.active && it === launch.item && it.s >= launch.piece.s0 + launch.piece.len / 2) { it.branch = true; it.bs = 0; continue; }
    items.place(it, path.pointAt(it.s), path.pointAt(it.s + 0.02));
  }

  // 버튼 ↔ 아이템 링크
  const dimOthers = ui.settings || (ui.busy && !launch.active) || launch.active;
  for (let i = 0; i < links.length; i++) {
    const it = links[i];
    if (it && !items.list.includes(it)) { items.paint(it, CFG.neutral); it.link = -1; links[i] = null; continue; }
    if (it && i !== (launch.active ? 0 : 1) && it.dim !== dimOthers) { it.dim = dimOthers; items.paint(it, dimOthers ? CFG.neutral : CFG.linked); }
    if (!it) {
      const cand = items.near(camS).filter(x => x.link < 0).sort((a, b) => b.s - a.s)[0];
      if (cand) { cand.link = i; items.paint(cand, CFG.linked); links[i] = cand; }
    }
  }
  // 앞선 아이템 = 위쪽 버튼 (선 교차 방지)
  const sorted = links.filter(Boolean).sort((a, b) => b.s - a.s);
  sorted.forEach((l, i) => { links[i] = l; l.link = i; });
  for (let i = sorted.length; i < links.length; i++) links[i] = null;

  // 카메라
  const focus = launch.active ? [launch.item] : links.filter(Boolean);
  const look = launch.arrived && launch.anchors ? launch.anchors[1].clone().setY(0)
    : focus.length ? focus.reduce((a, x) => a.add(x.mesh.position), new THREE.Vector3()).divideScalar(focus.length).setY(0)
    : path.pointAt(camS + TILE * 0.6);
  launch.zoom += ((launch.arrived ? CFG.dockZoom : launch.active ? CFG.launchZoom : 1) - launch.zoom) * Math.min(1, dt * 1.2);
  const dist = launch.active ? launch.zoom : 1 + CFG.dolly * Math.sin(time * Math.PI * 2 / CFG.dollyPeriod);
  look.x += CFG.lookShift * TILE * dist;
  const want = look.clone().add(new THREE.Vector3(...CFG.camOffset).multiplyScalar(TILE * dist));
  if (!inited) { camPos.copy(want); camTgt.copy(look); inited = true; }
  const k = 1 - Math.exp(-dt * 2.2);
  camPos.lerp(want, k); camTgt.lerp(look, k);
  camera.position.copy(camPos); camera.lookAt(camTgt);
  sun.position.copy(camTgt).add(new THREE.Vector3(6, 12, 4).multiplyScalar(TILE * .6)); sun.target.position.copy(camTgt);
  const sc = sun.shadow.camera, ext = TILE * 9; sc.left = -ext; sc.right = ext; sc.top = ext; sc.bottom = -ext; sc.updateProjectionMatrix();

  renderer.render(scene, camera);
  drawWires(dt, time);
  requestAnimationFrame(frame);
}

// 선: 상태별로 어느 DOM 앵커 → 어느 3D 지점을 잇는지 결정
function drawWires(dt, time) {
  wires.list.forEach((w, i) => {
    if (launch.docked) {              // 서브 메뉴 → 우주선
      launch.anchorsSorted ??= [...launch.anchors].sort((a, b) => b.clone().project(camera).y - a.clone().project(camera).y);
      const b = subBtns[i];
      w.target = (b.classList.contains('in') || b.dataset.shown) && !b.classList.contains('out') ? 1 : 0;
      return wires.draw(w, i, launch.anchorsSorted[i], b, camera, dt, time);
    }
    const it = launch.active ? (i === 0 ? launch.item : null) : links[i];
    let el = btns[i];
    if (ui.settings || (ui.busy && !launch.active && btns[0].classList.contains('out'))) {
      const usePanel = i === 1 && ui.settings && !isAnimating(panel);   // 설정 아이템 → 패널
      if (usePanel) { wires.rebind(w, panel); el = panel; }
      w.target = usePanel ? 1 : 0;
    } else {
      if (i === 1 && w.anchorEl === panel) wires.rebind(w, btns[1]);
      w.target = launch.arrived ? 0 : launch.active ? (i === 0 ? 1 : 0) : it && !isAnimating(btns[i]) ? 1 : 0;
    }
    if (!it && w.draw < 0.01) return wires.hide(w);
    const endPt = it ? it.mesh.position : w.lastEnd;
    if (!endPt) return wires.hide(w);
    if (it) w.lastEnd = it.mesh.position.clone();
    wires.draw(w, i, endPt, el, camera, dt, time);
  });
}

// ── 게임 시작 시퀀스 ──────────────────────────────────
// 화면 밖 첫 조각부터 북쪽 직진으로 재생성 → 분배기 설치 → 오른쪽 분기 벨트 → 우주선 착륙
function startGame() {
  if (launch.active || !links[0] || ui.settings || ui.busy) return;
  const it = links[0];
  camera.updateMatrixWorld(true);
  const offscreen = q => [path.pointAt(q.s0 + 0.001), path.pointAt(q.s0 + q.len - 0.001)]
    .every(p => { const v = p.setY(beltTop).project(camera); return Math.abs(v.x) > 1.15 || Math.abs(v.y) > 1.15; });
  const pcs = path.pieces;
  const cut = pcs.findIndex((q, idx) => q.s0 >= it.s + TILE * 1.2 && pcs.slice(idx).every(r => r.s0 < q.s0 || offscreen(r)));
  const cutS = cut >= 0 ? pcs[cut].s0 : it.s + TILE * 5;
  if (cut >= 0) path.truncateFrom(cut);
  path.forceStraight(16);
  path.ensure(it.s + TILE * 16);
  const p = pcs.find(q => q.type === 'S' && q.h === N && q.s0 >= cutS);
  if (!p) return;

  Object.assign(launch, { active: true, item: it, piece: p });
  document.body.classList.add('launching');
  btns[0].style.transition = 'opacity .6s'; btns[0].style.opacity = '.35';
  glitchOut(btns.slice(1));

  // 분배기 + 분기 벨트
  path.toSplitter(p);
  const c = { x: p.e.x + p.h.x * TILE / 2, z: p.e.z + p.h.z * TILE / 2 }, d = rightOf(p.h);
  launch.center = new THREE.Vector3(c.x, 0, c.z); launch.dir = new THREE.Vector3(d.x, 0, d.z);
  const n = CFG.branchTiles;
  for (let k = 1; k <= n; k++) {
    const { obj, mixer } = instantiate(tiles.S);
    obj.position.set(c.x + d.x * TILE * k, 0, c.z + d.z * TILE * k); obj.rotation.y = yawOf(d);
    path.addAside('B', obj, mixer, p.s0);
  }
  launch.len = TILE * (n + 0.8);

  // 우주선: 착륙 포즈 기준으로 스케일·위치 정렬 후 착륙 애니메이션
  const ce = new THREE.Vector3(c.x + d.x * TILE * (n + 1.6), 0, c.z + d.z * TILE * (n + 1.6));
  coreShip.position.copy(ce); coreShip.rotation.y = yawOf(d) + Math.PI; coreShip.scale.setScalar(1);
  path.addAside('C', coreShip, null, p.s0);
  const clips = coreShip.userData.clips;
  if (clips.length) {
    launch.coreMixer = new THREE.AnimationMixer(coreShip);
    const land = clips.find(x => /land/i.test(x.name)) || clips[0];
    const a = launch.coreMixer.clipAction(land); a.setLoop(THREE.LoopOnce); a.clampWhenFinished = true; a.play();
    launch.coreMixer.setTime(land.duration - 0.01);
    const bbox = () => { path.group.updateMatrixWorld(true); coreShip.traverse(o => { if (o.isSkinnedMesh) o.boundingBox = null; }); return new THREE.Box3().setFromObject(coreShip); };
    let bb = bbox(); const sz = bb.getSize(new THREE.Vector3());
    coreShip.scale.setScalar(TILE * CFG.coreTiles / Math.max(sz.x, sz.z));
    bb = bbox(); const cc = bb.getCenter(new THREE.Vector3()), target = path.toWorld(ce.clone());
    const off = path.toLocal(new THREE.Vector3(target.x - cc.x, 0, target.z - cc.z));
    coreShip.position.set(ce.x + off.x, coreShip.position.y - bb.min.y, ce.z + off.z);
    launch.coreMixer.setTime(0);
  }
}

function onArrive() {
  launch.arrived = true;
  launch.item.mesh.visible = false;
  glitchOut([btns[0]]);
  // 우주선 위 3개 앵커 (긴 축 기준 앞/중앙/뒤)
  path.group.updateMatrixWorld(true);
  coreShip.traverse(o => { if (o.isSkinnedMesh) o.boundingBox = null; });
  const bb = new THREE.Box3().setFromObject(coreShip), c = bb.getCenter(new THREE.Vector3()), sz = bb.getSize(new THREE.Vector3());
  const ax = sz.x > sz.z ? new THREE.Vector3(1, 0, 0) : new THREE.Vector3(0, 0, 1);
  launch.anchors = [-0.3, 0, 0.3].map(k => c.clone().addScaledVector(ax, k * Math.max(sz.x, sz.z)).setY(bb.max.y * 0.85));
  setTimeout(() => { launch.docked = true; wires.reset(); document.body.classList.add('docked'); glitchIn(subBtns); }, 700);
}

// 뒤로가기: 서브 메뉴 소멸 → 카메라를 아이템 흐름으로 복귀 → 메인 메뉴 등장. 분기/우주선은 뒤로 흘러가며 정리
function goBack() {
  if (launch.returning) return;
  launch.returning = true;
  subBtns.forEach(b => delete b.dataset.shown);
  glitchOut(subBtns);
  setTimeout(() => {
    Object.assign(launch, { docked: false, arrived: false, active: false, anchorsSorted: null, coreMixer: null, piece: null });
    document.body.classList.remove('docked', 'launching');
    subBtns.forEach(b => b.classList.remove('out'));
    if (launch.item) { items.remove(launch.item); launch.item = null; }
    path.pieces.forEach(q => { if (q.type === 'B' || q.type === 'C') q.s0 = camS; });
    const rest = items.list.filter(x => !x.branch).sort((a, b) => a.s - b.s);
    if (rest.length) camS = rest[Math.floor(rest.length / 2)].s - TILE * 0.5;
    items.resetAll(); links.fill(null);
    if (items.near(camS).length < 3) { items.clear(); items.seedRow(camS); }
    path.resumeRandom(); path.ensure(camS + TILE * 22);
    wires.reset();
    btns[0].style.opacity = ''; btns[0].style.transition = '';
    glitchIn(btns, 140, 200);
    launch.returning = false;
  }, 900);
}

function takeoff() {
  const clips = coreShip.userData.clips, off = clips.find(x => /take/i.test(x.name));
  if (launch.coreMixer && off) { launch.coreMixer.stopAllAction(); const a = launch.coreMixer.clipAction(off); a.setLoop(THREE.LoopOnce); a.clampWhenFinished = true; a.play(); }
  glitchOut(subBtns);
  setTimeout(() => { fadeEl.style.transition = `opacity ${CFG.takeoffFadeMs}ms`; fadeEl.style.opacity = 1; }, CFG.takeoffFadeDelay);
  console.log('new game');
}

// ── 설정 ──────────────────────────────────────────────
function openSettings() {
  if (launch.active || ui.settings || ui.busy) return;
  ui.busy = true; glitchOut(btns);
  setTimeout(() => { ui.settings = true; document.body.classList.add('settings'); glitchIn([panel]); ui.busy = false; }, 700);
}
function closeSettings() {
  if (!ui.settings || ui.busy) return;
  ui.busy = true; glitchOut([panel]);
  setTimeout(() => { ui.settings = false; document.body.classList.remove('settings'); glitchIn(btns); ui.busy = false; }, 700);
}

// ── 이벤트 ────────────────────────────────────────────
bindGlitchEnd(btns); bindGlitchEnd([panel]);
bindGlitchEnd(subBtns, b => { b.dataset.shown = '1'; });
btns[0].addEventListener('click', startGame);
btns[1].addEventListener('click', openSettings);
btns[2].addEventListener('click', () => console.log('quit'));
subBtns[0].addEventListener('click', takeoff);
subBtns[1].addEventListener('click', () => console.log('load game'));
subBtns[2].addEventListener('click', goBack);
$('#settingsBack').addEventListener('click', closeSettings);

$('#load').classList.add('hide');
frame();
