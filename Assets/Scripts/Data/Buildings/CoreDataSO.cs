using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.FPS;
using CoreDawn.Inventories;
using CoreDawn.Managers;
using CoreDawn.Save;
using CoreDawn.UI;
using CoreDawn.Sim;
using CoreDawn.Factory;

namespace CoreDawn.Data
{
    /// <summary>
    /// 팩토리 코어 — Satisfactory의 우주 엘리베이터/마일스톤에 해당.
    /// 티어별로 정해진 아이템을 정해진 개수만큼 모으면 다음 티어로 진화하며,
    /// 그 결과(GameManager.UnlockedTier 증가)로 상위 레시피/건물이 해금된다.
    ///
    /// 디펜스 코어(BuildingEntity.isCore)와 같은 오브젝트에 합체돼 배치된다 —
    /// 낮에는 이 SO의 행동(CoreBehavior)으로 자원을 납품받고, 밤에는 몬스터의 공격 대상이 된다.
    /// 씬에 미리 배치된 싱글턴이므로 빌드 메뉴에는 노출하지 않는다(hideFromBuildMenu = true 권장).
    /// </summary>
    [CreateAssetMenu(fileName = "NewCore", menuName = "Factory/Buildings/Core")]
    public class CoreDataSO : BuildingDataSO
    {
        [Header("티어 — tiers[N] = Tier N → N+1 진화에 필요한 요구량")]
        public CoreTierDefinition[] tiers;

        [Header("보호막 — 요구에 없는 자원을 소각해 채운다")]
        [Tooltip("끄면 예전 동작 — 요구 아이템만 통과하고 나머지는 입구에서 거절된다.")]
        public bool burnSurplusIntoShield = true;

        [Tooltip("소각 1개당 기본 보호막 회복량.")]
        public float shieldPerItem = 5f;

        [Tooltip("용도(ItemType)별 소각 가치 — 여기 적은 분류만 shieldPerItem을 덮어쓴다.")]
        public CoreShieldValue[] shieldValueByType;

        [Tooltip("보호막 기본 최대치. 완료한 단계의 maxShieldBonus가 여기에 누적된다.")]
        public float baseMaxShield = 100f;

        /// <summary>이 아이템 1개를 태웠을 때 차오르는 보호막. 분류별 지정이 있으면 그쪽이 이긴다.</summary>
        public float ShieldValueOf(ItemDataSO item)
        {
            if (item == null) return 0f;

            if (shieldValueByType != null)
                foreach (var v in shieldValueByType)
                    if (v != null && v.type == item.type) return Mathf.Max(0f, v.shieldPerItem);

            return Mathf.Max(0f, shieldPerItem);
        }

        public override IBuildingBehavior CreateBehavior(BuildingModule building) => new CoreBehavior(building, this);

        protected override void OnValidate()
        {
            base.OnValidate();
            if (tiers == null) return;
            foreach (var t in tiers)
            {
                int need = t?.requirements?.Length ?? 0;
                if (need > inputSlots)
                    Debug.LogError($"[Core] '{name}' 요구 아이템 종류({need})가 입력 슬롯({inputSlots})보다 많음 — " +
                                    "슬롯을 늘리거나 티어 요구량을 줄일 것.", this);
            }
        }
    }

    [System.Serializable]
    public class CoreTierRequirement
    {
        public ItemDataSO item;
        public int amount;
    }

    /// <summary>용도 분류별 소각 가치. 광석 한 덩이와 완성 부품 하나가 같은 값일 이유가 없다.</summary>
    [System.Serializable]
    public class CoreShieldValue
    {
        public ItemType type;
        public float shieldPerItem;
    }

    [System.Serializable]
    public class CoreTierDefinition
    {
        [Tooltip("단계 이름 — \"선체 봉합\" 등. GameData.json 의 tiers[].name.")]
        public string tierLabel;

        [Tooltip("이 단계가 무엇을 하는 일인지 한 줄. 코어 패널이 이름 아래 붙인다.")]
        [TextArea] public string description;

        public CoreTierRequirement[] requirements;

        [Tooltip("이 단계를 마치면 열리는 것들 — 표시 전용 문자열. 실제 해금은 requiredCoreTier가 한다.")]
        public string[] unlocks;

        [Tooltip("완료 시 코어 최대 체력 증가분.")]
        public int maxHpBonus;

        [Tooltip("완료 시 보호막 최대치 증가분. 최대치만 오르고 현재값은 오르지 않는다 — 보호막은 소각으로만 찬다.")]
        public float maxShieldBonus;

        [Tooltip("마지막 단계 — 완료하면 탈출(엔딩)로 이어진다.")]
        public bool isFinal;
    }

    // ─── 행동 ──────────────────────────────────────────────────────

}
