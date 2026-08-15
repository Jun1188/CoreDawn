using UnityEngine;

public class InventoryProcessor : BaseProcessor
{
    protected override bool IsAutomation => false; // 손제작은 1회성[cite: 13, 26]

    protected override bool HasEnoughIngredients()
    {
        if (currentRecipe == null || PlayerInventoryHolder.Instance == null) return false;
        var inputContainer = PlayerInventoryHolder.Instance.CraftingInputContainer;

        foreach (var input in currentRecipe.inputs)
        {
            if (input.item == null) continue;
            if (inputContainer.CountOf(input.item) < input.amount)
                return false;
        }
        return true;
    }

    protected override void ConsumeIngredients()
    {
        if (currentRecipe == null || PlayerInventoryHolder.Instance == null) return;
        var inputContainer = PlayerInventoryHolder.Instance.CraftingInputContainer;

        foreach (var input in currentRecipe.inputs)
        {
            if (input.item == null) continue;
            inputContainer.TryConsume(input.item, input.amount);
        }

    }

    protected override void GiveOutputs()
    {
        if (currentRecipe == null || PlayerInventoryHolder.Instance == null) return;
        var outputContainer = PlayerInventoryHolder.Instance.CraftingOutputContainer;

        foreach (var output in currentRecipe.outputs)
        {
            if (output.item == null || output.amount <= 0) continue;

            // 결과창에 넣고, 공간 부족 시 플레이어 가방/핫바로 자동 분배
            if (!outputContainer.TryAdd(output.item, output.amount))
            {
                PlayerInventoryHolder.Instance.AddItemToPlayer(output.item, output.amount);
            }
        }

    }
}