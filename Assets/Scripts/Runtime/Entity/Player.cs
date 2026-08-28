using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.FPS;
using CoreDawn.Placement;

namespace CoreDawn.Entities
{
    // 플레이어 엔티티 — 조작/건설(PlayerController, PlacementSystem 등)은 기존 시스템이
    // 담당하고, 여기서는 엔티티 측면(HP, 몬스터 감지 콜백)만 다룬다.
    //
    // 길찾기 최적화의 핵심: 몬스터 N마리가 각자 플레이어를 스캔하는 대신,
    // 플레이어 하나가 센서 범위의 몬스터를 찾아 OnDetectedByPlayer / OnLostByPlayer로
    // 알려준다. 감지된 몬스터만 런타임 A*를 쓰고 나머지는 플로우필드를 탄다.
    public class Player : EntityView
    {
        [SerializeField] private SensorComponent sensor = new SensorComponent();

        // 근접 자동 반격은 제거됐다 — 눈에 보이는 공격 동작 없이 몬스터 HP가 깎여
        // "보스가 자기 자신을 공격한다"로 보이는 혼란을 만들었다. 피해는 총기(Bullet)만 준다.
        // CombatComponent 자체는 엔티티 계약(Entity.Combat) 유지용으로만 남긴다.
        [SerializeField] private CombatComponent combat = new CombatComponent();

        public override SensorComponent Sensor => sensor;
        public override CombatComponent Combat => combat;

        private readonly List<EntityView> scanBuffer = new List<EntityView>();
        private readonly HashSet<Monster> detected = new HashSet<Monster>();
        private readonly List<Monster> removeBuffer = new List<Monster>();

        protected override void Awake()
        {
            base.Awake();
            sensor.Initialize(this);
            combat.Initialize(this);
        }

        protected override void Update()
        {
            base.Update();
            if (IsDead) return;
            ScanForMonsters();
        }

        private void ScanForMonsters()
        {
            if (!sensor.TryScan(scanBuffer)) return; // scanInterval 주기로만 실제 스캔

            // 새로 들어온 몬스터 → 감지 콜백
            foreach (var entity in scanBuffer)
            {
                if (entity is Monster monster && detected.Add(monster))
                {
                    monster.OnDetectedByPlayer(this);
                }
            }

            // 범위를 벗어났거나 죽은 몬스터 → 해제 콜백
            removeBuffer.Clear();
            foreach (var monster in detected)
            {
                if (monster == null || monster.IsDead || !scanBuffer.Contains(monster))
                {
                    removeBuffer.Add(monster);
                }
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
        public override void ReceiveDamage(float amount)
        {
            if (IsDead)
                return;

            float oldHealth = Health.CurrentHealth;

            base.ReceiveDamage(amount);

            float newHealth = Health.CurrentHealth;

            // 죽은 경우에는 일반 피격 연출을 실행하지 않음
            if (newHealth > 0f && newHealth < oldHealth)
            {
                PlayerController controller =
                    GetComponent<PlayerController>();

                controller?.HandlePlayerDamaged(
                    oldHealth,
                    newHealth
                );
            }
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
