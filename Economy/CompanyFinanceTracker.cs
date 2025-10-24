using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MarketBasedEconomy.Economy
{
    internal sealed class CompanyFinanceTracker
    {
        private static readonly CompanyFinanceTracker s_Instance = new CompanyFinanceTracker();
        public static CompanyFinanceTracker Instance => s_Instance;

        private readonly Dictionary<Entity, CompanyFinanceState> m_States = new Dictionary<Entity, CompanyFinanceState>(EntityComparer.Instance);

        private readonly HashSet<Entity> m_TempActiveSet = new HashSet<Entity>(EntityComparer.Instance);
        private readonly List<Entity> m_PrunedEntities = new List<Entity>();

        private CompanyFinanceTracker()
        {
        }

        public CompanyFinanceState GetState(Entity entity)
        {
            if (m_States.TryGetValue(entity, out var state))
            {
                return state;
            }

            state = CompanyFinanceState.CreateUninitialised();
            m_States.Add(entity, state);
            return state;
        }

        public void SetState(Entity entity, CompanyFinanceState state)
        {
            m_States[entity] = state;
        }

        public void Remove(Entity entity)
        {
            m_States.Remove(entity);
        }

        public void Prune(NativeArray<Entity> activeEntities)
        {
            if (m_States.Count == 0)
            {
                return;
            }

            m_TempActiveSet.Clear();
            for (int i = 0; i < activeEntities.Length; i++)
            {
                m_TempActiveSet.Add(activeEntities[i]);
            }

            m_PrunedEntities.Clear();
            foreach (var kvp in m_States)
            {
                if (!m_TempActiveSet.Contains(kvp.Key))
                {
                    m_PrunedEntities.Add(kvp.Key);
                }
            }

            for (int i = 0; i < m_PrunedEntities.Count; i++)
            {
                m_States.Remove(m_PrunedEntities[i]);
            }
        }
    }

    internal struct CompanyFinanceState
    {
        public bool Initialised;
        public float RentAccumulator;
        public int LastUntaxedIncome;
        public int LastAverageTaxRate;

        public static CompanyFinanceState CreateUninitialised()
        {
            return new CompanyFinanceState
            {
                Initialised = false,
                RentAccumulator = 0f,
                LastUntaxedIncome = 0,
                LastAverageTaxRate = 0
            };
        }

        public int AccrueRent(int rentPerDay)
        {
            if (rentPerDay <= 0)
            {
                return 0;
            }

            float perTick = rentPerDay / (float)Game.Simulation.PropertyRenterSystem.kUpdatesPerDay;
            float total = RentAccumulator + perTick;
            int rentThisTick = (int)math.floor(total);
            RentAccumulator = total - rentThisTick;
            return rentThisTick;
        }

        public void SyncCaches(int untaxedIncome, int averageRate)
        {
            LastUntaxedIncome = untaxedIncome;
            LastAverageTaxRate = averageRate;
            Initialised = true;
        }
    }

    internal sealed class EntityComparer : IEqualityComparer<Entity>
    {
        public static readonly EntityComparer Instance = new EntityComparer();

        private EntityComparer()
        {
        }

        public bool Equals(Entity x, Entity y)
        {
            return x.Equals(y);
        }

        public int GetHashCode(Entity obj)
        {
            return obj.GetHashCode();
        }
    }
}

