# 통합 입력 파이프라인 아키텍처

> **대상**: Unity + New Input System 기반 클라이언트 작업자
> **목적**: 입력 충돌(UI 열린 상태에서의 오조작, ESC 다중 처리 등)을 구조적으로 차단
> **상태**: 전 단계(①~④) 구현 완료 — §11 구현 노트 참조. 변경 이력은 LOG.md. 에셋은 GameInput으로 단일화(PlayerControls는 미사용, 삭제 대기)

---

## 1. 해결하려는 문제

| 증상 | 원인 |
|---|---|
| 인벤토리를 열었는데 캐릭터가 공격함 | Gameplay 입력이 UI 상태와 무관하게 계속 살아있음 |
| ESC 한 번에 창이 여러 개 닫힘 | 열려있는 모든 UI가 같은 이벤트를 동시에 수신 |
| 건설 모드 좌클릭이 공격으로도 처리됨 | 입력 소유권을 판정하는 계층이 없음 |
| 상태별 예외 처리가 `if` 지옥으로 번짐 | 입력 필터링과 행동 결정이 한 곳에 섞여 있음 |

공통 원인은 **입력을 "누가 받을 자격이 있는가"와 "받아서 무엇을 하는가"가 분리되어 있지 않다는 것**이다.

---

## 2. 핵심 설계 사상: 2단계 필터 + FSM

```
[물리 입력]
    ↓
① 액션 맵 스택 (거시적 제어)        ← "지금 게임 전체가 어떤 조작계인가?"
    ↓  통과한 입력만 이벤트로 변환
② 우선순위 라우팅 (미시적 제어)      ← "활성 조작계 안에서 누가 먼저 처리하나?"
    ↓  아무도 소비하지 않은 입력만 도달
③ FSM 위임                          ← "현재 상태에서 이 입력이 유효한가?"
```

각 계층의 책임은 서로 겹치지 않는다.

| 계층 | 책임 | 구현 위치 | 판단 기준 |
|---|---|---|---|
| ① 액션 맵 스택 | 신호 발생 자체를 차단 | Engine (New Input System) | 현재 컨텍스트 (Gameplay / UI / Cutscene) |
| ② 우선순위 라우팅 | 수신자 간 소유권 중재 | `InputManager` | `IInputReceiver.Priority` |
| ③ FSM | 실제 행동 결정 | `PlayerController` + State | 현재 상태 |

**설계 원칙**: 상위 계층에서 막을 수 있는 것을 하위 계층에서 `if`로 막지 않는다.
예) "UI가 열려있으면 공격 금지"를 `AttackState`에서 검사하지 않는다. ①이 이미 막았다.

---

## 3. 공통 타입 정의

```csharp
using UnityEngine.InputSystem;

/// 액션 문자열 직접 사용 금지. 오타가 런타임까지 살아남는다.
/// InputActionAsset의 액션 이름과 enum 이름은 반드시 일치시킬 것.
public enum InputActionId
{
    None,
    // Gameplay Map
    Move, Jump, Attack, Interact, Rotate, Pick,
    // UI Map (Cancel은 Gameplay 맵에도 같은 이름으로 존재 — 건설 취소 등.
    //         리시버가 기대하는 액션은 "그 리시버가 살아있는 컨텍스트의 맵"에 있어야 한다)
    Cancel, Submit, Navigate,
    // Global Map (맵 스택과 무관하게 항상 활성)
    ToggleInventory, ToggleBuildMenu,
}

public readonly struct InputEvent
{
    public readonly InputActionId Id;
    public readonly InputActionPhase Phase;              // Started / Performed / Canceled
    public readonly InputAction.CallbackContext Context; // 값 읽기용

    public InputEvent(InputActionId id, in InputAction.CallbackContext ctx)
    {
        Id = id;
        Phase = ctx.phase;
        Context = ctx;
    }

    public T Read<T>() where T : struct => Context.ReadValue<T>();
}

/// 입력을 받고자 하는 모든 객체가 구현한다.
public interface IInputReceiver
{
    /// 높을수록 먼저 수신. InputPriority 상수 사용.
    int Priority { get; }

    /// false면 라우팅에서 건너뜀. 등록/해제 반복 대신 이 플래그로 제어할 것.
    bool IsInputActive { get; }

    /// true 반환 시 입력을 소비(Consume)하며, 하위 우선순위로 전달되지 않는다.
    bool OnInput(in InputEvent e);
}
```

