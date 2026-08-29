using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 몬스터 한 종류의 수치 — 심이 모듈을 조립할 때 읽는 값 묶음. 순수 데이터(구조체), 에셋 참조 없음.
    ///
    /// 심이 MonsterDataSO(ScriptableObject)를 직접 받지 않는 이유: 데이터 SO는 프리팹(뷰) 참조를 들고 있고
    /// 뷰 쪽 네임스페이스에 산다. 심은 숫자만 알면 된다 — 뷰(스포너)가 <c>data.ToSpec()</c>으로 옮겨 넘긴다.
    /// 헤드리스 테스트는 이 구조체를 손으로 만들어 몬스터를 세울 수 있다.
    /// </summary>
    public struct MonsterSpec
    {
        public float MaxHp;

        public float MoveSpeed;
        public float RotateSpeed;
        public float CrowdRadius;
        public float KnockbackDamping;
        public bool StickToGround;

        public float AttackRange;
        public float AttackCooldown;

        // 보스 리쉬·인내심 — 보스로 배치될 때만 쓰인다
        public float MaxPatience;
        public float PatienceRadius;
        public float OutsidePatienceDrain;
        public float RangedPokePatienceDrain;
        public float PatienceRecoverRate;
        public float AbsoluteLeashMultiplier;
        public float ReturnRegenPerSecond;
        public float ReturnTimeout;

        /// <summary>구 Monster.prefab의 인라인 값 — 데이터가 비어 있을 때의 기준.</summary>
        public static MonsterSpec Default => new MonsterSpec
        {
            MaxHp = 30f, MoveSpeed = 4f, RotateSpeed = 720f, CrowdRadius = 0.4f, KnockbackDamping = 8f, StickToGround = true,
            AttackRange = 1.5f, AttackCooldown = 2f,
            MaxPatience = 3f, PatienceRadius = 0f, OutsidePatienceDrain = 2f, RangedPokePatienceDrain = 3f,
            PatienceRecoverRate = 2f, AbsoluteLeashMultiplier = 1f, ReturnRegenPerSecond = 0.12f, ReturnTimeout = 20f,
        };
    }

    /// <summary>
    /// 둥지의 교전 규칙 — 뷰의 NestEngagementZone(MonoBehaviour)에서 숫자만 뽑은 것. 두뇌가 추적·리쉬 판정에 쓴다.
    /// 낮에만 반응하는 규칙(DayOnly)은 시스템 시계(MonsterSystem.IsDay)로 판정한다.
    /// </summary>
    public readonly struct EngagementZone
    {
        public readonly float ChaseRange;
        public readonly float LeashRange;
        public readonly bool DayOnly;

        public EngagementZone(float chaseRange, float leashRange, bool dayOnly)
        {
            ChaseRange = Mathf.Max(0f, chaseRange);
            LeashRange = Mathf.Max(1f, leashRange);
            DayOnly = dayOnly;
        }
    }
}
