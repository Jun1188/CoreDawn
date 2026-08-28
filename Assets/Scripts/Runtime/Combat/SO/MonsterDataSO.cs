using UnityEngine;
using CoreDawn.Factory;

namespace CoreDawn.Combat
{
    /// <summary>
    /// 몬스터 종류의 정의 — 체력·이동·공격·보스 리쉬 수치와 씬 표현(프리팹). GameData.json의 "monsters" 섹션이 정본이고
    /// 임포터가 이 에셋을 만든다(Data/Monster). 웨이브(WaveDataSO.monster)와 둥지(NestSpawnPoint.bossData)가 참조한다.
    ///
    /// 예전에는 이 값들이 프리팹 3개(Monster·Monster_Spitter·BossMonster)의 인라인 컴포넌트에만 있었고, HP는
    /// 프리팹·WaveDataSO.monsterMaxHp·wave_settings.json 세 곳이 서로 덮어썼다. 종류가 데이터가 되면
    /// 디자이너가 GameData 에디터에서 고치고, 심이 프리팹 없이도(헤드리스) 몬스터를 만들 수 있다.
    /// 일차별 강약은 HP 덮어쓰기가 아니라 웨이브 버프(WaveDataSO.buffs — 효과)로 준다.
    ///
    /// 리팩토링 3단계: 지금은 스폰 시 프리팹의 뷰에 HP만 넣고, 이동·공격·두뇌는 커밋 3(심 모듈)부터 Build(entity)가 조립한다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewMonster", menuName = "Combat/Monster Data")]
    public class MonsterDataSO : GameDataSO
    {
        [Header("뷰")]
        [Tooltip("씬 표현 프리팹(Monster 컴포넌트가 붙은 것). 에셋 참조라 json에는 prefabGuid로 적힌다. 비우면 코드 조립 폴백(캡슐).")]
        public GameObject prefab;

        [Header("체력")]
        [Min(1f)] public float maxHp = 30f;

        [Header("이동")]
        [Tooltip("이동 속도(m/s).")]
        public float moveSpeed = 4f;
        [Tooltip("이동 방향으로 몸을 돌리는 속도(도/초).")]
        public float rotateSpeed = 720f;
        [Tooltip("군중 겹침 해소용 개체 반지름(m). 0이면 군중 시스템에서 제외.")]
        public float crowdRadius = 0.4f;
        [Tooltip("넉백 감쇠율(초당). 클수록 짧고 굵게 밀린다. 총 밀림 거리는 감쇠율과 무관하게 효과가 정한다.")]
        public float knockbackDamping = 8f;
        [Tooltip("매 프레임 발을 지면 높이에 맞춘다. 끄면 스폰 당시 높이를 유지한다(비행 유닛용).")]
        public bool stickToGround = true;

        [Header("공격")]
        [Tooltip("근접 사거리(m). 풋프린트 경계 기준.")]
        public float attackRange = 1.5f;
        [Tooltip("공격 간격(초).")]
        public float attackCooldown = 2f;
        [Tooltip("명중 시 무슨 일이 일어나는가 — 이 공격의 정의 전부. 피해도 항목의 하나다: {Damage, 10}.")]
        public EffectEntry[] attackEffects;

        [Header("보스 리쉬·인내심 (보스로 배치될 때만 쓰인다)")]
        [Tooltip("보스가 교전을 버티는 최대 인내심(초). 둥지 밖으로 나가 있으면 닳는다.")]
        public float maxPatience = 3f;
        [Tooltip("이 반경(보스 자기 자리 기준) 밖으로 나가면 인내심이 닳는다. 0이면 교전 구역의 추적 반경(없으면 25m).")]
        public float patienceRadius = 0f;
        [Tooltip("보스가 둥지 밖으로 끌려나가 있을 때 인내심이 닳는 속도(초당).")]
        public float outsidePatienceDrain = 2f;
        [Tooltip("보스는 둥지 안인데 표적이 밖에서 찔러댈 때(원거리 카이팅) 닳는 속도(초당).")]
        public float rangedPokePatienceDrain = 3f;
        [Tooltip("둥지 안에서 표적과 제대로 붙어 싸울 때 인내심이 차오르는 속도(초당).")]
        public float patienceRecoverRate = 2f;
        [Tooltip("강제 귀환 거리 = 리쉬 거리 × 이 배수. 인내심이 남아 있어도 이만큼 벗어나면 즉시 복귀한다.")]
        public float absoluteLeashMultiplier = 1f;
        [Tooltip("둥지로 복귀하는 동안의 체력 재생 — 최대 체력 대비 초당 비율(0.12 = 12%/s).")]
        public float returnRegenPerSecond = 0.12f;
        [Tooltip("복귀가 이 시간(초)을 넘기면 길이 막힌 것으로 보고 그 자리에서 복귀를 접는다. 0 이하면 제한 없음.")]
        public float returnTimeout = 20f;
    }
}
