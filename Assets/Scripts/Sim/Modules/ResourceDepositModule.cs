using System;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 광맥 — "이 칸 아래에 이 자원이 묻혀 있다". 심 엔티티의 모듈(구 MonoBehaviour ResourceNode의 심 절반).
    ///
    /// 한 칸짜리다: 넓은 광맥은 광맥 칸을 여럿 놓는 것이고, 2×2 채굴기는 덮는 칸들 위에 선다.
    /// 매장량은 없다 — 바닥나지 않고, 캐는 속도는 이 광맥의 채굴 시간(extractInterval)과 캐는 쪽의 배율(채굴기)만이 정한다. 손은 배율 1.
    /// 부서지지 않는다(Health 없음). 공장 격자를 차지하지 않는다(채굴기가 그 위에 올라간다) — 색인은 FactorySystem이 따로 든다.
    /// </summary>
    public sealed class ResourceDepositModule : EntityModule
    {
        public ResourceDepositModuleDef Def { get; }

        public ResourceDepositModule(ResourceDepositModuleDef def) { Def = def ?? throw new ArgumentNullException(nameof(def)); }

        /// <summary>이 광맥이 놓인 칸 — FactorySystem.PlaceDeposit이 색인하며 적는다.</summary>
        public Vector2Int Cell { get; internal set; }

        public ItemDef Resource => Def.Resource;

        /// <summary>누적 채굴량(채굴기+손) — 튜토리얼이 "채굴했는가"를 이걸로 본다.</summary>
        public int TotalExtracted { get; private set; }

        /// <summary>1개를 캐는 데 걸리는 시간(초) — 손은 그대로, 채굴기는 이 값 ÷ 배율. "얼마나 캐기 어려운 광맥인가"는 땅이 갖는다.</summary>
        public float ExtractInterval => Math.Max(0.01f, Def.ExtractInterval);

        /// <summary>캔다 — 매장량이 없으므로 요청한 만큼 그대로 나온다. 누적 채굴량에 더한다.</summary>
        public int Extract(int amount)
        {
            if (Resource == null || amount <= 0) return 0;
            TotalExtracted += amount;
            return amount;
        }

        public void RestoreState(int totalExtracted) => TotalExtracted = Math.Max(0, totalExtracted);
    }
}
