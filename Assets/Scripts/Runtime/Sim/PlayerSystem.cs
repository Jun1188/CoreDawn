using System;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 플레이어 시스템 — 플레이어 엔티티(편=Player, Health, Effects)의 생성 주체. 뷰(PlayerView)는 받아서 그리고,
    /// 물리 이동은 뷰가 굴려 위치를 심으로 미러한다(서버 권위 하이브리드 결정). 플레이어는 하나다.
    /// 근접 공격 모듈은 없다 — 플레이어 피해는 총기(투사체 → Effects)만 준다.
    /// </summary>
    public sealed class PlayerSystem : IDisposable
    {
        readonly EntityWorld world;

        /// <summary>살아 있는 플레이어 엔티티. 아직 없거나 제거됐으면 null.</summary>
        public Entity Entity { get; private set; }

        public event Action<Entity> Spawned;
        public event Action<Entity> Despawned;

        public PlayerSystem(EntityWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            world.Removed += OnRemoved;
        }

        /// <summary>플레이어 엔티티를 만든다. 이미 살아 있으면 그것을 돌려준다(씬 재진입·중복 부착 안전).</summary>
        public Entity Spawn(float maxHp, Vector3 position)
        {
            if (Entity != null && !Entity.IsRemoved) return Entity;

            var e = world.Create(Faction.Player, position);
            e.Add(new Health(Math.Max(1f, maxHp)));
            e.Add(new Effects());
            Entity = e;
            Spawned?.Invoke(e);
            return e;
        }

        /// <summary>월드에서 제거 — 뷰가 사라질 때(씬 전환). 죽음(부활 가능)과는 다르다.</summary>
        public void Despawn()
        {
            var e = Entity;
            if (e != null && !e.IsRemoved) world.Remove(e);
        }

        void OnRemoved(Entity e)
        {
            if (!ReferenceEquals(e, Entity)) return;
            Entity = null;
            Despawned?.Invoke(e);
        }

        public void Dispose()
        {
            world.Removed -= OnRemoved;
            Despawn();
            Entity = null;
        }
    }
}
