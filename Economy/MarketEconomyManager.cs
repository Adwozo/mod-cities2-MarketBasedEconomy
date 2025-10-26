using System;
using Colossal.Logging;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using MarketBasedEconomy.Analytics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Jobs;

namespace MarketBasedEconomy.Economy
{
    /// <summary>
    /// Central place for replacing vanilla price discovery with a market-driven model.
    /// </summary>
    public sealed class MarketEconomyManager
    {
        private static readonly Lazy<MarketEconomyManager> s_Instance = new(() => new MarketEconomyManager());
        private readonly ILog m_Log = LogManager.GetLogger($"{nameof(MarketBasedEconomy)}.{nameof(MarketEconomyManager)}");
        private BudgetSystem m_BudgetSystem;
        private IndustrialDemandSystem m_IndustrialDemandSystem;
        private CommercialDemandSystem m_CommercialDemandSystem;
        private CountCompanyDataSystem m_CountCompanyDataSystem;
        private ResourceSystem m_ResourceSystem;
        private NativeHashMap<int, MarketMetrics> m_MarketMetrics;
        private NativeHashMap<int, MarketPriceState> m_PriceStates;
        private NativeHashMap<Entity, CompanyFinanceState> m_CompanyFinanceStates;

        public struct MarketMetrics
        {
            public float SupplyAmount;
            public float DemandAmount;
            public uint LastUpdatedTick;

            public static MarketMetrics CreateSupply(float amount)
            {
                return new MarketMetrics
                {
                    SupplyAmount = math.max(0f, amount)
                };
            }

            public static MarketMetrics CreateDemand(float amount)
            {
                return new MarketMetrics
                {
                    DemandAmount = math.max(0f, amount)
                };
            }

            public MarketMetrics AccumulateSupply(float amount)
            {
                SupplyAmount += math.max(0f, amount);
                return this;
            }

            public MarketMetrics AccumulateDemand(float amount)
            {
                DemandAmount += math.max(0f, amount);
                return this;
            }
        }

        public struct MarketPriceState
        {
            public float Multiplier;
            public float ExternalFloor;
            public float ExternalCeiling;
        }


        public struct CompanyFinanceState
        {
            public float RentAccruedToday;
            public int LastRentTick;

            public void AccumulateRent(float rent, int currentTick)
            {
                if (LastRentTick != currentTick)
                {
                    RentAccruedToday = 0f;
                }

                RentAccruedToday += math.max(0f, rent);
                LastRentTick = currentTick;
            }

            public float ConsumeRent(float fraction = 1f)
            {
                fraction = math.saturate(fraction);
                float consumed = RentAccruedToday * fraction;
                RentAccruedToday = math.max(0f, RentAccruedToday - consumed);
                return consumed;
            }

            public void Reset()
            {
                RentAccruedToday = 0f;
                LastRentTick = 0;
            }
        }

        public struct MarketMetricsProxy : IComponentData
        {
        }

        private MarketEconomyManager()
        {
            m_Log.SetShowsErrorsInUI(false);
            m_MarketMetrics = new NativeHashMap<int, MarketMetrics>(EconomyUtils.ResourceCount, Allocator.Persistent);
            m_PriceStates = new NativeHashMap<int, MarketPriceState>(EconomyUtils.ResourceCount, Allocator.Persistent);
        }

        public static MarketEconomyManager Instance => s_Instance.Value;

        /// <summary>
        /// Prevent runaway prices by clamping multipliers.
        /// </summary>
        public float MinimumPriceMultiplier { get; set; } = 0.5f;

        /// <summary>
        /// Prevent runaway prices by clamping multipliers.
        /// </summary>
        public float MaximumPriceMultiplier { get; set; } = 1.5f;

        /// <summary>
        /// Controls how aggressively the system reacts to demand imbalance. Range [0,1].
        /// </summary>
        public float Sensitivity { get; set; } = 0.65f;

        /// <summary>
        /// How strongly external trade pricing influences local market prices. Range [0,1].
        /// </summary>
        public float ExternalPriceInfluence { get; set; } = 0.35f;

