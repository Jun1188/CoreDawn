using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Managers;
using CoreDawn.Combat;
using CoreDawn.Sim;
namespace CoreDawn.Entities
{
    // 사격·펄스·기폭 건물의 뷰 — 심이 내린 결정을 그린다. 판단은 하나도 없다.
    //
    // 역할 분담 (총과 같은 문법):
    //   심   = TurretModule(표적·조준·리드·쿨다운·탄 소비 → FireRequested) · AuraEmitterModule(펄스, 효과는 심이 직접 건다)
    //          · TriggerModule(기폭) · AmmoConsumerModule(탄창·배율)
    //   뷰   = 여기 — 발사 결정을 받아 탄 프리팹을 만들어 날리고(전달은 ProjectileSystem), 상태를 연출로 옮긴다
    //   연출 = TowerVisualController — 포탑 회전·반동·사운드
    //   전달 = ProjectileSystem — 투사체/히트스캔의 물리 판정(명중 효과는 심 Effects로 넘긴다)
    //
    // 탄의 프리팹·연출(총구 화염·착탄 파티클)은 뷰 카탈로그의 탄약 항목이 든다.
    public class TowerView : BuildingView
    {
        /// <summary>등장 연출 길이 — 이 동안은 연출만(심은 이미 돌고 있다).</summary>

        /// <summary>탄이 타워 모델 안에서 태어나지 않게 밀어내는 거리 — 진짜 총구가 있으면 필요 없다.</summary>
        private const float MuzzlePushout = 0.6f;

        /// <summary>펄스 뒤 "발사 중"으로 보이는 시간(초, 심 시계) — 오라는 순간이라 이만큼은 붙잡아 둬야 보인다.</summary>
        private const float PulseFlash = 0.2f;

        private int monsterMask;
        private TowerVisualController visual;

        private TurretModule turret;
        private AuraEmitterModule aura;
        private TriggerModule trigger;
        private bool bound;

        private TowerState state = (TowerState)(-1);
        /// <summary>지금 무엇을 하고 있는가 — 연출과 로직이 공유하는 단일 진실(심 상태에서 파생).</summary>
        public TowerState State => state;

        /// <summary>심의 포탑 모듈 — 상태 표시·테스트가 읽는다. 심이 없거나 포탑이 아니면 null.</summary>
        public TurretModule Turret => turret;
        public AuraEmitterModule Aura => aura;

        // 탄 프리팹이 없는 아이템은 한 번만 경고한다 — 매 발 찍으면 콘솔이 잠긴다
        private static readonly HashSet<string> warnedRounds = new HashSet<string>();

        protected override void Awake()
        {
            base.Awake();
            monsterMask = LayerMask.GetMask("Monster");
            visual = GetComponent<TowerVisualController>();
        }

        protected override void OnEntityAttached()
        {
            base.OnEntityAttached();
            Bind();
        }

        // 심 모듈을 잡고 이벤트를 듣는다. 배치(PlacementBridge)가 Building을 꽂는 순간 온다.
        private void Bind()
        {
            if (bound || Entity == null) return;
            turret  = Entity.Get<TurretModule>();
            aura    = Entity.Get<AuraEmitterModule>();
            trigger = Entity.Get<TriggerModule>();
            if (turret  != null) turret.FireRequested += OnFireRequested;
            if (aura    != null) aura.Pulsed          += OnPulsed;
            if (trigger != null) trigger.Triggered    += OnTriggered;
            bound = true;
        }

        protected override void OnDestroy()
        {
            if (turret  != null) turret.FireRequested -= OnFireRequested;
            if (aura    != null) aura.Pulsed          -= OnPulsed;
            if (trigger != null) trigger.Triggered    -= OnTriggered;
            base.OnDestroy();
        }

        private void SetState(TowerState next)
        {
            if (state == next) return;
            state = next;
            if (visual != null) visual.OnStateChanged(next);
        }

        protected override void Update()
        {
            base.Update();
            if (IsDead) { SetState(TowerState.Destroyed); return; }

            if (!bound) Bind();
            if (!bound) return;   // 심이 아직 안 붙었다(배치 직후) — 다음 프레임

            // 발사하지 않는 구조물(울타리) — 몸으로 막을 뿐이다
            if (turret == null && aura == null && trigger == null) { SetState(TowerState.Inert); return; }

            if (turret != null) { DrawTurret(); return; }
            if (aura != null)
            {
                if (aura.Starved) { SetState(TowerState.Starved); return; }
                float simNow = Building != null ? Building.Factory.Now : 0f;
                SetState(simNow - aura.LastPulseAt < PulseFlash ? TowerState.Firing : TowerState.Idle);
                return;
            }
            SetState(TowerState.Idle);   // 지뢰 — 무장 대기
        }

