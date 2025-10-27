using Game;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MarketBasedEconomy.Economy
{
    public partial class RealWorldCompanyInitializerSystem : GameSystemBase
    {
        private const float kFloatTolerance = 0.0001f;

        private EntityQuery m_CompanyQuery;
        private PrefabSystem m_PrefabSystem;
        private bool m_Pending;
        private RealWorldBaselineConfig m_Config;

        protected override void OnCreate()
        {
            base.OnCreate();
            Enabled = false;
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_CompanyQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PrefabData>()
                },
                Any = new[]
                {
                    ComponentType.ReadWrite<ServiceCompanyData>(),
                    ComponentType.ReadWrite<IndustrialProcessData>(),
                    ComponentType.ReadWrite<ExtractorCompanyData>()
                }
            });
            RequireForUpdate(m_CompanyQuery);
        }

        public void SetConfig(RealWorldBaselineConfig config)
        {
            m_Config = config;
        }

        public void RequestApply()
        {
            if (m_Config == null)
            {
                Mod.log.Warn("RealWorldBaseline: company config missing; skipping apply request.");
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
                Mod.log.Warn("RealWorldBaseline: company config missing during update; aborting.");
                m_Pending = false;
                Enabled = false;
                return;
            }

            NativeArray<Entity> entities = m_CompanyQuery.ToEntityArray(Allocator.TempJob);
            int serviceAdjusted = 0;
            int industrialAdjusted = 0;
            int extractorAdjusted = 0;

            foreach (Entity entity in entities)
            {
                PrefabData prefabData = EntityManager.GetComponentData<PrefabData>(entity);
                CompanyPrefab prefab = m_PrefabSystem.GetPrefab<CompanyPrefab>(prefabData);
                CompanyPrefabOverride prefabOverride = null;
                config.Companies?.TryGetOverride(prefab.name, out prefabOverride);

                if (EntityManager.HasComponent<ServiceCompanyData>(entity))
                {
                    ServiceCompanyData serviceData = EntityManager.GetComponentData<ServiceCompanyData>(entity);
                    bool changed = ApplyServiceBaseline(ref serviceData, config, prefabOverride);
                    if (changed)
                    {
                        serviceAdjusted++;
                        EntityManager.SetComponentData(entity, serviceData);
                    }
                }

                if (EntityManager.HasComponent<IndustrialProcessData>(entity))
                {
                    IndustrialProcessData processData = EntityManager.GetComponentData<IndustrialProcessData>(entity);
                    bool isExtractor = EntityManager.HasComponent<ExtractorCompanyData>(entity);
                    bool changed = ApplyProcessBaseline(ref processData, config, prefabOverride, isExtractor);
                    if (changed)
                    {
                        if (isExtractor)
                        {
                            extractorAdjusted++;
                        }
                        else
                        {
                            industrialAdjusted++;
                        }
                        EntityManager.SetComponentData(entity, processData);
                    }
                }
            }

            entities.Dispose();

            Mod.log.Info($"RealWorldBaseline: updated companies - service {serviceAdjusted}, industrial {industrialAdjusted}, extractor {extractorAdjusted}.");
            m_Pending = false;
            Enabled = false;
        }

        private static bool ApplyServiceBaseline(ref ServiceCompanyData serviceData, RealWorldBaselineConfig config, CompanyPrefabOverride overrideData)
        {
            bool changed = false;

            float workPerUnit = math.max(1f, serviceData.m_WorkPerUnit);
            float multiplier = config.Companies?.ServiceWorkPerUnitMultiplier ?? 1f;
            workPerUnit *= multiplier;

            if (overrideData != null)
            {
                if (overrideData.WorkPerUnitMultiplier.HasValue)
                {
                    workPerUnit *= overrideData.WorkPerUnitMultiplier.Value;
                }

                if (overrideData.WorkPerUnit.HasValue)
                {
                    workPerUnit = overrideData.WorkPerUnit.Value;
                }

                if (overrideData.MaxService.HasValue && overrideData.MaxService.Value > 0 && serviceData.m_MaxService != overrideData.MaxService.Value)
                {
                    serviceData.m_MaxService = overrideData.MaxService.Value;
                    changed = true;
                }
            }

            int newWork = Mathf.Clamp(Mathf.RoundToInt(workPerUnit), 1, 65535);
            if (serviceData.m_WorkPerUnit != newWork)
            {
                serviceData.m_WorkPerUnit = newWork;
                changed = true;
            }

            return changed;
        }

        private static bool ApplyProcessBaseline(ref IndustrialProcessData processData, RealWorldBaselineConfig config, CompanyPrefabOverride overrideData, bool isExtractor)
        {
            bool changed = false;

            float workPerUnit = math.max(1f, processData.m_WorkPerUnit);
            float multiplier = 1f;
            if (config.Companies != null)
            {
                multiplier = isExtractor ? config.Companies.ExtractorWorkPerUnitMultiplier : config.Companies.IndustrialWorkPerUnitMultiplier;
            }
            workPerUnit *= multiplier;

            float outputOverride = 0f;
            float baselineOutput = 0f;
            bool hasBaselineOutput = RealWorldBaselineState.TryGetOutputPerWorker(processData.m_Output.m_Resource, out baselineOutput);

            if (overrideData != null)
            {
                if (overrideData.WorkPerUnitMultiplier.HasValue)
                {
                    workPerUnit *= overrideData.WorkPerUnitMultiplier.Value;
                }

                if (overrideData.WorkPerUnit.HasValue)
                {
                    workPerUnit = overrideData.WorkPerUnit.Value;
                }

                if (overrideData.MaxWorkersPerCell.HasValue && !Approximately(overrideData.MaxWorkersPerCell.Value, processData.m_MaxWorkersPerCell))
                {
                    processData.m_MaxWorkersPerCell = overrideData.MaxWorkersPerCell.Value;
                    changed = true;
                }

                if (overrideData.OutputPerWorkerMultiplier.HasValue)
                {
                    float outputMultiplier = math.max(0f, overrideData.OutputPerWorkerMultiplier.Value);
                    if (outputMultiplier > 0f)
                    {
                        float baseValue = outputOverride > 0f ? outputOverride : (hasBaselineOutput ? baselineOutput : 0f);
                        if (baseValue > 0f)
                        {
                            outputOverride = baseValue * outputMultiplier;
                        }
                    }
                }

                if (overrideData.OutputPerWorkerPerDay.HasValue)
                {
                    outputOverride = math.max(0f, overrideData.OutputPerWorkerPerDay.Value);
                }
            }

            int newWork = Mathf.Clamp(Mathf.RoundToInt(workPerUnit), 1, 65535);
            if (processData.m_WorkPerUnit != newWork)
            {
                processData.m_WorkPerUnit = newWork;
                changed = true;
            }

            if (outputOverride > 0f)
            {
                RealWorldBaselineState.RecordOutputPerWorker(processData.m_Output.m_Resource, outputOverride);
            }

            return changed;
        }

        private static bool Approximately(float lhs, float rhs)
        {
            return Mathf.Abs(lhs - rhs) <= kFloatTolerance;
        }
    }
}
