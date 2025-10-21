using System;
using Colossal.Logging;
using Game.Companies;
using Game.Economy;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MarketBasedEconomy.Economy
{
    /// <summary>
    /// Centralizes logic for enforcing minimum utilization and charging maintenance fees on workplaces.
    /// </summary>
    public sealed class WorkforceUtilizationManager
    {
        private static readonly Lazy<WorkforceUtilizationManager> s_Instance = new(() => new WorkforceUtilizationManager());
        private readonly ILog m_Log = LogManager.GetLogger($"{nameof(MarketBasedEconomy)}.{nameof(WorkforceUtilizationManager)}");

        private WorkforceUtilizationManager()
        {
            m_Log.SetShowsErrorsInUI(false);
        }

        public static WorkforceUtilizationManager Instance => s_Instance.Value;

        /// <summary>
        /// Minimum share of occupied workplaces before the system clamps down maximum capacity.
        /// </summary>
        public float MinimumUtilizationShare { get; set; } = 0.25f;

        /// <summary>
        /// Base maintenance cost collected per in-game day for any company building.
        /// </summary>
        public float BaseMaintenancePerDay { get; set; } = 45f;

        /// <summary>
        /// Additional maintenance per workplace capacity slot.
        /// </summary>
        public float MaintenancePerCapacity { get; set; } = 3.5f;

        /// <summary>
        /// Multiplier applied when buildings sit below the minimum utilization.
        /// </summary>
        public float UnderUtilizationPenaltyMultiplier { get; set; } = 2.0f;

        /// <summary>
        /// Maintenance buffer threshold before a feee is deducted in one chunk.
        /// </summary>
        public float MaintenanceFeeThreshold { get; set; } = 200f;

        public void EnforceUtilizationAndMaintenance(Entity workplaceEntity, DynamicBuffer<Employee> employees, Workplaces maxWorkplaces, ref WorkProvider workProvider, ComponentLookup<Resources> resourcesLookup, BufferLookup<Resources> resourceBuffers)
        {
            int maxCapacity = math.max(1, maxWorkplaces.TotalCount);
            int staffed = employees.Length;
            float utilization = staffed / (float)maxCapacity;

            float minShare = math.clamp(MinimumUtilizationShare, 0.05f, 0.95f);
            if (utilization < minShare && workProvider.m_MaxWorkers > 0)
            {
                int target = math.max((int)math.ceil(minShare * maxCapacity), 1);
                workProvider.m_MaxWorkers = math.min(workProvider.m_MaxWorkers, target);
            }

            float maintenancePerDay = BaseMaintenancePerDay + MaintenancePerCapacity * maxCapacity;
            if (utilization < minShare)
            {
                maintenancePerDay *= UnderUtilizationPenaltyMultiplier;
            }

            workProvider.m_MaintenanceBuffer += maintenancePerDay / EconomyUtils.kCompanyUpdatesPerDay;
            if (workProvider.m_MaintenanceBuffer >= MaintenanceFeeThreshold)
            {
                int deduction = (int)math.floor(workProvider.m_MaintenanceBuffer);
                workProvider.m_MaintenanceBuffer -= deduction;
                workProvider.m_MaintenanceDebt += deduction;

                if (resourceBuffers.HasBuffer(workplaceEntity))
                {
                    var buffer = resourceBuffers[workplaceEntity];
                    EconomyUtils.AddResources(Resource.Money, -deduction, buffer);
                }
                else if (resourcesLookup.HasComponent(workplaceEntity))
                {
                    var buffer = resourcesLookup.GetRefRO(workplaceEntity).Value;
                    EconomyUtils.AddResources(Resource.Money, -deduction, ref buffer);
                }
            }
        }
    }
}

