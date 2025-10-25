using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using MarketBasedEconomy.Analytics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MarketBasedEconomy.Economy
{
    /// <summary>
    /// Updates <see cref="EconomyParameterData"/> with labor-market-driven wage multipliers
    /// before vanilla systems consume the component.
    /// </summary>
    public partial class WageAdjustmentSystem : Unity.Entities.SystemBase
    {
        private EntityQuery m_EconomyParameterQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EconomyParameterQuery = GetEntityQuery(ComponentType.ReadWrite<EconomyParameterData>());
            RequireForUpdate(m_EconomyParameterQuery);
        }

        protected override void OnUpdate()
        {
            var laborManager = LaborMarketManager.Instance;
            var info = laborManager.Evaluate();

            EntityManager entityManager = EntityManager;
            var economyParamType = GetComponentTypeHandle<EconomyParameterData>(false);

            var chunks = m_EconomyParameterQuery.ToArchetypeChunkArray(Allocator.TempJob);
            bool recordedSample = false;
            foreach (var chunk in chunks)
            {
                var dataArray = chunk.GetNativeArray(ref economyParamType);
                for (int i = 0; i < dataArray.Length; i++)
                {
                    var data = dataArray[i];
                    laborManager.EnsureBaseline(data);
                    laborManager.ApplyAdjustedWages(ref data, info);
                    dataArray[i] = data;

                    if (!recordedSample)
                    {
                        EconomyAnalyticsRecorder.Instance.RecordWageSample(
                            data.m_Wage0,
                            data.m_Wage1,
                            data.m_Wage2,
                            data.m_Wage3,
                            data.m_Wage4);
                        recordedSample = true;
                    }
                }
            }
            chunks.Dispose();
        }
    }
}

