using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoreDawn.Inputs
{
    /// <summary>
    /// 입력 파이프라인의 단일 진입점 — 설계: input-pipeline-architecture.md
    ///
    ///   ① 액션 맵 스택 (거시적 제어)  : 최상단 맵 + Global 맵만 활성. 신호 발생 자체를 차단
    ///   ② 우선순위 라우팅 (미시적 제어): 활성 맵에서 발화한 이벤트를 Priority 순으로 전달,
    ///                                    누군가 true를 반환하면 소비(Consume)
    ///
    /// 문서 대비 구현 보강:
    ///   - 공유 에셋이 아닌 **런타임 사본**에 구독 — 씬 리로드/도메인 리로드 꺼짐 환경에서
    ///     파괴된 매니저로 향하는 잔존 람다(유령 디스패치)를 원천 차단
    ///   - Push가 **토큰**을 반환하고 Pop은 토큰으로 중간 제거 가능 — 팝업이 LIFO 순서로
    ///     닫히지 않아도 스택이 오염되지 않는다 (이름 기반 Pop의 무결성 문제 해결)
    ///   - 같은 맵이 연속으로 겹칠 때 Disable→Enable 왕복 생략 (그 프레임 입력 유실 방지)
    /// </summary>
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        [SerializeField] private InputActionAsset asset;

        [Tooltip("시작 시 스택 바닥에 깔리는 기본 맵.")]
        [SerializeField] private string baseMap = "Gameplay";

        private InputActionAsset _runtime;   // 런타임 사본 — 구독은 전부 이쪽에
        private InputActionMap _globalMap;

        private readonly List<IInputReceiver> _receivers  = new();
        private readonly List<IInputReceiver> _iterBuffer = new();
        private bool _dirty;

        // 폴링용 액션 캐시 (Move/Look 등 연속 입력 — §7-1). 같은 이름이 여러 맵에 있으면 먼저 등록된 것 유지
        private readonly Dictionary<InputActionId, InputAction> _actions = new();

        // 맵 스택 — 최상단 항목의 맵만 활성. 토큰으로 중간 제거를 허용한다.
        private readonly List<(int token, InputActionMap map)> _stack = new();
        private int _nextToken = 1;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            _runtime = Instantiate(asset);
            _globalMap = _runtime.FindActionMap("Global");
            _globalMap?.Enable();

            BindAll();

            if (!string.IsNullOrEmpty(baseMap))
                PushMap(baseMap);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_runtime != null) Destroy(_runtime);   // 구독도 사본과 함께 소멸
        }

        /// <summary>
        /// 모든 액션을 enum에 매핑해 일괄 구독한다.
        /// 액션 추가 시 InputActionId에 같은 이름을 추가하면 자동으로 연결된다.
        /// </summary>
        private void BindAll()
        {
            foreach (var map in _runtime.actionMaps)
            foreach (var action in map.actions)
            {
                if (!Enum.TryParse(action.name, out InputActionId id))
                {
                    Debug.LogWarning($"[Input] 매핑되지 않은 액션: {map.name}/{action.name}");
                    continue;
                }
                var captured = id; // 클로저 캡처 주의
                action.started   += ctx => Dispatch(new InputEvent(captured, ctx));
                action.performed += ctx => Dispatch(new InputEvent(captured, ctx));
                action.canceled  += ctx => Dispatch(new InputEvent(captured, ctx));

                if (!_actions.ContainsKey(id)) _actions.Add(id, action);
            }
        }

        // ---------- 연속 입력 폴링 (§7-1) ----------

        /// <summary>
        /// Move/Look 같은 연속 입력을 매 프레임 읽는다.
        /// 소속 맵이 스택에서 비활성이면 default(T)를 반환 — 팝업이 열리면 이동/시점이 저절로 멎는다.
        /// </summary>
        public T ReadValue<T>(InputActionId id) where T : struct
        {
            if (_actions.TryGetValue(id, out var action) && action.enabled)
                return action.ReadValue<T>();
            return default;
        }

        // ---------- ① 액션 맵 스택 ----------

        /// <summary>맵을 스택 최상단에 올리고 활성화한다. 반환된 토큰으로 Pop할 것.</summary>
        public int PushMap(string mapName)
        {
            var map = _runtime.FindActionMap(mapName, throwIfNotFound: true);
            var prevTop = _stack.Count > 0 ? _stack[^1].map : null;

            int token = _nextToken++;
            _stack.Add((token, map));

            if (prevTop != map)   // 같은 맵이 겹치면 Disable→Enable 왕복 생략
            {
                prevTop?.Disable();
                map.Enable();
            }
            return token;
        }

        /// <summary>
        /// 토큰으로 스택에서 제거. 최상단이 아니어도 안전하다 —
        /// 팝업이 열린 순서와 다르게 닫혀도 스택이 오염되지 않는다.
        /// </summary>
        public void PopMap(int token)
        {
            int idx = _stack.FindIndex(e => e.token == token);
            if (idx < 0)
            {
                Debug.LogWarning($"[Input] 존재하지 않는 토큰 Pop 시도: {token}");
                return;
            }

            bool wasTop = idx == _stack.Count - 1;
            var removed = _stack[idx].map;
            _stack.RemoveAt(idx);

            if (!wasTop) return;   // 중간 제거는 활성 상태에 영향 없음

            var newTop = _stack.Count > 0 ? _stack[^1].map : null;
            if (newTop == removed) return;   // 같은 맵이 겹쳐 있었음 — 왕복 생략

            removed.Disable();
            newTop?.Enable();
        }

        // ---------- 리시버 등록 ----------

        public void Register(IInputReceiver r)
        {
            if (_receivers.Contains(r)) return;
            _receivers.Add(r);
            _dirty = true;   // 정렬은 Dispatch 시점에 지연 수행
        }

        public void Unregister(IInputReceiver r) => _receivers.Remove(r);

        // ---------- ② 우선순위 라우팅 ----------

        private void Dispatch(in InputEvent e)
        {
            if (_dirty)
            {
                _receivers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                _dirty = false;
            }

            // 콜백 안에서 Register/Unregister가 일어나도 안전하도록 스냅샷 순회
            _iterBuffer.Clear();
            _iterBuffer.AddRange(_receivers);

            for (int i = 0; i < _iterBuffer.Count; i++)
            {
                var r = _iterBuffer[i];
                if (!r.IsInputActive) continue;
                if (r.OnInput(e)) return;   // Consume → 순회 종료
            }
        }
    }
}
