import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { CFG, ASSETS } from './config.js';

const nearest = t => { t.colorSpace = THREE.SRGBColorSpace; t.magFilter = t.minFilter = THREE.NearestFilter; t.generateMipmaps = false; t.flipY = false; t.offset.x = -0.05; return t; };

// FactoryColor 셰이더그래프: Palette.png(4×4)를 UV로 샘플 → BaseColor. 알파>0 셀만 Emission.
async function makeFactoryMaterial() {
  const bmp = await createImageBitmap(await (await fetch(ASSETS.palette)).blob(), { premultiplyAlpha: 'none', colorSpaceConversion: 'none' });
  const base = nearest(new THREE.Texture(bmp)); base.premultiplyAlpha = false; base.needsUpdate = true;
  // 알파 마스크 → emissive 텍스처 (2D 캔버스가 알파 0 픽셀을 검정으로 만듦)
  const c = document.createElement('canvas'); c.width = bmp.width; c.height = bmp.height;
  const g = c.getContext('2d', { willReadFrequently: true }); g.drawImage(bmp, 0, 0);
  const d = g.getImageData(0, 0, c.width, c.height);
  for (let i = 3; i < d.data.length; i += 4) d.data[i] = 255;
  g.putImageData(d, 0, 0);
  const emis = nearest(new THREE.CanvasTexture(c));
  return new THREE.MeshStandardMaterial({ name: 'FactoryColor', map: base, metalness: 0.5, roughness: 0.2, emissive: '#ffffff', emissiveMap: emis, emissiveIntensity: 3.5 });
}

// Glass 셰이더그래프: Fresnel(power 5)로 Tint→Rim, 알파 0.35→0.95
function makeGlassMaterial() {
  const m = new THREE.MeshStandardMaterial({ name: 'Glass', color: '#d5beaa', metalness: 0, roughness: 0.08, transparent: true, depthWrite: false, side: THREE.DoubleSide });
  m.onBeforeCompile = sh => {
    Object.assign(sh.uniforms, { uTint: { value: new THREE.Color(0.835, 0.745, 0.667) }, uRim: { value: new THREE.Color(1.6, 1.35, 1.1) }, uAlphaC: { value: 0.35 }, uAlphaR: { value: 0.95 }, uPow: { value: 5.0 } });
    sh.fragmentShader = sh.fragmentShader
      .replace('#include <common>', '#include <common>\nuniform vec3 uTint, uRim; uniform float uAlphaC, uAlphaR, uPow;')
      .replace('#include <dithering_fragment>', '#include <dithering_fragment>\n{ vec3 V = normalize(vViewPosition); float f = pow(1.0 - clamp(dot(normalize(vNormal), V), 0.0, 1.0), uPow); gl_FragColor.rgb += f * (uRim - uTint) * 0.6; gl_FragColor.a = mix(uAlphaC, uAlphaR, f); }');
  };
  return m;
}

const loader = new GLTFLoader();
// 진행률 콜백: onProgress(done, total, label)
let progress = { done: 0, total: 0, cb: null };
const tick = label => { progress.done++; progress.cb?.(progress.done, progress.total, label); };
const loadGLB = url => new Promise((res, rej) => loader.load(url, g => { g.scene.userData.clips = g.animations; tick(url.split('/').pop()); res(g.scene); }, undefined, rej));

// 타일 프로토: 바닥 y=0, 원점 = 타일 중심. {proto, size, clips}
function prepTile(m, yaw, mat) {
  m.rotation.y = yaw; m.updateMatrixWorld(true);
  const bb = new THREE.Box3().setFromObject(m, true);
  m.position.y = -bb.min.y;
  m.traverse(o => { if (o.isMesh) { o.castShadow = o.receiveShadow = true; o.material = mat; } });
  const wrap = new THREE.Group(); wrap.add(m);
  return { proto: wrap, size: bb.getSize(new THREE.Vector3()), clips: m.userData.clips || [] };
}

export async function loadAssets(onProgress) {
  progress = { done: 0, total: 6, cb: onProgress };
  const [factoryMat, belt, curveL, curveR, splitter, core] = await Promise.all([
    makeFactoryMaterial().then(m => { tick('Palette.png'); return m; }), loadGLB(ASSETS.belt), loadGLB(ASSETS.curveL), loadGLB(ASSETS.curveR), loadGLB(ASSETS.splitter), loadGLB(ASSETS.core),
  ]);
  const tiles = {
    S: prepTile(belt, CFG.modelYaw.S, factoryMat),
    L: prepTile(curveL, CFG.modelYaw.L, factoryMat),
    R: prepTile(curveR, CFG.modelYaw.R, factoryMat),
    X: prepTile(splitter, CFG.modelYaw.X, factoryMat),
  };
  // 우주선: 스킨 메시라 clone 불가 → 단일 인스턴스. 창(Windows)만 Glass.
  const glassMat = makeGlassMaterial();
  core.traverse(o => {
    if (!o.isMesh) return;
    const glass = /^windows?$|glass/i.test(o.name) || /glass/i.test(o.material?.name || '');
    o.material = glass ? glassMat : factoryMat; o.receiveShadow = true; o.castShadow = !glass; if (glass) o.renderOrder = 2; o.frustumCulled = false;
  });
  const coreGroup = new THREE.Group(); coreGroup.add(core); coreGroup.userData.clips = core.userData.clips || [];
  const TILE = Math.max(tiles.S.size.x, tiles.S.size.z);
  return { tiles, core: coreGroup, TILE, beltTop: tiles.S.size.y };
}

// 타일 인스턴스 생성 (모프 애니메이션 포함)
export function instantiate(tile, speedRef) {
  const obj = tile.proto.clone();
  obj.traverse(o => { if (o.isMesh && o.morphTargetInfluences) o.morphTargetInfluences = [...o.morphTargetInfluences]; });
  let mixer = null;
  if (tile.clips.length) { mixer = new THREE.AnimationMixer(obj); tile.clips.forEach(c => mixer.clipAction(c).play()); mixer.setTime(Math.random() * 3); }
  return { obj, mixer };
}
