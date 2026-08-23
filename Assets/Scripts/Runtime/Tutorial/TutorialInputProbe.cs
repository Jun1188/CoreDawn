using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 이산 입력을 엿보기만 하는 관찰자.
///
/// <see cref="InputManager"/>는 우선순위 순으로 돌다가 <c>true</c>(소비)를 만나면 멈춘다.
/// 그래서 <b>가장 높은 우선순위로 등록하되 언제나 false를 반환</b>하면, 모든 이산 입력을
/// 가장 먼저 보면서도 게임플레이를 한 톨도 방해하지 않는다. 튜토리얼이 "B를 눌렀나"를
/// 알기 위해 PlacementSystem이나 PlayerController를 건드릴 필요가 없는 이유다.
///
/// Move/Look은 라우팅되지 않고 <see cref="InputManager.ReadValue{T}"/>로만 읽히므로
/// 여기로 오지 않는다 — 그쪽은 <see cref="TutorialConditions"/>가 PlayerMotionState를 폴링한다.
/// </summary>
public class TutorialInputProbe : MonoBehaviour, IInputReceiver
{
    /// <summary>가장 먼저 받는다. 소비하지 않으므로 아무도 가리지 않는다.</summary>
    public int Priority => InputPriority.SystemModal;

    public bool IsInputActive => isActiveAndEnabled;

    /// <summary>Performed 단계의 이산 입력. 눌림 상태형(Sprint/Aim 등)도 시작 시 한 번 온다.</summary>
    public event Action<InputActionId> Performed;

    bool _registered;

    void Update()
    {
        // InputManager는 GameBootstrap이 Systems 씬으로 얹으므로 우리보다 늦게 생길 수 있다.
        if (_registered || InputManager.Instance == null) return;
        InputManager.Instance.Register(this);
        _registered = true;
    }

    void OnDisable()
    {
        if (!_registered) return;
        InputManager.Instance?.Unregister(this);
        _registered = false;
    }

    public bool OnInput(in InputEvent e)
    {
        if (e.Phase == InputActionPhase.Performed)
            Performed?.Invoke(e.Id);

        return false;   // 절대 소비하지 않는다 — 이 한 줄이 이 컴포넌트의 전부다
    }
}
