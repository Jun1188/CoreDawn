using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>기폭 한 번의 결과 — 뷰의 연출(폭발 파티클)용. 효과는 이미 심에서 걸렸다.</summary>
    public readonly struct TriggerBlast
    {
        public readonly Effect[] Effects;
        public readonly int Hits;
        public TriggerBlast(Effect[] effects, int hits) { Effects = effects; Hits = hits; }
    }

    /// <summary>
    /// 접촉 기폭 — 반경에 적이 들어오면 반경 전원에게 효과(심 모듈). 지뢰. 발사기가 아니라 덫이다: 조준도 주기도 없다.
    ///
    /// 무엇이 터지는지는 탄의 출처(<see cref="IAmmoSource"/>)가 정한다 — 지뢰는 고정 탄(<see cref="FixedAmmoModule"/>: 자기 정의의 효과).
    /// once면 한 번 터지고 스스로 죽는다(Health.Kill → 공장이 건물을 치운다).
    /// </summary>
    public sealed class TriggerModule : EntityModule, ISteppable
    {
        public TriggerModuleDef Def { get; }

        /// <summary>적이 없을 때 다시 훑는 주기(초).</summary>
        public const float ScanInterval = 0.2f;

        public float Radius => Def.Radius;   // m

        /// <summary>아직 터질 수 있는가. once면 터진 뒤 false.</summary>
        public bool Armed { get; private set; } = true;
        /// <summary>다음 기폭이 허용되는 시각(once가 아닐 때의 쿨다운).</summary>
        public float ReadyAt { get; private set; }
        public float LastTriggeredAt { get; private set; } = float.NegativeInfinity;

        /// <summary>터졌다 — 연출용. once면 이 직후 소유 엔티티가 죽는다.</summary>
        public event Action<TriggerBlast> Triggered;

        readonly List<Entity> _buffer = new List<Entity>();
        readonly Func<Entity, bool> _hostile;
        IAmmoSource _ammo;
        bool _ammoLooked;

        public TriggerModule(TriggerModuleDef def)
        {
            Def = def ?? throw new ArgumentNullException(nameof(def));
            _hostile = IsHostile;
        }

        bool IsHostile(Entity e) => e.Faction.IsHostileTo(Owner.Faction);

        IAmmoSource Ammo()
        {
            if (_ammoLooked) return _ammo;
            _ammoLooked = true;
            _ammo = Owner.Get<IAmmoSource>()
                    ?? throw new InvalidOperationException($"{Owner}: Trigger에는 탄의 출처(FixedAmmo 또는 AmmoConsumer)가 필요합니다 — 무엇이 터지는지는 탄이 정한다");
            return _ammo;
        }

        /// <summary>한 틱 — 반경에 적이 있으면 터진다. now는 공장 시계.</summary>
        // ── 공통 틱(ISteppable): 터졌으면(Armed=false) 예약 없음, 아니면 다음 감지 시각(최소 한 틱) ──
        float ISteppable.Step(float now, float dt)
        {
            Step(now);
            if (!Armed) return 0f;
            float wait = ReadyAt > now ? ReadyAt - now : ScanInterval;
            return Mathf.Max(dt, wait);
        }

        public void Step(float now)
        {
            if (!Armed || now < ReadyAt) return;

            Owner.World.QueryRadius(Owner.Position, Radius, _hostile, _buffer, exclude: Owner);
            if (_buffer.Count == 0) return;

            var ammo = Ammo();
            if (!ammo.TryTake(out var ammoDef, out _)) return;   // 탄창형인데 비었다 — 다음에
            var effects = ammo.Bake(ammoDef);

            // 폭발은 방향이 없다 — 넉백은 원점 기준 방사형
            foreach (var e in _buffer)
                e.Get<EffectsModule>()?.Apply(effects, Owner, Owner.Position);

            LastTriggeredAt = now;
            Triggered?.Invoke(new TriggerBlast(effects, _buffer.Count));

            if (Def.Once)
            {
                Armed = false;
                // 자기 파괴 — 체력이 있으면 죽고(공장이 건물을 치운다), 없으면 월드에서 바로 뺀다
                if (Owner.Health != null) Owner.Health.Kill();
                else Owner.World.Remove(Owner);
            }
            else ReadyAt = now + Math.Max(0.01f, Def.Cooldown);
        }
    }
}
