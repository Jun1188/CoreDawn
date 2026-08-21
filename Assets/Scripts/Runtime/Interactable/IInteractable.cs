/// <summary>
/// 플레이어 E키 상호작용 계약 — 마인크래프트식 "동사 하나, 행동은 타겟이 결정".
///
/// 발견은 물리 레이캐스트: 콜라이더가 있는 GO의 부모 어딘가에 이 인터페이스가 있으면 대상.
/// 프롬프트와 실행은 반드시 같은 조준 판정을 공유한다 — PlayerInteractionManager.Current 참조.
///
/// 구현 경로 2가지:
///  - 단독 오브젝트(상자·드롭 아이템): Interactable 베이스 상속
///  - 엔티티(건물 등, 상속이 차 있는 경우): 이 인터페이스 직접 구현
/// </summary>
public interface IInteractable
{
    /// <summary>조준 시 표시할 문구 ("상자 열기"). null/빈 문자열 = 지금은 상호작용 불가 — 프롬프트도 숨겨진다.</summary>
    string Prompt { get; }

    void Interact(PlayerController player);
}

/// <summary>
/// 누르고 있어야 완료되는 상호작용 — 손 채굴처럼 "한 번에 끝나지 않는 일".
///
/// 조준·프롬프트는 <see cref="IInteractable"/>과 똑같이 돌아가고, 다른 것은 실행 시점뿐이다:
/// 누른 순간이 아니라 <see cref="HoldSeconds"/>를 채운 순간 <see cref="OnHoldComplete"/>가 불린다.
/// 진행도는 <see cref="PlayerInteractionManager.HoldProgress"/>가 들고, HUD가 크로스헤어 링으로 그린다.
///
/// 누르고 있으면 완료 후 곧바로 다음 회차가 시작된다 — 광석 하나마다 손을 뗐다 누르게 하면
/// 손으로 캐는 일이 손가락 운동이 된다.
///
/// <see cref="IInteractable.Interact"/>는 홀드 대상에서 호출되지 않는다. 탭과 홀드가 같은 키에
/// 얹히면 "눌렀다 뗐을 뿐인데 뭔가 실행됐다"가 되기 때문이다.
/// </summary>
public interface IHoldInteractable : IInteractable
{
    /// <summary>완료까지 눌러야 하는 시간(초). 0 이하면 홀드가 시작되지 않는다.</summary>
    float HoldSeconds { get; }

    /// <summary>링 가운데 글자 ("채굴"). 비면 링만 그린다.</summary>
    string HoldLabel { get; }

    /// <summary>지금 진행할 수 있는가. false면 링이 그 자리에 멈춘다 (취소는 아니다 — 재고가 차면 이어간다).</summary>
    bool CanHold { get; }

    /// <summary>한 회차를 채웠다. 여기서 실제 결과물(아이템 등)을 준다.</summary>
    void OnHoldComplete(PlayerController player);
}
