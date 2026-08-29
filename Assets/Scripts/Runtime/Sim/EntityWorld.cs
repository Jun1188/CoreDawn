using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 엔티티 등록부 — 정체성 발급·조회·제거·반경 질의와 월드 단위 이벤트. 그 이상은 아니다.
    ///
    /// 격자 기하·시계·시스템 목록은 여기 없다: 격자는 공장의 것(FactorySystem.Geometry)이고,
    /// 시계·시스템 묶음은 5단계의 심 루트(WorldRunner)가 가진다. 등록부에 그런 것을 얹으면
    /// "월드 = 등록부 + 지도 + 시계"로 책임이 뭉개진다.
    ///
    /// 반경 질의(<see cref="QueryRadius"/>)는 균일 격자 해시(셀 8m) 위에서 돈다 — 구 SensorComponent의
    /// OverlapSphere(PhysX·레이어) 대체. 위치가 바뀔 때마다 엔티티가 스스로 버킷을 옮기므로(Position setter)
    /// 질의는 이웃 셀만 훑는다. 수백 개체 규모에서 Job·ComputeShader는 필요 없다.
    ///
    /// 순회(<see cref="All"/>) 중에 만들거나 지우면 안 된다 — 사망 처리처럼 순회 중 제거가 필요한 곳은
    /// <see cref="CopyTo"/>로 스냅샷을 뜬 뒤 돈다.
    /// </summary>
    public sealed class EntityWorld
    {
        readonly Dictionary<EntityUUID, Entity> _entities = new Dictionary<EntityUUID, Entity>();

        public int Count => _entities.Count;

        /// <summary>살아 있는(제거되지 않은) 엔티티 전부. 순회 중 생성·제거 금지.</summary>
        public IEnumerable<Entity> All => _entities.Values;

        public event Action<Entity> Created;
        public event Action<Entity> Died;
        public event Action<Entity> Removed;

        public Entity Create(Faction faction, Vector3 position) => Create(EntityUUID.New(), faction, position);

        /// <summary>
        /// 정해진 정체성으로 만든다 — 세이브 복원·서버가 보낸 스폰·클라이언트 예측 확정처럼 id가 먼저 있는 경우.
        /// 같은 id가 이미 있으면 예외(같은 것을 두 번 되살리려는 것이므로 조용히 덮지 않는다).
        /// </summary>
        public Entity Create(EntityUUID id, Faction faction, Vector3 position)
        {
            if (id.IsNone) throw new ArgumentException("EntityUUID.None으로는 만들 수 없다", nameof(id));
            if (_entities.ContainsKey(id)) throw new InvalidOperationException($"entity {id} already exists");
            var e = new Entity(this, id, faction, position);
            _entities.Add(e.Id, e);
            AddToBucket(BucketOf(position), e);
            Created?.Invoke(e);
            return e;
        }

        public Entity Get(EntityUUID id) => _entities.TryGetValue(id, out var e) ? e : null;

        public bool Contains(Entity e) => e != null && !e.IsRemoved && e.World == this;

        /// <summary>월드에서 제거. 모듈 OnDetach → Entity.Removed → World.Removed 순. 중복 호출 안전.</summary>
        public void Remove(Entity e)
        {
            if (e == null || e.IsRemoved || e.World != this) return;
            _entities.Remove(e.Id);
            RemoveFromBucket(BucketOf(e.Position), e);
            e.MarkRemoved();
            Removed?.Invoke(e);
        }

        /// <summary>순회 중 제거가 필요한 호출자용 스냅샷. 버퍼를 비우고 채운다.</summary>
        public void CopyTo(List<Entity> buffer)
        {
            buffer.Clear();
            buffer.AddRange(_entities.Values);
        }

        internal void NotifyDied(Entity e) => Died?.Invoke(e);

        /// <summary>전부 제거 — 새 게임·씬 전환. 각 엔티티의 Removed는 발화한다(뷰가 정리할 수 있게).</summary>
        public void Clear()
        {
            var snapshot = new List<Entity>(_entities.Values);
            foreach (var e in snapshot) Remove(e);
        }

        // ── 공간 해시 ─────────────────────────────────────────────────

        /// <summary>버킷 한 변(m). 몬스터 반지름(0.4)·감지 반경(10)·사거리(수십 m) 규모에서 이웃 셀 수와 버킷 밀도의 절충.</summary>
        public const float BucketSize = 8f;

        readonly Dictionary<long, List<Entity>> _buckets = new Dictionary<long, List<Entity>>();

        static long BucketOf(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x / BucketSize);
            int z = Mathf.FloorToInt(p.z / BucketSize);
            return BucketKey(x, z);
        }

        static long BucketKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        void AddToBucket(long key, Entity e)
        {
            if (!_buckets.TryGetValue(key, out var list)) _buckets[key] = list = new List<Entity>();
            list.Add(e);
        }

        void RemoveFromBucket(long key, Entity e)
        {
            if (_buckets.TryGetValue(key, out var list))
            {
                list.Remove(e);
                if (list.Count == 0) _buckets.Remove(key);
            }
        }

        internal void OnEntityMoved(Entity e, Vector3 from, Vector3 to)
        {
            if (e.IsRemoved) return;
            long a = BucketOf(from), b = BucketOf(to);
            if (a == b) return;
            RemoveFromBucket(a, e);
            AddToBucket(b, e);
        }

        /// <summary>
        /// 반경 안의 살아 있는 엔티티를 모은다(3차원 거리). 버퍼를 비우고 채운다.
        /// </summary>
        /// <param name="faction">이 편만. null이면 전부.</param>
        /// <param name="exclude">빼고 볼 엔티티(자기 자신).</param>
        public void QueryRadius(Vector3 center, float radius, Faction? faction, List<Entity> results, Entity exclude = null)
        {
            results.Clear();
            if (radius <= 0f) return;

            float sq = radius * radius;
            int minX = Mathf.FloorToInt((center.x - radius) / BucketSize), maxX = Mathf.FloorToInt((center.x + radius) / BucketSize);
            int minZ = Mathf.FloorToInt((center.z - radius) / BucketSize), maxZ = Mathf.FloorToInt((center.z + radius) / BucketSize);

            for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!_buckets.TryGetValue(BucketKey(x, z), out var list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = list[i];
                        if (e == exclude || !e.IsAlive) continue;
                        if (faction.HasValue && e.Faction != faction.Value) continue;
                        if ((e.Position - center).sqrMagnitude > sq) continue;
                        results.Add(e);
                    }
                }
        }

        /// <summary>
        /// 반경 안에서 가장 가까운 살아 있는 엔티티. minRange보다 가까운 것은 건너뛴다(박격포의 사각).
        /// 거리는 엔티티 위치로 잰다 — 콜라이더 부피가 아니라 — 잡는 쪽과 유지하는 쪽의 기준이 같아야 경계에서 떨지 않는다.
        /// </summary>
        public Entity QueryClosest(Vector3 center, float radius, Faction? faction, float minRange = 0f, Entity exclude = null)
        {
            if (radius <= 0f) return null;

            Entity closest = null;
            float best = radius * radius;
            float minSq = minRange * minRange;
            int minX = Mathf.FloorToInt((center.x - radius) / BucketSize), maxX = Mathf.FloorToInt((center.x + radius) / BucketSize);
            int minZ = Mathf.FloorToInt((center.z - radius) / BucketSize), maxZ = Mathf.FloorToInt((center.z + radius) / BucketSize);

            for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!_buckets.TryGetValue(BucketKey(x, z), out var list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = list[i];
                        if (e == exclude || !e.IsAlive) continue;
                        if (faction.HasValue && e.Faction != faction.Value) continue;
                        float d = (e.Position - center).sqrMagnitude;
                        if (d < minSq || d > best) continue;
                        best = d;
                        closest = e;
                    }
                }
            return closest;
        }
    }

    /// <summary>
    /// "지금의 월드" — 과도기 정적 접근점. 씬 오브젝트(뷰)가 Awake에서 심 엔티티를 만들어 붙이는 동안만 필요하다.
    /// 5단계에서 WorldRunner(심 호스트)가 월드를 소유하면 이 접근점은 그 인스턴스를 가리키거나 사라진다.
    ///
    /// 씬을 넘어 살아남는 이유: 엔티티의 생사는 소유자(FactorySystem·뷰)가 책임지고, 씬이 내려가면
    /// 소유자들이 OnDestroy에서 자기 것을 뺀다. 도메인 리로드를 끈 환경에서 플레이를 넘어 남는 것만 막는다.
    /// </summary>
    public static class SimHost
    {
        static EntityWorld _world;

        public static EntityWorld World => _world ??= new EntityWorld();

        /// <summary>새 월드로 교체 — 새 게임 시작 등. 옛 월드의 엔티티는 Removed를 받는다.</summary>
        public static void Reset()
        {
            _world?.Clear();
            _world = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _world = null;
    }
}