        /// <summary>
        /// Extra multiplier applied to the industrial (production) portion of the price once the market multiplier is computed.
        /// </summary>
        public float IndustrialComponentBias { get; set; } = 1f;

        /// <summary>
        /// Extra multiplier applied to the service (retail) portion of the price once the market multiplier is computed.
        /// </summary>
        public float ServiceComponentBias { get; set; } = 1f;

        /// <summary>
        /// Step size applied when adjusting price multipliers based on supply/demand imbalance.
        /// </summary>
        public float PriceStep { get; set; } = 0.05f;

        /// <summary>
        /// Demand vs supply tolerance before a price step is triggered.
        /// </summary>
        public float DemandTolerance { get; set; } = 0.05f;

        /// <summary>
        /// Anchoring weight applied to pull elastic prices back toward the vanilla reference. Range [0,1].
        /// </summary>
        public float PriceAnchoringStrength { get; set; } = 0.1f;

        /// <summary>
        /// Controls how quickly prices ease into the transport-cost band edges. Range (0,1].
        /// </summary>
        public float LogisticSmoothingScale { get; set; } = 0.5f;

        public enum PriceComponent
        {
            Market,
            Industrial,
            Service
        }

        public readonly struct ElasticPriceMetrics
        {
            public ElasticPriceMetrics(
                float ratio,
                float exponent,
                float anchoring,
                float smoothing,
                float bias,
                float minPrice,
                float maxPrice,
                float elasticPrice,
                float blendedPrice,
                float finalPrice,
                float rawPrice,
                float anchoredPrice)
            {
                Ratio = ratio;
                Exponent = exponent;
                Anchoring = anchoring;
                Smoothing = smoothing;
                Bias = bias;
                MinPrice = minPrice;
                MaxPrice = maxPrice;
                ElasticPrice = elasticPrice;
                BlendedPrice = blendedPrice;
                FinalPrice = finalPrice;
                RawPrice = rawPrice;
                AnchoredPrice = anchoredPrice;
            }

            public float Ratio { get; }
            public float Exponent { get; }
            public float Anchoring { get; }
            public float Smoothing { get; }
            public float Bias { get; }
            public float MinPrice { get; }
            public float MaxPrice { get; }
            public float ElasticPrice { get; }
            public float BlendedPrice { get; }
            public float FinalPrice { get; }
            public float RawPrice { get; }
            public float AnchoredPrice { get; }
        }

