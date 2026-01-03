namespace junklite
{
    /// <summary>
    /// Runtime state for an equipped mod.
    /// Just tracks durability - all behavior lives in ModData.
    /// </summary>
    public class ActiveMod
    {
        public ModData data;
        public float durability;

        public ActiveMod(ModData modData)
        {
            data = modData;
            durability = modData.maxDurability;
        }

        public void ConsumeDurability(float amount)
        {
            durability -= amount;
            if (durability < 0f) durability = 0f;
        }

        public bool IsBroken => durability <= 0f;
        public float DurabilityPercent => data.maxDurability > 0f ? durability / data.maxDurability : 0f;
    }
}