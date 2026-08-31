using UnityEngine;
using System;
using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
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

        // ── 전환기 브리지(ItemDataSO와 같은 규칙) ──
        RecipeDef _def;
        public RecipeDef Def
        {
            get
            {
                if (_def == null) { var db = SimHost.Database; if (db != null) _def = db.Recipe(db.LegacyId(Id)); }
                return _def;
            }
        }
        public static implicit operator RecipeDef(RecipeDataSO so) => so != null ? so.Def : null;
    }
}
