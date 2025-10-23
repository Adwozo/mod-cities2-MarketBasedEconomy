using System;
using Colossal.Logging;
using Game;
using Game.Buildings;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

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

        public float
        MinimumUtilizationShare
        { get; set; } = 0.25f;

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
            var propertyRenters = system.GetComponentLookup<PropertyRenter>(true);
            var prefabRefs = system.GetComponentLookup<PrefabRef>(true);
            var buildingDatas = system.GetComponentLookup<BuildingData>(true);
            var buildingPropertyDatas = system.GetComponentLookup<BuildingPropertyData>(true);
            var spawnableBuildingDatas = system.GetComponentLookup<SpawnableBuildingData>(true);
            var industrialProcessDatas = system.GetComponentLookup<IndustrialProcessData>(true);
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
                    continue;
                }

                int minimumCompanyWorkers = ComputeMinimumCompanyWorkers(
                    entity,
                    propertyRenters,
                    prefabRefs,
                    buildingDatas,
                    buildingPropertyDatas,
                    spawnableBuildingDatas,
                    industrialProcessDatas);

                ApplyUtilization(entity, employeesLookup[entity], ref provider, minimumCompanyWorkers);

                commandBuffer.SetComponent(entity, provider);
                //Diagnostics.DiagnosticsLogger.Log("Workforce", $"Updated WorkProvider for entity {entity.Index}: newMaxWorkers={provider.m_MaxWorkers}");
            }
        }

        private void ApplyUtilization(Entity workplaceEntity, DynamicBuffer<Employee> employees, ref WorkProvider workProvider, int minimumCompanyWorkers)
        {
            if (minimumCompanyWorkers > 0 && workProvider.m_MaxWorkers < minimumCompanyWorkers)
            {
                Diagnostics.DiagnosticsLogger.Log(
                    "Workforce",
                    $"Entity {workplaceEntity.Index}: enforcing minimum company max workers {minimumCompanyWorkers} (was {workProvider.m_MaxWorkers})");
                workProvider.m_MaxWorkers = minimumCompanyWorkers;
            }
        }

        private static int ComputeMinimumCompanyWorkers(
            Entity companyEntity,
            in ComponentLookup<PropertyRenter> propertyRenters,
            in ComponentLookup<PrefabRef> prefabRefs,
            in ComponentLookup<BuildingData> buildingDatas,
            in ComponentLookup<BuildingPropertyData> buildingPropertyDatas,
            in ComponentLookup<SpawnableBuildingData> spawnableBuildingDatas,
            in ComponentLookup<IndustrialProcessData> industrialProcessDatas)
        {
            if (!propertyRenters.HasComponent(companyEntity) || !prefabRefs.HasComponent(companyEntity))
            {
                return 0;
            }

            var property = propertyRenters[companyEntity].m_Property;
            if (property == Entity.Null || !prefabRefs.HasComponent(property))
            {
                return 0;
            }

            Entity buildingPrefab = prefabRefs[property].m_Prefab;
            Entity companyPrefab = prefabRefs[companyEntity].m_Prefab;
            if (buildingPrefab == Entity.Null || companyPrefab == Entity.Null)
            {
                return 0;
            }

            if (!buildingDatas.HasComponent(buildingPrefab) || !buildingPropertyDatas.HasComponent(buildingPrefab) || !industrialProcessDatas.HasComponent(companyPrefab))
            {
                return 0;
            }

            int level = 0;
            if (spawnableBuildingDatas.HasComponent(buildingPrefab))
            {
                level = spawnableBuildingDatas[buildingPrefab].m_Level;
            }

            var buildingData = buildingDatas[buildingPrefab];
            var propertyData = buildingPropertyDatas[buildingPrefab];
            var processData = industrialProcessDatas[companyPrefab];

            int fittingWorkers = CalculateFittingWorkers(buildingData, propertyData, level, processData);
            if (fittingWorkers <= 0)
            {
                return 0;
            }

            return math.max(1, fittingWorkers / 4);
        }

        private static int CalculateFittingWorkers(
            in BuildingData building,
            in BuildingPropertyData properties,
            int level,
            in IndustrialProcessData processData)
        {
            if (building.m_LotSize.x <= 0 || building.m_LotSize.y <= 0)
            {
                return 0;
            }

            float lotArea = building.m_LotSize.x * building.m_LotSize.y;
            float levelMultiplier = 1f + 0.5f * level;
            float capacity = processData.m_MaxWorkersPerCell * lotArea * levelMultiplier * properties.m_SpaceMultiplier;
            if (!math.isfinite(capacity) || capacity <= 0f)
            {
                return 0;
            }

            return Mathf.CeilToInt(capacity);
        }
    }
}

