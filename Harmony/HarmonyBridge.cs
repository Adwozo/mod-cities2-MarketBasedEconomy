using System;
using System.Reflection;
using Colossal.Logging;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Unity.Entities;

namespace MarketBasedEconomy.Harmony
{
    internal static class HarmonyBridge
    {
        private static readonly ILog Log = LogManager.GetLogger($"{nameof(MarketBasedEconomy)}.{nameof(HarmonyBridge)}").SetShowsErrorsInUI(false);

        private static Type _harmonyType;
        private static Type _harmonyMethodType;
        private static MethodInfo _patchMethod;
        private static bool _initialized;

        public static bool Initialize()
        {
            if (_initialized)
                return _harmonyType != null;

            _initialized = true;
            try
            {
                // Try Harmony v2 namespace first.
                _harmonyType = Type.GetType("HarmonyLib.Harmony, HarmonyLib")
                               ?? Type.GetType("Harmony.HarmonyInstance, 0Harmony");

                if (_harmonyType == null)
                {
                    Log.Warn("Harmony not found. Market price patch will not be applied.");
                    return false;
                }

                _harmonyMethodType = _harmonyType.Assembly.GetType("HarmonyLib.HarmonyMethod")
                                   ?? _harmonyType.Assembly.GetType("Harmony.HarmonyMethod");

                _patchMethod = _harmonyType.GetMethod("Patch", BindingFlags.Instance | BindingFlags.Public);

                return _harmonyMethodType != null && _patchMethod != null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize Harmony bridge");
                return false;
            }
        }

        public static void ApplyAll(string harmonyId)
        {
            // Add all patches you want to apply here
            ApplyMarketPricePostfix(harmonyId);
            ApplyWorkforceMaintenancePostfix(harmonyId);
            ApplyWageAdjustmentPostfix(harmonyId);
        }

        public static void ApplyMarketPricePostfix(string harmonyId)
        {
            if (!Initialize()) return;

            try
            {
                var target = typeof(EconomyUtils).GetMethod(
                    nameof(EconomyUtils.GetMarketPrice),
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(Resource), typeof(ResourcePrefabs), typeof(ComponentLookup<ResourceData>).MakeByRefType() },
                    modifiers: null);

                var postfix = typeof(HarmonyBridge).GetMethod(nameof(MarketPricePostfix), BindingFlags.NonPublic | BindingFlags.Static);

                if (!PatchPostfix(harmonyId, target, postfix)) return;

                Log.Info("Applied market price postfix via Harmony.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply Harmony market price postfix");
            }
        }

        public static void ApplyWorkforceMaintenancePostfix(string harmonyId)
        {
            if (!Initialize()) return;

            try
            {
                var target = typeof(WorkProviderSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

                var postfix = typeof(HarmonyBridge).GetMethod(nameof(WorkProviderOnUpdatePostfix), BindingFlags.NonPublic | BindingFlags.Static);

                if (!PatchPostfix(harmonyId, target, postfix)) return;

                Log.Info("Applied workforce maintenance postfix.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply Harmony workforce maintenance postfix");
            }
        }

        public static void ApplyWageAdjustmentPostfix(string harmonyId)
        {
            if (!Initialize()) return;

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

                if (directWageTarget != null)
                {
                    PatchPostfix(harmonyId, directWageTarget, postfix);
                }

                if (aggregateWageTarget != null)
                {
                    PatchPostfix(harmonyId, aggregateWageTarget, postfix);
                }

                Log.Info("Applied wage adjustment postfix.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply Harmony wage adjustment postfix");
            }
        }

        private static bool PatchPostfix(string harmonyId, MethodInfo target, MethodInfo postfix)
        {
            if (target == null)
            {
                Log.Warn("Patch target method not found; skipping.");
                return false;
            }
            if (postfix == null)
            {
                Log.Warn("Patch postfix method not found; skipping.");
                return false;
            }

            try
            {
                object harmonyInstance = Activator.CreateInstance(_harmonyType, harmonyId);
                object harmonyMethod = Activator.CreateInstance(_harmonyMethodType, postfix);
                _patchMethod.Invoke(harmonyInstance, new object[] { target, null, harmonyMethod, null, null, null });
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to patch target method");
                return false;
            }
        }

        // Signature must match Harmony’s postfix expectations: (original args..., ref float __result)
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
