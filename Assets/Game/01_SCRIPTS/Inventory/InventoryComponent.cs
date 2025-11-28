using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using junklite;

public class InventoryComponent : MonoBehaviour
{
    
    struct InventoryItem
    {
        public string itemId;
        public int quantity;
        public bool isEquipped;
        public Sprite thumbnail;
        public InventoryItem(string id, int qty, bool equipped, Sprite thumbnail)
        {
            itemId = id;
            quantity = qty;
            isEquipped = equipped;
            this.thumbnail = thumbnail;
        }
    };
    List<InventoryItem> inventoryItems = new List<InventoryItem>();
    List<InventoryItem> equippedItems = new List<InventoryItem>();

    List<Mod_Data> ModsInReserve = new List<Mod_Data>();

    void Start()
    {
        
    }

    void Update()
    {
        
    }

}