### 우선순위 상수

우선순위는 **반드시 이 상수를 통해서만** 지정한다. 매직 넘버 금지.

```csharp
public static class InputPriority
{
    public const int SystemModal = 10000; // 종료 확인창, 로딩 오버레이
    public const int PopupBase   = 5000;  // + 열린 순서(depth)
    public const int HudWidget   = 1000;  // 툴바, 퀵바
    public const int BuildTool   = 500;   // 건설 모드 배치/회전
    public const int Player      = 0;     // 플레이어 조작
    public const int Fallback    = -100;  // 아무도 안 받은 입력의 최종 처리 (ESC → 일시정지 열기)
}
```

새 계층을 추가할 때는 기존 값 사이의 간격을 사용하고, 이 표를 갱신한다.

---

## 4. InputManager

파이프라인의 ①과 ②를 모두 담당하는 단일 진입점.

```csharp
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    [SerializeField] private InputActionAsset _asset;

    private readonly List<IInputReceiver> _receivers  = new();
    private readonly List<IInputReceiver> _iterBuffer = new();
    private bool _dirty;

    private readonly List<InputActionMap> _mapStack = new();
    private InputActionMap _globalMap;

    private void Awake()
    {
        Instance = this;
        _globalMap = _asset.FindActionMap("Global", true);
        _globalMap.Enable();
        BindAll();
    }

    /// 모든 액션을 enum에 매핑해 일괄 구독한다.
    /// 액션 추가 시 InputActionId에 같은 이름을 추가하면 자동으로 연결된다.
    private void BindAll()
    {
        foreach (var map in _asset.actionMaps)
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
        }
    }

    // ---------- ① 액션 맵 스택 ----------

    public void PushMap(string mapName)
    {
        if (_mapStack.Count > 0) _mapStack[^1].Disable();

        var map = _asset.FindActionMap(mapName, true);
        _mapStack.Add(map);
        map.Enable();
    }

    public void PopMap(string mapName)
    {
        // 스택 무결성 검사: 최상단이 아닌 항목을 빼려 한다면 호출부 버그다.
        if (_mapStack.Count == 0 || _mapStack[^1].name != mapName)
        {
            Debug.LogError($"[Input] 스택 최상단이 아닌 맵 Pop 시도: {mapName}");
            return;
        }
        _mapStack[^1].Disable();
        _mapStack.RemoveAt(_mapStack.Count - 1);
        if (_mapStack.Count > 0) _mapStack[^1].Enable();
    }

    // ---------- 리시버 등록 ----------

    public void Register(IInputReceiver r)
    {
        if (_receivers.Contains(r)) return;
        _receivers.Add(r);
        _dirty = true; // 정렬은 Dispatch 시점에 지연 수행
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
            if (r.OnInput(e)) return; // Consume → 순회 종료
        }
    }
}
```

### 구현 주의

- **정렬 시점**: `_dirty` 플래그로 등록 시점에만 정렬한다. 입력마다 `Sort`를 돌리지 않는다.
- **스냅샷 순회**: 팝업이 `OnInput` 안에서 자신을 `Unregister`하는 것은 흔한 케이스다. 원본 리스트를 직접 순회하면 예외가 난다.
- **`Global` 맵**: 인벤토리 토글처럼 UI/Gameplay 양쪽에서 살아있어야 하는 키를 여기 둔다. 스택의 영향을 받지 않는다.

---

## 5. UI 팝업

ESC 다중 처리 문제의 해법은 **열린 순서대로 depth를 부여**하는 것이다. 모든 팝업이 같은 우선순위면 라우팅이 무의미해진다.

```csharp
public abstract class UIPopup : MonoBehaviour, IInputReceiver
{
    private static int _depthCounter;
    private int _depth;

    public int Priority => InputPriority.PopupBase + _depth;
    public bool IsInputActive => gameObject.activeInHierarchy;

    /// 모달이면 처리하지 않은 입력도 전부 삼킨다. 기본값 true.
    protected virtual bool IsModal => true;

    protected virtual void OnEnable()
    {
        _depth = ++_depthCounter; // 나중에 열린 창이 항상 위
        InputManager.Instance.Register(this);
        InputManager.Instance.PushMap("UI");
    }

    protected virtual void OnDisable()
    {
        InputManager.Instance.Unregister(this);
        InputManager.Instance.PopMap("UI");
    }

    public virtual bool OnInput(in InputEvent e)
    {
        if (e.Phase != InputActionPhase.Performed) return IsModal;

        if (e.Id == InputActionId.Cancel)
        {
            Close();
            return true; // ← 최상단 팝업만 닫히는 이유
        }
        return IsModal;
    }

    protected void Close() => gameObject.SetActive(false);
}
```

