import * as THREE from 'three';
import { CFG } from './config.js';

// 벨트 위 정육면체 아이템들. 링크(버튼 연결) 색상 관리 포함.
export class Items {
  constructor(scene, TILE, beltTop) {
    this.scene = scene; this.TILE = TILE; this.y = beltTop + TILE * CFG.itemSize / 2;
    this.geo = new THREE.BoxGeometry(TILE * CFG.itemSize, TILE * CFG.itemSize, TILE * CFG.itemSize);
    this.list = [];
  }
  spawn(s) {
    const mat = new THREE.MeshStandardMaterial({ color: CFG.neutral, roughness: .45, emissive: '#000000', emissiveIntensity: .6 });
    const mesh = new THREE.Mesh(this.geo, mat); mesh.castShadow = true;
    this.scene.add(mesh);
    const it = { s, mesh, mat, link: -1, dim: false };
    this.list.push(it); return it;
  }
  remove(it) { this.scene.remove(it.mesh); const i = this.list.indexOf(it); if (i >= 0) this.list.splice(i, 1); }
  clear() { this.list.forEach(x => this.scene.remove(x.mesh)); this.list.length = 0; }
  // 한 줄 일정 간격 배치 (앵커 anchorS 근처 3개가 버튼과 연결됨)
  seedRow(anchorS) { for (let k = -5; k <= 9; k++) this.spawn(anchorS + this.TILE * (-0.6 + k * CFG.itemGap)); }
  near(anchorS) { return this.list.filter(x => x.s > anchorS - this.TILE * 0.9 && x.s < anchorS + this.TILE * 1.9); }

  place(it, p, q) {
    it.mesh.position.set(p.x, this.y, p.z);
    if (q) it.mesh.rotation.y = Math.atan2(-(q.x - p.x), -(q.z - p.z));
  }
  paint(it, color) { it.mat.color.set(color); it.mat.emissive.set(color === CFG.neutral ? '#000000' : '#1b3a44'); }
  resetAll() { this.list.forEach(x => { x.link = -1; x.dim = false; this.paint(x, CFG.neutral); }); }
}
