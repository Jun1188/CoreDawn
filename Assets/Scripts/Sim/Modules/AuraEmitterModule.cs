using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>펄스 한 번의 결과 — 뷰의 연출용. 효과는 이미 심에서 걸렸다.</summary>
    public readonly struct AuraPulse
    {
        public readonly Effect[] Effects;
        public readonly int Hits;
        public AuraPulse(Effect[] effects, int hits) { Effects = effects; Hits = hits; }
    }

    /// <summary>
    /// 오라 — 반경 안의 적 전원에게 주기마다 효과(심 모듈). 표적도 조준도 없다: "반경 안에 적이 하나라도 있나"만 보고 펄스한다.
    /// 포탑(<see cref="TurretModule"/>)의 파생이 아니라 일반 모듈이다 — 타워가 아닌 것(치유 코어·몬스터 오라)에도 붙는다.
    ///
    /// 효과는 탄의 출처(<see cref="IAmmoSource"/>)가 정한다: 탄창(<see cref="AmmoConsumerModule"/>)이면 펄스마다 연료 한 발을 태우고
    /// 그 연료의 효과를(연료를 바꾸면 오라가 바뀐다), 고정 탄(<see cref="FixedAmmoModule"/>)이면 자기 정의의 효과를. 반경이 비어 있으면 태우지 않는다.
    /// 적용은 심에서 직접(<see cref="EntityWorld.QueryRadius"/> → <see cref="EffectsModule.Apply"/>) — PhysX를 쓰지 않는다.
    /// </summary>
    public sealed class AuraEmitterModule : EntityModule, ISteppable, ISaveableModule
    {
        public AuraEmitterModuleDef Def { get; }

        /// <summary>반경이 비어 있을 때 다시 훑는 주기(초).</summary>
        public const float ScanInterval = 0.2f;

        public float Radius => Def.Radius;   // m
        public float Interval => Math.Max(0.01f, Def.Interval);

        /// <summary>다음 펄스가 허용되는 시각. 세이브 대상.</summary>
        public float ReadyAt { get; private set; }
        public bool Starved { get; private set; }
        public float LastPulseAt { get; private set; } = float.NegativeInfinity;
        public int LastHits { get; private set; }

        /// <summary>펄스가 나갔다 — 연출용. 효과는 이미 걸렸다.</summary>
        public event Action<AuraPulse> Pulsed;

        readonly List<Entity> _buffer = new List<Entity>();
        readonly Func<Entity, bool> _hostile;
        IAmmoSource _ammo;
        bool _ammoLooked;

        public AuraEmitterModule(AuraEmitterModuleDef def)
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
                    ?? throw new InvalidOperationException($"{Owner}: AuraEmitter에는 탄의 출처(AmmoConsumer 또는 FixedAmmo)가 필요합니다 — 효과는 탄이 정한다");
            return _ammo;
        }

        /// <summary>한 틱 — 쿨다운이 돌았고 반경에 적이 있으면 펄스. now는 공장 시계.</summary>
        // ── 공통 틱(ISteppable): 굶으면 예약 없음, 아니면 다음 펄스 시각(최소 한 틱) ──
        float ISteppable.Step(float now, float dt)
        {
            Step(now);
            if (Starved) return 0f;
            float wait = ReadyAt > now ? ReadyAt - now : ScanInterval;
            return Mathf.Max(dt, wait);
        }

        // ── 세이브(ISaveableModule) ──
        public sealed class SaveState { [JsonProperty("readyAt")] public float ReadyAt; }
        public object CaptureState() => new SaveState { ReadyAt = ReadyAt };
        public void RestoreState(JToken state) { var s = state?.ToObject<SaveState>(); if (s != null) RestoreState(s.ReadyAt); }

        public void Step(float now)
        {
            if (now < ReadyAt) return;

            var ammo = Ammo();
            Starved = !ammo.HasAmmo;
            if (Starved) return;

            Owner.World.QueryRadius(Owner.Position, Radius, _hostile, _buffer, exclude: Owner);
            if (_buffer.Count == 0) return;

            if (!ammo.TryPeek(out var ammoDef, out _)) { Starved = true; return; }
            var effects = ammo.Bake(ammoDef);
            if (effects.Length == 0) return;      // 효과 없는 연료는 태우지 않는다
            ammo.TryTake(out _, out _);

            // 오라는 방향이 없다 — 방향을 보는 효과(넉백)는 원점 기준 방사형으로 대체한다
            foreach (var e in _buffer)
                e.Get<EffectsModule>()?.Apply(effects, Owner, Owner.Position);

            ReadyAt = now + Interval;
            LastPulseAt = now;
            LastHits = _buffer.Count;
            Pulsed?.Invoke(new AuraPulse(effects, _buffer.Count));
        }

        public void RestoreState(float readyAt) => ReadyAt = readyAt;
    }
}