```csharp
public class InventoryPopup : UIPopup
{
    public override bool OnInput(in InputEvent e)
    {
        if (e.Phase == InputActionPhase.Performed && e.Id == InputActionId.ToggleInventory)
        {
            Close();
            return true;
        }
        return base.OnInput(e);
    }
}
```

**"인벤토리를 열었는데 공격" 문제는 이중으로 차단된다.**
`PushMap("UI")`로 Gameplay 맵이 꺼져 `Attack` 콜백 자체가 오지 않고(①), Global 맵의 키가 겹치더라도 모달 팝업이 `true`를 반환해 삼킨다(②).

---

## 6. PlayerController + FSM

```csharp
public interface IPlayerState
{
    void Enter(PlayerController c);
    void Exit(PlayerController c);
    void Tick(PlayerController c, float dt);
    bool HandleInput(PlayerController c, in InputEvent e);
}

public class PlayerController : MonoBehaviour, IInputReceiver
{
    public int Priority => InputPriority.Player; // 항상 최하위
    public bool IsInputActive => enabled;

    private IPlayerState _current;
    private Vector2 _moveInput;
    public Vector2 MoveInput => _moveInput;

    private void OnEnable()
    {
        InputManager.Instance.Register(this);
        InputManager.Instance.PushMap("Gameplay");
        ChangeState(new IdleState());
    }

    private void OnDisable() => InputManager.Instance.Unregister(this);

    public bool OnInput(in InputEvent e)
    {
        // 연속 입력은 상태와 무관하게 값만 갱신 (§7-1 참고)
        if (e.Id == InputActionId.Move)
        {
            _moveInput = e.Phase == InputActionPhase.Canceled
                ? Vector2.zero
                : e.Read<Vector2>();
            return true;
        }
        return _current?.HandleInput(this, e) ?? false;
    }

    private void Update() => _current?.Tick(this, Time.deltaTime);

    public void ChangeState(IPlayerState next)
    {
        _current?.Exit(this);
        _current = next;
        _current.Enter(this);
    }
}
```

```csharp
public class IdleState : IPlayerState
{
    public void Enter(PlayerController c) { }
    public void Exit(PlayerController c) { }

    public void Tick(PlayerController c, float dt)
    {
        if (c.MoveInput.sqrMagnitude > 0.01f) c.ChangeState(new MoveState());
    }

    public bool HandleInput(PlayerController c, in InputEvent e)
    {
        if (e.Phase != InputActionPhase.Performed) return false;

        switch (e.Id)
        {
            case InputActionId.Attack: c.ChangeState(new AttackState()); return true;
            case InputActionId.Jump:   c.ChangeState(new JumpState());   return true;
        }
        return false;
    }
}

/// "공격 중엔 점프 불가" 같은 제약이 상태 내부에 자연스럽게 표현된다.
public class AttackState : IPlayerState
{
    private float _timer;

    public void Enter(PlayerController c) => _timer = 0.4f;
    public void Exit(PlayerController c) { }

    public void Tick(PlayerController c, float dt)
    {
        _timer -= dt;
        if (_timer <= 0f) c.ChangeState(new IdleState());
    }

    public bool HandleInput(PlayerController c, in InputEvent e) => false; // 전부 무시
}
```

---

## 7. 구현 시 반드시 지킬 것

### 7-1. 연속 입력(Move)은 라우팅에 태우지 않는다

`Move`를 이벤트로 흘리면 스틱을 기울인 동안 매 프레임 `performed`가 들어와 `Dispatch`가 도배된다.
값만 캐싱하거나(위 코드), 아예 라우팅에서 제외하고 `Tick`에서 `_moveAction.ReadValue<Vector2>()`로 폴링한다.

### 7-2. `Phase` 가드를 반드시 넣는다

`started / performed / canceled`를 모두 구독하므로, 가드가 없으면 입력 한 번이 3번 처리된다.
모든 리시버는 진입부에 아래를 넣는다.

```csharp
if (e.Phase != InputActionPhase.Performed) return false; // 또는 IsModal
```