        public float ComputeElasticPrice(Resource resource, float vanillaPrice, float supply, float demand, bool skipLogging, out ElasticPriceMetrics metrics)
        {
            float sanitizedVanilla = math.max(0f, vanillaPrice);
            float smoothing = math.clamp(LogisticSmoothingScale, 0.05f, 1f);

            if (resource == Resource.Money || sanitizedVanilla <= 0f)
            {
                metrics = new ElasticPriceMetrics(1f, 0f, PriceAnchoringStrength, smoothing, 0f, sanitizedVanilla, sanitizedVanilla, sanitizedVanilla, sanitizedVanilla, sanitizedVanilla, sanitizedVanilla, sanitizedVanilla);
                return sanitizedVanilla;
            }

            float sanitizedSupply = math.max(1f, supply);
            float sanitizedDemand = math.max(1f, demand);
            float ratio = sanitizedDemand / sanitizedSupply;

            float sensitivity = math.clamp(Sensitivity, 0f, 1f);
            float exponent = math.lerp(0.25f, 3f, sensitivity);
            float rawPrice = sanitizedVanilla * math.pow(ratio, exponent);

            float anchoring = math.clamp(PriceAnchoringStrength, 0f, 1f);
            float anchoredPrice = math.lerp(rawPrice, sanitizedVanilla, anchoring);

            float minMultiplier = math.max(0f, MinimumPriceMultiplier);
            float maxMultiplier = math.max(0f, MaximumPriceMultiplier);
            if (maxMultiplier < minMultiplier)
            {
                float swapMultiplier = minMultiplier;
                minMultiplier = maxMultiplier;
                maxMultiplier = swapMultiplier;
            }

            float minPrice = sanitizedVanilla * minMultiplier;
            float maxPrice = sanitizedVanilla * maxMultiplier;
            if (maxPrice < minPrice)
            {
                float swapPrice = minPrice;
                minPrice = maxPrice;
                maxPrice = swapPrice;
            }

            float bandWidth = math.max(1e-3f, maxPrice - minPrice);
            float halfBand = 0.5f * bandWidth;
            float baselineNormalized = math.clamp((sanitizedVanilla - minPrice) / bandWidth, 1e-3f, 1f - 1e-3f);
            float bias = math.log(baselineNormalized / (1f - baselineNormalized));

            float denominator = math.max(1e-3f, smoothing * halfBand);
            float logisticArg = (anchoredPrice - sanitizedVanilla) / denominator;
            float logisticInput = math.clamp(logisticArg + bias, -60f, 60f);
            float sigma = 1f / (1f + math.exp(-logisticInput));

            float elasticPrice = minPrice + bandWidth * sigma;
            float externalBlend = math.clamp(ExternalPriceInfluence, 0f, 1f);
            float blendedPrice = math.lerp(elasticPrice, sanitizedVanilla, externalBlend);
            float finalPrice = math.clamp(blendedPrice, minPrice, maxPrice);

            metrics = new ElasticPriceMetrics(ratio, exponent, anchoring, smoothing, bias, minPrice, maxPrice, elasticPrice, blendedPrice, finalPrice, rawPrice, anchoredPrice);

            EconomyAnalyticsRecorder.Instance.RecordPrice(resource, finalPrice);

            if (!skipLogging)
            {
                Diagnostics.DiagnosticsLogger.Log(
                    "Economy",
                    $"Elastic price for {resource}: vanilla={sanitizedVanilla:F2}, supply={sanitizedSupply:F1}, demand={sanitizedDemand:F1}, ratio={ratio:F3}, exponent={exponent:F2}, anchor={anchoring:F2}, smoothing={smoothing:F2}, bias={bias:F2}, raw={rawPrice:F2}, anchored={anchoredPrice:F2}, elastic={elasticPrice:F2}, blended={blendedPrice:F2}, final={finalPrice:F2}");
            }

            return finalPrice;
        }

        /// <summary>
        /// Clears cached references so the next request resolves fresh systems.
        /// </summary>
        public void ResetCaches()
        {
            m_BudgetSystem = null;
            m_IndustrialDemandSystem = null;
            m_CommercialDemandSystem = null;
            m_CountCompanyDataSystem = null;
            m_ResourceSystem = null;
            if (m_MarketMetrics.IsCreated)
            {
                m_MarketMetrics.Clear();
            }
            if (m_PriceStates.IsCreated)
            {
                m_PriceStates.Clear();
            }
        }

        /// <summary>
        /// Returns a new price based on supply-demand information. Falls back to vanilla price when insufficient data is available.
        /// </summary>
        public float AdjustMarketPrice(Resource resource, float vanillaPrice, bool skipLogging = false)
        {
            if (resource == Resource.Money)
            {
                return vanillaPrice;
            }
            if (vanillaPrice <= 0f || resource == Resource.NoResource)
            {
                if (!skipLogging)
                {
                    Diagnostics.DiagnosticsLogger.Log("Economy", $"Price adjust skipped for {resource}: vanilla={vanillaPrice:F2}.");
                }
                return vanillaPrice;
            }

            if (IsZeroWeightResource(resource))
            {
                if (!skipLogging)
                {
                    Diagnostics.DiagnosticsLogger.Log("Economy", $"Price adjust skipped for zero-weight {resource}; using vanilla price {vanillaPrice:F2}.");
                }

                return vanillaPrice;
            }

            float supply;
            float demand;
            if (!TryGetSupplyDemand(resource, out supply, out demand))
            {
                if (!skipLogging)
                {
                    Diagnostics.DiagnosticsLogger.Log("Economy", $"No supply/demand for {resource}; using vanilla price {vanillaPrice:F2}.");
                }
                return vanillaPrice;
            }

            float finalPrice = ComputeElasticPrice(resource, vanillaPrice, supply, demand, skipLogging, out _);
            return finalPrice;
        }

