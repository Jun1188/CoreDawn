using UnityEngine;
using System;

[CreateAssetMenu(fileName = "NewRecipe", menuName = "Factory/Recipe")]
public class RecipeDataSO : GameDataSO
{
    public int tier = 0;

    [Tooltip("이 값보다 GameManager.UnlockedTier가 낮으면 수제작/조립기 목록에서 숨겨진다.")]
    public int requiredCoreTier = 0;

    [Serializable]
    public struct Slot { public ItemDataSO item; public int amount; }

    public Slot[] inputs;
    public Slot[] outputs;
    public float  craftTime = 2f;
}
