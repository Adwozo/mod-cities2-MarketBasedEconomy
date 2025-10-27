using System;
using System.IO;
using Game;
using Game.Simulation;
using Unity.Entities;

namespace MarketBasedEconomy.Economy
{
    public static class RealWorldBaselineFeature
    {
        private static bool s_Initialized;
        private static bool s_Enabled;
        private static bool s_PendingApply;
        private static UpdateSystem s_UpdateSystem;
        private static RealWorldBaselineConfig s_Config;

        public static bool Enabled
        {
            get => s_Enabled;
            set
            {
                if (s_Enabled == value)
                {
                    if (value)
                    {
                        Refresh();
                    }
                    return;
                }

                s_Enabled = value;
                s_PendingApply = true;
                if (s_Initialized)
                {
                    ApplyState();
                }
            }
        }

        public static RealWorldBaselineConfig Config => s_Config;

        public static void Refresh()
        {
            if (!s_Enabled)
            {
                return;
            }

            s_PendingApply = true;
            if (s_Initialized)
            {
                ApplyState();
            }
        }

        public static void Initialize(UpdateSystem updateSystem)
        {
            if (s_Initialized)
            {
                return;
            }

            s_UpdateSystem = updateSystem ?? throw new ArgumentNullException(nameof(updateSystem));
            World world = updateSystem.World;
            world.GetOrCreateSystemManaged<RealWorldResourceInitializerSystem>();
            world.GetOrCreateSystemManaged<RealWorldCompanyInitializerSystem>();
            world.GetOrCreateSystemManaged<RealWorldEconomyParameterSystem>();
            updateSystem.UpdateAt<RealWorldResourceInitializerSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<RealWorldCompanyInitializerSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<RealWorldEconomyParameterSystem>(SystemUpdatePhase.GameSimulation);

            s_Initialized = true;
            if (s_Enabled)
            {
                s_PendingApply = true;
            }

            if (s_PendingApply)
            {
                ApplyState();
            }
        }

        public static void Dispose()
        {
            var world = s_UpdateSystem?.World;
            if (world != null && world.IsCreated)
            {
                try
                {
                    var resourceSystem = world.GetExistingSystemManaged<RealWorldResourceInitializerSystem>();
                    resourceSystem.Disable();
                }
                catch (InvalidOperationException)
                {
                }

                try
                {
                    var companySystem = world.GetExistingSystemManaged<RealWorldCompanyInitializerSystem>();
                    companySystem.Disable();
                }
                catch (InvalidOperationException)
                {
                }

                try
                {
                    var economySystem = world.GetExistingSystemManaged<RealWorldEconomyParameterSystem>();
                    economySystem.Disable();
                }
                catch (InvalidOperationException)
                {
                }
            }

            s_Config = null;
            s_UpdateSystem = null;
            s_Initialized = false;
            s_PendingApply = false;
            RealWorldBaselineState.Reset();
        }

        private static void ApplyState()
        {
            if (!s_Initialized || !s_PendingApply)
            {
                return;
            }

            var world = s_UpdateSystem.World;
            if (world == null || !world.IsCreated)
            {
                return;
            }

            var resourceSystem = world.GetOrCreateSystemManaged<RealWorldResourceInitializerSystem>();
            var companySystem = world.GetOrCreateSystemManaged<RealWorldCompanyInitializerSystem>();
            var economySystem = world.GetOrCreateSystemManaged<RealWorldEconomyParameterSystem>();

            if (s_Enabled)
            {
                string configPath = GetConfigPath();
                s_Config = RealWorldBaselineConfigLoader.LoadOrCreate(configPath);
                resourceSystem.SetConfig(s_Config);
                companySystem.SetConfig(s_Config);
                economySystem.SetConfig(s_Config);
                resourceSystem.RequestApply();
                companySystem.RequestApply();
                economySystem.RequestApply();
                Mod.log.Info($"RealWorldBaseline: applied configuration from {configPath}");
            }
            else
            {
                resourceSystem.Disable();
                companySystem.Disable();
                economySystem.Disable();
                s_Config = null;
                RealWorldBaselineState.Reset();
                Mod.log.Info("RealWorldBaseline: feature disabled");
            }

            s_PendingApply = false;
        }

        private static string GetConfigPath()
        {
            string baseDirectory = !string.IsNullOrWhiteSpace(Mod.ModDirectory)
                ? Mod.ModDirectory
                : AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDirectory, "Config", "RealWorldBaseline.json");
        }
    }
}
