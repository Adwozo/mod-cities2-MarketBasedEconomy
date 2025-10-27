using System;
using System.Reflection;
using Game;
using Game.Common;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MarketBasedEconomy.Economy
{
    public partial class RealWorldResourceInitializerSystem : GameSystemBase
    {
        private const float kFloatTolerance = 0.0001f;

        private EntityQuery m_ResourceQuery;
        private PrefabSystem m_PrefabSystem;
        private ResourceSystem m_ResourceSystem;
        private bool m_Pending;
        private RealWorldBaselineConfig m_Config;
        private static FieldInfo s_BaseConsumptionSumField;

        protected override void OnCreate()
        {
            base.OnCreate();
            Enabled = false;
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_ResourceQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadWrite<ResourceData>());
            RequireForUpdate(m_ResourceQuery);
            s_BaseConsumptionSumField ??= typeof(ResourceSystem).GetField("m_BaseConsumptionSum", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public void SetConfig(RealWorldBaselineConfig config)
        {
            m_Config = config;
        }

        public void RequestApply()
        {
            if (m_Config == null)
            {
                Mod.log.Warn("RealWorldBaseline: resource config missing; skipping apply request.");
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
                Mod.log.Warn("RealWorldBaseline: resource config missing during update; aborting.");
                m_Pending = false;
                Enabled = false;
                return;
            }

            NativeArray<Entity> entities = m_ResourceQuery.ToEntityArray(Allocator.TempJob);
            int adjustments = 0;
            float baseConsumptionAccumulator = 0f;

            foreach (Entity entity in entities)
            {
                PrefabData prefabData = EntityManager.GetComponentData<PrefabData>(entity);
                ResourceData resourceData = EntityManager.GetComponentData<ResourceData>(entity);
                ResourcePrefab prefab = m_PrefabSystem.GetPrefab<ResourcePrefab>(prefabData);
                string resourceName = prefab.m_Resource.ToString();

                Resource resource = (Resource)prefab.m_Resource;
                float2 originalPriceVector = prefab.m_InitialPrice;
                float originalTotalPrice = math.max(0f, originalPriceVector.x + originalPriceVector.y);
                RealWorldBaselineState.RecordOriginalPrice(resource, originalTotalPrice);

                bool modified = false;
                if (config.Resources != null && config.Resources.TryGetValue(resourceName, out ResourceBaseline baseline))
                {
                    modified = ApplyPrefabBaseline(prefab, baseline, config);
                    float defaultOutputPerWorker = math.max(0f, config.Companies?.DefaultOutputPerWorkerPerDay ?? 6f);
                    float outputPerWorker = baseline.OutputPerWorkerPerDay ?? defaultOutputPerWorker;
                    if (outputPerWorker <= 0f)
                    {
                        outputPerWorker = defaultOutputPerWorker;
                    }

                    RealWorldBaselineState.RecordOutputPerWorker(resource, outputPerWorker);
                }
                else
                {
                    float fallbackOutput = math.max(0f, config.Companies?.DefaultOutputPerWorkerPerDay ?? 6f);
                    if (fallbackOutput > 0f)
                    {
                        RealWorldBaselineState.RecordOutputPerWorker(resource, fallbackOutput);
                    }
                }

                CopyPrefabToResourceData(prefab, ref resourceData);
                baseConsumptionAccumulator += math.max(0f, resourceData.m_BaseConsumption);
                EntityManager.SetComponentData(entity, resourceData);

                float appliedTotalPrice = math.max(0f, prefab.m_InitialPrice.x + prefab.m_InitialPrice.y);
                RealWorldBaselineState.RecordAppliedPrice(resource, appliedTotalPrice);

                if (modified)
                {
                    adjustments++;
                }
            }

            entities.Dispose();

            UpdateBaseConsumptionSum(baseConsumptionAccumulator);

            Mod.log.Info($"RealWorldBaseline: updated {adjustments} resource prefabs.");
            m_Pending = false;
            Enabled = false;
        }

        private void UpdateBaseConsumptionSum(float combinedValue)
        {
            if (s_BaseConsumptionSumField == null)
            {
                Mod.log.Warn("RealWorldBaseline: unable to locate ResourceSystem.m_BaseConsumptionSum via reflection; base consumption sum not updated.");
                return;
            }

            try
            {
                int newValue = Mathf.RoundToInt(combinedValue);
                s_BaseConsumptionSumField.SetValue(m_ResourceSystem, newValue);
                Mod.log.Info($"RealWorldBaseline: base consumption sum set to {newValue}.");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"RealWorldBaseline: failed to update base consumption sum: {ex}");
            }
        }

        private static bool ApplyPrefabBaseline(ResourcePrefab prefab, ResourceBaseline baseline, RealWorldBaselineConfig config)
        {
            bool changed = false;

            float2 priceVector = prefab.m_InitialPrice;
            float originalTotal = math.max(0f, priceVector.x + priceVector.y);
            float price = originalTotal;
            if (baseline.Price.HasValue)
            {
                price = baseline.Price.Value;
            }
            else if (baseline.ReferencePriceUsd.HasValue)
            {
                float multiplier = baseline.PriceMultiplier ?? 1f;
                price = baseline.ReferencePriceUsd.Value * config.PriceScale * multiplier;
            }
            else if (baseline.PriceMultiplier.HasValue)
            {
                price *= baseline.PriceMultiplier.Value;
            }

            float clampedPrice = math.max(config.MinimumPrice, price);
            if (originalTotal > kFloatTolerance)
            {
                float scale = clampedPrice / originalTotal;
                float2 scaled = priceVector * scale;
                if (!Approximately(scaled.x, prefab.m_InitialPrice.x) || !Approximately(scaled.y, prefab.m_InitialPrice.y))
                {
                    prefab.m_InitialPrice = scaled;
                    changed = true;
                }
            }
            else
            {
                float half = clampedPrice * 0.5f;
                float2 fallback = new float2(half, clampedPrice - half);
                if (!Approximately(fallback.x, prefab.m_InitialPrice.x) || !Approximately(fallback.y, prefab.m_InitialPrice.y))
                {
                    prefab.m_InitialPrice = fallback;
                    changed = true;
                }
            }

            float baseConsumption = prefab.m_BaseConsumption;
            if (baseline.BaseConsumption.HasValue)
            {
                baseConsumption = baseline.BaseConsumption.Value;
            }
            else if (baseline.BaseConsumptionPerCapitaKgPerDay.HasValue)
            {
                baseConsumption = baseline.BaseConsumptionPerCapitaKgPerDay.Value * config.BaseConsumptionScale;
            }

            if (baseline.BaseConsumptionMultiplier.HasValue)
            {
                baseConsumption *= baseline.BaseConsumptionMultiplier.Value;
            }

            float clampedBase = math.max(0f, baseConsumption);
            if (!Approximately(clampedBase, prefab.m_BaseConsumption))
            {
                prefab.m_BaseConsumption = clampedBase;
                changed = true;
            }

            if (baseline.Weight.HasValue && !Approximately(baseline.Weight.Value, prefab.m_Weight))
            {
                prefab.m_Weight = baseline.Weight.Value;
                changed = true;
            }

            if (baseline.ChildWeight.HasValue)
            {
                int value = Mathf.Max(0, Mathf.RoundToInt(baseline.ChildWeight.Value));
                if (prefab.m_ChildWeight != value)
                {
                    prefab.m_ChildWeight = value;
                    changed = true;
                }
            }

            if (baseline.TeenWeight.HasValue)
            {
                int value = Mathf.Max(0, Mathf.RoundToInt(baseline.TeenWeight.Value));
                if (prefab.m_TeenWeight != value)
                {
                    prefab.m_TeenWeight = value;
                    changed = true;
                }
            }

            if (baseline.AdultWeight.HasValue)
            {
                int value = Mathf.Max(0, Mathf.RoundToInt(baseline.AdultWeight.Value));
                if (prefab.m_AdultWeight != value)
                {
                    prefab.m_AdultWeight = value;
                    changed = true;
                }
            }

            if (baseline.ElderlyWeight.HasValue)
            {
                int value = Mathf.Max(0, Mathf.RoundToInt(baseline.ElderlyWeight.Value));
                if (prefab.m_ElderlyWeight != value)
                {
                    prefab.m_ElderlyWeight = value;
                    changed = true;
                }
            }

            if (baseline.CarConsumption.HasValue)
            {
                int value = Mathf.Max(0, Mathf.RoundToInt(baseline.CarConsumption.Value));
                if (prefab.m_CarConsumption != value)
                {
                    prefab.m_CarConsumption = value;
                    changed = true;
                }
            }

            if (baseline.IsTradable.HasValue && prefab.m_IsTradable != baseline.IsTradable.Value)
            {
                prefab.m_IsTradable = baseline.IsTradable.Value;
                changed = true;
            }

            if (baseline.IsProduceable.HasValue && prefab.m_IsProduceable != baseline.IsProduceable.Value)
            {
                prefab.m_IsProduceable = baseline.IsProduceable.Value;
                changed = true;
            }

            if (baseline.IsLeisure.HasValue && prefab.m_IsLeisure != baseline.IsLeisure.Value)
            {
                prefab.m_IsLeisure = baseline.IsLeisure.Value;
                changed = true;
            }

            return changed;
        }

        private static void CopyPrefabToResourceData(ResourcePrefab prefab, ref ResourceData resourceData)
        {
            resourceData.m_IsMaterial = prefab.m_IsMaterial;
            resourceData.m_IsProduceable = prefab.m_IsProduceable;
            resourceData.m_IsTradable = prefab.m_IsTradable;
            resourceData.m_IsLeisure = prefab.m_IsLeisure;
            resourceData.m_Weight = prefab.m_Weight;
            resourceData.m_Price = prefab.m_InitialPrice;
            resourceData.m_WealthModifier = prefab.m_WealthModifier;
            resourceData.m_BaseConsumption = prefab.m_BaseConsumption;
            resourceData.m_ChildWeight = prefab.m_ChildWeight;
            resourceData.m_TeenWeight = prefab.m_TeenWeight;
            resourceData.m_AdultWeight = prefab.m_AdultWeight;
            resourceData.m_ElderlyWeight = prefab.m_ElderlyWeight;
            resourceData.m_CarConsumption = prefab.m_CarConsumption;
            resourceData.m_RequireTemperature = prefab.m_RequireTemperature;
            resourceData.m_RequiredTemperature = prefab.m_RequiredTemperature;
            resourceData.m_RequireNaturalResource = prefab.m_RequireNaturalResource;
        }

        private static bool Approximately(float lhs, float rhs)
        {
            return Mathf.Abs(lhs - rhs) <= kFloatTolerance;
        }
    }
}
