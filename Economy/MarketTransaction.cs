using Game.Economy;
using Unity.Entities;

namespace MarketBasedEconomy.Economy
{
    public struct MarketTransaction : IBufferElementData
    {
        public Resource Resource;
        public float Amount;
        public MarketTransactionSystem.TransactionType Type;

        public MarketTransactionSystem.Transaction ToData()
        {
            return new MarketTransactionSystem.Transaction
            {
                Resource = Resource,
                Amount = Amount,
                Type = Type
            };
        }
    }
}
