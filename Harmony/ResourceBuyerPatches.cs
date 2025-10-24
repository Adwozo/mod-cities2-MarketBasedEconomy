using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Colossal.Logging;
using Game.Economy;
using Game.Simulation;
using HarmonyLib;
using MarketBasedEconomy.Economy;

namespace MarketBasedEconomy.Harmony
{
    internal static class ResourceBuyerPatches
    {
        private static readonly ILog Log = LogManager.GetLogger($"{nameof(MarketBasedEconomy)}.{nameof(ResourceBuyerPatches)}").SetShowsErrorsInUI(false);

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                var buyJobType = typeof(ResourceBuyerSystem).GetNestedType("BuyJob", AccessTools.all);
                if (buyJobType == null)
                {
                    Log.Warn("BuyJob type not found; skipping ResourceBuyer patches.");
                    return;
                }

                Log.Info("BuyJob instrumentation disabled; no ResourceBuyer patches applied.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply ResourceBuyerSystem patches");
            }
        }

    }
}
