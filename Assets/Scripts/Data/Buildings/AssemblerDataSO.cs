using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.FPS;
using CoreDawn.Save;
using CoreDawn.UI;
using CoreDawn.Factory;

namespace CoreDawn.Data
{
    /// <summary>조합기. 입력 버퍼에 재료가 모이면 레시피대로 조합해 출력한다.</summary>
    [CreateAssetMenu(fileName = "NewAssembler", menuName = "Factory/Buildings/Assembler")]
    public class AssemblerDataSO : BuildingDataSO
    {
        [Header("레시피")]
        public RecipeDataSO[] availableRecipes;


        protected override void OnValidate()
        {
            base.OnValidate();
            // 슬롯이 부족한 레시피는 런타임에 조용히 영구 stall되므로 에디터에서 잡는다.
            // (자동 확장은 하지 않음 — 슬롯 수는 디자이너가 의도한 값이어야 함)
            if (availableRecipes == null) return;
            foreach (var r in availableRecipes)
            {
                if (r == null) continue;
                int needIn  = r.inputs?.Length  ?? 0;
                int needOut = r.outputs?.Length ?? 0;
                if (needIn > inputSlots || needOut > outputSlots)
                    Debug.LogError($"[Assembler] '{name}'의 슬롯이 레시피 '{r.name}'에 부족함 — " +
                                   $"입력 {inputSlots}/{needIn}칸, 출력 {outputSlots}/{needOut}칸. " +
                                   "슬롯을 늘리거나 레시피를 제거할 것.", this);
            }
        }
    }

    // ─── 행동 ──────────────────────────────────────────────────────

}
