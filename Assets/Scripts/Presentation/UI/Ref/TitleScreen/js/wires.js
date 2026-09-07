import * as THREE from 'three';
import { CFG } from './config.js';

const NS = 'http://www.w3.org/2000/svg';
const tmp = new THREE.Vector3();

// 버튼(DOM) ↔ 3D 지점 사이의 점선. draw(0..1)로 그려지고 지워지는 진행률을 가짐.
export class Wires {
  constructor(svg, count) {
    this.svg = svg;
    this.list = Array.from({ length: count }, (_, i) => {
      const c = CFG.lineColors[i];
      const g = document.createElementNS(NS, 'g');
      const path = document.createElementNS(NS, 'path');
      Object.entries({ fill: 'none', stroke: c, 'stroke-width': 1.5, 'stroke-dasharray': '6 4' }).forEach(([k, v]) => path.setAttribute(k, v));
      // 타겟 레티클: 네 모서리 브래킷 + 중심 십자 틱
      const ret = document.createElementNS(NS, 'g');
      const corners = document.createElementNS(NS, 'path');
      Object.entries({ fill: 'none', stroke: c, 'stroke-width': 1.5, 'stroke-linecap': 'square' }).forEach(([k, v]) => corners.setAttribute(k, v));
      const tick = document.createElementNS(NS, 'path');
      Object.entries({ fill: 'none', stroke: c, 'stroke-width': 1, opacity: .8 }).forEach(([k, v]) => tick.setAttribute(k, v));
      ret.append(corners, tick);
      g.append(path, ret); svg.append(g);
      return { g, path, ret, corners, tick, draw: 0, target: 0, anchor: null, anchorEl: null, lastEnd: null };
    });
  }
  resize(w, h) { this.svg.setAttribute('viewBox', `0 0 ${w} ${h}`); this.svg.setAttribute('width', w); this.svg.setAttribute('height', h); }
  hide(w) { w.g.style.display = 'none'; }
  // 앵커 엘리먼트를 바꾸면 처음부터 다시 그려짐
  rebind(w, el) { if (w.anchorEl !== el) { w.draw = 0; w.anchor = null; w.anchorEl = el; } }
  reset() { this.list.forEach(w => { w.draw = 0; w.target = 0; }); }

  // endPt: 월드 좌표, el: 시작 앵커 DOM. 글리치 애니메이션 중엔 마지막 정상 앵커 유지.
  draw(w, i, endPt, el, camera, dt, time) {
    const animating = el.classList.contains('out') || el.classList.contains('in');
    const r = el.getBoundingClientRect();
    if (!animating && r.width > 40 && r.height > 20) { w.anchor = { x: r.right, y: r.top + r.height / 2 }; w.anchorEl = el; }
    if (!w.anchor) return this.hide(w);
    const { x: bx, y: by } = w.anchor;
    tmp.copy(endPt).project(camera);
    const x = (tmp.x + 1) / 2 * innerWidth, y = (1 - tmp.y) / 2 * innerHeight;
    const kx = bx + Math.max(60, (x - bx) * 0.45);
    // 레티클 먼저: 소멸은 레티클이 사라진 뒤 선이 줄어들고, 등장은 선이 도착한 뒤 레티클
    const tr = w.target > 0.5 ? (w.draw > 0.97 ? 1 : 0) : 0;
    w.retA = (w.retA ?? 0) + (tr - (w.retA ?? 0)) * Math.min(1, dt * 10);
    const holdLine = w.target < 0.5 && w.retA > 0.05;   // 레티클이 아직 보이뱴 선 유지
    if (!holdLine) w.draw += (w.target - w.draw) * Math.min(1, dt * 6);
    const t = THREE.MathUtils.clamp(w.draw, 0, 1);
    const L1 = Math.abs(kx - bx), L2 = Math.hypot(x - kx, y - by), L = L1 + L2;
    let d, ex, ey;
    if (t * L <= L1) { ex = bx + Math.sign(kx - bx) * t * L; ey = by; d = `M${bx} ${by} L${ex} ${ey}`; }
    else { const u = (t * L - L1) / L2; ex = kx + (x - kx) * u; ey = by + (y - by) * u; d = `M${bx} ${by} H${kx} L${ex} ${ey}`; }
    w.g.style.display = t < 0.01 ? 'none' : '';
    w.g.style.opacity = Math.min(1, t * 3);
    w.path.setAttribute('d', d); w.path.setAttribute('stroke-dashoffset', -(time * 30) % 10);
    // 레티클: 등장 시 큰 사각에서 조여들며 나타나고, 천천히 호흡. 선 끝은 레티클 왼쪽 가장자리에 닿음
    const s = (13 + Math.sin(time * 1.6 + i) * 1.2) * (1 + (1 - w.retA) * 1.6), a = s * 0.42;
    const cs = `M${-s} ${-s + a} V${-s} H${-s + a} M${s - a} ${-s} H${s} V${-s + a} M${s} ${s - a} V${s} H${s - a} M${-s + a} ${s} H${-s} V${s - a}`;
    w.corners.setAttribute('d', cs);
    w.tick.setAttribute('d', `M-3 0 H3 M0 -3 V3`);
    w.ret.setAttribute('transform', `translate(${ex} ${ey})`);
    // 레티클: 등장 시 선이 도착한 뒤 조여들며 나타나고, 소멸 시 선보다 먼저 사라짐
    w.ret.style.opacity = w.retA;
  }
}
