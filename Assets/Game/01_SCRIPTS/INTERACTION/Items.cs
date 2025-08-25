using UnityEngine;

[CreateAssetMenu(fileName = "Items", menuName = "Junklite/Items")]
public class Items : ScriptableObject
{
    public enum ItemType
    {
        Consumable,
        Equipment,
        Material
    }

    [Header("Core")]
    public ItemType itemType;
    public string itemId;
    public string displayName;

    [Header("Stats")]
    public float weight;
    public int stackSize;
}
