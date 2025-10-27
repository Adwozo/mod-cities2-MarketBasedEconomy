using Game;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MarketBasedEconomy.Economy
{
    public partial class RealWorldEconomyParameterSystem : GameSystemBase
    {
        private const float kFloatTolerance = 0.0001f;

        private EntityQuery m_EconomyQuery;
        private RealWorldBaselineConfig m_Config;
        private bool m_Pending;

        protected override void OnCreate()
        {
            base.OnCreate();
            Enabled = false;
            m_EconomyQuery = GetEntityQuery(ComponentType.ReadWrite<EconomyParameterData>());
            RequireForUpdate(m_EconomyQuery);
        }

        public void SetConfig(RealWorldBaselineConfig config)
        {
            m_Config = config;
        }

        public void RequestApply()
        {
            if (m_Config == null)
            {
                Mod.log.Warn("RealWorldBaseline: economy parameter config missing; skipping apply request.");
                return;
            }

            m_Pending = true;
            Enabled = true;
        }

        public void Disable()
        {
            m_Pending = false;
            Enabled = false;
        }

        protected override void OnWorldReady()
        {
            if (RealWorldBaselineFeature.Enabled && m_Config != null)
            {
                RequestApply();
            }
        }

        protected override void OnUpdate()
        {
            if (!m_Pending)
            {
                Enabled = false;
                return;
            }

            var config = m_Config;
            if (config == null)
            {
                Mod.log.Warn("RealWorldBaseline: economy parameter config missing during update; aborting.");
                m_Pending = false;
                Enabled = false;
                return;
            }

            using var entities = m_EconomyQuery.ToEntityArray(Allocator.TempJob);
            int adjustments = 0;

            foreach (var entity in entities)
            {
                var data = EntityManager.GetComponentData<EconomyParameterData>(entity);

                RealWorldBaselineState.RecordOriginalEconomy(data);

                bool changed = ApplyEconomyBaselines(ref data, config);

                if (changed)
                {
                    EntityManager.SetComponentData(entity, data);
                    adjustments++;
                }

                RealWorldBaselineState.RecordAppliedEconomy(data);
            }

            Mod.log.Info($"RealWorldBaseline: updated economy parameters for {adjustments} entries.");

            m_Pending = false;
            Enabled = false;
        }

        private static bool ApplyEconomyBaselines(ref EconomyParameterData data, RealWorldBaselineConfig config)
        {
            bool changed = false;

            var household = config.Household;
            if (household != null)
            {
                if (household.ResourceConsumption.HasValue)
                {
                    float value = math.max(0f, household.ResourceConsumption.Value);
                    bool shouldApply = true;
                    if (EconomyParameterAccess.TryGetResourceConsumption(ref data, out var current))
                    {
                        shouldApply = !Approximately(value, current);
                    }

                    if (shouldApply && EconomyParameterAccess.TrySetResourceConsumption(ref data, value))
                    {
                        changed = true;
                    }
                }

                if (household.TouristConsumptionMultiplier.HasValue)
                {
                    float value = math.max(0f, household.TouristConsumptionMultiplier.Value);
                    bool shouldApply = true;
                    if (EconomyParameterAccess.TryGetTouristConsumptionMultiplier(ref data, out var current))
                    {
                        shouldApply = !Approximately(value, current);
                    }

                    if (shouldApply && EconomyParameterAccess.TrySetTouristConsumptionMultiplier(ref data, value))
                    {
                        changed = true;
                    }
                }

                if (household.ResidentialMinimumEarnings.HasValue)
                {
                    int value = math.max(0, household.ResidentialMinimumEarnings.Value);
                    bool shouldApply = true;
                    if (EconomyParameterAccess.TryGetResidentialMinimumEarnings(ref data, out var current))
                    {
                        shouldApply = current != value;
                    }

                    if (shouldApply && EconomyParameterAccess.TrySetResidentialMinimumEarnings(ref data, value))
                    {
                        changed = true;
                    }
                }

                if (household.FamilyAllowance.HasValue)
                {
                    int value = math.max(0, household.FamilyAllowance.Value);
                    bool shouldApply = true;
                    if (EconomyParameterAccess.TryGetFamilyAllowance(ref data, out var current))
                    {
                        shouldApply = current != value;
                    }

                    if (shouldApply && EconomyParameterAccess.TrySetFamilyAllowance(ref data, value))
                    {
                        changed = true;
                    }
                }

                if (household.Pension.HasValue)
                {
                    int value = math.max(0, household.Pension.Value);
                    bool shouldApply = true;
                    if (EconomyParameterAccess.TryGetPension(ref data, out var current))
                    {
                        shouldApply = current != value;
                    }

                    if (shouldApply && EconomyParameterAccess.TrySetPension(ref data, value))
                    {
                        changed = true;
                    }
                }

                if (household.UnemploymentBenefit.HasValue)
                {
                    int value = math.max(0, household.UnemploymentBenefit.Value);
                    bool shouldApply = true;
                    if (EconomyParameterAccess.TryGetUnemploymentBenefit(ref data, out var current))
                    {
                        shouldApply = current != value;
                    }

                    if (shouldApply && EconomyParameterAccess.TrySetUnemploymentBenefit(ref data, value))
                    {
                        changed = true;
                    }
                }
            }

            var wages = config.Wages;
            if (wages != null)
            {
                int?[] overrides =
                {
                    wages.Level0,
                    wages.Level1,
                    wages.Level2,
                    wages.Level3,
                    wages.Level4
                };

                for (int level = 0; level < overrides.Length; level++)
                {
                    int? overrideValue = overrides[level];
                    if (!overrideValue.HasValue)
                    {
                        continue;
                    }

                    int value = math.max(1, overrideValue.Value);
                    bool shouldApply = true;
                    if (EconomyParameterAccess.TryGetWage(ref data, level, out var current))
                    {
                        shouldApply = current != value;
                    }

                    if (shouldApply && EconomyParameterAccess.TrySetWage(ref data, level, value))
                    {
                        changed = true;
                    }
                }
            }

            return changed;
        }

        private static bool Approximately(float lhs, float rhs)
        {
            return math.abs(lhs - rhs) <= kFloatTolerance;
        }
    }
}
