using UnityEngine;
using System;

[CreateAssetMenu(fileName = "NewRecipe", menuName = "Factory/Recipe")]
public class RecipeDataSO : GameDataSO
{
    [Tooltip("해금 코어 티어. GameManager.UnlockedTier가 이보다 낮으면 수제작/조립기 목록에서 숨겨진다.")]
    public int tier = 0;

    [Serializable]
    public struct Slot { public ItemDataSO item; public int amount; }

    public Slot[] inputs;
    public Slot[] outputs;
    public float  craftTime = 2f;
}
