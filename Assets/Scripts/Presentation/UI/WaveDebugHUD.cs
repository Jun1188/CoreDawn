#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using CoreDawn.Combat;
using CoreDawn.DayTime;
using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.UI
{
    /// <summary>
    /// 밤 웨이브 디버그 오버레이 — F3로 켜고 끈다. 에디터·개발 빌드에서만 존재하며 씬에 넣지 않고 스스로 생긴다.
    /// 점수(총량 배율·남은 점수·버스트 진행·다음 버스트/무리까지 시간)·자극과 버프값·몬스터 수를 매 프레임 읽고,
    /// WaveSystem 이벤트(밤 시작·버스트·진입로 무리·클리어)를 시간표로 남긴다. 디자인 시스템 밖의 개발 도구라 OnGUI.
    /// </summary>
    public sealed class WaveDebugHUD : MonoBehaviour
    {
        const int LogLines = 80;

        public static WaveDebugHUD Instance { get; private set; }
        public bool Visible { get; set; }

        WaveSystem waves;
        readonly List<string> log = new List<string>();
        int pendingTrickle; Vector3 pendingTrickleAt; int pendingTrickleFrame = -1;
        Vector2 scroll; bool stickToBottom = true; bool logDirty;   // 새 줄이 붙으면 바닥으로 — 위로 올려 보는 중이면 따라가지 않는다

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("WaveDebugHUD (F3)");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<WaveDebugHUD>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame) Visible = !Visible;

            var current = BattleManager.Instance != null ? BattleManager.Instance.Waves : null;
            if (!ReferenceEquals(current, waves)) Rebind(current);

            // 진입로 무리는 한 프레임에 여러 마리가 Spawned로 오므로 프레임이 지나면 한 줄로 묶는다
            if (pendingTrickle > 0 && Time.frameCount != pendingTrickleFrame)
            {
                Log($"진입로 무리 +{pendingTrickle} @({pendingTrickleAt.x:0},{pendingTrickleAt.z:0})");
                pendingTrickle = 0;
            }
        }

        void Rebind(WaveSystem next)
        {
            if (waves != null)
            {
                waves.NightStarted -= OnNightStarted; waves.BurstSpawned -= OnBurst; waves.Spawned -= OnSpawned; waves.NightCleared -= OnCleared;
            }
            waves = next;
            if (waves != null)
            {
                waves.NightStarted += OnNightStarted; waves.BurstSpawned += OnBurst; waves.Spawned += OnSpawned; waves.NightCleared += OnCleared;
                Log("WaveSystem 연결");
            }
        }

        void OnDestroy() { Rebind(null); if (Instance == this) Instance = null; }

        void OnNightStarted(int day, float score)
        {
            int living = 0, total = 0; foreach (var n in waves.AllNests()) { total++; if (!n.IsDestroyed) living++; }
            Log($"밤 {day} 시작 — 점수 {score:0}pt (게이트 {waves.Gate}, 둥지 {living}/{total}, 총량 ×{waves.Rule.TotalFactor(living, total):0.00}) 출구 {waves.SelectedPoints.Count} 버스트 {waves.Bursts}회");
        }

        void OnBurst(Vector3 at, int count)
            => Log($"버스트 {waves.BurstsDone}/{waves.Bursts} +{count} @({at.x:0},{at.z:0}) 남은 {waves.Remaining:0}pt");

        void OnSpawned(Entity e, WaveSpawnKind kind)
        {
            if (kind != WaveSpawnKind.Trickle) return;
            if (Time.frameCount != pendingTrickleFrame) { pendingTrickleFrame = Time.frameCount; pendingTrickleAt = e.Position; }
            pendingTrickle++;
        }

        void OnCleared(int day, int killed) => Log($"밤 {day} 클리어 — 점수 몬스터 {killed} 처치");

        void Log(string line)
        {
            float t = waves != null ? waves.Now : 0f;
            log.Add($"[{t,5:0.0}s] {line}");
            if (log.Count > LogLines) log.RemoveAt(0);
            logDirty = true;
        }

        /// <summary>패널 본문 — 화면과 같은 글. 검증·로그용.</summary>
        public string Snapshot()
        {
            var sb = new StringBuilder();
            var tm = TimeManager.Instance;
            if (tm != null) sb.Append(tm.Phase == DayPhase.Day ? "낮" : "밤").Append(' ').Append(tm.DayNumber).Append("일차  시계 ").Append(tm.Cycle.PhaseRemaining.ToString("0.0")).Append('/').Append(tm.Cycle.PhaseDuration.ToString("0")).Append("s\n");
            if (waves == null) { sb.Append("WaveSystem 없음 (BattleManager 대기)"); return sb.ToString(); }

            int living = 0, total = 0; foreach (var n in waves.AllNests()) { total++; if (!n.IsDestroyed) living++; }
            int gate = GameManager.Instance != null ? GameManager.Instance.UnlockedTier : 0;
            var rule = waves.Rule;
            float basePts = rule.BasePoints + (tm != null ? tm.DayNumber : waves.Day) * rule.DayPoints + gate * rule.GatePoints;
            sb.Append($"둥지 {living}/{total}  게이트 {gate}  기본 {basePts:0}pt × 총량 {rule.TotalFactor(living, total):0.00} (살아 있는 몫 {(total > 0 ? (float)living / total : 0f):0.00} + 강화분 {rule.BonusFor(total - living, total):0.00})\n");
            float stim = waves.Stimuli;
            sb.Append($"자극 ×{stim:0.00}");
            foreach (var b in rule.StimulusBuffs) if (b.Spec != null) sb.Append($"  {b.Spec.Id.Replace("coredawn:effect/", "")}={b.ValueAt(stim):0.00}");
            sb.Append('\n');

            if (!waves.Active) sb.Append($"웨이브 없음 (마지막 밤 {waves.Day}: 스폰 {waves.SpawnedCount} 처치 {waves.KilledCount})\n");
            else
            {
                sb.Append($"밤 {waves.Day}  점수 {waves.Score:0}pt  남은 {waves.Remaining:0}pt  버스트 {waves.BurstsDone}/{waves.Bursts}");
                if (waves.BurstsDone < waves.Bursts) sb.Append($"  다음 {Mathf.Max(0f, waves.NextBurstAt - waves.Now):0.0}s");
                sb.Append('\n');
                sb.Append($"점수 몹 {waves.ScoreAlive} 생존 / {waves.SpawnedCount} 스폰 / {waves.KilledCount} 처치 ({waves.KilledFraction:P0})  진입로 무리 {waves.TrickleMonsters.Count}");
                if (waves.KilledFraction < rule.Trickle.UntilKilledFraction && rule.Trickle.Monster != null)
                    sb.Append($"  다음 무리 {Mathf.Max(0f, waves.NextTrickleAt - waves.Now):0.0}s ({rule.Trickle.Group}마리, 진입로 하나 무작위)");
                else sb.Append("  무리 종료");
                sb.Append('\n');
                sb.Append($"출구 {waves.SelectedPoints.Count}  시각 {waves.Now:0.0}s\n");
            }
            sb.Append("── 이벤트 ──\n");
            foreach (var l in log) sb.Append(l).Append('\n');
            return sb.ToString();
        }

        void OnGUI()
        {
            if (!Visible) return;
            const float W = 640f, H = 420f;
            var rect = new Rect(Screen.width - W - 12f, 12f, W, H);
            GUI.Box(rect, "웨이브 디버그 (F3)");
            var inner = new Rect(rect.x + 8f, rect.y + 22f, rect.width - 16f, rect.height - 62f);
            // 스크롤 — 내용 높이를 재서 뷰보다 길면 스크롤, 새 로그가 붙으면 바닥으로(사용자가 위로 올려 둔 동안은 그대로)
            string text = Snapshot();
            var style = GUI.skin.textArea;
            float innerW = inner.width - 18f;
            float contentH = Mathf.Max(inner.height, style.CalcHeight(new GUIContent(text), innerW));
            float maxY = Mathf.Max(0f, contentH - inner.height);
            if (logDirty) { if (stickToBottom) scroll.y = maxY; logDirty = false; }
            var next = GUI.BeginScrollView(inner, scroll, new Rect(0f, 0f, innerW, contentH));
            if (!Mathf.Approximately(next.y, scroll.y)) stickToBottom = next.y >= maxY - 2f;   // 사용자가 움직였다 — 바닥이면 다시 따라간다
            scroll = next;
            GUI.TextArea(new Rect(0f, 0f, innerW, contentH), text, style);
            GUI.EndScrollView();
            float by = rect.y + rect.height - 34f;
            if (GUI.Button(new Rect(rect.x + 8f, by, 120f, 26f), "배속 x1 / x5")) Time.timeScale = Time.timeScale >= 5f ? 1f : 5f;
            if (TimeManager.Instance != null && GUI.Button(new Rect(rect.x + 136f, by, 120f, 26f), "밤 조기 종료")) TimeManager.Instance.EndNightEarly();
            if (GUI.Button(new Rect(rect.x + 264f, by, 120f, 26f), "이벤트 지우기")) { log.Clear(); scroll = Vector2.zero; stickToBottom = true; }
        }
    }
}
#endif
