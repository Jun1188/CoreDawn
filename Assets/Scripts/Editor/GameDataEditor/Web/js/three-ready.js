// three.js 로딩 완료를 기다릴 수 있게 Promise 를 먼저 만든다.
// module 스크립트는 defer 처럼 동작해 이 아래 classic 블록들보다 늦게 실행되므로,
// 여기서 만들어두지 않으면 건물 에디터가 "아직 없음"으로 보고 3D 를 포기한다.
window.__THREE_READY__ = new Promise(r => {
  window.__THREE_RESOLVE__ = r;
  // module 스크립트가 아예 실행되지 않는 환경(구형 브라우저·차단)에서도 멈추지 않게 한다.
  // 여기서 포기해도 3D 뷰만 꺼지고 데이터 편집은 그대로 열린다.
  setTimeout(() => r(!!window.__THREE__), 6000);
});
