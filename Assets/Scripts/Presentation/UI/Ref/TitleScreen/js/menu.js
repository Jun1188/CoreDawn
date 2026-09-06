// DOM 메뉴: 글리치 전환 헬퍼 + 설정 패널
export const isAnimating = el => el.classList.contains('in') || el.classList.contains('out');

// 순차 글리치 소멸/등장
export function glitchOut(els, step = 120) { els.forEach((el, i) => setTimeout(() => { el.classList.remove('in'); el.classList.add('out'); }, i * step)); }
export function glitchIn(els, step = 140, delay = 0) {
  els.forEach((el, i) => { el.classList.remove('out'); el.classList.add('in'); el.style.animationDelay = (delay + i * step) + 'ms'; });
}
// 등장이 끝나면 클래스 정리 (호버 등 원래 스타일 복원). onShown(el) 콜백
export function bindGlitchEnd(els, onShown) {
  els.forEach(el => el.addEventListener('animationend', e => {
    if (e.animationName !== 'glitchIn') return;
    el.classList.remove('in'); el.style.animationDelay = ''; onShown?.(el);
  }));
}

// ── 설정 패널 ──────────────────────────────────────────
const STORAGE = 'coredawn.settings';
const DEFAULTS = { master: 8, bgm: 6, sfx: 7, res: 2, vsync: 1, mode: 1, quality: 2 };
export const CAROUSEL = {
  res: ['1280×720', '1600×900', '1920×1080', '2560×1440', '3440×1440', '3840×2160'],
  quality: ['LOW', 'MEDIUM', 'HIGH', 'ULTRA'],
};

export function initSettingsPanel(root) {
  const settings = Object.assign({}, DEFAULTS, JSON.parse(localStorage.getItem(STORAGE) || '{}'));
  const save = () => localStorage.setItem(STORAGE, JSON.stringify(settings));

  root.querySelectorAll('.bar').forEach(bar => {           // 0~10 네온 바
    const key = bar.dataset.key;
    for (let i = 0; i < 10; i++) bar.append(document.createElement('i'));
    const paint = () => [...bar.children].forEach((c, i) => c.classList.toggle('on', i < settings[key]));
    const pick = e => { const r = bar.getBoundingClientRect(); settings[key] = Math.round(Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)) * 10); paint(); save(); };
    bar.addEventListener('pointerdown', e => { pick(e); addEventListener('pointermove', pick); addEventListener('pointerup', () => removeEventListener('pointermove', pick), { once: true }); });
    paint();
  });
  root.querySelectorAll('.car').forEach(car => {           // 좌우 캐러셀
    const key = car.dataset.key, opts = CAROUSEL[key], [prev, label, next] = car.children;
    settings[key] = Math.min(opts.length - 1, Math.max(0, settings[key] ?? 0));
    const paint = () => { label.textContent = opts[settings[key]]; };
    const step = d => { settings[key] = (settings[key] + d + opts.length) % opts.length; paint(); save(); };
    prev.addEventListener('click', () => step(-1)); next.addEventListener('click', () => step(1));
    paint();
  });
  root.querySelectorAll('.seg').forEach(seg => {           // 세그먼트
    const key = seg.dataset.key, bs = [...seg.children];
    const paint = () => bs.forEach((b, i) => b.classList.toggle('on', i === settings[key]));
    bs.forEach((b, i) => b.addEventListener('click', () => { settings[key] = i; paint(); save(); }));
    paint();
  });
  return settings;
}