        /// <summary>
        /// Adjusts a specific price component (industrial or service) based on the market multiplier and component bias.
        /// </summary>
        public float AdjustPriceComponent(Resource resource, float industrialComponent, float serviceComponent, PriceComponent component, bool skipLogging = false)
        {
            float sanitizedIndustrial = math.max(0f, industrialComponent);
            float sanitizedService = math.max(0f, serviceComponent);
            float vanillaTotal = sanitizedIndustrial + sanitizedService;

            if (vanillaTotal <= 0f || resource == Resource.NoResource || resource == Resource.Money)
            {
                float fallback = component switch
                {
                    PriceComponent.Industrial => sanitizedIndustrial,
                    PriceComponent.Service => sanitizedService,
                    _ => vanillaTotal
                };

                if (!skipLogging)
                {
                    Diagnostics.DiagnosticsLogger.Log(
                        "Economy",
                        $"AdjustPriceComponent {component} for {resource}: vanillaTotal<=0, industrial={sanitizedIndustrial:F2}, service={sanitizedService:F2}, multiplier=1.00, returning {fallback:F2}");
                }

                return fallback;
            }

            float multiplier = GetOrUpdatePriceMultiplier(resource);

            float industrialBias = math.max(0f, IndustrialComponentBias);
            float serviceBias = math.max(0f, ServiceComponentBias);

            float industrialAdjusted = math.max(0f, sanitizedIndustrial * multiplier * industrialBias);
            float serviceAdjusted = math.max(0f, sanitizedService * multiplier * serviceBias);

            float totalAdjusted = industrialAdjusted + serviceAdjusted;

            if (component == PriceComponent.Market)
            {
                EconomyAnalyticsRecorder.Instance.RecordPrice(resource, totalAdjusted);

                if (!skipLogging)
                {
                    Diagnostics.DiagnosticsLogger.Log(
                        "Economy",
                        $"AdjustPriceComponent {component} for {resource}: vanillaTotal={vanillaTotal:F2}, multiplier={multiplier:F3}, result={totalAdjusted:F2}");
                }

                return totalAdjusted;
            }

            float result = component switch
            {
                PriceComponent.Industrial => industrialAdjusted,
                PriceComponent.Service => serviceAdjusted,
                _ => industrialAdjusted + serviceAdjusted
            };

            if (!skipLogging)
            {
                Diagnostics.DiagnosticsLogger.Log(
                    "Economy",
                    $"AdjustPriceComponent {component} for {resource}: vanillaTotal={vanillaTotal:F2}, multiplier={multiplier:F3}, biases=({industrialBias:F2},{serviceBias:F2}), industrial={sanitizedIndustrial:F2}->{industrialAdjusted:F2}, service={sanitizedService:F2}->{serviceAdjusted:F2}, result={result:F2}");
            }

            return result;
        }

        /// <summary>
        /// Exposes raw snapshot data for debugging or UI overlays.
        /// </summary>
        public bool TryGetSnapshot(Resource resource, out MarketSnapshot snapshot)
        {
            snapshot = default;
            var budgetSystem = GetBudgetSystem();
            if (budgetSystem == null || !budgetSystem.HasData)
            {
                return false;
            }

            try
            {
                int tradeBalance = budgetSystem.GetTrade(resource);
                int tradeWorth = budgetSystem.GetTradeWorth(resource);
                int2 processingWorkers = budgetSystem.GetCompanyWorkers(service: false, resource);
                int2 serviceWorkers = budgetSystem.GetCompanyWorkers(service: true, resource);
                int processingCompanies = budgetSystem.GetCompanyCount(service: false, resource);
                int serviceCompanies = budgetSystem.GetCompanyCount(service: true, resource);

                snapshot = new MarketSnapshot
                {
                    Resource = resource,
                    TradeBalance = tradeBalance,
                    TradeWorth = tradeWorth,
                    ProcessingWorkers = processingWorkers,
                    ServiceWorkers = serviceWorkers,
                    ProcessingCompanies = processingCompanies,
                    ServiceCompanies = serviceCompanies,
                    Supply = math.max(1f, processingWorkers.x + serviceWorkers.x),
                    Demand = math.max(1f, processingWorkers.y + serviceWorkers.y)
                };

                Diagnostics.DiagnosticsLogger.Log(
                    "Economy",
                    $"Snapshot {resource}: supply={snapshot.Supply:F1}, demand={snapshot.Demand:F1}, processingWorkers=({processingWorkers.x}/{processingWorkers.y}), serviceWorkers=({serviceWorkers.x}/{serviceWorkers.y}), processingCompanies={processingCompanies}, serviceCompanies={serviceCompanies}, tradeBalance={tradeBalance}, tradeWorth={tradeWorth}");

                return true;
            }
            catch (Exception ex)
            {
                m_Log.Warn(ex, $"Failed to build market snapshot for {resource}");
                return false;
            }
        }

