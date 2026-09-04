using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 건물 — 격자 풋프린트와 배치 규칙. 키는 전부 평행이다(배치 정보를 블록으로 묶지 않는다).
    /// 배치 불가(둥지·나무·코어)는 placeable=false. 런타임 모듈(BuildingModule)은 5a-2에서 이 정의로 만든다.
    /// </summary>
    public sealed class BuildingModuleDef : EntityModuleDef
    {
        [JsonProperty("size")] public Vec2i Size = new Vec2i(1, 1);
        [JsonProperty("placeable")] public bool Placeable = true;
        [JsonProperty("isDemolishable")] public bool IsDemolishable = true;
        [JsonProperty("isAttackable")] public bool IsAttackable = true;
        /// <summary>밟고 지나갈 수 있는가 — 길찾기가 이 건물의 칸을 땅으로 본다(지뢰). 배치 격자는 그대로 차지한다(겹쳐 놓지 못한다).</summary>
        [JsonProperty("walkable")] public bool Walkable;
        [JsonProperty("category")] public string Category;
        [JsonProperty("requiredCoreTier")] public int RequiredCoreTier;
        /// <summary>
        /// 위협도 시드 — 두 곳에서 읽는다. ① 플레이어 건물: 진격 목표의 시작 비용(작을수록 먼저 노린다, 코어 0·포탑 10·벽 100).
        /// ② 모든 못 걷는 건물: 진격 경로가 이 건물을 뚫는 비용에 합산(칸당 HP×0.5(상한 200) + 시드). 나무는 1000 —
        /// 숲을 돌아가는 것이 나무를 부수는 것보다 싸게. 뚫는 것이 여전히 가능해(유한) 갇히지는 않는다(2026-09-04).
        /// </summary>
        [JsonProperty("threatSeedCost")] public int ThreatSeedCost;
        [JsonProperty("menuOrder")] public int MenuOrder;
        [JsonProperty("cost")] public List<ItemAmount> Cost = new List<ItemAmount>();

        public override void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            foreach (var c in Cost) c.Resolve(db, errors, owner);
        }
    }
}
