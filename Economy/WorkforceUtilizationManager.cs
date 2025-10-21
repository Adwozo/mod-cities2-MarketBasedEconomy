using System;
using Colossal.Logging;
using Game;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
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

            var employeesLookup = system.GetBufferLookup<Employee>(true);
            var resourceBuffers = system.GetBufferLookup<Resources>(false);
            var maintenanceLookup = system.GetComponentLookup<WorkforceMaintenanceState>(false);

            using var entities = workProviderQuery.ToEntityArray(Allocator.TempJob);
            using var providers = workProviderQuery.ToComponentDataArray<WorkProvider>(Allocator.TempJob);

            if (entities.Length == 0)
            {
                return;
            }

            var commandBuffer = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<EndFrameBarrier>().CreateCommandBuffer();

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var provider = providers[i];

                if (!employeesLookup.HasBuffer(entity))
                {
                    Diagnostics.DiagnosticsLogger.Log($"Entity {entity.Index} has no employee buffer; skipping utilization check.");
                    continue;
                }

                bool hasState = maintenanceLookup.HasComponent(entity);
                var state = hasState ? maintenanceLookup[entity] : new WorkforceMaintenanceState();

                Diagnostics.DiagnosticsLogger.Log($"Processing entity {entity.Index}: currentMaxWorkers={provider.m_MaxWorkers}, existingMaintenance={state.AccumulatedMaintenance:F2}");

                ApplyUtilizationAndMaintenance(entity, employeesLookup[entity], ref provider, ref state, resourceBuffers);

                if (hasState)
                {
                    commandBuffer.SetComponent(entity, state);
                }
                else
                {
                    commandBuffer.AddComponent(entity, state);
                    Diagnostics.DiagnosticsLogger.Log($"Added maintenance state for entity {entity.Index}.");
                }

                commandBuffer.SetComponent(entity, provider);
                Diagnostics.DiagnosticsLogger.Log($"Updated WorkProvider for entity {entity.Index}: newMaxWorkers={provider.m_MaxWorkers}, accumulatedMaintenance={state.AccumulatedMaintenance:F2}");
            }
        }

        private void ApplyUtilizationAndMaintenance(Entity workplaceEntity, DynamicBuffer<Employee> employees, ref WorkProvider workProvider, ref WorkforceMaintenanceState state, BufferLookup<Resources> resourceBuffers)
        {
            int maxCapacity = math.max(1, workProvider.m_MaxWorkers);
            int staffed = employees.Length;
            float utilization = staffed / (float)maxCapacity;

            Diagnostics.DiagnosticsLogger.Log($"Entity {workplaceEntity.Index} utilization: staffed={staffed}, capacity={maxCapacity}, utilization={utilization:P1}");

            float minShare = math.clamp(MinimumUtilizationShare, 0.05f, 0.95f);
            if (utilization < minShare && workProvider.m_MaxWorkers > 0)
            {
                int target = math.max((int)math.ceil(minShare * maxCapacity), 1);
                Diagnostics.DiagnosticsLogger.Log($"Utilization low for {workplaceEntity.Index}: staffed={staffed} capacity={maxCapacity} reducing maxWorkers to {target}");
                workProvider.m_MaxWorkers = math.min(workProvider.m_MaxWorkers, target);
            }

            float maintenancePerDay = (BaseMaintenancePerDay + MaintenancePerCapacity * maxCapacity) * math.max(0f, MaintenanceCostMultiplier);
            if (utilization < minShare)
            {
                maintenancePerDay *= UnderUtilizationPenaltyMultiplier;
            }

            Diagnostics.DiagnosticsLogger.Log($"Maintenance accrual for {workplaceEntity.Index}: base={BaseMaintenancePerDay:F1}, perCapacity={MaintenancePerCapacity:F1}, multiplier={MaintenanceCostMultiplier:F2}, underUtilPenalty={(utilization < minShare ? UnderUtilizationPenaltyMultiplier : 1f):F2}, totalPerDay={maintenancePerDay:F1}");

            state.AccumulatedMaintenance += maintenancePerDay / EconomyUtils.kCompanyUpdatesPerDay;
            int deduction = (int)math.floor(state.AccumulatedMaintenance);
            if (deduction <= 0)
            {
                Diagnostics.DiagnosticsLogger.Log($"Accumulating maintenance for {workplaceEntity.Index}: buffer={state.AccumulatedMaintenance:F2} (no deduction yet).");
                return;
            }

            state.AccumulatedMaintenance -= deduction;
            Diagnostics.DiagnosticsLogger.Log($"Maintenance deduction for {workplaceEntity.Index}: {deduction} (buffer={state.AccumulatedMaintenance:F2})");

            if (resourceBuffers.HasBuffer(workplaceEntity))
            {
                var buffer = resourceBuffers[workplaceEntity];
                EconomyUtils.AddResources(Resource.Money, -deduction, buffer);
                Diagnostics.DiagnosticsLogger.Log($"Applied maintenance charge to entity {workplaceEntity.Index}: -{deduction} money");
            }
            else
            {
                Diagnostics.DiagnosticsLogger.Log($"Entity {workplaceEntity.Index} missing resource buffer; maintenance deduction not applied.");
            }
        }
    }
}

