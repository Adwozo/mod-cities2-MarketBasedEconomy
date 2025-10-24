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
    internal static class ResourceExporterPatches
    {
        private static readonly ILog Log = LogManager.GetLogger($"{nameof(MarketBasedEconomy)}.{nameof(ResourceExporterPatches)}").SetShowsErrorsInUI(false);

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                var handleExportsType = typeof(ResourceExporterSystem).GetNestedType("HandleExportsJob", AccessTools.all);
                if (handleExportsType == null)
                {
                    Log.Warn("HandleExportsJob type not found; skipping ResourceExporter patches.");
                    return;
                }

                Log.Info("HandleExports instrumentation disabled; no ResourceExporter patches applied.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply ResourceExporterSystem patches");
            }
        }
    }
}
