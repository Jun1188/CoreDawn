using UnityEngine;
using CoreDawn.Entities;

namespace CoreDawn.Factory
{
    [RequireComponent(typeof(BuildingView))]
    public class MachineProcessor : BaseProcessor
    {
        private Building _building;   

        // 공장 기계는 재료가 공급되는 한 계속 자동 순환
        protected override bool IsAutomation => true; 

        private void Start()
        {
            var entity = GetComponentInParent<BuildingView>();
            _building = entity != null ? entity.Sim : null;
            if (_building == null)
                Debug.LogWarning("[MachineProcessor] 심 건물(Sim) 연결을 찾지 못했습니다.", this);
        }

        protected override bool HasEnoughIngredients()
        {
            if (_building == null || currentRecipe == null) return false;

            var snapshot = _building.Input.Snapshot(); 
            foreach (var input in currentRecipe.inputs)
            {
                if (input.item == null) continue;
                int required = input.amount;
                int found = 0;
                foreach (var (item, count) in snapshot)
                {
                    if (item == input.item) { found = count; break; }
                }
                if (found < required) return false;
            }
            return true;
        }

        protected override void ConsumeIngredients()
        {
            foreach (var input in currentRecipe.inputs)
            {
                if (input.item != null)
                    _building.Input.TryConsume(input.item, input.amount); 
            }
            _building.NotifyUpstream(); 
        }

        protected override void GiveOutputs()
        {
            foreach (var output in currentRecipe.outputs)
            {
                if (output.item != null) 
                    _building.Output.TryAdd(output.item, output.amount);
            }
            _building.NotifyUpstream(); 
        }
    }
}
