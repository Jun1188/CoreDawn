using UnityEngine;

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
}
