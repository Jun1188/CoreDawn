using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튜토리얼 진행을 소유한다. 지금 어떤 안내를 띄울지 정하고, 그것을 우상단 카드에 그린다.
///
/// <b>씬을 고치지 않는다.</b> SaveManager와 같은 방식으로 스스로 생겨나 DontDestroyOnLoad로 산다 —
/// 게임플레이 씬 9개와 GameUI 프리팹을 한 줄도 건드리지 않으므로 팀원의 씬 병합과 충돌하지 않는다.
///
/// <b>진행 규칙(하이브리드)</b>: 매 틱마다 <i>미완료 스텝 전체</i>를 평가한다. 현재 스텝뿐 아니라
/// 뒤쪽 스텝까지 함께 보기 때문에, 플레이어가 앞질러 해버린 단계는 자기 차례가 오기 전에 완료로
/// 찍혀 그냥 지나간다. "이미 할 줄 아는 것 같으면 다음 문구"가 별도 코드 없이 이 루프 하나로 나온다.
///
/// 시간이 지난다고 넘어가지는 않는다 — 그 동작을 해내야만 다음으로 간다(사용자 확정 사항).
/// </summary>
[DefaultExecutionOrder(-400)]
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    /// <summary>폴링 주기. 매 프레임 돌 이유가 없는 것들(인벤토리·건물 수·광맥)의 간격.</summary>
    const float TickInterval = 0.2f;

    readonly List<TutorialStepSO> _steps = new();
    readonly HashSet<string> _completed = new();
    readonly Dictionary<string, int> _baseline = new();

    TutorialConditions _cond;
    TutorialHUD _hud;
    TutorialInputProbe _probe;

    TutorialStepSO _current;
    int _currentIndex;
    string _hudShownId;
    float _nextTick;
    float _judgeFrom;   // 이 시각(unscaled) 전에는 어떤 스텝도 완료로 찍지 않는다
    bool _skipped;

    // ── 공개 상태 ──

    public TutorialStepSO CurrentStep => _current;
    public bool IsFinished => _skipped || _current == null;
    public bool Skipped => _skipped;
    public int StepCount => _steps.Count;
    public int CompletedCount => _completed.Count;

    /// <summary>현재 안내가 바뀔 때. 끝났으면 null이 온다. (Runtime 컨벤션 — On 접두사 없음)</summary>
    public event Action<TutorialStepSO> StepChanged;
    public event Action TutorialFinished;

    // ─────────────────────────── 부팅 ───────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;

        // GameBootstrap과 같은 규칙 — 플레이어가 없는 순수 테스트 씬은 오염시키지 않는다
        if (FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include) == null) return;

        new GameObject("[TutorialManager]").AddComponent<TutorialManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        var db = TutorialDatabaseSO.LoadDefault();
        if (db == null)
        {
            Debug.LogWarning($"[Tutorial] Resources/{TutorialDatabaseSO.ResourcePath}.asset 이 없어 튜토리얼이 꺼집니다.");
            enabled = false;
            return;
        }

        _steps.AddRange(db.BuildOrdered());
        if (_steps.Count == 0)
        {
            Debug.LogWarning("[Tutorial] 스텝이 하나도 없습니다 — TutorialDatabase.asset의 steps를 채우세요.");
            enabled = false;
            return;
        }

        _cond = new TutorialConditions();
        _hud = new TutorialHUD();

        _probe = gameObject.AddComponent<TutorialInputProbe>();
        _cond.AttachProbe(_probe);
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        _cond?.Detach();
        _hud?.Kill();   // 남은 트윈이 죽은 VisualElement를 물고 돌지 않게
        Instance = null;
    }

    // ─────────────────────────── 진행 ───────────────────────────

    void Update()
    {
        if (_skipped) return;

        _cond.UpdateFast(Time.deltaTime);

        // 일시정지(timeScale 0) 중에도 HUD 바인딩은 이어져야 하므로 unscaled로 잰다
        if (Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + TickInterval;

        _cond.Tick();

        // 완료 판정에만 뜸을 들인다. 카드 연출(RefreshHud)은 그동안에도 계속 돈다 —
        // 안 그러면 들어오는 중인 카드가 다 들어오지도 못하고 도로 나간다.
        if (Time.unscaledTime >= _judgeFrom) EvaluateAll();

        RefreshHud();
    }

    /// <summary>미완료 스텝을 전부 평가하고, 남은 것 중 가장 앞선 것을 현재 안내로 삼는다.</summary>
    void EvaluateAll()
    {
        for (int i = 0; i < _steps.Count; i++)
        {
            var s = _steps[i];
            if (_completed.Contains(s.Id)) continue;

            bool everShown = _baseline.ContainsKey(s.Id);

            // 앞질러 완료를 금지한 스텝은 자기 차례가 오기 전엔 아예 보지 않는다.
            // 숫자키·T처럼 앞선 안내를 따르다 얻어걸리는 동작, 그리고 밤 경고가 여기 해당한다 —
            // 그런 것까지 "이미 할 줄 아네" 규칙에 맡기면 안내가 뜨자마자 사라진다.
            if (s.requireInOrder && !everShown) continue;

            // 기준점이 없는 스텝(= 아직 뜬 적 없는 뒤쪽 스텝)은 0 — 절대값으로 판정되어 자동 완료된다
            int baseline = everShown ? _baseline[s.Id] : 0;
            if (_cond.Evaluate(s, baseline)) _completed.Add(s.Id);
        }

        TutorialStepSO next = null;
        int index = 0;
        for (int i = 0; i < _steps.Count; i++)
        {
            if (_completed.Contains(_steps[i].Id)) continue;
            next = _steps[i];
            index = i + 1;
            break;
        }

        if (next == _current) { _currentIndex = index; return; }

        _current = next;
        _currentIndex = index;

        if (_current != null)
        {
            // 이제부터 "n번 더"를 세기 시작한다 — 뜬 순간의 값이 기준점이다
            _baseline[_current.Id] = _cond.CounterOf(_current);

            // 그리고 잠시 판정을 멈춘다. 카드가 다 들어오는 데 걸리는 시간을 더하므로
            // minSeconds는 순수하게 "읽을 시간"이다. 이게 없으면 한 번의 동작이 여러 안내를
            // 동시에 만족시켜 카드가 두세 장씩 스쳐 지나간다.
            _judgeFrom = Time.unscaledTime + TutorialHUD.LeadInSeconds + Mathf.Max(0f, _current.minSeconds);
        }

        StepChanged?.Invoke(_current);
        if (_current == null) TutorialFinished?.Invoke();
    }

    void RefreshHud()
    {
        if (!_hud.IsBound)
        {
            if (!_hud.TryBind()) return;
            _hudShownId = null;   // 새로 붙었으면 다시 그린다 (HUD 재활성·씬 전환)
        }

        string wantId = _current != null ? _current.Id : null;
        if (_hudShownId == wantId) return;

        _hudShownId = wantId;
        _hud.PlayTransition(_current, _currentIndex, _steps.Count);
    }

    // ─────────────────────── 외부 조작 ───────────────────────

    /// <summary>남은 안내를 전부 접는다. 세이브에 남으므로 다시 켜려면 ResetProgress.</summary>
    public void SkipAll()
    {
        _skipped = true;
        _current = null;
        _hudShownId = null;
        _hud?.PlayTransition(null, 0, 0);   // 접을 때도 오른쪽으로 미끄러져 나간다
        StepChanged?.Invoke(null);
        TutorialFinished?.Invoke();
    }

    /// <summary>처음부터 다시. 진행도만 지우고 관측 카운터는 그대로 둔다(이미 한 것은 이미 한 것이다).</summary>
    public void ResetProgress()
    {
        _skipped = false;
        _completed.Clear();
        _baseline.Clear();
        _current = null;
        _hudShownId = null;
        _nextTick = 0f;
        _judgeFrom = 0f;
    }

    // ─────────────────────── 세이브 연동 ───────────────────────

    public List<string> CaptureCompleted() => new List<string>(_completed);

    /// <summary>
    /// 세이브에서 되돌린다. 기준점은 일부러 버린다 — 불러온 직후의 카운터로 다시 잡아야
    /// "여기서부터 n번 더"가 맞다(옛 기준점을 쓰면 이미 채운 것으로 오인된다).
    /// </summary>
    public void RestoreProgress(IEnumerable<string> completedIds, bool skipped)
    {
        _completed.Clear();
        if (completedIds != null)
            foreach (var id in completedIds)
                if (!string.IsNullOrEmpty(id)) _completed.Add(id);

        _baseline.Clear();
        _skipped = skipped;
        _current = null;
        _hudShownId = null;
        _nextTick = 0f;
        _judgeFrom = 0f;
    }

    /// <summary>eval로 상태를 들여다볼 때 쓰는 한 줄 요약.</summary>
    public string DebugState()
        => $"step={(_current != null ? _current.Id : "(none)")} {_currentIndex}/{_steps.Count} " +
           $"done={_completed.Count} skipped={_skipped} " +
           $"move={_cond?.MoveSeconds:0.0}s look={_cond?.LookSeconds:0.0}s " +
           $"inv={_cond?.InventoryOpened} build={_cond?.BuildModeEntered} weapon={_cond?.WeaponEquipped} " +
           $"hold={Mathf.Max(0f, _judgeFrom - Time.unscaledTime):0.0}s " +
           $"mined={_cond?.MinedTotal} placed={_cond?.PlacedCount} belts={_cond?.PlacedBelts} " +
           $"hotbar={_cond?.HotbarSwitches} demo={_cond?.DemolishedCount} " +
           $"craftedWeapon={_cond?.CraftedOfType(ItemType.Weapon)} beltShape={_cond?.BeltShapeCycles} " +
           $"tier={_cond?.CoreTier} nights={_cond?.NightsStarted}/{_cond?.NightsSurvived}";
}
