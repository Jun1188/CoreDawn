using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.Navigation;
using CoreDawn.Sim;
using CoreDawn.Data;

namespace CoreDawn.Combat
{
    /// <summary>
    /// 밤 웨이브의 뷰 어댑터. 판단(점수·둥지 선택·버스트·명단 추첨·진입로 무리·밤 종료)은 심 <see cref="WaveSystem"/>이 하고,
    /// 여기는 심이 세운 몬스터 엔티티에 프리팹 뷰를 붙이고(<see cref="MonsterSpawner.AttachView"/>), 뷰 목록(HUD 적 수·위협 표시)을 들고,
    /// 둥지의 낮 방어자 스폰(<see cref="SpawnNestDefenders"/>)과 세이브 복원(<see cref="RestoreMonster"/>)의 프리팹 생성만 맡는다.
    /// </summary>
    [Serializable]
    public class WaveSpawnManager
    {
        [Tooltip("스폰 높이 보정")]
        [SerializeField] private float spawnHeight = 0f;

        private GridManager grid;
        private Transform parent;
        private WaveSystem system;
        private readonly List<MonsterView> monsters = new List<MonsterView>();

        public IReadOnlyList<MonsterView> Monsters => monsters;
        public WaveSystem System => system;

        public int AliveCount
        {
            get
            {
                int count = 0;
                foreach (var monster in monsters)
                    if (monster != null && !monster.IsDead) count++;
                return count;
            }
        }

        public void Initialize(GridManager grid, Transform parent, WaveSystem waveSystem)
        {
            this.grid = grid;
            this.parent = parent;
            if (system != null) system.Spawned -= OnSimSpawned;
            system = waveSystem;
            if (system != null) system.Spawned += OnSimSpawned;
            if (grid == null)
                Debug.LogWarning("[WaveSpawnManager] GridManager가 없습니다. 지면 스냅이 안 될 수 있습니다.");
        }

        public void Dispose()
        {
            if (system != null) system.Spawned -= OnSimSpawned;
            system = null;
        }

        public void Tick() => CleanupDead();

        // 심이 세운 웨이브 몬스터 — 프리팹만 붙인다. 위치·종류·버프는 이미 심의 것
        private void OnSimSpawned(Entity entity, WaveSpawnKind kind)
        {
            var data = MonsterAssets.OfEntity(entity);   // 엔티티가 조립된 정의(Entity.Def) → 뷰 에셋(프리팹). 5a-3 카탈로그 전까지 SO
            var view = MonsterSpawner.AttachView(entity, data, parent);
            if (view == null) return;
            SnapToGround(view.gameObject, pushToSim: true);
            monsters.Add(view);
        }

        public void DespawnAll()
        {
            foreach (var m in monsters)
                if (m != null) UnityEngine.Object.Destroy(m.gameObject);
            monsters.Clear();
        }

        /// <summary>
        /// 둥지 방어 몬스터 스폰. <paramref name="spawnSlots"/>는 둥지가 판정한 "지금 스폰 가능한 자리들"(NestView.GetDaySpawnableSlots) —
        /// 거리·가림 규칙은 반경 값을 소유한 둥지 쪽에 있다. null이면 모든 활성 포인트를 쓴다.
        /// </summary>
        public void SpawnNestDefenders(NestView nest, PlayerView target, int amount,
                                       List<NestView.DefenderSpawnSlot> spawnSlots = null,
                                       MonsterView escortBoss = null)
        {
            if (spawnSlots == null) spawnSlots = nest.GetAllActiveDefenderSlots();
            if (spawnSlots.Count == 0 || amount <= 0) return;

            for (int i = 0; i < amount; i++)
            {
                NestView.DefenderSpawnSlot slot = spawnSlots[i % spawnSlots.Count];
                var monster = InstantiateMonster(nest.DefenderData, slot.position, Quaternion.identity);
                SnapToGround(monster.gameObject, pushToSim: true);

                var zone = nest.GetComponent<NestEngagementZone>();
                if (zone != null || escortBoss != null)
                    monster.SetAsNestDefender(target, zone, escortBoss);
                else
                    monster.SetAsNestDefender(target);
            }
            Debug.Log(escortBoss != null
                ? $"[WaveSpawnManager] 보스 교전 지원군 {amount}마리를 스폰했습니다."
                : $"[WaveSpawnManager] 둥지 근처에 방어 몬스터 {amount}마리를 스폰했습니다.");
        }

        /// <summary>세이브 복원 전용 — 저장된 자리에 몬스터를 되살린다(지형 스냅 없음: 저장 좌표가 이미 지형 위다).</summary>
        public MonsterView RestoreMonster(Vector3 position, Quaternion rotation, MonsterDataSO data = null)
        {
            var monster = InstantiateMonster(data, position, rotation);
            monster.transform.SetPositionAndRotation(position, rotation);
            return monster;
        }

        private void CleanupDead()
        {
            for (int i = monsters.Count - 1; i >= 0; i--)
            {
                var m = monsters[i];
                if (m == null) { monsters.RemoveAt(i); continue; }
                if (m.IsDead && !m.gameObject.activeInHierarchy)
                {
                    UnityEngine.Object.Destroy(m.gameObject);
                    monsters.RemoveAt(i);
                }
            }
        }

        /// <summary>종류 데이터로 몬스터(심 + 뷰)를 세운다 — 둥지 방어자·세이브 복원이 지나는 관문.</summary>
        private MonsterView InstantiateMonster(MonsterDataSO data, Vector3 position, Quaternion rotation)
        {
            var monster = MonsterSpawner.Spawn(data, position, rotation, parent);
            monsters.Add(monster);
            return monster;
        }

        // 지면 스냅 — 심의 위치는 y=0 평면이라 콜라이더 바닥을 지표에 맞춘다. 심이 정본이므로 심 위치도 함께 올린다.
        private void SnapToGround(GameObject go, bool pushToSim)
        {
            float surfaceY = grid != null ? grid.SurfaceY : go.transform.position.y;
            var col = go.GetComponentInChildren<Collider>();
            if (col != null)
            {
                float bottom = col.bounds.min.y;
                go.transform.position += Vector3.up * (surfaceY - bottom + 0.02f + spawnHeight);
            }
            else
            {
                var pos = go.transform.position;
                pos.y = surfaceY + spawnHeight;
                go.transform.position = pos;
            }
            if (pushToSim)
            {
                var view = go.GetComponent<MonsterView>();
                if (view != null && view.Entity != null) view.Entity.Position = go.transform.position;
            }
        }
    }
}
