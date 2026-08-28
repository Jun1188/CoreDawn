using System;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.DayTime;
using CoreDawn.Save;
using CoreDawn.UI;

namespace CoreDawn.Managers
{
    /// <summary>
    /// 게임 전역 매니저 — 세이브/로드 훅과 게임 수준 상태를 담당.
    /// 낮/밤 시간은 TimeManager(→ DayCycle)가 전담한다.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        // ── 코어 진행도(티어) — CoreBehavior가 요구량 충족 시 AdvanceTier를 호출한다.
        public int UnlockedTier { get; private set; } = 0;
        public event Action<int> TierUnlocked;

        public bool IsTierUnlocked(int requiredTier) => requiredTier <= UnlockedTier;

        public void AdvanceTier(int newTier)
        {
            if (newTier <= UnlockedTier) return;
            UnlockedTier = newTier;
            Debug.Log($"====== 🚀 코어 진화 — Tier {UnlockedTier} 해금 ======");
            TierUnlocked?.Invoke(UnlockedTier);
        }

        /// <summary>
        /// 세이브 복원 전용 — 티어를 저장된 값으로 되돌린다 (내려가는 것도 허용).
        /// 진화 연출 로그는 남기지 않지만 구독자에게는 알린다 — 빌드 메뉴·레시피 목록이 다시 그려져야 한다.
        /// </summary>
        public void RestoreTier(int tier)
        {
            UnlockedTier = Mathf.Max(0, tier);
            TierUnlocked?.Invoke(UnlockedTier);
        }

        /// <summary>
        /// 씬을 넘어 살아남는 진행도를 초기 상태로. 새 게임·다른 세이브 불러오기 직전에 호출된다.
        /// 이게 없으면 이전 세션의 티어가 새로 시작한 게임에 그대로 따라붙는다.
        /// </summary>
        public void ResetProgress()
        {
            UnlockedTier = 0;
            TierUnlocked?.Invoke(UnlockedTier);
        }

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (TimeManager.Instance == null) return;

            // 기획: 밤을 넘겨 아침이 밝으면 자동 저장 (1일차 시작은 제외)
            TimeManager.Instance.Cycle.DayStarted += day => { if (day > 1) SaveGame(); };

            // 밤이 시작되기 <b>직전</b>에도 저장한다 — 코어가 부서질 수 있는 유일한 시간이라,
            // 게임오버 후 되돌아갈 지점이 여기여야 밤을 통째로 다시 살지 않는다.
            // 밤 자체는 저장 대상이 아니므로(웨이브 진행이 스키마에 없다) 그 직전이 마지막 지점이다.
            TimeManager.Instance.Cycle.NightImminent += _ => SaveNightfall();
        }

        // ── 기존 코드 호환용 (시간 관련 조회는 TimeManager로 위임)
        public bool IsBuildingAllowed() =>
            TimeManager.Instance == null || TimeManager.Instance.IsBuildingAllowed;

        // ── 세이브/로드 훅 — 실제 작업은 SaveManager가 한다

        /// <summary>아침마다 불리는 자동 저장. 정책(켬/끔, 슬롯 순환)은 SaveManager가 판단한다.</summary>
        public void SaveGame()
        {
            if (SaveManager.Instance == null) return;

            int day = TimeManager.Instance != null ? TimeManager.Instance.DayNumber : 0;
            if (SaveManager.Instance.AutoSaveOnDayStart())
                Debug.Log($"====== 💾 [{day - 1}일차 완료] 자동 저장 완료 ======");
        }

        /// <summary>밤이 시작될 때마다 불리는 자동 저장. 정책(켬/끔, 슬롯 순환)은 SaveManager가 판단한다.</summary>
        public void SaveNightfall()
        {
            if (SaveManager.Instance == null) return;

            int day = TimeManager.Instance != null ? TimeManager.Instance.DayNumber : 0;
            if (SaveManager.Instance.AutoSaveOnNightStart())
                Debug.Log($"====== 💾 [{day}일차 밤 시작] 자동 저장 완료 ======");
        }

        /// <summary>
        /// 구 uGUI 일시정지 메뉴의 Load 버튼 호환 경로 — 슬롯을 고를 수 없으므로 가장 최근 세이브를 연다.
        /// 새 UI(SaveLoadPanelView)는 슬롯을 직접 지정해 SaveManager.Load를 호출한다.
        /// </summary>
        public void LoadGame()
        {
            if (SaveManager.Instance == null) return;
            if (!SaveManager.Instance.LoadMostRecent())
                Debug.LogWarning("[시스템] 불러올 세이브가 없습니다.");
        }
    }
}
