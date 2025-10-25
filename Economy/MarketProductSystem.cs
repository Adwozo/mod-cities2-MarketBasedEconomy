using System;
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
    /// Performs sales for company products using market-driven pricing. Zero-weight products follow the existing
    /// virtual-sale flow, while weighted products are priced dynamically before being transferred to money.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ResourceExporterSystem))]
    [UpdateBefore(typeof(ResourceBuyerSystem))]
    public partial class MarketProductSystem : SystemBase
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
                .WithName("MarketProductSale")
                .WithStoreEntityQueryInField(ref m_CompanyQuery)
                .WithReadOnly(resourcePrefabs)
                .WithReadOnly(resourceDatas)
                .WithReadOnly(processLookup)
                .WithoutBurst() // managed helpers + diagnostics
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
                    bool isZeroWeight = resourceData.m_Weight == 0;

                    float sanitizedIndustrialPrice = math.max(0f, resourceData.m_Price.x);
                    float sanitizedServicePrice = math.max(0f, resourceData.m_Price.y);

                    int available = EconomyUtils.GetResources(outputResource, resources);
                    if (available < kMinSaleAmount)
                    {
                        return;
                    }

                    int batch = math.max(processData.m_Output.m_Amount, kMinSaleAmount);
                    int saleAmount = math.clamp(available, kMinSaleAmount, kMaxSalePerTick);
                    saleAmount = math.min(saleAmount, batch);
                    if (saleAmount <= 0)
                    {
                        return;
                    }

                    var manager = MarketEconomyManager.Instance;

                    if (isZeroWeight)
                    {
                        float unitPrice = manager.AdjustPriceComponent(
                            outputResource,
                            sanitizedIndustrialPrice,
                            sanitizedServicePrice,
                            MarketEconomyManager.PriceComponent.Market,
                            skipLogging: false);

                        int revenue = Mathf.RoundToInt(unitPrice * saleAmount);
                        if (revenue <= 0)
                        {
                            return;
                        }

                        EconomyUtils.AddResources(outputResource, -saleAmount, resources);
                        EconomyUtils.AddResources(Resource.Money, revenue, resources);

                        manager.RegisterSupply(outputResource, saleAmount);
                        manager.RegisterDemand(outputResource, saleAmount);

                        Diagnostics.DiagnosticsLogger.Log(
                            "Economy",
                            $"Virtual sale for {outputResource}: amount={saleAmount}, unit={unitPrice:F2}, revenue={revenue}");
                        return;
                    }

                    float industrialComponent = Math.Max(0f, resourceData.m_Price.x);
                    float serviceComponent = Math.Max(0f, resourceData.m_Price.y);
                    float vanillaPrice = industrialComponent + serviceComponent;
                    if (vanillaPrice <= 0f)
                    {
                        return;
                    }

                    float supply;
                    float demand;
                    if (!manager.TryGetSupplyDemand(outputResource, out supply, out demand))
                    {
                        supply = 1f;
                        demand = 1f;
                    }

                    float ratio = demand / math.max(1f, supply);
                    float adjustedPrice = vanillaPrice * ratio;
                    float minPrice = vanillaPrice * manager.MinimumPriceMultiplier;
                    float maxPrice = vanillaPrice * manager.MaximumPriceMultiplier;
                    adjustedPrice = math.clamp(adjustedPrice, minPrice, maxPrice);

                    Analytics.EconomyAnalyticsRecorder.Instance.RecordPrice(outputResource, adjustedPrice);

                    int weightedRevenue = Mathf.RoundToInt(adjustedPrice * saleAmount);
                    if (weightedRevenue <= 0)
                    {
                        return;
                    }

                    EconomyUtils.AddResources(outputResource, -saleAmount, resources);
                    EconomyUtils.AddResources(Resource.Money, weightedRevenue, resources);

                    manager.RegisterSupply(outputResource, saleAmount);
                    manager.RegisterDemand(outputResource, saleAmount);

                    Diagnostics.DiagnosticsLogger.Log(
                        "Economy",
                        $"Market sale for {outputResource}: weight={resourceData.m_Weight}, available={available}, sale={saleAmount}, vanilla={vanillaPrice:F2}, supply={supply:F1}, demand={demand:F1}, ratio={ratio:F2}, adjusted={adjustedPrice:F2}, revenue={weightedRevenue}");
                })
                .Run();
        }
    }
}
