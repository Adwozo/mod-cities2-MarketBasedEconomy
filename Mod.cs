using System;
using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Simulation;
using MarketBasedEconomy.Harmony;
using MarketBasedEconomy.Analytics;
using MarketBasedEconomy.Diagnostics;
using MarketBasedEconomy.Economy;
using Unity.Burst;

namespace MarketBasedEconomy
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(MarketBasedEconomy)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        private Setting m_Setting;
        public static Setting m_Setting_Static;
        public const string HarmonyId = "com.andrew.marketbasedeconomy";

        public const string kToggleOverlayActionName = "ToggleAnalyticsOverlay";

        public static string ModDirectory { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                log.Info($"Current mod asset at {asset.path}");
                ModDirectory = Path.GetDirectoryName(asset.path);
            }
            else
            {
                ModDirectory = AppDomain.CurrentDomain.BaseDirectory;
            }

            m_Setting = new Setting(this);
            m_Setting_Static = m_Setting;

            ProductChainLoggingFeature.Initialize(updateSystem);
            AssetDatabase.global.LoadSettings(nameof(MarketBasedEconomy), m_Setting, new Setting(this));
            m_Setting.EnsureKeyBindingsRegistered();
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));

            LaborMarketManager.Instance.Reset();
            EconomyAnalyticsRecorder.Instance.Clear();
            BurstCompiler.Options.EnableBurstCompilation = true;
            updateSystem.UpdateBefore<WageAdjustmentSystem, PayWageSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<MarketProductSystem, ResourceExporterSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<CompanyProfitAdjustmentSystem, TaxSystem>(SystemUpdatePhase.GameSimulation);

            RealWorldBaselineFeature.Initialize(updateSystem);
            RealWorldBaselineFeature.Refresh();


            EconomyAnalyticsOverlayHost.Ensure();
            HarmonyBridge.ApplyAll(HarmonyId);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            EconomyAnalyticsOverlayHost.Dispose();
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }

            RealWorldBaselineFeature.Dispose();

            // No explicit unpatch via reflection; safe to leave patched during game session.
        }
    }
}
