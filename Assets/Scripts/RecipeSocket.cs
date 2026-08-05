using Sirenix.OdinInspector;
using UnityEngine;

public class RecipeSocket : MonoBehaviour
{

    public Transform inputSocket;
    public Transform outputSocket;
    public Transform slotPrefab;

    [HideInInspector]
    public AssemblerBehavior target;

    ItemSocket[] slots;
    RecipeDataSO recipe;

    public void Setup(RecipeDataSO _recipe)
    {
        if (_recipe == null) { Debug.LogError("trying setup recipeSocket without proper recipe"); return; }

        recipe = _recipe;

        //foreach (var x in slots)
        //{
        //    if(x != null)
        //        Destroy(x.gameObject);
        //}

        slots = new ItemSocket[_recipe.inputs.Length + _recipe.outputs.Length];

        for (int i = 0; i < _recipe.inputs.Length; i++)
        {
            slots[i] = Instantiate(slotPrefab, inputSocket).GetComponent<ItemSocket>();
            slots[i].SetItem(_recipe.inputs[i].item, _recipe.inputs[i].amount);
        }

        for (int i = 0; i < _recipe.outputs.Length; i++)
        {
            // 출력 슬롯은 입력 슬롯 뒤에 이어 붙는다. 인덱스는 반드시 inputs.Length 기준 —
            // outputs.Length로 잡으면 출력이 2개 이상일 때 엉뚱한 슬롯에 쓴다.
            int slot = i + _recipe.inputs.Length;
            slots[slot] = Instantiate(slotPrefab, outputSocket).GetComponent<ItemSocket>();
            slots[slot].SetItem(_recipe.outputs[i].item, _recipe.outputs[i].amount);
        }
    }

 
    public void OnClicked()
    {
        if (recipe == null) { Debug.LogError("trying select recipe without proper recipe"); return; }
        if(target == null) { Debug.LogError("trying select recipe without proper target"); return; }
        
        target.SetRecipe(recipe);

        

    }

}
