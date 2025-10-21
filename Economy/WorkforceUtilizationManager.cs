using System;
using Colossal.Logging;
using Game.Companies;
using Game.Economy;
using Game.Simulation;
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

        public float MinimumUtilizationShare { get; set; } = 0.25f;
        public float BaseMaintenancePerDay { get; set; } = 45f;
        public float MaintenancePerCapacity { get; set; } = 3.5f;
        public float UnderUtilizationPenaltyMultiplier { get; set; } = 2.0f;
        public float MaintenanceFeeThreshold { get; set; } = 200f;
        public float MaintenanceCostMultiplier { get; set; } = 1f;

        public void ApplyPostUpdate(WorkProviderSystem system)
        {
            var entityManager = system.EntityManager;
            if (!system.Enabled)
            {
                return;
            }

            var workProviderQuery = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<WorkProvider>(),
                    ComponentType.ReadOnly<PrefabRef>()
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<CompanyData>()
                },
                None = Array.Empty<ComponentType>()
            });

            var employeesLookup = entityManager.GetBufferLookup<Employee>(true);
            var resourcesLookup = entityManager.GetComponentLookup<Resources>(false);
            var resourceBuffers = entityManager.GetBufferLookup<Resources>(false);

            var commandBuffer = system.World.GetExistingSystemManaged<EndFrameBarrier>().CreateCommandBuffer();

            using var entities = workProviderQuery.ToEntityArray(Allocator.TempJob);
            using var providers = workProviderQuery.ToComponentDataArray<WorkProvider>(Allocator.TempJob);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var provider = providers[i];

                if (!employeesLookup.HasBuffer(entity))
                {
                    continue;
                }

                var employees = employeesLookup[entity];
                var maxWorkplaces = EconomyUtils.CalculateNumberOfWorkplaces(provider.m_MaxWorkers, WorkplaceComplexity.Simple, 1);
                ApplyUtilizationAndMaintenance(entity, employees, maxWorkplaces, ref provider, resourcesLookup, resourceBuffers);
                commandBuffer.SetComponent(entity, provider);
            }
        }

        private void ApplyUtilizationAndMaintenance(Entity workplaceEntity, DynamicBuffer<Employee> employees, Workplaces maxWorkplaces, ref WorkProvider workProvider, ComponentLookup<Resources> resourcesLookup, BufferLookup<Resources> resourceBuffers)
        {
            int maxCapacity = math.max(1, workProvider.m_MaxWorkers);
            int staffed = employees.Length;
            float utilization = staffed / (float)maxCapacity;

            float minShare = math.clamp(MinimumUtilizationShare, 0.05f, 0.95f);
            if (utilization < minShare && workProvider.m_MaxWorkers > 0)
            {
                int target = math.max((int)math.ceil(minShare * maxCapacity), 1);
                workProvider.m_MaxWorkers = math.min(workProvider.m_MaxWorkers, target);
            }

            float maintenancePerDay = (BaseMaintenancePerDay + MaintenancePerCapacity * maxCapacity) * math.max(0f, MaintenanceCostMultiplier);
            maintenancePerDay *= math.max(0f, MaintenanceCostMultiplier);
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
                    var buffer = resourcesLookup.GetRefRO(workplaceEntity);
                    EconomyUtils.AddResources(Resource.Money, -deduction, buffer.Value);
                }
            }
        }
    }
}

