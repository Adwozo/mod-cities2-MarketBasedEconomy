using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MarketBasedEconomy.Economy
{
    /// <summary>
    /// Ensures office/telecom style products (zero-weight resources) are sold and generate revenue.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ResourceExporterSystem))]
    [UpdateBefore(typeof(ResourceBuyerSystem))]
    public partial class ZeroWeightProductSystem : SystemBase
    {
        private const int kUpdatesPerDay = 32;
        private const int kMinSaleAmount = 20;
        private const int kMaxSalePerTick = 1000;

        private ResourceSystem m_ResourceSystem;
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_CompanyQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            m_CompanyQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<UpdateFrame>(),
                ComponentType.ReadWrite<Game.Economy.Resources>(),
                ComponentType.Exclude<ServiceAvailable>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            RequireForUpdate(m_CompanyQuery);
        }

        protected override void OnUpdate()
        {
            if (m_CompanyQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            uint updateSlot = SimulationUtils.GetUpdateFrame(m_SimulationSystem.frameIndex, kUpdatesPerDay, 16);
            ResourcePrefabs resourcePrefabs = m_ResourceSystem.GetPrefabs();
            var resourceDatas = GetComponentLookup<ResourceData>(true);
            var processLookup = GetComponentLookup<IndustrialProcessData>(true);

            Entities
                .WithName("ZeroWeightProductSale")
                .WithStoreEntityQueryInField(ref m_CompanyQuery)
                .WithReadOnly(resourcePrefabs)
                .WithReadOnly(resourceDatas)
                .WithReadOnly(processLookup)
                .WithoutBurst() // uses managed helpers + logging
                .ForEach((Entity entity,
                          DynamicBuffer<Game.Economy.Resources> resources,
                          in PrefabRef prefabRef,
                          in UpdateFrame updateFrame) =>
                {
                    if (updateFrame.m_Index != updateSlot)
                    {
                        return;
                    }

                    if (!processLookup.HasComponent(prefabRef.m_Prefab))
                    {
                        return;
                    }

                    IndustrialProcessData processData = processLookup[prefabRef.m_Prefab];
                    Resource outputResource = processData.m_Output.m_Resource;

                    Entity resourceEntity = resourcePrefabs[outputResource];
                    if (!resourceDatas.HasComponent(resourceEntity))
                    {
                        return;
                    }

                    ResourceData resourceData = resourceDatas[resourceEntity];
                    if (resourceData.m_Weight != 0)
                    {
                        return; // handled by vanilla logistics
                    }

                    int available = EconomyUtils.GetResources(outputResource, resources);
                    if (available < kMinSaleAmount)
                    {
                        return;
                    }

                    int batch = math.max(processData.m_Output.m_Amount, kMinSaleAmount);
                    int saleAmount = math.clamp(available, kMinSaleAmount, kMaxSalePerTick);
                    saleAmount = math.min(saleAmount, batch);

                    float unitPrice = MarketEconomyManager.Instance.AdjustPriceComponent(
                        outputResource,
                        resourceData.m_Price.x,
                        resourceData.m_Price.y,
                        MarketEconomyManager.PriceComponent.Service,
                        skipLogging: true);

                    int revenue = Mathf.RoundToInt(unitPrice * saleAmount);
                    if (revenue <= 0)
                    {
                        return;
                    }

                    EconomyUtils.AddResources(outputResource, -saleAmount, resources);
                    EconomyUtils.AddResources(Resource.Money, revenue, resources);

                    Diagnostics.DiagnosticsLogger.Log(
                        "Economy",
                        $"Virtual sale for {outputResource}: amount={saleAmount}, unit={unitPrice:F2}, revenue={revenue}");
                })
                .Run();
        }
    }
}

