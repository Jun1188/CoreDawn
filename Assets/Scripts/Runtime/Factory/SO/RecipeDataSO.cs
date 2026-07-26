using UnityEngine;
using System;

[CreateAssetMenu(fileName = "NewRecipe", menuName = "Factory/Recipe")]
public class RecipeDataSO : GameDataSO
{
    public int tier = 0;
    
    [Serializable]
    public struct Slot { public ItemDataSO item; public int amount; }

    public Slot[] inputs;
    public Slot[] outputs;
    public float  craftTime = 2f;
}
