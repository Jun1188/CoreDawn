using System;
using System.IO;
using UnityEngine;

namespace CoreDawn.Combat
{
    [Serializable]
    public class WaveBalanceEntry
    {
        [Tooltip("이 항목이 적용되기 시작하는 일차(Day)")]
        public int day = 1;

        [Tooltip("밤 웨이브 몬스터 최대 HP. 0 이하면 프리팹 기본값을 그대로 쓴다.")]
        public float monsterMaxHp;
    }

    [Serializable]
    public class WaveBalanceData
    {
        public WaveBalanceEntry[] entries;
    }

    /// <summary>
    /// 웨이브 밸런스 JSON(StreamingAssets/wave_settings.json) 로더.
    /// 일차(Day)가 지날수록 밤 웨이브 몬스터의 최대 HP를 바꿀 수 있다 —
    /// 현재 일차 이하의 day 중 가장 큰 항목이 적용된다(계단식).
    /// 파일이 없거나 항목이 비어 있으면 0을 돌려 프리팹 기본 HP를 유지한다.
    /// StreamingAssets라 에디터·빌드 모두 같은 경로로 읽고, 빌드 후에도 파일만 고치면 반영된다.
    /// </summary>
    public static class WaveBalanceSettings
    {
        private const string FileName = "wave_settings.json";

        private static WaveBalanceData data;
        private static bool loaded;

        private static string FilePath => Path.Combine(Application.streamingAssetsPath, FileName);

        /// <summary>해당 일차의 밤 웨이브 몬스터 최대 HP. 0 이하 = 프리팹 기본값 유지.</summary>
        public static float GetNightMonsterMaxHp(int day)
        {
            EnsureLoaded();
            if (data?.entries == null) return 0f;

            float result = 0f;
            int bestDay = int.MinValue;
            foreach (var entry in data.entries)
            {
                if (entry == null || entry.day > day) continue;
                if (entry.day < bestDay) continue;
                bestDay = entry.day;
                result = entry.monsterMaxHp;
            }
            return result;
        }

        /// <summary>플레이 중 JSON을 고쳐 다시 읽고 싶을 때(다음 조회 때 재로드).</summary>
        public static void Reload() => loaded = false;

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            data = null;

            try
            {
                if (!File.Exists(FilePath))
                {
                    Debug.LogWarning($"[WaveBalanceSettings] {FileName}이 없어 프리팹 기본 HP를 사용합니다: {FilePath}");
                    return;
                }

                data = JsonUtility.FromJson<WaveBalanceData>(File.ReadAllText(FilePath));
                int count = data?.entries?.Length ?? 0;
                Debug.Log($"[WaveBalanceSettings] 웨이브 밸런스 {count}개 항목 로드 완료.");
            }
            catch (Exception e)
            {
                data = null;
                Debug.LogError($"[WaveBalanceSettings] {FileName} 파싱 실패 — 프리팹 기본 HP를 사용합니다: {e.Message}");
            }
        }
    }
}
