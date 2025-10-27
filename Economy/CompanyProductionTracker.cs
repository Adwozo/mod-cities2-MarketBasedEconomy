using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MarketBasedEconomy.Economy
{
    internal sealed class CompanyProductionTracker
    {
        private static readonly CompanyProductionTracker s_Instance = new CompanyProductionTracker();
        public static CompanyProductionTracker Instance => s_Instance;

        private readonly Dictionary<Entity, ProductionState> m_States = new Dictionary<Entity, ProductionState>(EntityComparer.Instance);
        private readonly HashSet<Entity> m_TempActiveSet = new HashSet<Entity>(EntityComparer.Instance);
        private readonly List<Entity> m_PrunedEntities = new List<Entity>();

        private CompanyProductionTracker()
        {
        }

        public int AccumulateProduction(Entity entity, float desiredUnits)
        {
            if (!m_States.TryGetValue(entity, out var state))
            {
                state = ProductionState.Create();
            }

            float total = state.Accumulator + math.max(0f, desiredUnits);
            int wholeUnits = (int)math.floor(total);
            state.Accumulator = total - wholeUnits;
            m_States[entity] = state;
            return wholeUnits;
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

        private struct ProductionState
        {
            public float Accumulator;

            public static ProductionState Create()
            {
                return new ProductionState
                {
                    Accumulator = 0f
                };
            }
        }
    }
}