모달 팝업이 `false` 대신 `IsModal`을 반환하는 이유는, started/canceled도 하위로 새어나가면 안 되기 때문이다.

### 7-3. 재진입이 잦은 맵은 참조 카운트로 전환

팝업 2개가 동시에 열리면 `PushMap("UI")`가 두 번 호출된다. 현재 구현은 스택에 두 번 쌓이고 Pop도 두 번 되므로 동작은 하지만, `Disable → Enable`이 중복 실행되며 **해당 프레임의 입력이 유실될 수 있다.**

UI 맵처럼 중첩 진입이 잦은 경우 참조 카운트 방식으로 교체한다.

```csharp
private readonly Dictionary<InputActionMap, int> _refCount = new();
// Push: count++ → 0에서 1이 될 때만 Enable
// Pop:  count-- → 1에서 0이 될 때만 Disable
```

### 7-4. 모드 전환은 Register/Unregister가 아니라 `IsInputActive`로

등록/해제를 반복하면 매번 재정렬이 발생한다. 일시적 비활성은 플래그로 처리한다.

---

## 8. 확장 예시: 건설 모드 (팩토리 게임 전용)

건설 모드의 회전(R)/배치(좌클릭)/취소(우클릭)는 **UI보다 아래, 플레이어 FSM보다 위**에 위치해야 한다.
별도 리시버로 분리하면 플레이어 FSM에 건설 관련 조건문이 전혀 들어가지 않는다.

```csharp
public class BuildModeController : MonoBehaviour, IInputReceiver
{
    public int Priority => InputPriority.BuildTool;
    public bool IsInputActive => _isBuilding; // 건설 모드일 때만 라우팅 참여

    private bool _isBuilding;

    public bool OnInput(in InputEvent e)
    {
        if (!_isBuilding || e.Phase != InputActionPhase.Performed) return false;

        switch (e.Id)
        {
            case InputActionId.Rotate: RotateGhost();   return true;
            case InputActionId.Attack: PlaceBuilding(); return true; // 좌클릭 가로채기
            case InputActionId.Cancel: ExitBuildMode(); return true;
        }
        return false;
    }
}
```

---

## 9. 신규 입력 수신자 추가 절차

1. `InputActionAsset`에 액션을 추가하고, **동일한 이름**으로 `InputActionId`에 enum 값을 추가한다.
2. 어느 액션 맵에 속할지 결정한다. (맵 스택 영향을 받지 않아야 하면 `Global`)
3. `IInputReceiver`를 구현하고 `InputPriority`에서 적절한 상수를 고른다. 새 계층이면 상수를 추가하고 §3 표를 갱신한다.
4. `OnEnable`/`OnDisable`에서 `Register`/`Unregister`, 진입부에 `Phase` 가드.
5. 소비 여부(`true`/`false`)를 의도적으로 결정한다. **`return true`는 "이 입력은 여기서 끝난다"는 선언이다.**

---

## 10. 체크리스트

- [ ] `InputActionId` enum 이름과 에셋의 액션 이름이 1:1 일치하는가
- [ ] 리시버가 기대하는 액션이 **그 리시버가 살아있는 컨텍스트의 맵**에 존재하는가
- [ ] 모든 `OnInput` 진입부에 `Phase` 가드가 있는가
- [ ] `Move` 등 연속 입력이 라우팅에 올라와 있지 않은가
- [ ] `InputEvent`를 `OnInput` 스코프 밖으로 내보내지 않는가 (큐잉/필드 보관 금지)
- [ ] `IsPointerOverGameObject`를 `OnInput` 안에서 직접 호출하지 않는가 (Update에서 프레임 캐싱)
- [ ] `PushMap`이 반환한 토큰을 보관했다가 `PopMap(token)`으로 해제하는가
- [ ] `PlayerController`보다 낮은 우선순위의 리시버가 없는가
- [ ] 팝업이 depth 없이 고정 우선순위를 쓰고 있지 않은가

---

## 11. 구현 노트 — 설계 대비 변경점 (1단계 구현 반영)

구현 코드가 진실이다. 이 문서의 예시 코드와 다른 부분은 아래가 이유다.