        private void DrawTurret()
        {
            var def = turret.Def;
            switch (turret.Phase)
            {
                case TurretPhase.Starved:
                    // 탄이 없는 포탑은 훑지 않는다 — 포신이 처진 채 멈춰 있어야 "죽어 있다"가 한눈에 보인다
                    SetState(TowerState.Starved);
                    break;
                case TurretPhase.Idle:
                    if (visual != null) visual.AimIdle(def.TurnSpeed);
                    SetState(TowerState.Idle);
                    break;
                default:
                    // 연출은 심이 정한 방향을 향해 자기 속도로 돈다 — 정렬 판정(발사 게이트)은 심의 것이다
                    if (visual != null) visual.AimTowards(turret.AimDirection, def.TurnSpeed, def.AimTolerance);
                    SetState(turret.Phase == TurretPhase.Ready ? TowerState.Firing : TowerState.Aiming);
                    break;
            }
        }

        // ── 심 → 뷰 ─────────────────────────────────────────────

        /// <summary>발사 결정 — 탄 프리팹을 만들어 심이 준 방향으로 날린다. 판정(명중 효과)은 전달 계층이 심에 넘긴다.</summary>
        private void OnFireRequested(TurretShot s)
        {
            var round = AmmoAssetOf(s.Round);

            // 총구 — 리그에 진짜 총구가 있으면 포신 끝에서(다총신은 배럴 교대), 없으면 심의 총구(높이)에서 밀어낸다.
            // 표적·탄착점(리드)은 심의 결정이고, 각은 탄이 실제로 태어나는 자리에서 그 탄착점을 향해 다시 푼다 —
            // 리그 총구는 심의 총구(muzzleHeight)보다 높거나 옆으로 빠져 있어, 심의 각을 그대로 쓰면 가까운 표적 머리 위로 지나간다.
            Transform muzzleTf = null;
            bool hasMuzzle = visual != null && visual.TryTakeMuzzle(out muzzleTf);
            Vector3 dir = s.Direction;
            if (hasMuzzle)
            {
                Vector3 muzzle = muzzleTf.position;
                if (!s.Hitscan && s.Ammo.Gravity > 0f)
                    dir = Ballistics.BallisticAim(muzzle, s.Impact, s.Ammo.Speed, s.Ammo.Gravity, turret.Def.PreferHighArc);
                else if ((s.Impact - muzzle).sqrMagnitude > 0.0001f)
                    dir = (s.Impact - muzzle).normalized;
            }
            Vector3 origin = hasMuzzle ? muzzleTf.position : s.Origin + dir * MuzzlePushout;

            var shot = new ProjectileShot(s.Ammo.Speed, s.Ammo.Lifetime, s.Range,
                                          s.Effects, monsterMask, this, s.Ammo.Gravity, s.Ammo.ExplosionRadius,
                                          s.Hitscan ? FireMode.Hitscan : FireMode.Projectile,
                                          round != null ? round.bulletPrefab : null, s.Ammo.Pierce, null,
                                          round != null ? round.hitEffectPrefab : null);

            // 총구 화염 — 같은 탄이면 총과 타워가 같은 연출을 쓴다 (탄약이 연출의 주인)
            if (round != null) ProjectileSystem.PlayEffect(round.muzzleFlashPrefab, origin, Quaternion.LookRotation(dir));

            // 전달은 총(Gun)과 같은 단일 진입점 — 방식 분기는 ProjectileSystem이 한다
            ProjectileSystem.Fire(origin, dir, shot);

            if (visual != null) visual.OnShotFired();
            SetState(TowerState.Firing);
        }

        private void OnPulsed(AuraPulse pulse)
        {
            if (visual != null) visual.OnShotFired();
            SetState(TowerState.Firing);
        }

        // 폭발 파티클은 아직 없다 — 고정 탄은 아이템(프리팹)이 없고, 건물의 연출 카탈로그는 5a-3에서 온다
        private void OnTriggered(TriggerBlast blast)
        {
            if (visual != null) visual.OnShotFired();
        }

        /// <summary>탄의 표현 에셋(프리팹·연출) — 뷰 카탈로그. 없으면 경고 한 번 — 판정은 되지만 투사체는 몸(프리팹)이 없어 맞힐 수 없다.</summary>
        private static BuiltinEffects.Ammo AmmoAssetOf(ItemDef item)
        {
            if (item == null) return null;
            var round = BuiltinEffects.AmmoOf(item);
            if ((round == null || round.bulletPrefab == null) && warnedRounds.Add(item.Id))
                Debug.LogError($"[TowerView] 탄 '{item.Id}'의 연출(view.bullet — 내장 연출 이름)이 없습니다 — 탄 프리팹·연출 없이 발사됩니다.");
            return round;
        }
    }
}
