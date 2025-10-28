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
    private const float kDefaultOutputPerWorkerPerDay = 6f;

        private ResourceSystem m_ResourceSystem;
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_CompanyQuery;
    private BufferLookup<Employee> m_EmployeeLookup;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_EmployeeLookup = GetBufferLookup<Employee>(true);

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
            m_EmployeeLookup.Update(this);

            using var activeCompanies = m_CompanyQuery.ToEntityArray(Allocator.TempJob);
            var productionTracker = CompanyProductionTracker.Instance;
            productionTracker.Prune(activeCompanies);
            var employeeLookup = m_EmployeeLookup;

            Entities
                .WithName("MarketProductSale")
                .WithStoreEntityQueryInField(ref m_CompanyQuery)
                .WithReadOnly(resourcePrefabs)
                .WithReadOnly(resourceDatas)
                .WithReadOnly(processLookup)
                .WithReadOnly(employeeLookup)
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

                    var manager = MarketEconomyManager.Instance;

                    float industrialPrice = math.max(0f, resourceData.m_Price.x);
                    float servicePrice = math.max(0f, resourceData.m_Price.y);
                    float baselinePrice = manager.AlignComponentsToBaseline(outputResource, ref industrialPrice, ref servicePrice);

                    int employeeCount = employeeLookup.HasBuffer(entity) ? employeeLookup[entity].Length : 0;
                    float configuredOutputPerWorker = kDefaultOutputPerWorkerPerDay;
                    if (RealWorldBaselineState.TryGetOutputPerWorker(outputResource, out float configuredOutput))
                    {
                        configuredOutputPerWorker = math.max(0.1f, configuredOutput);
                    }

                    float desiredProductionPerTick = 0f;
                    if (employeeCount > 0 && configuredOutputPerWorker > 0f)
                    {
                        desiredProductionPerTick = (employeeCount * configuredOutputPerWorker) / kUpdatesPerDay;
                    }

                    int producedThisTick = productionTracker.AccumulateProduction(entity, desiredProductionPerTick);
                    if (producedThisTick > 0)
                    {
                        EconomyUtils.AddResources(outputResource, producedThisTick, resources);
                    }

                    int available = EconomyUtils.GetResources(outputResource, resources);
                    if (available < kMinSaleAmount)
                    {
                        return;
                    }

                    int desiredBatch = (int)math.ceil(math.max(desiredProductionPerTick, producedThisTick));
                    int batch = math.max(math.max(processData.m_Output.m_Amount, desiredBatch), kMinSaleAmount);
                    int saleAmount = math.clamp(available, kMinSaleAmount, kMaxSalePerTick);
                    saleAmount = math.min(saleAmount, batch);
                    if (saleAmount <= 0)
                    {
                        return;
                    }

                    if (isZeroWeight)
                    {
                        if (baselinePrice <= 0f)
                        {
                            return;
                        }

                        int revenue = Mathf.RoundToInt(baselinePrice * saleAmount);
                        if (revenue <= 0)
                        {
                            return;
                        }

                        EconomyUtils.AddResources(outputResource, -saleAmount, resources);
                        EconomyUtils.AddResources(Resource.Money, revenue, resources);

                        Diagnostics.DiagnosticsLogger.Log(
                            "Economy",
                            $"Virtual sale for {outputResource}: amount={saleAmount}, baselineUnit={baselinePrice:F2}, revenue={revenue}");
                        return;
                    }

                    if (baselinePrice <= 0f)
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

                    float sanitizedSupply = math.max(1f, supply);
                    float sanitizedDemand = math.max(1f, demand);

                    MarketEconomyManager.ElasticPriceMetrics metrics;
                    float finalPrice = manager.ComputeElasticPrice(outputResource, baselinePrice, sanitizedSupply, sanitizedDemand, skipLogging: true, out metrics);
                    float multiplier = baselinePrice > 0f ? finalPrice / baselinePrice : 1f;

                    int weightedRevenue = Mathf.RoundToInt(finalPrice * saleAmount);
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
                        $"Market sale for {outputResource}: weight={resourceData.m_Weight}, available={available}, sale={saleAmount}, baseline={baselinePrice:F2}, supply={sanitizedSupply:F1}, demand={sanitizedDemand:F1}, ratio={metrics.Ratio:F3}, exponent={metrics.Exponent:F2}, anchor={metrics.Anchoring:F2}, smoothing={metrics.Smoothing:F2}, bias={metrics.Bias:F2}, raw={metrics.RawPrice:F2}, anchored={metrics.AnchoredPrice:F2}, elastic={metrics.ElasticPrice:F2}, blended={metrics.BlendedPrice:F2}, finalMultiplier={multiplier:F3}, finalPrice={finalPrice:F2}, revenue={weightedRevenue}");
                })
                .Run();
        }
    }
}
