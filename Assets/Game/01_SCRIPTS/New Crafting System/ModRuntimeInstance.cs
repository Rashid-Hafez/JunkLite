using junklite;


namespace junklite
{

    public class ModRuntimeInstance
    {
        public Mod_Data data;
        public ModLogic logic;
        public float durability;

        public ModRuntimeInstance(Mod_Data data)
        {
            this.data = data;
            this.logic = data.logic;
            this.durability = data.maxModDurability;
        }

        internal void Consume(float amount)
        {
            durability -= amount;
            if (durability < 0) durability = 0;
        }


        public bool IsBroken => durability <= 0;
    }
}
