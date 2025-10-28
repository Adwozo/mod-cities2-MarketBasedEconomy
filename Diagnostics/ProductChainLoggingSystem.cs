using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace MarketBasedEconomy.Diagnostics
{
    /// <summary>
    /// Logs industrial process recipes for every company prefab when diagnostics logging is enabled.
    /// </summary>
    public partial class ProductChainLoggingSystem : GameSystemBase
    {
        private EntityQuery m_ProcessQuery;
        private PrefabSystem m_PrefabSystem;
        private bool m_Pending;

        protected override void OnCreate()
        {
            base.OnCreate();
            Enabled = false;
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ProcessQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PrefabData>(),
                    ComponentType.ReadOnly<IndustrialProcessData>()
                }
            });
            RequireForUpdate(m_ProcessQuery);
        }

        protected override void OnWorldReady()
        {
            if (DiagnosticsLogger.Enabled)
            {
                RequestDump();
            }
        }

        public void RequestDump()
        {
            if (!DiagnosticsLogger.Enabled)
            {
                return;
            }

            m_Pending = true;
            Enabled = true;
        }

        protected override void OnUpdate()
        {
            if (!m_Pending)
            {
                Enabled = false;
                return;
            }

            if (!DiagnosticsLogger.Enabled)
            {
                m_Pending = false;
                Enabled = false;
                return;
            }

            using NativeArray<Entity> entities = m_ProcessQuery.ToEntityArray(Allocator.TempJob);
            var seenPrefabs = new HashSet<string>(StringComparer.Ordinal);

            DiagnosticsLogger.Log("ProductChains", $"Dumping {entities.Length} industrial process entities");

            foreach (Entity entity in entities)
            {
                PrefabData prefabData = EntityManager.GetComponentData<PrefabData>(entity);
                CompanyPrefab prefab = m_PrefabSystem.GetPrefab<CompanyPrefab>(prefabData);
                if (prefab == null)
                {
                    continue;
                }

                string prefabName = prefab.name;
                if (!seenPrefabs.Add(prefabName))
                {
                    continue;
                }

                IndustrialProcessData processData = EntityManager.GetComponentData<IndustrialProcessData>(entity);
                string output = FormatStack(processData.m_Output);
                string input1 = FormatStack(processData.m_Input1);
                string input2 = FormatStack(processData.m_Input2);
                float outputPerWorker = processData.m_WorkPerUnit > 0
                    ? (float)processData.m_Output.m_Amount / processData.m_WorkPerUnit
                    : 0f;

                DiagnosticsLogger.Log(
                    "ProductChains",
                    $"{prefabName}: workPerUnit={processData.m_WorkPerUnit}, maxWorkersPerCell={processData.m_MaxWorkersPerCell}, output={output}, input1={input1}, input2={input2}, outputPerWorker≈{outputPerWorker:0.###}"
                );
            }

            m_Pending = false;
            Enabled = false;
        }

        private static string FormatStack(ResourceStack stack)
        {
            if (stack.m_Amount <= 0 || stack.m_Resource == Resource.NoResource)
            {
                return "None";
            }

            return $"{stack.m_Amount}x{stack.m_Resource}";
        }
    }
}
