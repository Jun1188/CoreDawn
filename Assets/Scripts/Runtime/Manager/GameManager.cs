using System;
using UnityEngine;

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

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // 기획: 밤을 넘겨 아침이 밝으면 자동 저장 (1일차 시작은 제외)
        if (TimeManager.Instance != null)
            TimeManager.Instance.Cycle.DayStarted += day => { if (day > 1) SaveGame(); };
    }

    // ── 기존 코드 호환용 (시간 관련 조회는 TimeManager로 위임)
    public bool IsBuildingAllowed() =>
        TimeManager.Instance == null || TimeManager.Instance.IsBuildingAllowed;

    // ── 세이브/로드 훅 (뼈대 — 세이브 시스템 작업 시 구현)

    public void SaveGame()
    {
        int day = TimeManager.Instance != null ? TimeManager.Instance.DayNumber : 0;
        Debug.Log($"====== 💾 [{day - 1}일차 완료] 데이터 자동 저장 중... ======");
        // TODO: 세이브 시스템 연동 (심 상태는 plain 데이터라 직렬화 준비됨)
    }

    public void LoadGame()
    {
        Debug.Log("====== 📂 세이브 데이터 불러오는 중... ======");
        // TODO: 세이브 시스템 연동
    }
}