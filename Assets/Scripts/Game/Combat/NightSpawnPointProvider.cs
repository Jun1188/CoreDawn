using System.Collections.Generic;
using UnityEngine;
namespace CoreDawn.Combat
{
    /// <summary>
    /// Scene-owned source for night assault entrances. Keeping this separate from
    /// MonsterNest prevents day boss points from becoming night wave entrances.
    /// </summary>
    public sealed class NightSpawnPointProvider : MonoBehaviour
    {
        [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

        public IReadOnlyList<Transform> SpawnPoints => spawnPoints;

        /// <summary>맵 데이터로 세울 때 쓴다 — 씬에 손으로 놓지 않고 부트스트랩이 채운다.</summary>
        public void SetSpawnPoints(List<Transform> points)
        {
            spawnPoints = points ?? new List<Transform>();
        }
    }
}
