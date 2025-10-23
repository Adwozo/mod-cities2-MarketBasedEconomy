using System;
using Colossal.Logging;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using HarmonyLib;
using MarketBasedEconomy.Diagnostics;
using Unity.Entities;

namespace MarketBasedEconomy.Harmony
{
    internal static class HarmonyBridge
    {
        private static readonly ILog Log = LogManager.GetLogger($"{nameof(MarketBasedEconomy)}.{nameof(HarmonyBridge)}").SetShowsErrorsInUI(false);
        private static readonly HarmonyLib.Harmony HarmonyInstance = new HarmonyLib.Harmony(Mod.HarmonyId);
        private static bool _patchesApplied;
        private static bool _marketPricePostfixLogged;
        private static bool _wagePostfixLogged;

        public static void ResetDebugFlags()
        {
            _marketPricePostfixLogged = false;
            _wagePostfixLogged = false;
        }

        public static void ApplyAll(string harmonyId)
        {
            if (_patchesApplied)
            {
                return;
            }

            HarmonyInstance.PatchAll(typeof(HarmonyBridge).Assembly);
            ApplyMarketPricePostfix();
            ApplyMarketPriceEntityManagerPostfix();
            ApplyMarketPriceComponentPostfixes();
            ApplyWorkforceMaintenancePostfix();

            _patchesApplied = true;
        }

        private static void ApplyMarketPricePostfix()
        {
            try
            {
                var target = AccessTools.Method(
                    typeof(EconomyUtils),
                    nameof(EconomyUtils.GetMarketPrice),
                    new[] { typeof(Resource), typeof(ResourcePrefabs), typeof(ComponentLookup<ResourceData>).MakeByRefType() });

                var postfix = AccessTools.Method(typeof(HarmonyBridge), nameof(MarketPricePostfix));

                if (target == null || postfix == null)
                {
                    Log.Warn("Market price patch target or postfix not found; skipping.");
                    return;
                }

                HarmonyInstance.Patch(target, postfix: new HarmonyMethod(postfix));

                Log.Info("Applied market price postfix via Harmony.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply Harmony market price postfix");
            }
        }

        private static void ApplyMarketPriceEntityManagerPostfix()
        {
            try
            {
                var target = AccessTools.Method(
                    typeof(EconomyUtils),
                    nameof(EconomyUtils.GetMarketPrice),
                    new[] { typeof(Resource), typeof(ResourcePrefabs), typeof(EntityManager) });

                var postfix = AccessTools.Method(typeof(HarmonyBridge), nameof(MarketPriceEntityManagerPostfix));

                if (target == null || postfix == null)
                {
                    Log.Warn("Market price (EntityManager) patch target or postfix not found; skipping.");
                    return;
                }

                HarmonyInstance.Patch(target, postfix: new HarmonyMethod(postfix));

                Log.Info("Applied market price (EntityManager) postfix via Harmony.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply Harmony market price (EntityManager) postfix");
            }
        }

        private static void ApplyMarketPriceComponentPostfixes()
        {
            try
            {
                var industrialTarget = AccessTools.Method(
                    typeof(EconomyUtils),
                    nameof(EconomyUtils.GetIndustrialPrice),
                    new[] { typeof(Resource), typeof(ResourcePrefabs), typeof(ComponentLookup<ResourceData>).MakeByRefType() });

                var serviceTarget = AccessTools.Method(
                    typeof(EconomyUtils),
                    nameof(EconomyUtils.GetServicePrice),
                    new[] { typeof(Resource), typeof(ResourcePrefabs), typeof(ComponentLookup<ResourceData>).MakeByRefType() });

                var industrialPostfix = AccessTools.Method(typeof(HarmonyBridge), nameof(IndustrialPricePostfix));
                var servicePostfix = AccessTools.Method(typeof(HarmonyBridge), nameof(ServicePricePostfix));

                if (industrialTarget == null || industrialPostfix == null)
                {
                    Log.Warn("Industrial price patch target or postfix not found; skipping.");
                }
                else
                {
                    HarmonyInstance.Patch(industrialTarget, postfix: new HarmonyMethod(industrialPostfix));
                    Log.Info("Applied industrial price postfix via Harmony.");
                }

                if (serviceTarget == null || servicePostfix == null)
                {
                    Log.Warn("Service price patch target or postfix not found; skipping.");
                }
                else
                {
                    HarmonyInstance.Patch(serviceTarget, postfix: new HarmonyMethod(servicePostfix));
                    Log.Info("Applied service price postfix via Harmony.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply Harmony component price postfixes");
            }
        }

        private static void ApplyWorkforceMaintenancePostfix()
        {
            try
            {
                var target = AccessTools.Method(typeof(WorkProviderSystem), "OnUpdate");
                var postfix = AccessTools.Method(typeof(HarmonyBridge), nameof(WorkProviderOnUpdatePostfix));

                if (target == null || postfix == null)
                {
                    Log.Warn("Workforce maintenance patch target or postfix not found; skipping.");
                    return;
                }

                HarmonyInstance.Patch(target, postfix: new HarmonyMethod(postfix));

                Log.Info("Applied workforce maintenance postfix.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply Harmony workforce maintenance postfix");
            }
        }

        private static void MarketPricePostfix(Resource r, ref float __result)
        {
            __result = Economy.MarketEconomyManager.Instance.AdjustMarketPrice(r, __result);
            if (!_marketPricePostfixLogged)
            {
                _marketPricePostfixLogged = true;
                DiagnosticsLogger.Log("Harmony", $"MarketPricePostfix invoked for {r}.");
            }
        }

        private static void MarketPriceEntityManagerPostfix(Resource r, ref float __result)
        {
            __result = Economy.MarketEconomyManager.Instance.AdjustMarketPrice(r, __result);
        }

        private static void IndustrialPricePostfix(Resource r, ResourcePrefabs __1, ComponentLookup<ResourceData> __2, ref float __result)
        {
            if (__result <= 0f)
            {
                return;
            }

            Entity entity = __1[r];
            if (!__2.HasComponent(entity))
            {
                return;
            }

            var data = __2[entity];

            __result = Economy.MarketEconomyManager.Instance.AdjustPriceComponent(
                r,
                data.m_Price.x,
                data.m_Price.y,
                Economy.MarketEconomyManager.PriceComponent.Industrial,
                skipLogging: false);
        }

        private static void ServicePricePostfix(Resource r, ResourcePrefabs __1, ComponentLookup<ResourceData> __2, ref float __result)
        {
            if (__result <= 0f)
            {
                return;
            }

            Entity entity = __1[r];
            if (!__2.HasComponent(entity))
            {
                return;
            }

            var data = __2[entity];

            __result = Economy.MarketEconomyManager.Instance.AdjustPriceComponent(
                r,
                data.m_Price.x,
                data.m_Price.y,
                Economy.MarketEconomyManager.PriceComponent.Service,
                skipLogging: false);
        }

        private static void WorkProviderOnUpdatePostfix(WorkProviderSystem __instance)
        {
            Economy.WorkforceUtilizationManager.Instance?.ApplyPostUpdate(__instance);
        }

        private static void GetWagePostfix(EconomyParameterData __instance, int jobLevel, bool cityServiceJob, ref int __result)
        {
        }
    }
}
