using System;
using Colossal.Logging;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace MarketBasedEconomy.Economy
{
    public sealed class LaborMarketManager
    {
        private static readonly Lazy<LaborMarketManager> s_Instance = new(() => new LaborMarketManager());
        private readonly ILog m_Log = LogManager.GetLogger($"{nameof(MarketBasedEconomy)}.{nameof(LaborMarketManager)}");
        private CountHouseholdDataSystem m_HouseholdDataSystem;

        private LaborMarketManager()
        {
            m_Log.SetShowsErrorsInUI(false);
        }

        public static LaborMarketManager Instance => s_Instance.Value;

        public float UnemploymentWagePenalty { get; set; } = 0.6f;
        public float SkillShortagePremium { get; set; } = 0.8f;

        public int ApplyWageMultiplier(int currentWage)
        {
            if (currentWage <= 0)
            {
                return currentWage;
            }

            var householdSystem = GetHouseholdDataSystem();
            if (householdSystem == null)
            {
                return currentWage;
            }

            try
            {
                var data = householdSystem.GetHouseholdCountData();
                int workforce = math.max(1, data.m_WorkableCitizenCount);
                int employed = math.min(workforce, data.m_CityWorkerCount);
                float unemploymentRate = 1f - employed / (float)workforce;

                int skilledWorkers = data.m_WellEducatedCount + data.m_HighlyEducatedCount;
                float skilledShare = skilledWorkers / (float)math.max(1, data.m_WorkableCitizenCount);
                float skillShortage = math.saturate(0.3f - skilledShare);

                float wageMultiplier = 1f;
                wageMultiplier -= unemploymentRate * math.max(0f, UnemploymentWagePenalty);
                wageMultiplier += skillShortage * math.max(0f, SkillShortagePremium);
                wageMultiplier = math.clamp(wageMultiplier, 0.5f, 1.75f);

                return (int)math.max(1f, currentWage * wageMultiplier);
            }
            catch (Exception ex)
            {
                m_Log.Warn(ex, "Failed to adjust wage with labor market data.");
                return currentWage;
            }
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
                m_Log.Warn(ex, "Unable to resolve CountHouseholdDataSystem for labor market adjustments.");
            }

            return m_HouseholdDataSystem;
        }
    }
}

