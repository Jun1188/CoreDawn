// 조정값 — 숫자만 바꿔서 느낌을 튠할 수 있습니다.
export const CFG = {
  // 벨트 / 아이템
  beltSpeed: 1.0,        // 아이템 속도 (타일/초) — 벨트 애니메이션과 동기
  beltAnimSpeed: 2.0,    // 벨트 모프 애니메이션 재생 배속
  itemSize: 0.2,         // 정육면체 크기 (타일 대비)
  itemGap: 1.1,          // 아이템 간격 (타일)
  beltYaw: Math.PI / 9,  // 벨트 전체 기울기 (왼쪽 20°)

  // 카메라
  camOffset: [-0.3, 5.2, 3.2], // 타일 단위, 시선 목표 기준 오프셋
  lookShift: -1.3,       // 시선 목표를 왼쪽으로 밀어 벨트를 우측에 배치
  dolly: 0.14,           // 타이틀 화면 돌리 진폭 (비율)
  dollyPeriod: 11,       // 돌리 주기 (초)
  launchZoom: 0.62,      // 게임 시작 시 카메라 거리 배율 (줌 인)
  dockZoom: 1.1,         // 우주선 도착 후 카메라 거리 배율
  launchSpeed: 1.4,      // 게임 시작 시 벨트/아이템 속도 배율

  // 발사 시퀀스
  branchTiles: 4,        // 분배기 → 우주선 사이 분기 벨트 칸수
  coreTiles: 3.2,        // 우주선 길이 (타일)
  takeoffFadeDelay: 5000, takeoffFadeMs: 2000,

  // 모델 진행방향(-X) → 경로 기준(-Z) 회전 보정
  modelYaw: { S: -Math.PI / 2, L: -Math.PI / 2, R: -Math.PI / 2, X: 0 },

  // 색
  lineColors: ['#4fe8f0', '#9ff7ff', '#2fb8c4'],
  neutral: '#6d778f',
  linked: '#b9c6de',     // 연결된 아이템 (은은한 밝기, 발광 없음)
  bg: '#070d1a',
};

export const ASSETS = {
  belt: 'uploads/belt.glb',
  curveL: 'uploads/belt_curve_l.glb',
  curveR: 'uploads/belt_curve_r.glb',
  splitter: 'uploads/splitter.glb',
  core: 'uploads/core.glb',
  palette: 'uploads/Palette.png',
};
