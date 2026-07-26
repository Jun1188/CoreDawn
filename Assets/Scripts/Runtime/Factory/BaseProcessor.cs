using System;
using System.Collections;
using UnityEngine;

public abstract class BaseProcessor : MonoBehaviour
{
    [Header("=== Base Production Settings ===")]
    public RecipeDataSO currentRecipe; 

    protected bool isProcessing = false;
    protected float progressNormalized = 0f; 

    protected abstract bool IsAutomation { get; }

    // === UI 및 비주얼 연동용 이벤트 (UI 게이지, 이펙트 등) ===
    public event Action OnProductionStarted;                     
    public event Action<RecipeDataSO> OnProductionCompleted;     
    public event Action OnProductionStopped;                     
    public event Action OnRecipeChanged;                         

    /// <summary>
    /// 외부 호출 API: 레시피를 주입하고 시간제 제작 코루틴을 가동
    /// </summary>
    public void SetRecipeAndStart(RecipeDataSO recipe)
    {
        if (recipe == null) return;
        
        currentRecipe = recipe;
        OnRecipeChanged?.Invoke();

        if (!isProcessing)
        {
            StartCoroutine(ProductionRoutine());
        }
    }

    public void StopProduction()
    {
        currentRecipe = null;
    }

    private IEnumerator ProductionRoutine()
    {
        isProcessing = true;
        OnProductionStarted?.Invoke();

        while (currentRecipe != null)
        {
            // 1. 재료 충분 여부 확인
            if (!HasEnoughIngredients())
            {
                OnProductionStopped?.Invoke();
                if (IsAutomation)
                {
                    yield return new WaitForSeconds(1f);
                    continue;
                }
                else break;
            }

            // 2. 제작 시작 시 재료 바로 차감 (작업 도중 재료 뺴돌리기 방지)
            ConsumeIngredients();
            progressNormalized = 0f;

            // 3. craftTime 동안 게이지 상승
            float timer = 0f;
            float duration = currentRecipe.craftTime;

            if (duration > 0f)
            {
                while (timer < duration)
                {
                    timer += Time.deltaTime;
                    progressNormalized = Mathf.Clamp01(timer / duration);
                    yield return null;
                }
            }

            // 4. 제작 완료 및 결과물 지급
            RecipeDataSO finishedRecipe = currentRecipe;
            GiveOutputs();
            
            OnProductionCompleted?.Invoke(finishedRecipe); 
            progressNormalized = 0f;

            // 손제작(IsAutomation == false)은 1회 제작 후 종료
            if (!IsAutomation) break;
        }

        isProcessing = false;
        OnProductionStopped?.Invoke();
    }

    protected abstract bool HasEnoughIngredients();
    protected abstract void ConsumeIngredients();
    protected abstract void GiveOutputs();

    public float GetProgress() => progressNormalized; 
    public bool IsProcessing() => isProcessing;       
    public string GetRecipeName() => currentRecipe != null ? currentRecipe.name : "None";
}