        private MarketSnapshot? AggregateSnapshot(Resource resource)
        {
            MarketSnapshot? result = null;
            if (TryGetSnapshot(resource, out var snapshot))
            {
                result = snapshot;
            }

            if (m_MarketMetrics.IsCreated && m_MarketMetrics.TryGetValue((int)resource, out var metrics))
            {
                if (!result.HasValue)
                {
                    result = new MarketSnapshot
                    {
                        Resource = resource
                    };
                }

                MarketSnapshot value = result.Value;
                value.Supply = math.max(1f, value.Supply + metrics.SupplyAmount);
                value.Demand = math.max(1f, value.Demand + metrics.DemandAmount);
                result = value;
            }

            return result;
        }

        private BudgetSystem GetBudgetSystem()
        {
            if (m_BudgetSystem != null)
            {
                return m_BudgetSystem;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return null;
            }

            try
            {
                m_BudgetSystem = world.GetExistingSystemManaged<BudgetSystem>();
            }
            catch (Exception ex)
            {
                m_Log.Warn(ex, "Unable to resolve BudgetSystem. Market pricing will fallback to vanilla values.");
            }

            return m_BudgetSystem;
        }

        private IndustrialDemandSystem GetIndustrialDemandSystem()
        {
            if (m_IndustrialDemandSystem != null)
            {
                return m_IndustrialDemandSystem;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return null;
            }

            try
            {
                m_IndustrialDemandSystem = world.GetExistingSystemManaged<IndustrialDemandSystem>();
            }
            catch (Exception ex)
            {
                m_Log.Warn(ex, "Unable to resolve IndustrialDemandSystem for market metrics.");
            }

            return m_IndustrialDemandSystem;
        }

        private CommercialDemandSystem GetCommercialDemandSystem()
        {
            if (m_CommercialDemandSystem != null)
            {
                return m_CommercialDemandSystem;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return null;
            }

            try
            {
                m_CommercialDemandSystem = world.GetExistingSystemManaged<CommercialDemandSystem>();
            }
            catch (Exception ex)
            {
                m_Log.Warn(ex, "Unable to resolve CommercialDemandSystem for market metrics.");
            }

            return m_CommercialDemandSystem;
        }

        private CountCompanyDataSystem GetCountCompanyDataSystem()
        {
            if (m_CountCompanyDataSystem != null)
            {
                return m_CountCompanyDataSystem;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return null;
            }

            try
            {
                m_CountCompanyDataSystem = world.GetExistingSystemManaged<CountCompanyDataSystem>();
            }
            catch (Exception ex)
            {
                m_Log.Warn(ex, "Unable to resolve CountCompanyDataSystem for market metrics.");
            }

            return m_CountCompanyDataSystem;
        }

        private ResourceSystem GetResourceSystem()
        {
            if (m_ResourceSystem != null)
            {
                return m_ResourceSystem;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return null;
            }

            try
            {
                m_ResourceSystem = world.GetExistingSystemManaged<ResourceSystem>();
            }
            catch (Exception ex)
            {
                m_Log.Warn(ex, "Unable to resolve ResourceSystem for market metrics.");
            }

            return m_ResourceSystem;
        }

