import * as THREE from 'three';
import { CFG } from './config.js';
import { instantiate } from './assets.js';

// 헤딩 (그리드 방향). 항상 이 상수 객체로 비교합니다.
export const N = { x: 0, z: -1 }, E = { x: 1, z: 0 }, W = { x: -1, z: 0 }, S = { x: 0, z: 1 };
const canon = v => [N, E, W, S].find(h => Math.abs(h.x - v.x) < 1e-6 && Math.abs(h.z - v.z) < 1e-6) || v;
export const leftOf = h => canon({ x: h.z, z: -h.x });
export const rightOf = h => canon({ x: -h.z, z: h.x });
export const yawOf = h => Math.atan2(-h.x, -h.z);
const UP = new THREE.Vector3(0, 1, 0);

// 랜덤 S자 컨베이어 경로. 조각 타입: S 직선, L/R 곡선, X 분배기(경로 위), B 분기 벨트, C 우주선(경로 밖)
export class BeltPath {
  constructor(tiles, TILE) {
    this.tiles = tiles; this.TILE = TILE; this.R = TILE / 2;
    this.group = new THREE.Group(); this.group.rotation.y = CFG.beltYaw;
    this.pieces = [];
    this.gen = { e: { x: 0, z: 0 }, h: N, s: 0, plan: [], lat: 0 };
  }
  toWorld(p) { return p.applyAxisAngle(UP, this.group.rotation.y); }
  toLocal(p) { return p.applyAxisAngle(UP, -this.group.rotation.y); }

  // 다음 계획: 직진 몇 칸 → 옆으로 한 번 꺾고 0~1칸 → 복귀. lat로 좌우 치우침 보정
  nextPlan() {
    const g = this.gen;
    if (g.plan.length === 0) {
      const k = 2 + Math.floor(Math.random() * 3);
      for (let i = 0; i < k; i++) g.plan.push('S');
      const side = Math.random() < 0.62 - g.lat * 0.35 ? 'R' : 'L';
      const run = Math.random() < 0.5 ? 0 : 1;
      g.lat += (side === 'R' ? 1 : -1) * (run + 1);
      g.plan.push(side, ...Array(run).fill('S'), side === 'L' ? 'R' : 'L');
    }
    return g.plan.shift();
  }

  addPiece() {
    const type = this.nextPlan(), g = this.gen, { h, e } = g, T = this.TILE, R = this.R;
    const { obj, mixer } = instantiate(this.tiles[type]);
    obj.rotation.y = yawOf(h);
    obj.position.set(e.x + h.x * T / 2, 0, e.z + h.z * T / 2);
    let hOut = h, len = T, exit = { x: e.x + h.x * T, z: e.z + h.z * T };
    if (type !== 'S') {
      hOut = type === 'L' ? leftOf(h) : rightOf(h);
      len = Math.PI * R / 2;
      exit = { x: e.x + (h.x + hOut.x) * R, z: e.z + (h.z + hOut.z) * R };
    }
    this.group.add(obj);
    this.pieces.push({ type, e, h, hOut, s0: g.s, len, obj, mixer });
    g.s += len; g.e = exit; g.h = hOut;
  }
  ensure(s) { while (this.gen.s < s) this.addPiece(); }

  // 경로 뒤쪽 정리. keep(piece) → true면 유지
  prune(minS, keep) {
    for (let i = this.pieces.length - 1; i >= 0; i--) {
      const q = this.pieces[i];
      if (keep?.(q)) continue;
      if (q.s0 + q.len < minS) { this.group.remove(q.obj); this.pieces.splice(i, 1); }
    }
  }
  update(dt) { for (const p of this.pieces) p.mixer?.update(dt); }

  onPath(q) { return q.type !== 'B' && q.type !== 'C'; }
  // 경로 거리 s → 월드 좌표 (y=0)
  pointAt(s) {
    const on = this.pieces.filter(q => this.onPath(q));
    const p = on.find(q => s >= q.s0 && s < q.s0 + q.len) || on.at(-1);
    const t = THREE.MathUtils.clamp((s - p.s0) / p.len, 0, 1), R = this.R;
    if (p.type === 'S' || p.type === 'X') return this.toWorld(new THREE.Vector3(p.e.x + p.h.x * this.TILE * t, 0, p.e.z + p.h.z * this.TILE * t));
    const cx = p.e.x + p.hOut.x * R, cz = p.e.z + p.hOut.z * R, th = t * Math.PI / 2;
    return this.toWorld(new THREE.Vector3(cx + R * (-p.hOut.x * Math.cos(th) + p.h.x * Math.sin(th)), 0, cz + R * (-p.hOut.z * Math.cos(th) + p.h.z * Math.sin(th))));
  }

  // 경로 거리 fromS 이후를 잘라내고 그 지점부터 다시 생성 (진행방향이 N이 아니면 먼저 복귀 회전)
  truncateFrom(index) {
    const first = this.pieces[index];
    this.pieces.splice(index).forEach(q => this.group.remove(q.obj));
    this.gen = { e: first.e, h: first.h, s: first.s0, plan: this.returnPlan(first.h), lat: 0 };
  }
  returnPlan(h) { return h === N ? [] : [h === E ? 'L' : 'R']; }
  // 랜덤 패턴 재개 (옆으로 가던 중이면 북쪽 복귀 먼저)
  resumeRandom() { this.gen.plan = this.returnPlan(this.gen.h); this.gen.lat = 0; }
  forceStraight(n) { this.gen.plan.push(...Array(n).fill('S')); }

  // 직선 조각 → 분배기로 교체
  toSplitter(p) {
    this.group.remove(p.obj); p.mixer = null;
    const { obj } = instantiate(this.tiles.X);
    obj.position.copy(p.obj.position); obj.rotation.y = p.obj.rotation.y;
    this.group.add(obj); p.obj = obj; p.type = 'X';
  }
  // 경로 밖 조각 등록 (분기 벨트, 우주선)
  addAside(type, obj, mixer, s0) {
    this.group.add(obj);
    this.pieces.push({ type, e: null, h: null, hOut: null, s0, len: this.TILE, obj, mixer });
  }
}
