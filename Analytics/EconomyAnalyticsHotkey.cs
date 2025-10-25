using UnityEngine;
using Game.Input;
using Unity.Entities;

namespace MarketBasedEconomy.Analytics
{
    /// <summary>
    /// Monitors the configured keyboard shortcut and toggles the overlay visibility.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EconomyAnalyticsHotkey : MonoBehaviour
    {

        private EconomyAnalyticsOverlay m_Overlay;

        private void Awake()
        {
            m_Overlay = GetComponent<EconomyAnalyticsOverlay>();
            if (m_Overlay == null)
            {
                m_Overlay = gameObject.AddComponent<EconomyAnalyticsOverlay>();
            }
        }

        private void Update()
        {
            // Check if the configured keybinding was pressed
            var binding = EconomyAnalyticsConfig.GetToggleBinding();
            if (binding != null)
            {
                // Use the game's InputManager to check if the action was triggered
                var inputManager = InputManager.instance;
                if (inputManager != null)
                {
                    // Find the action using the binding information
                    var action = inputManager.FindAction(binding);
                    if (action != null && action.WasPerformedThisFrame())
                    {
                        m_Overlay.ToggleVisibility();
                    }
                }
            }
        }


    }
}
