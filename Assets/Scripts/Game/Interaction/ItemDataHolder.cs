using UnityEngine;
using CoreDawn.Data;

namespace CoreDawn.Interaction
{
    public class ItemDataHolder : MonoBehaviour
    {
        [SerializeField]
        ItemDataSO item;

        public ItemDataSO GetItem()
        {
            return item;
        }

    }
}
