using System;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Simulation;
using MarketBasedEconomy.Harmony;
using MarketBasedEconomy.Economy;

namespace MarketBasedEconomy
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(MarketBasedEconomy)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        private Setting m_Setting;
        public const string HarmonyId = "com.andrew.marketbasedeconomy";

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));

            AssetDatabase.global.LoadSettings(nameof(MarketBasedEconomy), m_Setting, new Setting(this));

            LaborMarketManager.Instance.Reset();
            updateSystem.UpdateBefore<WageAdjustmentSystem, PayWageSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<ZeroWeightProductSystem, ResourceExporterSystem>(SystemUpdatePhase.GameSimulation);

            HarmonyBridge.ApplyAll(HarmonyId);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }

            // No explicit unpatch via reflection; safe to leave patched during game session.
        }
    }
}
