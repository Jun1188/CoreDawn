using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>포탑이 지금 무엇을 하고 있는가 — 심의 판단. 뷰의 TowerState(등장·파괴 연출 포함)는 이것에서 파생한다.</summary>
    public enum TurretPhase
    {
        /// <summary>탄이 없다. 목표 유무보다 위 — 보급이 끊겼다는 사실이 더 급한 정보다.</summary>
        Starved,
        /// <summary>사거리 안에 표적이 없다.</summary>
        Idle,
        /// <summary>표적을 잡았고 아직 그쪽을 보지 않았다. 정렬될 때까지 쏘지 않는다.</summary>
        Aiming,
        /// <summary>정렬 완료 — 쿨다운이 도는 대로 쏜다.</summary>
        Ready,
    }

    /// <summary>
    /// 발사 한 번의 결정 — 심이 정한 것 전부. 뷰는 이것으로 탄(프리팹)을 만들어 날릴 뿐 아무것도 다시 판단하지 않는다.
    /// 효과는 이미 배율·버프가 구워진 최종 목록이다.
    /// </summary>
    public readonly struct TurretShot
    {
        public readonly ItemDef Round;          // 소비한 탄 아이템 — 고정 탄(FixedAmmo)이면 null
        public readonly AmmoModuleDef Ammo;     // 그 탄의 성질(탄속·중력·폭발·수명·관통)
        public readonly Vector3 Origin;         // 심의 총구(엔티티 위치 + muzzleHeight)
        public readonly Vector3 Direction;      // 발사 방향 — 리드·탄도해가 끝난 값
        public readonly Vector3 Impact;         // 예측 탄착점
        public readonly float Range;            // 탄의 소멸 거리 — 사거리와 탄착 거리 중 큰 쪽 + 여유
        public readonly Effect[] Effects;       // 명중 시 효과(최종)
        public readonly Entity Target;
        public readonly bool Hitscan;           // 즉시 판정(레이저)인가 — 아니면 투사체

        public TurretShot(ItemDef round, AmmoModuleDef ammo, Vector3 origin, Vector3 direction, Vector3 impact, float range,
                          Effect[] effects, Entity target, bool hitscan)
        {
            Round = round; Ammo = ammo; Origin = origin; Direction = direction; Impact = impact; Range = range;
            Effects = effects; Target = target; Hitscan = hitscan;
        }
    }

    /// <summary>
    /// 포탑 — 조준 사격의 두뇌(심 모듈). 표적 선택(최근접·최소 사거리·유지 여유·차폐 없음 — 차폐는 SimHost.LineOfSight로 뷰에 묻는다), 선회(turnSpeed), 정렬 판정(aimTolerance),
    /// 리드·탄도해(<see cref="Ballistics"/>), 쿨다운(fireRate), 탄 꺼내기(<see cref="IAmmoSource"/>)까지 여기서 끝나고,
    /// 뷰에는 <see cref="FireRequested"/> 하나만 나간다 — 뷰는 탄 프리팹을 만들어 그 방향으로 날린다.
    ///
    /// 오라(<see cref="AuraEmitterModule"/>)의 형제가 아니라 별개다: 저쪽은 표적도 조준도 없는 펄스. 둘이 공유하는 것은 탄 소비뿐이다.
    /// 시계는 공장 틱(Step의 now) — 몬스터 시계와 별개지만 쿨다운은 상대 시간이라 문제 없다.
    /// 사거리·반경은 m — 플레이어 총(GunData.range)과 같은 단위. 칸 단위가 아니다.
    /// </summary>
    public sealed class TurretModule : EntityModule, ISteppable, ISaveableModule
    {
        public TurretModuleDef Def { get; }

        /// <summary>표적 재탐색 주기(초) — 매 틱 훑지 않는다. 잡은 표적은 놓칠 때까지 유지한다.</summary>
        public const float ScanInterval = 0.2f;

        /// <summary>
        /// 이미 잡은 표적을 놓는 사거리에 주는 여유(m) — 잡을 때보다 이만큼 더 버틴다(히스테리시스).
        /// 경계에 걸친 몬스터가 걸음마다 안팎을 오가며 포탑을 떨게 하지 않는다.
        /// </summary>
        public const float TargetKeepMargin = 0.5f;

        public float Range => Def.Range;
        public float MinRange => Def.MinRange;
        public float Cooldown => Def.FireRate > 0f ? 1f / Def.FireRate : 1f;

        public Entity Target { get; private set; }
        public TurretPhase Phase { get; private set; } = TurretPhase.Idle;
        /// <summary>포탑이 향하려는 방향(월드) — 리드·탄도해가 끝난 값. 연출이 이 방향으로 돈다.</summary>
        public Vector3 AimDirection { get; private set; } = Vector3.forward;
        /// <summary>현재 포탑 방위(도, 월드 XZ) — 정렬 판정의 기준. 세이브 대상.</summary>
        public float Yaw { get; private set; }
        public bool Aligned { get; private set; }
        /// <summary>다음 발사가 허용되는 시각. 세이브 대상.</summary>
        public float ReadyAt { get; private set; }
        public float LastFiredAt { get; private set; } = float.NegativeInfinity;

        /// <summary>발사 결정 — 뷰가 듣고 탄을 만든다. 탄 소비·쿨다운은 이미 끝난 뒤다.</summary>
        public event Action<TurretShot> FireRequested;

        IAmmoSource _ammo;
        bool _ammoLooked;
        float _nextScan = float.NegativeInfinity;
        readonly Func<Entity, bool> _hostile;

        public TurretModule(TurretModuleDef def)
        {
            Def = def ?? throw new ArgumentNullException(nameof(def));
            _hostile = IsCandidate;
        }

        bool IsHostile(Entity e) => e.Faction.IsHostileTo(Owner.Faction);
        /// <summary>적이고 총구에서 조준점까지 가려지지 않은 것만 표적. 고각 포탑(preferHighArc)은 벽을 넘겨 쏘므로 차폐를 보지 않는다.</summary>
        bool IsCandidate(Entity e) => IsHostile(e) && Visible(e);
        bool Visible(Entity e) => Def.PreferHighArc || SimHost.HasLineOfSight(Owner, e, Muzzle, AimPointOf(e));

        /// <summary>심의 총구 — 엔티티 위치 + muzzleHeight. 뷰의 진짜 총구는 연출 출발점일 뿐, 각은 여기서 푼다.</summary>
        public Vector3 Muzzle => Owner.Position + Vector3.up * Def.MuzzleHeight;

        // 탄의 출처(IAmmoSource: 탄창 소비 또는 고정 탄)는 정의 순서상 이 모듈 뒤에 붙을 수 있어 첫 Step에서 찾는다.
        // 없으면 정의 오류 — 탄 없는 포탑은 없다. 포탑은 효과를 모른다: 무엇이 나가는지는 출처가 말한다.
        IAmmoSource Ammo()
        {
            if (_ammoLooked) return _ammo;
            _ammoLooked = true;
            _ammo = Owner.Get<IAmmoSource>()
                    ?? throw new InvalidOperationException($"{Owner}: Turret에는 탄의 출처(AmmoConsumer 또는 FixedAmmo)가 필요합니다 — 탄 없는 포탑은 정의 오류");
            return _ammo;
        }

        /// <summary>한 틱 — 표적·조준·발사. now는 공장 시계, dt는 이번 틱 길이(선회 적분용).</summary>
        public void Step(float now, float dt)
        {
            var ammo = Ammo();
            if (!ammo.HasAmmo)
            {
                Target = null; Aligned = false; Phase = TurretPhase.Starved;
                return;
            }

            AcquireTarget(now);
            if (Target == null) { Aligned = false; Phase = TurretPhase.Idle; return; }

            // 다음 발의 탄으로 곡사 여부·탄속을 미리 본다 — 박격포가 표적을 똑바로 겨누면 포탄은 발밑에 떨어진다
            AmmoModuleDef nextAmmo = ammo.TryPeek(out var peeked, out _) ? peeked : null;
            Vector3 origin = Muzzle, aimPoint = AimPointOf(Target), velocity = VelocityOf(Target);
            AimDirection = Solve(origin, aimPoint, velocity, nextAmmo, out _);

            // 선회 — 정렬 판정은 좌우(yaw)만. 부앙은 탄종마다 발사 순간 달라지므로 게이트로 쓰지 않는다
            float desiredYaw = Mathf.Atan2(AimDirection.x, AimDirection.z) * Mathf.Rad2Deg;
            Yaw = Def.TurnSpeed <= 0f ? desiredYaw : Mathf.MoveTowardsAngle(Yaw, desiredYaw, Def.TurnSpeed * dt);
            Aligned = Mathf.Abs(Mathf.DeltaAngle(Yaw, desiredYaw)) <= Def.AimTolerance;
            Phase = Aligned ? TurretPhase.Ready : TurretPhase.Aiming;

            // 조준이 끝나기 전에는 탄을 소비하지 않는다 — 뒤에 두면 포탑이 도는 동안 매 틱 한 발씩 사라진다
            if (!Aligned || now < ReadyAt) return;

            if (!ammo.TryTake(out var ammoDef, out var round)) { Phase = TurretPhase.Starved; return; }
            var effects = ammo.Bake(ammoDef);

            // 발사각은 소비한 탄으로 다시 푼다(미리 본 탄과 다를 수 있다 — 탄창에 두 종류가 섞인 경우)
            Vector3 dir = Solve(origin, aimPoint, velocity, ammoDef, out Vector3 impact);
            // 탄의 소멸 거리 — 사거리 + 여유가 기본이지만, 리드가 탄착점을 사거리 밖으로 밀어낸 경우(달아나는 목표)에는 그 거리 기준
            float travel = Vector2.Distance(new Vector2(origin.x, origin.z), new Vector2(impact.x, impact.z));
            float range = Mathf.Max(Range, travel) + 2f;

            ReadyAt = now + Cooldown;
            LastFiredAt = now;
            FireRequested?.Invoke(new TurretShot(round, ammoDef, origin, dir, impact, range, effects, Target, Def.Hitscan));
        }

        /// <summary>
        /// 발사 방향. 즉시 판정(hitscan)은 그냥 겨눈다. 투사체는 중력탄이면 탄착점까지 탄도해(반복), 직사탄이면 만나는 점(이차식 한 번).
        /// </summary>
        Vector3 Solve(Vector3 origin, Vector3 aimPoint, Vector3 velocity, AmmoModuleDef ammo, out Vector3 impact)
        {
            impact = aimPoint;
            if (!Def.Hitscan && ammo != null)
            {
                if (ammo.Gravity > 0f)
                    return Ballistics.BallisticLead(origin, aimPoint, velocity, ammo.Speed, ammo.Gravity, Def.PreferHighArc, out impact);
                if (ammo.Speed > 0f)
                    return Ballistics.LinearLead(origin, aimPoint, velocity, ammo.Speed, out impact);
            }
            Vector3 d = aimPoint - origin;
            return d.sqrMagnitude > 0.0001f ? d.normalized : (AimDirection.sqrMagnitude > 0.0001f ? AimDirection : Vector3.forward);
        }

        Vector3 AimPointOf(Entity target) => target.Position + Vector3.up * Def.AimHeight;

        static Vector3 VelocityOf(Entity target)
        {
            var m = target.Get<MovementModule>();
            return m != null ? m.Velocity : Vector3.zero;
        }

        void AcquireTarget(float now)
        {
            if (Target != null && Target.IsAlive)
            {
                float d = Vector3.Distance(Target.Position, Owner.Position);
                if (d <= Range + TargetKeepMargin && d >= MinRange && Visible(Target)) return;   // 유지 — 가려지면 놓고 다시 찾는다
            }
            Target = null;
            if (now < _nextScan) return;
            _nextScan = now + ScanInterval;
            // 거리는 엔티티 위치로 잰다 — 잡는 쪽과 유지하는 쪽의 기준이 같아야 경계에서 떨지 않는다
            Target = Owner.World.QueryClosest(Owner.Position, Range, _hostile, MinRange, exclude: Owner);
        }

        /// <summary>세이브 복원 — 쿨다운·방위. 표적은 저장하지 않는다(다음 탐색이 싸다).</summary>
        public void RestoreState(float readyAt, float yaw) { ReadyAt = readyAt; Yaw = yaw; }

        // ── 공통 틱(ISteppable): 굶으면 예약 없음(탄이 오면 그릇 변화가 깨운다), 표적이 있으면 매 틱, 없으면 탐색 주기 ──
        float ISteppable.Step(float now, float dt)
        {
            Step(now, dt);
            if (Phase == TurretPhase.Starved) return 0f;
            return Target != null ? dt : ScanInterval;
        }

        // ── 세이브(ISaveableModule) — 키는 옛 행동 저장과 같다 ──
        public sealed class SaveState
        {
            [JsonProperty("readyAt")] public float ReadyAt;
            [JsonProperty("yaw")] public float Yaw;
        }
        public object CaptureState() => new SaveState { ReadyAt = ReadyAt, Yaw = Yaw };
        public void RestoreState(JToken state)
        {
            var s = state?.ToObject<SaveState>();
            if (s != null) RestoreState(s.ReadyAt, s.Yaw);
        }
    }
}
