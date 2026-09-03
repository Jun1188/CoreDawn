using System;
using System.Collections.Generic;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 효과 시스템 — 월드의 모든 <see cref="EffectsModule"/> 모듈을 한 틱 진행시키고, 죽은 엔티티의 효과를 걷는다.
    /// 몬스터·건물·플레이어 누구의 것이든 같은 시계로 돈다(구 EffectController는 뷰마다 Update에서 따로 돌았다).
    /// 이동보다 먼저 틱한다 — 이번 틱의 속도 배율이 이번 틱의 이동에 쓰이게.
    /// </summary>
    public sealed class EffectSystem : IDisposable, ISimSystem
    {
        readonly SimWorld sim;
        readonly EntityWorld world;
        readonly List<Entity> buffer = new List<Entity>();

        public EffectSystem(SimWorld sim)
        {
            this.sim = sim ?? throw new ArgumentNullException(nameof(sim));
            world = sim.Entities;
            world.Died += OnDied;
            sim.AddSystem(this, SimOrder.Effects);
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            world.CopyTo(buffer);   // 틱 중 사망·제거가 일어나도 안전하게 스냅샷 위에서 돈다
            for (int i = 0; i < buffer.Count; i++)
            {
                var e = buffer[i];
                if (e.IsRemoved) continue;
                e.Get<EffectsModule>()?.Tick(dt);
            }
        }

        // 사망 즉시 효과 정리 — DoT가 시체를 때리거나 감속이 사망 연출에 남지 않게. 순서: 월드가 먼저 결정, 뷰 릴레이는 그 뒤.
        void OnDied(Entity e) => e.Get<EffectsModule>()?.Clear();

        public void Dispose() { world.Died -= OnDied; sim.RemoveSystem(this); }
    }
}
