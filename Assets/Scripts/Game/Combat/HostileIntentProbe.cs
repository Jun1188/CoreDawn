using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.FPS;
using CoreDawn.UI;
using CoreDawn.Entities;

namespace CoreDawn.Combat
{
    /// <summary>
    /// "명백한 적의" 감지 — 플레이어가 보스를 때리려다 <b>빗맞아도</b> 공격과 같은 판정을 준다.
    ///
    /// 보스가 피해를 받아야만 깨어나면, 눈앞에서 총알이 스쳐 지나가도 가만히 서 있는
    /// 저능한 그림이 나온다. 방아쇠를 당긴 순간의 조준선과 보스 사이의 관계를 재서,
    /// 실수로 흘린 오발이 아니라 명백히 겨눈 사격이면 각성시킨다.
    ///
    /// 판정의 핵심은 각도와 거리를 따로 보지 않고 <b>빗나간 폭</b>으로 합치는 것이다:
    ///   miss = 거리 × tan(각도)
    /// 40m에서 아슬아슬하게 빗나간 저격도, 5m 코앞에서 크게 빗나간 난사도 둘 다 적의로 잡힌다.
    /// 반대로 멀리서 엉뚱한 방향으로 쏜 탄은 각도가 같아도 빗나간 폭이 커져 걸러진다.
    ///
    /// 명중/빗나감을 구분할 필요는 없다 — 명중하면 어차피 Monster.ReceiveDamage가 각성시키고,
    /// 빗나가면 여기가 받는다. 그래서 호출부는 Gun.Fire() 한 곳이면 충분하다.
    /// </summary>
    public static class HostileIntentProbe
    {
        /// <summary>이보다 멀면 적의로 보지 않는다.</summary>
        public const float MaxRange = 60f;

        /// <summary>조준선과의 각도 하드 캡 — 등 뒤로 갈긴 오발까지 적의로 세지 않기 위한 상한.</summary>
        public const float MaxAngle = 35f;

        /// <summary>빗나간 폭 허용치(m). 보스 몸통 폭 + 약간의 여유.</summary>
        public const float MissTolerance = 4f;

        // 몬스터·플레이어·총알 콜라이더는 시야를 가리는 "벽"이 아니다
        // (MonsterNest.IsOnPlayerScreen이 쓰는 것과 같은 방식).
        private static int blockerMask;
        private static bool blockerMaskReady;

        private static int BlockerMask
        {
            get
            {
                if (!blockerMaskReady)
                {
                    blockerMask = Physics.DefaultRaycastLayers &
                                  ~LayerMask.GetMask("Monster", "Player", "Character", "Bullet");
                    blockerMaskReady = true;
                }
                return blockerMask;
            }
        }

        // 도메인 리로드를 끈 환경(Enter Play Mode Options)에서 static이 플레이를 넘어 살아남는 것 방지
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => blockerMaskReady = false;

        /// <summary>
        /// 플레이어가 방아쇠를 당긴 순간 호출한다 — 조준선 근처의 비선공 보스를 깨운다.
        /// </summary>
        /// <param name="eye">조준의 기준점(카메라 위치).</param>
        /// <param name="aimDir">조준 방향(카메라 forward). 정규화되지 않아도 된다.</param>
        /// <param name="source">사격 주체. 플레이어가 아니면(포탑 등) 아무것도 하지 않는다.</param>
        public static void Report(Vector3 eye, Vector3 aimDir, EntityView source)
        {
            var attacker = source as PlayerView;
            if (!attacker.IsValidTarget()) return;

            if (aimDir.sqrMagnitude < 1e-6f) return;
            Vector3 aim = aimDir.normalized;

            var monsters = SimRunner.Monsters.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                MonsterView boss = EntityViewRegistry.ViewOf<MonsterView>(monsters[i]);

                // 보스만, 그리고 아직 자고 있는 보스만 — 이미 깨어난 쪽은 인내심이 알아서 굴린다.
                // 집으로 돌아가는 중인 보스도 건너뛴다: 그 복귀는 되돌릴 수 없어서
                // Provoke가 어차피 거절하고, 아래 레이캐스트만 헛되이 쏘게 된다.
                if (boss == null || boss.IsDead || !boss.IsBoss ||
                    boss.HasBeenAttacked || boss.IsReturningHome) continue;

                Vector3 center = AimPointOf(boss);
                Vector3 toBoss = center - eye;
                float dist = toBoss.magnitude;
                if (dist < 0.01f || dist > MaxRange) continue;

                float angle = Vector3.Angle(aim, toBoss);
                if (angle > MaxAngle) continue;

                // 빗나간 폭 — 각도와 거리를 하나로 합친 실제 오차. 각도 캡을 이미 통과했으므로
                // tan은 90°에 닿지 않아 발산하지 않는다.
                float miss = dist * Mathf.Tan(angle * Mathf.Deg2Rad);
                if (miss > MissTolerance) continue;

                // 벽·절벽에 가려 보이지 않는 보스가 총소리만으로 깨어나면 안 된다.
                if (Physics.Raycast(eye, toBoss / dist, dist - 0.5f, BlockerMask)) continue;

                boss.Provoke(attacker);
            }
        }

        /// <summary>보스의 조준 기준점 — 콜라이더 중심(없으면 몸통 높이 추정).</summary>
        private static Vector3 AimPointOf(MonsterView boss)
        {
            var col = boss.GetComponentInChildren<Collider>();
            return col != null ? col.bounds.center : boss.transform.position + Vector3.up * 1.2f;
        }
    }
}
