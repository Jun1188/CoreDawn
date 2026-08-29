using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.FPS;
using CoreDawn.Sim;
using SimEntity = CoreDawn.Sim.Entity;

namespace CoreDawn.Entities
{
    // 플레이어 뷰 — 심(PlayerSystem)이 만든 플레이어 엔티티를 받아 그린다. 조작/건설(PlayerController, PlacementSystem 등)은
    // 기존 시스템이 담당하고, 여기서는 엔티티 측면(HP 이벤트 릴레이, 몬스터 감지 콜백, 위치 미러)만 다룬다.
    // 물리 이동은 뷰(CharacterController)가 굴리고 위치를 심으로 밀어 넣는다 — 서버 권위 하이브리드 결정.
    //
    // 길찾기 최적화의 핵심: 몬스터 N마리가 각자 플레이어를 스캔하는 대신,
    // 플레이어 하나가 센서 범위의 몬스터를 찾아 OnDetectedByPlayer / OnLostByPlayer로
    // 알려준다. 감지된 몬스터만 런타임 A*를 쓰고 나머지는 플로우필드를 탄다.
    //
    // 감지는 PhysX(OverlapSphere·레이어)가 아니라 심의 반경 질의(EntityWorld.QueryRadius, 편=Monster)다 —
    // 레이어는 물리·렌더링의 도구이고, 헤드리스 심에는 콜라이더가 없다.
    public class PlayerView : EntityView
    {
        // 심(PlayerSystem.Spawn)이 먼저 만든다 — BattleManager가 붙여 준 것을 받는다
        protected override bool CreatesOwnEntity => false;
        protected override Faction Faction => Faction.Player;
        // 이동은 뷰가 굴린다 — 매 프레임 위치를 심으로 미러(몬스터는 반대 방향)
        protected override bool PushesPositionToSim => true;

        [Header("감지")]
        [Tooltip("이 반경(m) 안의 몬스터를 감지한다. BattleManager가 런타임 부착 시 덮어쓴다.")]
        [SerializeField] private float detectionRange = 10f;
        [Tooltip("스캔 주기(초). 매 프레임 훑지 않는다.")]
        [SerializeField] private float scanInterval = 0.2f;

        // 근접 자동 반격은 없다 — 눈에 보이는 공격 동작 없이 몬스터 HP가 깎여
        // "보스가 자기 자신을 공격한다"로 보이는 혼란을 만들었다. 피해는 총기(Bullet)만 준다. 그래서 심 Attack 모듈도 없다.

        public float DetectionRange => detectionRange;

        // 런타임 부착 엔티티(EnsurePlayerEntity 등) 전용 — 인스펙터를 못 쓰는 경우 감지 범위 조정
        public void SetDetectionRange(float range) => detectionRange = Mathf.Max(0f, range);

        private float lastScanTime = float.MinValue;
        private readonly List<SimEntity> scanBuffer = new List<SimEntity>();
        private readonly HashSet<MonsterView> detected = new HashSet<MonsterView>();
        private readonly HashSet<MonsterView> seenThisScan = new HashSet<MonsterView>();
        private readonly List<MonsterView> removeBuffer = new List<MonsterView>();

        // 보스 두뇌가 보는 PlayerEntity는 SimRunner가 PlayerSystem.Spawned에서 이어 준다 — 뷰는 이벤트만 듣는다
        protected override void OnEntityAttached()
        {
            base.OnEntityAttached();
            if (Entity.Health != null) Entity.Health.Damaged += OnDamaged;   // 피격 연출 — 피해가 뷰를 거치지 않으므로 심 이벤트로 듣는다
        }

        // 뷰가 사라지면(씬 전환) 심의 플레이어도 내린다 — 죽음(부활 가능)과는 다르다. 종료 중엔 심을 건드리지 않는다
        protected override void OnDestroy()
        {
            var e = Entity;
            base.OnDestroy();
            if (e != null && !e.IsRemoved && !ApplicationQuitting) SimRunner.Players.Despawn();
        }

        // 죽은 경우에는 일반 피격 연출을 실행하지 않는다 (사망 연출은 HandleDeath)
        void OnDamaged(float amount, SimEntity source)
        {
            if (amount <= 0f) return;
            float newHealth = Health.CurrentHealth;
            if (newHealth <= 0f) return;
            GetComponent<PlayerController>()?.HandlePlayerDamaged(newHealth + amount, newHealth);
        }

        protected override void Update()
        {
            base.Update();
            if (IsDead) return;
            ScanForMonsters();
        }

        private void ScanForMonsters()
        {
            if (Entity == null || Time.time < lastScanTime + scanInterval) return;   // scanInterval 주기로만 실제 스캔
            lastScanTime = Time.time;

            // 심 질의 — 몬스터 편의 살아 있는 엔티티. 뷰는 등록부로 찾는다(심은 뷰를 모른다)
            SimHost.World.QueryRadius(Entity.Position, detectionRange, Faction.Monster, scanBuffer, exclude: Entity);

            // 새로 들어온 몬스터 → 감지 콜백
            seenThisScan.Clear();
            foreach (var e in scanBuffer)
            {
                var monster = EntityViewRegistry.ViewOf<MonsterView>(e);
                if (monster == null || !monster.IsValidTarget()) continue;
                seenThisScan.Add(monster);
                if (detected.Add(monster)) monster.OnDetectedByPlayer(this);
            }

            // 범위를 벗어났거나 죽은 몬스터 → 해제 콜백
            removeBuffer.Clear();
            foreach (var monster in detected)
            {
                if (monster == null || monster.IsDead || !seenThisScan.Contains(monster))
                    removeBuffer.Add(monster);
            }
            foreach (var monster in removeBuffer)
            {
                detected.Remove(monster);
                if (monster != null) monster.OnLostByPlayer();
            }
        }

        // 부활 및 사망 연출은 PlayerController가 전담하도록 비워둡니다.
        protected override void HandleDeath()
        {
            PlayerController controller = GetComponent<PlayerController>();
            controller.HandlePlayerDeath();
        }

        private void OnDisable()
        {
            // 플레이어가 비활성화되면(사망 등) 추적 중이던 몬스터를 모두 해제
            foreach (var monster in detected)
            {
                if (monster != null) monster.OnLostByPlayer();
            }
            detected.Clear();
        }
    }
}
