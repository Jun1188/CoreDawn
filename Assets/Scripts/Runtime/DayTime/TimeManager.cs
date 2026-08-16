using UnityEngine;

/// <summary>
/// 낮/밤 주기 전용 매니저 — DayCycle(plain C# 심 코어)의 Unity 드라이버.
/// 시간 로직은 전부 DayCycle에 있고, 여기는 구동·설정·이벤트 중계만 담당한다.
///
/// 다른 시스템 연동법:
///   TimeManager.Instance.Cycle.NightStarted += day => ...   // 웨이브 스포너
///   TimeManager.Instance.Cycle.DayStarted   += day => ...   // 보상/세이브 등
///   TimeManager.Instance.Cycle.NormalizedTimeOfDay          // 조명 연출
///   TimeManager.Instance.EndNightEarly()                    // 웨이브 전멸 시 즉시 아침
/// </summary>
public class TimeManager : MonoBehaviour
{
    public static TimeManager Instance { get; private set; }

    [Header("Time Settings (초 단위)")]
    [SerializeField] float dayDuration = 60f;   // 낮 유지 시간
    [SerializeField] float nightDuration = 40f; // 밤 유지 시간

    /// <summary>낮/밤 주기 심 코어. 이벤트 구독/시간 조회는 이쪽으로.</summary>
    public DayCycle Cycle { get; private set; }

    // ── 자주 쓰는 조회 단축
    public DayPhase Phase     => Cycle.Phase;
    public int      DayNumber => Cycle.DayNumber;

    public float RemainingPhaseTime => Cycle != null ? Cycle.PhaseRemaining : 0f;
    public float PhaseProgress      => Cycle != null ? Cycle.PhaseProgress01 : 0f;

    /// <summary>건축 가능한 타이밍(낮)인지 체크.</summary>
    public bool IsBuildingAllowed => Cycle.Phase == DayPhase.Day;

    public bool IsNightCompletionControlled => Cycle != null && Cycle.IsNightCompletionControlled;

    /// <summary>
    /// Opt-in gate used by objective-based nights. False preserves the original timed night.
    /// </summary>
    public void SetNightCompletionControlled(bool controlled)
    {
        if (Cycle != null) Cycle.IsNightCompletionControlled = controlled;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        else
        {
            Instance = this;
        }
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        Cycle = new DayCycle(dayDuration, nightDuration);
        Cycle.DayStarted   += OnDayStarted;
        Cycle.NightStarted += OnNightStarted;
    }

    void Start() => Cycle.Begin();   // 다른 시스템의 구독(Awake/Start)이 끝난 뒤 1일차 시작 알림

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update() => Cycle.Advance(Time.deltaTime);

    /// <summary>웨이브 전멸 등으로 밤을 조기 종료하고 아침을 연다.</summary>
    public void EndNightEarly() => Cycle.EndNightEarly();

    // ── 페이즈 전환 로그 + HUD 갱신 (UI는 기존 방식대로 UpdateHUD 호출을 받는다)

    void OnDayStarted(int day)
    {
        Debug.Log(day == 1
            ? $"[게임 시작] {day}일차 낮이 시작되었습니다. (건축 가능)"
            : $"[☀️ 알림] 아침이 밝았습니다! {day}일차 — 무사 생존 완료.");
        RefreshHUD();
    }

    void OnNightStarted(int day)
    {
        Debug.Log("[⚠️ 경고] 밤이 되었습니다! 전투를 준비하세요.");
        RefreshHUD();
    }

    static void RefreshHUD()
    {
        if (SystemUIManager.Instance != null)
            SystemUIManager.Instance.UpdateHUD();
    }
}
