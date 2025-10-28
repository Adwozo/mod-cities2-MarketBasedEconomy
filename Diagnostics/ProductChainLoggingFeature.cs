using System;
using Game;
using Game.Simulation;

namespace MarketBasedEconomy.Diagnostics
{
    /// <summary>
    /// Coordinates the lifecycle of <see cref="ProductChainLoggingSystem"/> and exposes a simple trigger.
    /// </summary>
    internal static class ProductChainLoggingFeature
    {
        private static bool s_Initialized;
        private static bool s_PendingRequest;
        private static UpdateSystem s_UpdateSystem;

        public static void Initialize(UpdateSystem updateSystem)
        {
            if (updateSystem == null)
            {
                throw new ArgumentNullException(nameof(updateSystem));
            }

            if (s_Initialized)
            {
                return;
            }

            s_UpdateSystem = updateSystem;
            var world = updateSystem.World;
            world.GetOrCreateSystemManaged<ProductChainLoggingSystem>();
            updateSystem.UpdateAt<ProductChainLoggingSystem>(SystemUpdatePhase.GameSimulation);

            s_Initialized = true;

            if (s_PendingRequest)
            {
                s_PendingRequest = false;
                RequestLogDump();
            }
        }

        public static void RequestLogDump()
        {
            if (!DiagnosticsLogger.Enabled)
            {
                return;
            }

            if (!s_Initialized)
            {
                s_PendingRequest = true;
                return;
            }

            var world = s_UpdateSystem?.World;
            if (world == null || !world.IsCreated)
            {
                s_PendingRequest = true;
                return;
            }

            var system = world.GetExistingSystemManaged<ProductChainLoggingSystem>();
            system?.RequestDump();
        }
    }
}
