using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using CoreDawn.FPS;
using CoreDawn.Factory;
using CoreDawn.Data;
using CoreDawn.UI;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    /// <summary>
    /// 튜토리얼의 씬 접점 — 수명주기·틱 박자·화면 연결만 남긴 얇은 겉면.
    ///
    /// 실제 일은 셋이 나눠 한다. 이 분업이 곧 파일 지도다:
    ///   관측  <see cref="TutorialObserver"/>          — 세계를 세기만 한다 (스텝을 모른다)
    ///   조건  <see cref="TutorialConditionSO"/>          — 스텝에 조합하는 판정 모듈 (서브에셋, 임포터가 관리)
    ///   진행  <see cref="TutorialProgress"/>           — 완료·기준점·현재 안내 선정
    ///   표시  <see cref="TutorialHUD"/>                — 우상단 카드 그리기·연출
    ///
    /// <b>씬을 고치지 않는다.</b> SaveManager와 같은 방식으로 스스로 생겨나 DontDestroyOnLoad로 산다 —
    /// 게임플레이 씬 9개와 GameUI 프리팹을 한 줄도 건드리지 않으므로 팀원의 씬 병합과 충돌하지 않는다.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    public class TutorialManager : MonoBehaviour
    {
        public static TutorialManager Instance { get; private set; }

        /// <summary>폴링 주기. 매 프레임 돌 이유가 없는 것들(인벤토리·건물 수·광맥)의 간격.</summary>
        const float TickInterval = 0.2f;

        TutorialObserver _world;
        TutorialProgress _progress;
        TutorialHUD _hud;
        TutorialInputProbe _probe;

        string _hudShownId;
        float _nextTick;

        // ── 공개 상태 — 전부 진행 상태기로 위임 ──

        public TutorialStepSO CurrentStep => _progress?.CurrentStep;
        public bool IsFinished => _progress == null || _progress.IsFinished;
        public bool Skipped => _progress != null && _progress.Skipped;
        public int StepCount => _progress?.StepCount ?? 0;
        public int CompletedCount => _progress?.CompletedCount ?? 0;

        /// <summary>현재 안내가 바뀔 때. 끝났으면 null이 온다.</summary>
        public event Action<TutorialStepSO> StepChanged;
        public event Action TutorialFinished;

        // ─────────────────────────── 부팅 ───────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // 이 어트리뷰트는 플레이 세션에서 첫 씬 로드 직후 딱 한 번만 불린다. 타이틀에서
            // 시작하면 그 시점엔 플레이어가 없어 그냥 지나가는데, 재시도 장치가 없으면
            // 그 뒤 World로 넘어가도 튜토리얼이 영영 생기지 않는다 — 그래서 GameBootstrap과
            // 같은 방식으로 씬 전환(Single 로드)마다 다시 검사한다.
            // 이름 있는 메서드를 빼고 다시 거는 것도 같은 이유다 — 도메인 리로드를 끈 환경에서는
            // static 구독이 플레이를 넘어 살아남는다.
            SceneManager.sceneLoaded -= OnSceneLoadedTrySpawn;
            SceneManager.sceneLoaded += OnSceneLoadedTrySpawn;
            TrySpawn();
        }

        static void OnSceneLoadedTrySpawn(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) TrySpawn();
        }

        static void TrySpawn()
        {
            if (Instance != null) return;

            // GameBootstrap과 같은 규칙 — 플레이어가 없는 씬(타이틀·순수 테스트)은 오염시키지 않는다
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

            List<TutorialStepSO> steps = db.BuildOrdered();
            if (steps.Count == 0)
            {
                Debug.LogWarning("[Tutorial] 스텝이 하나도 없습니다 — TutorialDatabase.asset의 steps를 채우세요.");
                enabled = false;
                return;
            }

            _world = new TutorialObserver();
            _hud = new TutorialHUD();

            // 판정 유예의 고정분은 카드가 들어오는 연출 시간 — UI 상수를 진행기가 직접 알 필요는
            // 없으므로 여기서 건네준다 (Progress는 UI를 모른 채로 남는다)
            _progress = new TutorialProgress(steps, _world, TutorialHUD.LeadInSeconds);
            _progress.StepChanged += s => StepChanged?.Invoke(s);
            _progress.TutorialFinished += () => TutorialFinished?.Invoke();

            _probe = gameObject.AddComponent<TutorialInputProbe>();
            _world.AttachProbe(_probe);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            _world?.Detach();
            _hud?.Kill();   // 남은 트윈이 죽은 VisualElement를 물고 돌지 않게
            Instance = null;
        }

        // ─────────────────────────── 진행 ───────────────────────────

        void Update()
        {
            if (_progress == null || _progress.Skipped) return;

            _world.UpdateFast(Time.deltaTime);

            // 일시정지(timeScale 0) 중에도 HUD 바인딩은 이어져야 하므로 unscaled로 잰다
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickInterval;

            _world.Tick();

            // 완료 판정에만 뜸을 들인다(판정 유예는 Progress가 안다). 카드 연출(RefreshHud)은
            // 그동안에도 계속 돈다 — 안 그러면 들어오는 중인 카드가 다 들어오지도 못하고 도로 나간다.
            _progress.Evaluate(Time.unscaledTime);

            RefreshHud();
        }

        void RefreshHud()
        {
            if (!_hud.IsBound)
            {
                if (!_hud.TryBind()) return;
                _hudShownId = null;   // 새로 붙었으면 다시 그린다 (HUD 재활성·씬 전환)
            }

            var current = _progress.CurrentStep;
            string wantId = current != null ? current.Id : null;
            if (_hudShownId == wantId) return;

            _hudShownId = wantId;
            _hud.PlayTransition(current, _progress.CurrentIndex, _progress.StepCount);
        }

        // ─────────────────────── 외부 조작 ───────────────────────

        /// <summary>남은 안내를 전부 접는다. 세이브에 남으므로 다시 켜려면 ResetProgress.</summary>
        public void SkipAll()
        {
            if (_progress == null) return;
            _progress.SkipAll();
            _hudShownId = null;
            _hud?.PlayTransition(null, 0, 0);   // 접을 때도 오른쪽으로 미끄러져 나간다
        }

        /// <summary>처음부터 다시. 진행도만 지우고 관측 카운터는 그대로 둔다(이미 한 것은 이미 한 것이다).</summary>
        public void ResetProgress()
        {
            if (_progress == null) return;
            _progress.Reset();
            _hudShownId = null;
            _nextTick = 0f;
        }

        // ─────────────────────── 세이브 연동 ───────────────────────

        public List<string> CaptureCompleted() => _progress?.CaptureCompleted() ?? new List<string>();

        /// <summary>세이브에서 되돌린다. 기준점은 진행기가 복원 후 카운터로 새로 잡는다.</summary>
        public void RestoreProgress(IEnumerable<string> completedIds, bool skipped)
        {
            if (_progress == null) return;
            _progress.Restore(completedIds, skipped);
            _hudShownId = null;
            _nextTick = 0f;
        }

        /// <summary>eval로 상태를 들여다볼 때 쓰는 한 줄 요약.</summary>
        public string DebugState()
            => $"step={(CurrentStep != null ? CurrentStep.Id : "(none)")} {_progress?.CurrentIndex ?? 0}/{StepCount} " +
               $"done={CompletedCount} skipped={Skipped} " +
               $"move={_world?.MoveSeconds:0.0}s look={_world?.LookSeconds:0.0}s sprint={_world?.SprintSeconds:0.0}s " +
               $"jump={_world?.JumpCount} slide={_world?.SlideCount} " +
               $"inv={_world?.InventoryOpened} build={_world?.BuildModeEntered} weapon={_world?.WeaponEquipped} " +
               $"hold={_progress?.JudgeHoldRemaining(Time.unscaledTime):0.0}s " +
               $"mined={_world?.MinedTotal} placed={_world?.PlacedCount} belts={_world?.PlacedBelts} " +
               $"hotbar={_world?.HotbarSwitches} demo={_world?.DemolishedCount} " +
               $"craftedWeapon={_world?.CraftedOfType(ItemType.Weapon)} beltShape={_world?.BeltShapeCycles} " +
               $"tier={_world?.CoreTier} nights={_world?.NightsStarted}/{_world?.NightsSurvived}";
    }
}
