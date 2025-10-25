# Keybinding System Implementation

## Overview

The MarketBasedEconomy mod now includes proper keybinding support using the Cities: Skylines 2 settings system.

## What was implemented:

1. **Proper Settings UI Integration**: 
   - Added `ToggleOverlayBinding` property with `ProxyBinding` type
   - Added keybinding group to settings UI with proper localization
   - Added "Reset key bindings" functionality

2. **Input Action Definition**:
   - Defined `kToggleOverlayActionName = "ToggleAnalyticsOverlay"` in Mod.cs
   - Added `SettingsUIKeyboardAction` attribute to register the action with the game
   - Set default binding to Shift+G

3. **Keybinding Configuration**:
   - Players can now configure the toggle overlay keybinding through the mod settings UI
   - Default keybinding is Shift+G
   - Keybinding follows Cities: Skylines 2 UI patterns

## Current Implementation Details:

- The `ProxyBinding` handles UI configuration and storage
- The actual input detection uses Cities: Skylines 2's `InputManager` and `ProxyAction` system
- Input is checked via `action.WasPerformedThisFrame()` for proper frame-accurate detection
- Default binding: Shift+G to toggle analytics overlay
- Users can reconfigure the binding through the mod's settings page
- The system properly integrates with the game's input system for reliable input detection

## Files Modified:

- `Setting.cs`: Added keybinding properties and UI configuration
- `Mod.cs`: Added input action name constant
- `EconomyAnalyticsConfig.cs`: Updated to work with new binding system
- `EconomyAnalyticsHotkey.cs`: Simplified input detection

## Usage:

1. Open Cities: Skylines 2
2. Go to Options > Mods > Market Based Economy
3. Navigate to "Key bindings" section
4. Configure "Toggle Analytics Overlay" keybinding as desired
5. Use the configured keybinding in-game to toggle the analytics overlay

## Notes:

The keybinding system follows the same pattern used by the Cities: Skylines 2 template mod, ensuring compatibility with the game's input system and providing a familiar configuration experience for players.