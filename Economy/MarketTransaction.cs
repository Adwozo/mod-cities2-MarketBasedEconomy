using Game.Economy;
using Unity.Entities;

namespace MarketBasedEconomy.Economy
{
    public struct MarketTransaction : IBufferElementData
    {
        public Resource Resource;
        public float Amount;
        public MarketTransactionType Type;
    }

    public enum MarketTransactionType : byte
    {
        Supply,
        Demand
    }
}
