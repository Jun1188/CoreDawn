using UnityEngine;
using CoreDawn.Combat;

namespace CoreDawn.Data
{
    /// <summary>
    /// 일차별 밤 공세 — 규모(몇 마리·간격)와 무엇이 나오는가(종류), 그리고 그날의 강약(버프).
    ///
    /// 강약은 HP 덮어쓰기가 아니라 <b>효과</b>로 준다: 주는 피해 배율(AttackModifier)·받는 피해 배율(IncomingDamage)을
    /// 스폰 시 영구 효과로 건다. 예전의 <c>monsterMaxHp</c>는 프리팹·wave_settings.json과 세 곳이 서로 덮어쓰던 값이라 없앴다 —
    /// 같은 종류가 밤마다 다른 최대 체력을 갖는 것보다, 종류는 하나이고 그날의 버프가 다른 쪽이 데이터로 읽힌다.
    /// </summary>
    [CreateAssetMenu(fileName = "WaveData", menuName = "Factory/Wave Data")]
    public class WaveDataSO : GameDataSO
    {
        [Header("Wave Settings")]
        [Tooltip("이 웨이브가 발생하는 일차(Day)")]
        public int day;

        [Tooltip("이 웨이브 발생에 필요한 코어 티어 (CoreTier). 일차 조건을 만족하더라도 코어 티어가 낮으면 이전 웨이브가 반복될 수 있음.")]
        public int requiredCoreTier;

        [Tooltip("웨이브 시 생성되는 몬스터의 총량 (둥지 파괴 전 기준)")]
        public int baseAmount;

        [Tooltip("동시 생존 몬스터 상한")]
        public int maxAliveAmount;

        [Tooltip("스폰 시도 간격(초)")]
        public float spawnInterval;

        [Header("무엇이 나오는가")]
        [Tooltip("이 웨이브가 스폰하는 몬스터 종류. 비우면 MonsterDatabase의 기본 종류 — 편집기가 경고한다.")]
        public MonsterDataSO monster;

        [Tooltip("스폰된 몬스터에게 거는 영구 효과 — 그날의 강약. 예: Effect:DamageTaken 0.75(받는 피해 −25%), Effect:AttackUp 1.2(주는 피해 +20%).")]
        public EffectEntry[] buffs;
    }
}
