using System;
using Colossal.Logging;
using Game.Prefabs;
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
        public float EducationMismatchPremium { get; set; } = 0.2f;

        private bool m_BaselineInitialized;
        private readonly int[] m_BaselineWages = new int[5];
        private readonly int[] m_LastAppliedWages = new int[5];
        private float m_LastMultiplier = 1f;

        public void Reset()
        {
            m_BaselineInitialized = false;
            m_LastMultiplier = 1f;
            Array.Clear(m_BaselineWages, 0, m_BaselineWages.Length);
            Array.Clear(m_LastAppliedWages, 0, m_LastAppliedWages.Length);
        }

        public bool EnsureBaseline(EconomyParameterData data)
        {
            if (m_BaselineInitialized)
            {
                return false;
            }

            CaptureBaseline(data);
            return true;
        }

        public void RestoreBaseline(ref EconomyParameterData data)
        {
            if (!m_BaselineInitialized)
            {
                return;
            }

            data.m_Wage0 = m_BaselineWages[0];
            data.m_Wage1 = m_BaselineWages[1];
            data.m_Wage2 = m_BaselineWages[2];
            data.m_Wage3 = m_BaselineWages[3];
            data.m_Wage4 = m_BaselineWages[4];
            Array.Copy(m_BaselineWages, m_LastAppliedWages, m_BaselineWages.Length);
            m_LastMultiplier = 1f;
        }

        public WageAdjustmentInfo Evaluate()
        {
            var householdSystem = GetHouseholdDataSystem();
            if (householdSystem == null)
            {
                Diagnostics.DiagnosticsLogger.Log("Labor", "Household system unavailable; using baseline wages.");
                return WageAdjustmentInfo.Empty;
            }

            try
            {
                Diagnostics.DiagnosticsLogger.Log("Labor", "Begin wage adjust: fetching household count data");
                var data = householdSystem.GetHouseholdCountData();
                int workforce = math.max(1, data.m_WorkableCitizenCount);
                int employed = math.min(workforce, data.m_CityWorkerCount);
                float unemploymentRate = 1f - employed / (float)workforce;

                int skilledWorkers = data.m_WellEducatedCount + data.m_HighlyEducatedCount;
                int lowSkilledWorkers = data.m_PoorlyEducatedCount;
                float totalWorkforce = math.max(1, data.m_WorkableCitizenCount);
                float skilledShare = skilledWorkers / totalWorkforce;
                float lowSkillShare = lowSkilledWorkers / totalWorkforce;
                float skillShortage = math.saturate(0.3f - skilledShare);
                float educationMismatch = math.saturate(lowSkillShare - skilledShare);

                float penalty = unemploymentRate * math.max(0f, UnemploymentWagePenalty);
                float premium = skillShortage * math.max(0f, SkillShortagePremium);
                float mismatchPremium = educationMismatch * math.max(0f, EducationMismatchPremium);

                float multiplier = 1f - penalty + premium + mismatchPremium;
                multiplier = math.clamp(multiplier, 0.5f, 1.75f);

                var info = new WageAdjustmentInfo
                {
                    HasData = true,
                    Multiplier = multiplier,
                    Penalty = penalty,
                    Premium = premium,
                    MismatchPremium = mismatchPremium,
                    Workforce = workforce,
                    Employed = employed,
                    SkilledShare = skilledShare,
                    LowSkillShare = lowSkillShare
                };

                Diagnostics.DiagnosticsLogger.Log(
                    "Labor",
                    $"Wage adjust data: workforce={workforce}, employed={employed}, unemployment={unemploymentRate:P1}, skilledShare={skilledShare:P1}, lowSkillShare={lowSkillShare:P1}, penalty={penalty:F2}, premium={premium:F2}, mismatchPremium={mismatchPremium:F2}, multiplier={multiplier:F2}");

                return info;
            }
            catch (Exception ex)
            {
                m_Log.Warn(ex, "Failed to evaluate wage adjustment.");
                Diagnostics.DiagnosticsLogger.Log("Labor", $"Wage adjust exception: {ex.Message}");
                return WageAdjustmentInfo.Empty;
            }
        }

        public void ApplyAdjustedWages(ref EconomyParameterData data, in WageAdjustmentInfo info)
        {
            if (!m_BaselineInitialized)
            {
                CaptureBaseline(data);
            }

            if (!info.HasData)
            {
                Diagnostics.DiagnosticsLogger.Log("Labor", "No wage adjustment data; reverting to baseline wages.");
                RestoreBaseline(ref data);
                return;
            }

            data.m_Wage0 = AdjustLevel(0, info.Multiplier);
            data.m_Wage1 = AdjustLevel(1, info.Multiplier);
            data.m_Wage2 = AdjustLevel(2, info.Multiplier);
            data.m_Wage3 = AdjustLevel(3, info.Multiplier);
            data.m_Wage4 = AdjustLevel(4, info.Multiplier);

            m_LastMultiplier = info.Multiplier;

            Diagnostics.DiagnosticsLogger.Log(
                "Labor",
                $"Applied wage multiplier {info.Multiplier:F2} -> wages=({data.m_Wage0},{data.m_Wage1},{data.m_Wage2},{data.m_Wage3},{data.m_Wage4}) baseline=({m_BaselineWages[0]},{m_BaselineWages[1]},{m_BaselineWages[2]},{m_BaselineWages[3]},{m_BaselineWages[4]})");
        }

        public int ApplyWageMultiplier(int currentWage)
        {
            var info = Evaluate();
            if (!info.HasData)
            {
                return currentWage;
            }
            return (int)math.max(1f, currentWage * info.Multiplier);
        }

        private void CaptureBaseline(in EconomyParameterData data)
        {
            m_BaselineWages[0] = data.m_Wage0;
            m_BaselineWages[1] = data.m_Wage1;
            m_BaselineWages[2] = data.m_Wage2;
            m_BaselineWages[3] = data.m_Wage3;
            m_BaselineWages[4] = data.m_Wage4;
            Array.Copy(m_BaselineWages, m_LastAppliedWages, m_BaselineWages.Length);
            m_BaselineInitialized = true;
            m_LastMultiplier = 1f;
            Diagnostics.DiagnosticsLogger.Log(
                "Labor",
                $"Captured baseline wages: ({m_BaselineWages[0]},{m_BaselineWages[1]},{m_BaselineWages[2]},{m_BaselineWages[3]},{m_BaselineWages[4]})");
        }

        private int AdjustLevel(int level, float multiplier)
        {
            int baseline = m_BaselineWages[level];
            int adjusted = math.max(1, (int)math.round(baseline * multiplier));
            m_LastAppliedWages[level] = adjusted;
            return adjusted;
        }

        public struct WageAdjustmentInfo
        {
            public static readonly WageAdjustmentInfo Empty = default;

            public bool HasData;
            public float Multiplier;
            public float Penalty;
            public float Premium;
            public float MismatchPremium;
            public int Workforce;
            public int Employed;
            public float SkilledShare;
            public float LowSkillShare;
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

