using System;
using Colossal.Logging;
using Game.Economy;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

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
        private CountHouseholdDataSystem m_HouseholdDataSystem;

        private MarketEconomyManager()
        {
            m_Log.SetShowsErrorsInUI(false);
        }

        public static MarketEconomyManager Instance => s_Instance.Value;

        /// <summary>
        /// Prevent runaway prices by clamping multipliers.
        /// </summary>
        public float MinimumPriceMultiplier { get; set; } = 0.5f;

        /// <summary>
        /// Prevent runaway prices by clamping multipliers.
        /// </summary>
        public float MaximumPriceMultiplier { get; set; } = 2.5f;

        /// <summary>
        /// Controls how aggressively the system reacts to demand imbalance. Range [0,1].
        /// </summary>
        public float Sensitivity { get; set; } = 0.65f;

        /// <summary>
        /// How strongly external trade pricing influences local market prices. Range [0,1].
        /// </summary>
        public float ExternalPriceInfluence { get; set; } = 0.35f;

        /// <summary>
        /// Baseline share of well/highly educated workers expected in the city workforce.
        /// </summary>
        public float EducationBaseline { get; set; } = 0.25f;

        /// <summary>
        /// Bonus multiplier applied per point above the education baseline.
        /// </summary>
        public float EducationPremiumStrength { get; set; } = 0.6f;

        /// <summary>
        /// Penalty multiplier applied per point below the education baseline.
        /// </summary>
        public float EducationPenaltyStrength { get; set; } = 0.35f;

        /// <summary>
        /// Clears cached references so the next request resolves fresh systems.
        /// </summary>
        public void ResetCaches()
        {
            m_BudgetSystem = null;
        }

        /// <summary>
        /// Returns a new price based on supply-demand information. Falls back to vanilla price when insufficient data is available.
        /// </summary>
        public float AdjustMarketPrice(Resource resource, float vanillaPrice)
        {
            if (vanillaPrice <= 0f || resource == Resource.NoResource)
            {
                return vanillaPrice;
            }

            if (!TryGetSnapshot(resource, out var snapshot))
            {
                return vanillaPrice;
            }

            float supply = math.max(1f, snapshot.Supply);
            float demand = math.max(1f, snapshot.Demand);
            float ratio = demand / supply;

            float sensitivity = math.clamp(Sensitivity, 0f, 1f);
            float multiplier = math.clamp(1f + (ratio - 1f) * sensitivity, MinimumPriceMultiplier, MaximumPriceMultiplier);

            float price = vanillaPrice * multiplier;

            float externalBlend = math.clamp(ExternalPriceInfluence, 0f, 1f);
            if (externalBlend > 0f)
            {
                float externalPrice = ComputeExternalReferencePrice(snapshot, price);
                price = math.lerp(price, externalPrice, externalBlend);
            }

            if (TryGetEducationMultiplier(out float educationMultiplier))
            {
                price *= educationMultiplier;
            }

            float minPrice = vanillaPrice * MinimumPriceMultiplier;
            float maxPrice = vanillaPrice * MaximumPriceMultiplier;
            return math.clamp(price, minPrice, maxPrice);
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

                return true;
            }
            catch (Exception ex)
            {
                m_Log.Warn(ex, $"Failed to build market snapshot for {resource}");
                return false;
            }
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

        private CountHouseholdDataSystem GetHouseholdDataSystem()
        {
            if (m_HouseholdDataSystem != null)
            {
                return m_HouseholdDataSystem;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return null;
            }

            try
            {
                m_HouseholdDataSystem = world.GetExistingSystemManaged<CountHouseholdDataSystem>();
            }
            catch (Exception ex)
            {
                m_Log.Warn(ex, "Unable to resolve CountHouseholdDataSystem for education metrics.");
            }

            return m_HouseholdDataSystem;
        }

        private float ComputeExternalReferencePrice(in MarketSnapshot snapshot, float fallbackPrice)
        {
            int tradeAmount = math.abs(snapshot.TradeBalance);
            if (tradeAmount <= 0)
            {
                return fallbackPrice;
            }

            float external = math.abs(snapshot.TradeWorth) / tradeAmount;
            if (!math.isfinite(external) || external <= 0f)
            {
                return fallbackPrice;
            }

            return external;
        }

        private bool TryGetEducationMultiplier(out float multiplier)
        {
            multiplier = 1f;
            var householdSystem = GetHouseholdDataSystem();
            if (householdSystem == null)
            {
                return false;
            }

            try
            {
                var householdData = householdSystem.GetHouseholdCountData();
                int workable = math.max(1, householdData.m_WorkableCitizenCount);
                int wellEducated = math.max(0, householdData.m_WellEducatedCount);
                int highlyEducated = math.max(0, householdData.m_HighlyEducatedCount);

                float educatedShare = (wellEducated + highlyEducated) / (float)workable;
                float baseline = math.saturate(EducationBaseline);
                float delta = educatedShare - baseline;

                if (delta >= 0f)
                {
                    multiplier = math.clamp(1f + delta * math.max(0f, EducationPremiumStrength), 0.5f, 1.8f);
                }
                else
                {
                    multiplier = math.clamp(1f + delta * math.max(0f, EducationPenaltyStrength), 0.5f, 1.8f);
                }

                return true;
            }
            catch (Exception ex)
            {
                m_Log.Warn(ex, "Failed to evaluate education-based multiplier.");
                return false;
            }
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
