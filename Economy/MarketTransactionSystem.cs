using Game.Economy;
using Game.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace MarketBasedEconomy.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    internal partial struct MarketTransactionSystem : ISystem
    {
        private NativeList<Transaction> m_Transactions;

        public void OnCreate(ref SystemState state)
        {
            m_Transactions = new NativeList<Transaction>(Allocator.Persistent);
            state.RequireForUpdate<MarketEconomyManager.MarketMetricsProxy>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (m_Transactions.IsCreated)
            {
                m_Transactions.Dispose();
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            var writer = m_Transactions.AsParallelWriter();
            var job = new TransactionBufferJob
            {
                Transactions = writer
            };
            job.ScheduleParallel().Complete();

            if (m_Transactions.Length == 0)
            {
                return;
            }

            for (int i = 0; i < m_Transactions.Length; i++)
            {
                var transaction = m_Transactions[i];
                if (transaction.Amount <= 0f)
                {
                    continue;
                }

                switch (transaction.Type)
                {
                    case TransactionType.Supply:
                        MarketEconomyManager.Instance.RegisterSupply(transaction.Resource, transaction.Amount);
                        break;
                    case TransactionType.Demand:
                        MarketEconomyManager.Instance.RegisterDemand(transaction.Resource, transaction.Amount);
                        break;
                }
            }

            m_Transactions.Clear();
        }

        [BurstCompile]
        public partial struct TransactionBufferJob : IJobEntity
        {
            public NativeList<Transaction>.ParallelWriter Transactions;

            public void Execute(in MarketTransaction transaction)
            {
                Transactions.AddNoResize(transaction.ToData());
            }
        }

        internal struct Transaction
        {
            public Resource Resource;
            public float Amount;
            public TransactionType Type;
        }

        internal enum TransactionType : byte
        {
            Supply,
            Demand
        }
    }
}
