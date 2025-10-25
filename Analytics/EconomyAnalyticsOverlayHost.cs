using UnityEngine;

namespace MarketBasedEconomy.Analytics
{
    /// <summary>
    /// Ensures the overlay MonoBehaviour exists exactly once while the mod is loaded.
    /// </summary>
    public static class EconomyAnalyticsOverlayHost
    {
        private static GameObject s_Root;

        public static void Ensure()
        {
            if (s_Root != null)
            {
                return;
            }

            s_Root = new GameObject("MarketEconomyAnalyticsOverlay")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Object.DontDestroyOnLoad(s_Root);

            if (!s_Root.TryGetComponent(out EconomyAnalyticsOverlay _))
            {
                s_Root.AddComponent<EconomyAnalyticsOverlay>();
            }

            if (!s_Root.TryGetComponent(out EconomyAnalyticsHotkey _))
            {
                s_Root.AddComponent<EconomyAnalyticsHotkey>();
            }
        }

        public static void Dispose()
        {
            if (s_Root == null)
            {
                return;
            }

            Object.Destroy(s_Root);
            s_Root = null;
        }
    }
}
