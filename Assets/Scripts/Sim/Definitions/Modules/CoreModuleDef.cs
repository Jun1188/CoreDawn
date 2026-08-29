using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>코어 — 티어 진행·보호막·HP 보너스. 마커가 아니라 진짜 모듈이다: "코어인가"는 이 모듈의 존재로 판정한다.</summary>
    public sealed class CoreModuleDef : EntityModuleDef
    {
        [JsonProperty("tiers")] public List<CoreTierDef> Tiers = new List<CoreTierDef>();

        // ── 보호막 — 요구에 없는 자원을 소각해 채운다 (구 CoreDataSO의 값. 기본값 = 옛 에셋 값)
        /// <summary>끄면 예전 동작 — 요구 아이템만 통과하고 나머지는 입구에서 거절된다.</summary>
        [JsonProperty("burnSurplusIntoShield")] public bool BurnSurplusIntoShield = true;
        /// <summary>소각 1개당 기본 보호막 회복량.</summary>
        [JsonProperty("shieldPerItem")] public float ShieldPerItem = 5f;
        /// <summary>용도(ItemType)별 소각 가치 — 여기 적은 분류만 shieldPerItem을 덮어쓴다.</summary>
        [JsonProperty("shieldValueByType")] public Dictionary<ItemType, float> ShieldValueByType = new Dictionary<ItemType, float>();
        /// <summary>보호막 기본 최대치. 완료한 단계의 maxShieldBonus가 여기에 누적된다.</summary>
        [JsonProperty("baseMaxShield")] public float BaseMaxShield = 100f;

        public float ShieldValueOf(ItemDef item)
        {
            if (item == null) return 0f;
            if (ShieldValueByType != null && ShieldValueByType.TryGetValue(item.Type, out var v)) return System.Math.Max(0f, v);
            return System.Math.Max(0f, ShieldPerItem);
        }

        public override void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            foreach (var t in Tiers)
                foreach (var r in t.Requirements) r.Resolve(db, errors, owner);
        }
    }

    public sealed class CoreTierDef
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("requirements")] public List<ItemAmount> Requirements = new List<ItemAmount>();
        [JsonProperty("unlocks")] public List<string> Unlocks = new List<string>();
        [JsonProperty("maxHpBonus")] public float MaxHpBonus;
        /// <summary>완료 시 보호막 최대치 증가분. 최대치만 오르고 현재값은 오르지 않는다 — 보호막은 소각으로만 찬다.</summary>
        [JsonProperty("maxShieldBonus")] public float MaxShieldBonus;
        [JsonProperty("isFinal")] public bool IsFinal;
    }
}
