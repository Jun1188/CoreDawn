using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 엔티티 등록부 — 번호 발급·조회·제거와 월드 단위 이벤트. 그 이상은 아니다.
    ///
    /// 격자 기하·시계·시스템 목록은 여기 없다: 격자는 공장의 것(FactorySystem.Geometry)이고,
    /// 시계·시스템 묶음은 5단계의 심 루트(WorldRunner)가 가진다. 등록부에 그런 것을 얹으면
    /// "월드 = 등록부 + 지도 + 시계"로 책임이 뭉개진다.
    ///
    /// 순회(<see cref="All"/>) 중에 만들거나 지우면 안 된다 — 사망 처리처럼 순회 중 제거가 필요한 곳은
    /// <see cref="CopyTo"/>로 스냅샷을 뜬 뒤 돈다.
    /// </summary>
    public sealed class EntityWorld
    {
        readonly Dictionary<EntityId, Entity> _entities = new Dictionary<EntityId, Entity>();
        ulong _next = 1;   // 0 = None

        /// <summary>다음에 발급될 번호. 세이브 헤더가 저장하고 복원은 <see cref="RestoreNextId"/>로.</summary>
        public ulong NextId => _next;

        public int Count => _entities.Count;

        /// <summary>살아 있는(제거되지 않은) 엔티티 전부. 순회 중 생성·제거 금지.</summary>
        public IEnumerable<Entity> All => _entities.Values;

        public event Action<Entity> Created;
        public event Action<Entity> Died;
        public event Action<Entity> Removed;

        public Entity Create(Faction faction, Vector3 position)
        {
            var e = new Entity(this, new EntityId(_next++), faction, position);
            _entities.Add(e.Id, e);
            Created?.Invoke(e);
            return e;
        }

        public Entity Get(EntityId id) => _entities.TryGetValue(id, out var e) ? e : null;

        public bool Contains(Entity e) => e != null && !e.IsRemoved && e.World == this;

        /// <summary>월드에서 제거. 모듈 OnDetach → Entity.Removed → World.Removed 순. 중복 호출 안전.</summary>
        public void Remove(Entity e)
        {
            if (e == null || e.IsRemoved || e.World != this) return;
            _entities.Remove(e.Id);
            e.MarkRemoved();
            Removed?.Invoke(e);
        }

        /// <summary>순회 중 제거가 필요한 호출자용 스냅샷. 버퍼를 비우고 채운다.</summary>
        public void CopyTo(List<Entity> buffer)
        {
            buffer.Clear();
            buffer.AddRange(_entities.Values);
        }

        /// <summary>
        /// 세이브 복원 전용 — 번호 발급 지점을 저장 시점으로 되돌린다. 엔티티를 복원하기 전에 호출할 것.
        /// 이미 발급된 번호보다 뒤로 물리지는 않는다(중복 발급 방지).
        /// </summary>
        public void RestoreNextId(ulong next)
        {
            if (next > _next) _next = next;
        }

        internal void NotifyDied(Entity e) => Died?.Invoke(e);

        /// <summary>전부 제거 — 새 게임·씬 전환. 각 엔티티의 Removed는 발화한다(뷰가 정리할 수 있게).</summary>
        public void Clear()
        {
            var snapshot = new List<Entity>(_entities.Values);
            foreach (var e in snapshot) Remove(e);
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
