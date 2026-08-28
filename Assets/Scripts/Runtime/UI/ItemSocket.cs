using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ItemSocket : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private Image it_sprite;       
    [SerializeField] private TextMeshProUGUI it_counter;       
    
    [Header("Current Data")]
    [SerializeField] private ItemDataSO itemDataSO; // 현재 이 칸에 들어있는 아이템 정보

    public void ClearSlot() 
    {
        itemDataSO = null; 
        
        if (it_sprite != null) 
        {
            it_sprite.sprite = null; 
            it_sprite.enabled = false; 
        }

        if (it_counter != null) 
        {
            it_counter.text = ""; 
        }
    }

    public void SetItem(ItemDataSO item, int amount) 
    {
        if (item == null || amount <= 0) 
        {
            ClearSlot(); 
            return; 
        }

        itemDataSO = item; 
        
        if (it_sprite != null) 
        {
            it_sprite.sprite = item.icon; 
            it_sprite.enabled = true;      
        }

        if (it_counter != null) 
        {
            it_counter.text = (amount > 1) ? amount.ToString() : ""; 
        }
    }

    public ItemDataSO GetItem() 
    {
        return itemDataSO; 
    }
}