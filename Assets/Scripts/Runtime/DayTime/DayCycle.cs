using System;

public enum DayPhase { Day, Night }

/// <summary>
/// 낮/밤 주기의 심 코어 — plain C#, Unity 의존 없음.
/// GameManager(드라이버)가 Advance(dt)로 구동하고, 스포너/조명/UI는 이벤트를 구독한다.
///
/// 코어 루프 (기획):
///   낮  = 건설/자동화 페이즈 (dayDuration초)
///   밤  = 웨이브 디펜스 페이즈 (nightDuration초, 또는 웨이브 클리어 시 EndNightEarly())
///   밤이 끝나면 DayNumber가 올라가며 다음 낮 시작.
/// </summary>
public class DayCycle
{
    public DayPhase Phase { get; private set; } = DayPhase.Day;

    /// <summary>현재 생존 일수. 1일차 낮부터 시작.</summary>
    public int DayNumber { get; private set; } = 1;

    /// <summary>현재 페이즈의 남은 시간(초).</summary>
    public float PhaseRemaining { get; private set; }

    /// <summary>
    /// When enabled, elapsed night time reaches zero but cannot start the next day.
    /// An external objective (for example a finite monster wave) must call EndNightEarly.
    /// Defaults to false so legacy scenes keep their timed-night behaviour.
    /// </summary>
    public bool IsNightCompletionControlled { get; set; }

    /// <summary>현재 페이즈의 전체 길이(초).</summary>
    public float PhaseDuration => Phase == DayPhase.Day ? _dayDuration : _nightDuration;

    /// <summary>현재 페이즈 진행도 0~1.</summary>
    public float PhaseProgress01 => 1f - PhaseRemaining / PhaseDuration;

    /// <summary>
    /// 하루 전체 기준 연속 시간 0~1 — 조명 연출용.
    /// 0 = 아침(일출), 0.25 = 정오, 0.5 = 일몰(밤 시작), 0.75 = 자정, 1 = 다음 아침.
    /// 낮/밤 길이가 달라도 각각 절반씩 매핑된다.
    /// </summary>
    public float NormalizedTimeOfDay =>
        Phase == DayPhase.Day ? PhaseProgress01 * 0.5f : 0.5f + PhaseProgress01 * 0.5f;

    /// <summary>새 낮 시작 (인자 = 일수). 최초 1일차 시작 시에도 발화한다.</summary>
    public event Action<int> DayStarted;

    /// <summary>밤 시작 (인자 = 일수). 웨이브 스포너가 구독할 지점.</summary>
    public event Action<int> NightStarted;

    readonly float _dayDuration, _nightDuration;
    bool _started;

    public DayCycle(float dayDuration, float nightDuration)
    {
        _dayDuration   = Math.Max(1f, dayDuration);
        _nightDuration = Math.Max(1f, nightDuration);
        PhaseRemaining = _dayDuration;
    }

    /// <summary>1일차 낮 시작을 알린다. 구독이 끝난 뒤 1회 호출.</summary>
    public void Begin()
    {
        if (_started) return;
        _started = true;
        DayStarted?.Invoke(DayNumber);
    }

    public void Advance(float dt)
    {
        if (!_started) return;
        PhaseRemaining -= dt;
        if (PhaseRemaining > 0f) return;

        if (Phase == DayPhase.Night && IsNightCompletionControlled)
        {
            PhaseRemaining = 0f;
            return;
        }

        float overshoot = -PhaseRemaining;   // 페이즈 경계를 넘긴 시간은 다음 페이즈로 이월
        if (Phase == DayPhase.Day) BeginNight(overshoot);
        else                       BeginDay(overshoot);
    }

    /// <summary>
    /// 밤 조기 종료 — 웨이브를 전멸시켰을 때 웨이브 매니저가 호출.
    /// 낮에는 아무 일도 하지 않는다.
    /// </summary>
    public void EndNightEarly()
    {
        if (Phase == DayPhase.Night) BeginDay(0f);
    }

    void BeginNight(float overshoot)
    {
        Phase          = DayPhase.Night;
        PhaseRemaining = _nightDuration - overshoot;
        NightStarted?.Invoke(DayNumber);
    }

    void BeginDay(float overshoot)
    {
        DayNumber++;
        Phase          = DayPhase.Day;
        PhaseRemaining = _dayDuration - overshoot;
        DayStarted?.Invoke(DayNumber);
    }
}
