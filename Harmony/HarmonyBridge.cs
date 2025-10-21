using System;
using System.Reflection;
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

            ApplyExecutableAssetFilterPatch();
            HarmonyInstance.PatchAll(typeof(HarmonyBridge).Assembly);
            ApplyMarketPricePostfix();
            ApplyWorkforceMaintenancePostfix();
            ApplyWageAdjustmentPostfix();

            _patchesApplied = true;
        }

        private static void ApplyExecutableAssetFilterPatch()
        {
            try
            {
                var executableAssetType = AccessTools.TypeByName("Colossal.IO.AssetDatabase.ExecutableAsset");
                var displayClassType = executableAssetType?.GetNestedType("<>c__DisplayClass68_0", BindingFlags.NonPublic);
                var target = displayClassType?.GetMethod("<GetModAssets>b__2", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var prefix = typeof(HarmonyBridge).GetMethod(nameof(SkipDynamicAssemblies), BindingFlags.NonPublic | BindingFlags.Static);

                if (target == null || prefix == null)
                {
                    Log.Warn("ExecutableAsset.GetModAssets predicate not found; dynamic assembly filter skipped.");
                    return;
                }

                HarmonyInstance.Patch(target, prefix: new HarmonyMethod(prefix));
                Log.Info("Patched ExecutableAsset.GetModAssets predicate to ignore dynamic assemblies.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to patch ExecutableAsset.GetModAssets predicate");
            }
        }

        private static void ApplyMarketPricePostfix()
        {
            try
            {
                var target = typeof(EconomyUtils).GetMethod(
                    nameof(EconomyUtils.GetMarketPrice),
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(Resource), typeof(ResourcePrefabs), typeof(ComponentLookup<ResourceData>).MakeByRefType() },
                    modifiers: null);

                var postfix = typeof(HarmonyBridge).GetMethod(nameof(MarketPricePostfix), BindingFlags.NonPublic | BindingFlags.Static);

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
                var target = typeof(WorkProviderSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                var postfix = typeof(HarmonyBridge).GetMethod(nameof(WorkProviderOnUpdatePostfix), BindingFlags.NonPublic | BindingFlags.Static);

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

                var directWageTarget = typeof(EconomyUtils).GetMethod(
                    "CalculateTotalWage",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { dynamicBufferType, econParamRef },
                    modifiers: null);

                var aggregateWageTarget = typeof(EconomyUtils).GetMethod(
                    "CalculateTotalWage",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(int), typeof(WorkplaceComplexity), typeof(int), typeof(EconomyParameterData) },
                    modifiers: null);

                var postfix = typeof(HarmonyBridge).GetMethod(nameof(WageCalculationPostfix), BindingFlags.NonPublic | BindingFlags.Static);

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

        private static bool SkipDynamicAssemblies(Assembly __0, ref bool __result)
        {
            if (__0 == null)
            {
                return true;
            }

            if (__0.IsDynamic)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
