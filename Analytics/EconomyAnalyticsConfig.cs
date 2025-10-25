using System;
using UnityEngine;
using Game.Input;

namespace MarketBasedEconomy.Analytics
{
    /// <summary>
    /// Stores runtime-configurable options for the analytics overlay.
    /// </summary>
    public static class EconomyAnalyticsConfig
    {
        private const KeyCode kDefaultHotkey = KeyCode.G;

        public static bool HotkeyEnabled { get; set; } = true;
        public static bool RequireShift { get; set; } = true;
        public static KeyCode HotkeyKey { get; private set; } = kDefaultHotkey;
        public static bool AwaitingHotkeyCapture { get; private set; }

        /// <summary>
        /// Get the current keybinding from the settings system
        /// </summary>
        public static ProxyBinding GetToggleBinding()
        {
            return Mod.m_Setting_Static?.ToggleOverlayBinding ?? new ProxyBinding();
        }

        public static string GetHotkeyDisplayName()
        {
            return GetKeyDisplayName(HotkeyKey);
        }

        public static string GetKeyDisplayName(KeyCode key)
        {
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                return ((int)key - (int)KeyCode.Alpha0).ToString();
            }

            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
            {
                return $"Numpad {(int)key - (int)KeyCode.Keypad0}";
            }

            return key.ToString();
        }

        public static void ResetToDefaults()
        {
            HotkeyEnabled = true;
            RequireShift = true;
            HotkeyKey = kDefaultHotkey;
            AwaitingHotkeyCapture = false;
        }
    }
}