| 설계 | 구현 | 이유 |
|---|---|---|
| 공유 에셋에 직접 구독 (§4) | **런타임 사본**(`Instantiate(asset)`)에 구독, `OnDestroy`에서 파괴 | 공유 에셋 구독은 씬 리로드·도메인 리로드 꺼짐 환경에서 잔존 람다(파괴된 매니저로의 유령 디스패치)를 만든다 |
| `PushMap/PopMap(string)` + 최상단 검사 (§4) | `PushMap`이 **토큰** 반환, `PopMap(token)`은 중간 제거 허용 | 팝업은 LIFO로 닫힌다는 보장이 없다 (X 버튼). 이름 기반 Pop은 혼합 스택에서 오염을 남김. §7-3의 참조 카운트 문제도 함께 해소 (같은 맵 중첩 시 Disable→Enable 왕복 생략) |
| `Cancel`은 UI 맵 (§3) | Gameplay 맵에도 동명 액션 | 건설 취소 등 Gameplay 컨텍스트에서도 Cancel이 필요. 활성 맵의 것이 발화 |
| §6 `PlayerController`가 Move를 라우팅으로 수신 | (미구현 — ④단계) 구현 시 **라우팅 제외 + Tick 폴링**으로 통일 예정 | §7-1과의 모순 해소. 게임패드 스틱은 매 프레임 이벤트를 만든다 |
| §8 `BuildModeController`가 로직 포함 | **BuildController**(입력 해석) + **PlacementSystem**(배치 로직·API) 분리 | 배치 로직은 파이프라인을 모른다 → 배치 UI·테스트 코드가 같은 API를 직접 호출 가능 |
| — | `InputEvent` 스코프 밖 보관 금지 명시 | `CallbackContext`는 콜백 동안만 유효 |
| — | `IsPointerOverGameObject`는 Update에서 프레임당 1회 캐싱 | 입력 콜백 안 호출은 Unity 경고 + 어차피 직전 프레임 상태 |

| §5 `UIPopup.Close()`가 SetActive(false) | `InventoryPopup.Close()`는 `PlayerController.CloseInventory()`로 위임 | 마우스 캐리지 드롭·상자 패널·커서 복원 등 정리 절차가 그쪽에 있음. 팝업은 파이프라인 어댑터일 뿐 |
| ESC → 일시정지가 PlayerController 콜백 | **열기 = Fallback 리시버**(SystemUIManager, 아무도 안 받은 Cancel), **닫기 = 각 팝업** | 소유권 단일화. 인벤 열림 중 일시정지가 겹치던 버그 해소. timeScale·커서는 PausePopup의 Enter/Exit에 집중 |
| §6 `IPlayerState` FSM | **보류** — PlayerController는 리시버 + 폴링만 | 현재 플레이어에 상호배타적 상태가 없다 (사격=WeaponManager, 점프=물리 체크, 인벤=맵 스택이 차단). 빈 Idle/Move 상태 클래스는 소비자 없는 선언 — 상태 간 입력 규칙이 실제로 갈리는 시점(탈것, 사다리 등)에 도입 |
| §6 Move를 라우팅으로 수신 | Move/Look은 **라우팅 제외, `InputManager.ReadValue<T>()` 폴링** | §7-1과의 모순 해소. 비활성 맵은 0을 반환 → 팝업 열림 중 이동·시점이 저절로 멎어 isInventoryOpen 가드가 불필요해짐 |
| 사격이 PlayerController 경유 | **WeaponManager가 직접 Player 리시버** (Attack/Reload) | 건설 모드(BuildTool이 Attack 소비)·팝업(UI 맵)에서 신호가 도달하지 않음 — "건설 중 좌클릭 사격" 구조적 해소 |
| — | **R키 겹침 처리**: 건설 모드 중 BuildController가 Reload도 소비 | Rotate(건설 회전)와 Reload(재장전)가 같은 R — 모드 중 전투 입력 차단 규칙(Attack과 동일)으로 해소 |
| — | **ToggleInventory(I)는 Global 맵** | 열기 = PlayerController(최하위), 닫기 = InventoryPopup이 상위에서 가로챔 → I로 열고 닫기. 일시정지 중엔 모달이 삼키고, 건설 모드 중엔 BuildTool이 소비(모드 배타) |

**구현 위치**: `InputTypes.cs` / `InputManager.cs` / `UIPopup.cs` / `GameInput.inputactions`(Gameplay·UI·Global 맵) / `GridSystem/BuildController.cs` / `Inventory/InventoryPopup.cs` / `Inventory/HotbarController.cs` / `Manager/PausePopup.cs` / `FPS/PlayerController.cs` / `WeaponManager.cs`
