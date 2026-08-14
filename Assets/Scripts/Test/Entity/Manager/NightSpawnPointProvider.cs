using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-owned source for night assault entrances. Keeping this separate from
/// MonsterNest prevents day boss points from becoming night wave entrances.
/// </summary>
public sealed class NightSpawnPointProvider : MonoBehaviour
{
    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

    public IReadOnlyList<Transform> SpawnPoints => spawnPoints;
}