        private bool IsZeroWeightResource(Resource resource)
        {
            var resourceSystem = GetResourceSystem();
            var world = World.DefaultGameObjectInjectionWorld;

            if (resourceSystem == null || world == null)
            {
                return false;
            }

            var resourcePrefabs = resourceSystem.GetPrefabs();
            Entity resourceEntity = resourcePrefabs[resource];

            if (resourceEntity == Entity.Null)
            {
                return false;
            }

            var entityManager = world.EntityManager;
            if (!entityManager.HasComponent<ResourceData>(resourceEntity))
            {
                return false;
            }

            var data = entityManager.GetComponentData<ResourceData>(resourceEntity);
            return data.m_Weight <= 0f;
        }

        private uint GetCurrentTick()
        {
            return m_BudgetSystem != null ? m_BudgetSystem.LastUpdate : 0;
        }

        private float GetOrUpdatePriceMultiplier(Resource resource)
        {
            if (!m_PriceStates.TryGetValue((int)resource, out var state))
            {
                state = new MarketPriceState
                {
                    Multiplier = 1f,
                    ExternalFloor = MinimumPriceMultiplier,
                    ExternalCeiling = MaximumPriceMultiplier
                };
            }

            var snapshot = AggregateSnapshot(resource);
            if (!snapshot.HasValue)
            {
                return state.Multiplier;
            }

            float supply;
            float demand;
            if (!TryGetDemandSupplyFromSystems(resource, out supply, out demand))
            {
                supply = math.max(1f, snapshot.Value.Supply);
                demand = math.max(1f, snapshot.Value.Demand);
            }

            float ratio = demand / math.max(1f, supply);
            float multiplier = math.clamp(ratio, MinimumPriceMultiplier, MaximumPriceMultiplier);
            state.Multiplier = multiplier;
            m_PriceStates[(int)resource] = state;

            if (m_MarketMetrics.IsCreated)
            {
                m_MarketMetrics.Remove((int)resource);
            }

            return multiplier;
        }

        private bool TryGetDemandSupplyFromSystems(Resource resource, out float supply, out float demand)
        {
            supply = 0f;
            demand = 0f;

            var industrial = GetIndustrialDemandSystem();
            var commercial = GetCommercialDemandSystem();
            var companyData = GetCountCompanyDataSystem();
            var resourceSystem = GetResourceSystem();
            var world = World.DefaultGameObjectInjectionWorld;

            if (industrial == null || commercial == null || companyData == null || resourceSystem == null || world == null)
            {
                return false;
            }

            JobHandle indDeps;
            var industrialConsumption = industrial.GetConsumption(out indDeps);
            JobHandle prodDeps;
            var productions = companyData.GetProduction(out prodDeps);
            JobHandle comDeps;
            var commercialConsumption = commercial.GetConsumption(out comDeps);
            JobHandle commercialCompanyDeps;
            var commercialCompanyDatas = companyData.GetCommercialCompanyDatas(out commercialCompanyDeps);

            JobHandle.CompleteAll(ref indDeps, ref prodDeps, ref comDeps);
            commercialCompanyDeps.Complete();

            int index = EconomyUtils.GetResourceIndex(resource);
            if (index < 0 ||
                index >= industrialConsumption.Length ||
                index >= productions.Length ||
                index >= commercialConsumption.Length ||
                index >= commercialCompanyDatas.m_ProduceCapacity.Length)
            {
                return false;
            }

            int productionValue = productions[index];

            var resourcePrefabs = resourceSystem.GetPrefabs();
            Entity resourceEntity = resourcePrefabs[resource];
            var entityManager = world.EntityManager;
            if (resourceEntity != Entity.Null && entityManager.HasComponent<ResourceData>(resourceEntity))
            {
                var data = entityManager.GetComponentData<ResourceData>(resourceEntity);
                if (!data.m_IsProduceable)
                {
                    productionValue = commercialCompanyDatas.m_ProduceCapacity[index];
                }
            }

            int demandValue = industrialConsumption[index] + commercialConsumption[index];

            supply = math.max(1f, productionValue);
            demand = math.max(1f, demandValue);

            return true;
        }

