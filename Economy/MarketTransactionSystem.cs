using Game.Economy;
using Game.Simulation;
using Unity.Entities;

namespace MarketBasedEconomy.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    internal partial struct MarketTransactionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MarketEconomyManager.MarketMetricsProxy>();
        }

        public void OnDestroy(ref SystemState state)
        {
            // Nothing to dispose
        }

        public void OnUpdate(ref SystemState state)
        {
            var manager = MarketEconomyManager.Instance;

            foreach (var transactions in SystemAPI.Query<DynamicBuffer<MarketTransaction>>())
            {
                for (int i = 0; i < transactions.Length; i++)
                {
                    var transaction = transactions[i];
                    if (transaction.Amount <= 0f || transaction.Resource == Resource.NoResource)
                    {
                        continue;
                    }

                    switch (transaction.Type)
                    {
                        case MarketTransactionType.Supply:
                            manager.RegisterSupply(transaction.Resource, transaction.Amount);
                            break;
                        case MarketTransactionType.Demand:
                            manager.RegisterDemand(transaction.Resource, transaction.Amount);
                            break;
                    }
                }

                transactions.Clear();
            }
        }
    }
}
