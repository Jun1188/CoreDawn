using UnityEngine;

namespace CoreDawn.Factory
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
