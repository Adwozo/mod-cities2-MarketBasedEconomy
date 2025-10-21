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
