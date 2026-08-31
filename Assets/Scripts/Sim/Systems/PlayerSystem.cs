using System;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 플레이어 시스템 — 플레이어 엔티티(편=Player, Health, Effects)의 생성 주체. 뷰(PlayerView)는 받아서 그리고,
    /// 물리 이동은 뷰가 굴려 위치를 심으로 미러한다(서버 권위 하이브리드 결정). 플레이어는 하나다.
    /// 근접 공격 모듈은 없다 — 플레이어 피해는 총기(WeaponModule이 승인, 뷰가 투사체 → Effects)만 준다.
    /// </summary>
    public sealed class PlayerSystem : IDisposable
    {
        readonly EntityWorld world;

        /// <summary>살아 있는 플레이어 엔티티. 아직 없거나 제거됐으면 null.</summary>
        public Entity Entity { get; private set; }

        /// <summary>플레이어 심 시계(초) — 무기의 연사 간격·재장전이 이 시계로 돈다. 러너가 매 프레임 Tick으로 올린다.</summary>
        public float Now { get; private set; }

        public event Action<Entity> Spawned;
        public event Action<Entity> Despawned;

        public PlayerSystem(EntityWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            world.Removed += OnRemoved;
        }

        /// <summary>플레이어 엔티티를 만든다. 이미 살아 있으면 그것을 돌려준다(씬 재진입·중복 부착 안전).</summary>
        /// <summary>정의(coredawn:entity/player)로 조립 — Health·Effects·Inventory·Crafter가 팩 json에서 온다. 이미 있으면 그것.</summary>
        public Entity Spawn(EntityDef def, Vector3 position)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (Entity != null && !Entity.IsRemoved) return Entity;
            var e = world.Create(def.Faction, position);
            def.Assemble(e);
            if (e.Health == null) e.Add(new HealthModule(100f));   // Health가 빠진 팩 — 죽지 않는 플레이어보다 기본값이 낫다
            Entity = e;
            Spawned?.Invoke(e);
            return e;
        }

        /// <summary>팩 없는 씬의 폴백 — Health·Effects만. 가방·제작 모듈은 호출자가 붙인다.</summary>
        public Entity Spawn(float maxHp, Vector3 position)
        {
            if (Entity != null && !Entity.IsRemoved) return Entity;

            var e = world.Create(Faction.Player, position);
            e.Add(new HealthModule(Math.Max(1f, maxHp)));
            e.Add(new EffectsModule());
            Entity = e;
            Spawned?.Invoke(e);
            return e;
        }

        /// <summary>한 틱 — 시계를 올리고 플레이어 모듈(무기: 재장전 완료·자동 재장전)을 돌린다.</summary>
        public void Tick(float dt)
        {
            Now += dt;
            var e = Entity;
            if (e == null || e.IsRemoved) return;
            e.Get<WeaponModule>()?.Tick(Now);
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
