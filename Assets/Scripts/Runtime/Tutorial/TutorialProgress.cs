using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Tutorial
{
    /// <summary>
    /// 튜토리얼 진행 상태기 — 완료 목록·기준점·현재 안내 선정을 소유한다. (plain C#)
    ///
    /// 구 구조에서는 "이 스텝이 뜬 뒤로 n번 더"라는 규칙 하나가 두 파일에 나뉘어 있었다 —
    /// 기준점 딕셔너리는 Manager가 들고, 기준점의 의미는 Conditions.CounterOf가 정했다.
    /// 지금은 그 규칙 전체가 여기 있고, Manager는 수명주기와 화면만 남는다.
    ///
    /// <b>진행 규칙(하이브리드)</b>: 매 판정마다 <i>미완료 스텝 전체</i>를 평가한다. 현재 스텝뿐 아니라
    /// 뒤쪽 스텝까지 함께 보기 때문에, 플레이어가 앞질러 해버린 단계는 자기 차례가 오기 전에 완료로
    /// 찍혀 그냥 지나간다. "이미 할 줄 아는 것 같으면 다음 문구"가 별도 코드 없이 이 루프 하나로 나온다.
    ///
    /// 시간이 지난다고 넘어가지는 않는다 — 그 동작을 해내야만 다음으로 간다(사용자 확정 사항).
    /// </summary>
    public sealed class TutorialProgress
    {
        readonly List<TutorialStepSO> _steps;
        readonly TutorialObserver _world;
        readonly HashSet<string> _completed = new();
        readonly Dictionary<string, int[]> _baseline = new();   // 스텝 id → 조건별 기준점

        /// <summary>새 안내가 뜰 때 판정을 미루는 고정분(초) — 카드가 들어오는 연출 시간.
        /// 스텝의 minSeconds는 여기에 더해지므로 순수한 "읽을 시간"이 된다.</summary>
        readonly float _leadInSeconds;

        TutorialStepSO _current;
        int _currentIndex;
        float _judgeFrom;   // 이 시각(unscaled) 전에는 어떤 스텝도 완료로 찍지 않는다
        bool _skipped;

        public TutorialProgress(List<TutorialStepSO> steps, TutorialObserver world, float leadInSeconds)
        {
            _steps = steps ?? new List<TutorialStepSO>();
            _world = world;
            _leadInSeconds = leadInSeconds;
        }

        // ── 공개 상태 ──

        public TutorialStepSO CurrentStep => _current;
        /// <summary>1부터 시작하는 현재 스텝 번호 — HUD의 "n/전체" 표시용.</summary>
        public int CurrentIndex => _currentIndex;
        public int StepCount => _steps.Count;
        public int CompletedCount => _completed.Count;
        public bool Skipped => _skipped;
        public bool IsFinished => _skipped || _current == null;

        /// <summary>판정 유예가 끝나기까지 남은 시간 — 디버그 표시용.</summary>
        public float JudgeHoldRemaining(float nowUnscaled) => Mathf.Max(0f, _judgeFrom - nowUnscaled);

        /// <summary>현재 안내가 바뀔 때. 끝났으면 null이 온다. (Runtime 컨벤션 — On 접두사 없음)</summary>
        public event Action<TutorialStepSO> StepChanged;
        public event Action TutorialFinished;

        // ─────────────────────────── 판정 ───────────────────────────

        /// <summary>
        /// 미완료 스텝을 전부 평가하고, 남은 것 중 가장 앞선 것을 현재 안내로 삼는다.
        /// 판정 유예(_judgeFrom) 중이면 아무 스텝도 완료로 찍지 않는다 — 카드가 들어오기도 전에
        /// 완료로 찍으면 플레이어는 그런 안내가 있었는지도 모른다.
        /// </summary>
        public void Evaluate(float nowUnscaled)
        {
            if (_skipped) return;
            if (nowUnscaled < _judgeFrom) return;

            for (int i = 0; i < _steps.Count; i++)
            {
                var s = _steps[i];
                if (_completed.Contains(s.Id)) continue;

                bool everShown = _baseline.ContainsKey(s.Id);

                // 앞질러 완료를 금지한 스텝은 자기 차례가 오기 전엔 아예 보지 않는다.
                // 숫자키·T처럼 앞선 안내를 따르다 얻어걸리는 동작, 그리고 밤 경고가 여기 해당한다 —
                // 그런 것까지 "이미 할 줄 아네" 규칙에 맡기면 안내가 뜨자마자 사라진다.
                if (s.requireInOrder && !everShown) continue;

                // 기준점이 없는 스텝(= 아직 뜬 적 없는 뒤쪽 스텝)은 null(=전부 0) — 절대값으로 판정되어 자동 완료된다
                int[] baseline = everShown ? _baseline[s.Id] : null;
                if (s.Evaluate(_world, baseline)) _completed.Add(s.Id);
            }

            SelectCurrent(nowUnscaled);
        }

        void SelectCurrent(float nowUnscaled)
        {
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
                _baseline[_current.Id] = _current.CounterOf(_world);

                // 그리고 잠시 판정을 멈춘다. 카드가 다 들어오는 데 걸리는 시간을 더하므로
                // minSeconds는 순수하게 "읽을 시간"이다. 이게 없으면 한 번의 동작이 여러 안내를
                // 동시에 만족시켜 카드가 두세 장씩 스쳐 지나간다.
                _judgeFrom = nowUnscaled + _leadInSeconds + Mathf.Max(0f, _current.minSeconds);
            }

            StepChanged?.Invoke(_current);
            if (_current == null) TutorialFinished?.Invoke();
        }

        // ─────────────────────── 외부 조작 ───────────────────────

        /// <summary>남은 안내를 전부 접는다. 세이브에 남으므로 다시 켜려면 ResetProgress.</summary>
        public void SkipAll()
        {
            _skipped = true;
            _current = null;
            _currentIndex = 0;
            StepChanged?.Invoke(null);
            TutorialFinished?.Invoke();
        }

        /// <summary>처음부터 다시. 진행도만 지우고 관측 카운터는 그대로 둔다(이미 한 것은 이미 한 것이다).</summary>
        public void Reset()
        {
            _skipped = false;
            _completed.Clear();
            _baseline.Clear();
            _current = null;
            _currentIndex = 0;
            _judgeFrom = 0f;
        }

        // ─────────────────────── 세이브 연동 ───────────────────────

        public List<string> CaptureCompleted() => new List<string>(_completed);

        /// <summary>
        /// 세이브에서 되돌린다. 기준점은 일부러 버린다 — 불러온 직후의 카운터로 다시 잡아야
        /// "여기서부터 n번 더"가 맞다(옛 기준점을 쓰면 이미 채운 것으로 오인된다).
        /// </summary>
        public void Restore(IEnumerable<string> completedIds, bool skipped)
        {
            _completed.Clear();
            if (completedIds != null)
                foreach (var id in completedIds)
                    if (!string.IsNullOrEmpty(id)) _completed.Add(id);

            _baseline.Clear();
            _skipped = skipped;
            _current = null;
            _currentIndex = 0;
            _judgeFrom = 0f;
        }
    }
}