        public void RegisterSupply(Resource resource, float amount)
        {
            if (!m_MarketMetrics.IsCreated)
            {
                m_MarketMetrics = new NativeHashMap<int, MarketMetrics>(EconomyUtils.ResourceCount, Allocator.Persistent);
            }

            int key = (int)resource;
            if (m_MarketMetrics.TryGetValue(key, out var metrics))
            {
                metrics = metrics.AccumulateSupply(amount);
            }
            else
            {
                metrics = MarketMetrics.CreateSupply(amount);
            }

            metrics.LastUpdatedTick = GetCurrentTick();
            m_MarketMetrics[key] = metrics;
        }

        public void RegisterDemand(Resource resource, float amount)
        {
            if (!m_MarketMetrics.IsCreated)
            {
                m_MarketMetrics = new NativeHashMap<int, MarketMetrics>(EconomyUtils.ResourceCount, Allocator.Persistent);
            }

            int key = (int)resource;
            if (m_MarketMetrics.TryGetValue(key, out var metrics))
            {
                metrics = metrics.AccumulateDemand(amount);
            }
            else
            {
                metrics = MarketMetrics.CreateDemand(amount);
            }

            metrics.LastUpdatedTick = GetCurrentTick();
            m_MarketMetrics[key] = metrics;
        }

        public bool TryGetSupplyDemand(Resource resource, out float supply, out float demand)
        {
            bool hasDirectMetrics = TryGetDemandSupplyFromSystems(resource, out supply, out demand);

            if (!hasDirectMetrics)
            {
                var snapshot = AggregateSnapshot(resource);
                if (!snapshot.HasValue)
                {
                    return false;
                }

                supply = math.max(1f, snapshot.Value.Supply);
                demand = math.max(1f, snapshot.Value.Demand);
            }
            else
            {
                supply = math.max(1f, supply);
                demand = math.max(1f, demand);
            }

            if ((resource & (Resource)28672UL) != Resource.NoResource)
            {
                float neutral = math.max(1f, (supply + demand) * 0.5f);
                supply = neutral;
                demand = neutral;
                return true;
            }

            float span = math.max(1f, supply + demand);
            float balance = supply - demand;
            float tolerance = span * DemandTolerance;

            if (math.abs(balance) <= tolerance)
            {
                float neutral = math.max(1f, span * 0.5f);
                supply = neutral;
                demand = neutral;
                return true;
            }

            float lowStock = StorageCompanySystem.kStorageLowStockAmount;
            float exportStart = StorageCompanySystem.kStorageExportStartAmount;

            float surplusRatio = math.saturate(lowStock / math.max(1f, exportStart));
            float deficitRatio = math.max(1f, exportStart / math.max(1f, lowStock));

            float normalized = math.saturate(math.abs(balance) / span);
            float severity = math.saturate(normalized * Sensitivity);

            if (balance > 0f)
            {
                float targetRatio = math.lerp(1f, surplusRatio, severity);
                demand = math.max(1f, supply * targetRatio);
            }
            else
            {
                float targetRatio = math.lerp(1f, deficitRatio, severity);
                supply = math.max(1f, demand / math.max(0.001f, targetRatio));
            }

            return true;
        }

        public float GetDemandSupplyRatio(Resource resource)
        {
            if (TryGetSupplyDemand(resource, out var supply, out var demand))
            {
                return demand / math.max(1f, supply);
            }

            return 1f;
        }
    }

    public struct MarketSnapshot
    {
        public Resource Resource { get; set; }
        public int TradeBalance { get; set; }
        public int TradeWorth { get; set; }
        public int2 ProcessingWorkers { get; set; }
        public int2 ServiceWorkers { get; set; }
        public int ProcessingCompanies { get; set; }
        public int ServiceCompanies { get; set; }
        public float Supply { get; set; }
        public float Demand { get; set; }

        public override string ToString()
        {
            return $"{Resource}: Supply={Supply}, Demand={Demand}, Trade={TradeBalance}";
        }
    }
}
