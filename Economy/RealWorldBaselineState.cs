using System.Collections.Generic;
using Game.Economy;
using Game.Prefabs;

namespace MarketBasedEconomy.Economy
{
    public static class RealWorldBaselineState
    {
        private static readonly Dictionary<Resource, float> s_OriginalPrices = new();
        private static readonly Dictionary<Resource, float> s_AppliedPrices = new();
        private static readonly Dictionary<Resource, float> s_OutputPerWorkerPerDay = new();
    private static EconomyParameterSnapshot s_OriginalEconomyParameters;
    private static EconomyParameterSnapshot s_AppliedEconomyParameters;
        private static bool s_HasOriginalEconomyParameters;
        private static bool s_HasAppliedEconomyParameters;

        public static void RecordOriginalPrice(Resource resource, float price)
        {
            if (!s_OriginalPrices.ContainsKey(resource))
            {
                s_OriginalPrices[resource] = price;
            }
        }

        public static void RecordAppliedPrice(Resource resource, float price)
        {
            s_AppliedPrices[resource] = price;
        }

        public static void RecordOutputPerWorker(Resource resource, float outputPerWorkerPerDay)
        {
            if (outputPerWorkerPerDay <= 0f)
            {
                return;
            }

            s_OutputPerWorkerPerDay[resource] = outputPerWorkerPerDay;
        }

        public static bool TryGetOriginalPrice(Resource resource, out float price)
        {
            return s_OriginalPrices.TryGetValue(resource, out price);
        }

        public static bool TryGetAppliedPrice(Resource resource, out float price)
        {
            return s_AppliedPrices.TryGetValue(resource, out price);
        }

        public static void Reset()
        {
            s_OriginalPrices.Clear();
            s_AppliedPrices.Clear();
            s_OutputPerWorkerPerDay.Clear();
            s_OriginalEconomyParameters = default;
            s_AppliedEconomyParameters = default;
            s_HasOriginalEconomyParameters = false;
            s_HasAppliedEconomyParameters = false;
        }

        public static bool TryGetOutputPerWorker(Resource resource, out float value)
        {
            return s_OutputPerWorkerPerDay.TryGetValue(resource, out value);
        }

        public static void RecordOriginalEconomy(EconomyParameterData data)
        {
            if (s_HasOriginalEconomyParameters)
            {
                return;
            }

            s_OriginalEconomyParameters = new EconomyParameterSnapshot(ref data);
            s_HasOriginalEconomyParameters = true;
        }

        public static void RecordAppliedEconomy(EconomyParameterData data)
        {
            s_AppliedEconomyParameters = new EconomyParameterSnapshot(ref data);
            s_HasAppliedEconomyParameters = true;
        }

        public static bool TryGetOriginalEconomy(out EconomyParameterSnapshot snapshot)
        {
            snapshot = s_OriginalEconomyParameters;
            return s_HasOriginalEconomyParameters;
        }

        public static bool TryGetAppliedEconomy(out EconomyParameterSnapshot snapshot)
        {
            snapshot = s_AppliedEconomyParameters;
            return s_HasAppliedEconomyParameters;
        }

        public readonly struct EconomyParameterSnapshot
        {
            public EconomyParameterSnapshot(ref EconomyParameterData data)
            {
                ResourceConsumption = EconomyParameterAccess.TryGetResourceConsumption(ref data, out var resourceConsumption)
                    ? resourceConsumption
                    : (float?)null;
                TouristConsumptionMultiplier = EconomyParameterAccess.TryGetTouristConsumptionMultiplier(ref data, out var touristMultiplier)
                    ? touristMultiplier
                    : (float?)null;
                ResidentialMinimumEarnings = EconomyParameterAccess.TryGetResidentialMinimumEarnings(ref data, out var minimumEarnings)
                    ? minimumEarnings
                    : (int?)null;
                FamilyAllowance = EconomyParameterAccess.TryGetFamilyAllowance(ref data, out var familyAllowance)
                    ? familyAllowance
                    : (int?)null;
                Pension = EconomyParameterAccess.TryGetPension(ref data, out var pension)
                    ? pension
                    : (int?)null;
                UnemploymentBenefit = EconomyParameterAccess.TryGetUnemploymentBenefit(ref data, out var unemploymentBenefit)
                    ? unemploymentBenefit
                    : (int?)null;
                Wage0 = EconomyParameterAccess.TryGetWage(ref data, 0, out var wage0)
                    ? wage0
                    : (int?)null;
                Wage1 = EconomyParameterAccess.TryGetWage(ref data, 1, out var wage1)
                    ? wage1
                    : (int?)null;
                Wage2 = EconomyParameterAccess.TryGetWage(ref data, 2, out var wage2)
                    ? wage2
                    : (int?)null;
                Wage3 = EconomyParameterAccess.TryGetWage(ref data, 3, out var wage3)
                    ? wage3
                    : (int?)null;
                Wage4 = EconomyParameterAccess.TryGetWage(ref data, 4, out var wage4)
                    ? wage4
                    : (int?)null;
            }

            public float? ResourceConsumption { get; }

            public float? TouristConsumptionMultiplier { get; }

            public int? ResidentialMinimumEarnings { get; }

            public int? FamilyAllowance { get; }

            public int? Pension { get; }

            public int? UnemploymentBenefit { get; }

            public int? Wage0 { get; }

            public int? Wage1 { get; }

            public int? Wage2 { get; }

            public int? Wage3 { get; }

            public int? Wage4 { get; }
        }
    }
}