using System;
using Colossal.Logging;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using HarmonyLib;
using Unity.Entities;

namespace MarketBasedEconomy.Harmony
{
    internal static class HarmonyBridge
    {
        private static readonly ILog Log = LogManager.GetLogger($"{nameof(MarketBasedEconomy)}.{nameof(HarmonyBridge)}").SetShowsErrorsInUI(false);
        private static readonly HarmonyLib.Harmony HarmonyInstance = new HarmonyLib.Harmony(Mod.HarmonyId);
        private static bool _patchesApplied;

        public static void ApplyAll(string harmonyId)
        {
            if (_patchesApplied)
            {
                return;
            }

            HarmonyInstance.PatchAll(typeof(HarmonyBridge).Assembly);
            ApplyMarketPricePostfix();
            ApplyWorkforceMaintenancePostfix();
            ApplyWageAdjustmentPostfix();

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

        private static void ApplyWageAdjustmentPostfix()
        {
            try
            {
                var dynamicBufferType = typeof(DynamicBuffer<>).MakeGenericType(typeof(Employee));
                var econParamRef = typeof(EconomyParameterData).MakeByRefType();

                var directWageTarget = AccessTools.Method(
                    typeof(EconomyUtils),
                    "CalculateTotalWage",
                    new[] { dynamicBufferType, econParamRef });

                var aggregateWageTarget = AccessTools.Method(
                    typeof(EconomyUtils),
                    "CalculateTotalWage",
                    new[] { typeof(int), typeof(WorkplaceComplexity), typeof(int), typeof(EconomyParameterData) });

                var postfix = AccessTools.Method(typeof(HarmonyBridge), nameof(WageCalculationPostfix));

                if (postfix == null)
                {
                    Log.Warn("Wage adjustment postfix not found; skipping.");
                    return;
                }

                if (directWageTarget != null)
                {
                    HarmonyInstance.Patch(directWageTarget, postfix: new HarmonyMethod(postfix));
                }

                if (aggregateWageTarget != null)
                {
                    HarmonyInstance.Patch(aggregateWageTarget, postfix: new HarmonyMethod(postfix));
                }

                Log.Info("Applied wage adjustment postfix.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply Harmony wage adjustment postfix");
            }
        }

        private static void MarketPricePostfix(Resource r, ref float __result)
        {
            __result = Economy.MarketEconomyManager.Instance.AdjustMarketPrice(r, __result);
        }

        private static void WorkProviderOnUpdatePostfix(WorkProviderSystem __instance)
        {
            Economy.WorkforceUtilizationManager.Instance?.ApplyPostUpdate(__instance);
        }

        private static void WageCalculationPostfix(ref int __result)
        {
            __result = Economy.LaborMarketManager.Instance.ApplyWageMultiplier(__result);
        }
    }
}
