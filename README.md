## Market-Based Economy Mod for Cities: Skylines II

### Overview
- Replaces the vanilla fixed pricing system with a market-driven model that reacts to local supply, demand, and external trade prices.
- Encourages companies to fully utilize their workplaces by enforcing minimum staffing levels and charging maintenance when buildings sit idle.
- Adjusts wages based on labor market conditions so shortages of skilled workers drive pay up while high unemployment pushes it down.

### Key Features
- **Dynamic pricing**: Calculates multipliers per resource using live demand/supply ratios. Can blend in external trade prices for a more global economy feel.
- **Labor-aware wages**: Monitors unemployment and education levels to apply penalties or premiums to company wages.
- **Utilization requirements**: Office and industrial workplaces must staff at least 25% of their capacity (configurable) to avoid penalties.
- **Maintenance fees**: Under-utilized workplaces accrue daily upkeep and additional costs when they sit idle for too long.
- **Diagnostics toggle**: Optional logging to help modders tune parameters and troubleshoot behavior.

### Installation
1. Build the solution via `dotnet build MarketBasedEconomy.sln -c Release` (or `Debug` while testing). The toolchain now produces both the main mod and the `Bootstrap` helper assemblies.
2. Locate the compiled outputs:
   - Main mod: `bin/<Configuration>/net48/`
   - Bootstrap helper: `Bootstrap/bin/<Configuration>/net48/`
3. Create (or update) two directories inside the game `Mods` folder (`%LOCALAPPDATA%/Colossal Order/Cities Skylines II/Mods/` on Windows):
   - `MarketBasedEconomy`
   - `MarketBasedEconomy.Bootstrap`
4. Copy the platform-specific bundles/DLLs from each build output into the matching directory. The bootstrap files must stay separate from the main mod so the game loads them as two entries.
5. Ensure `lib/0Harmony.dll` sits alongside the **main** mod binaries; the bootstrap uses only game-provided assemblies.
6. Launch Cities: Skylines II and enable both **Market-Based Economy** and **Market-Based Economy Bootstrap** in the content manager.

### In-Game Setup
- Open **Options → Mods → Market-Based Economy** to configure sliders and toggles provided by the mod.
- Enable diagnostics logging if you want a detailed `MarketEconomy.log` in the game data folder for balancing.
- Adjust economic sliders (e.g., external price weight, maintenance costs) to suit your save file’s balance goals.

### Gameplay Tips
- Expect industries to adjust production and pricing gradually; allow a few in-game days for markets to settle.
- Keep an eye on company notifications—under-staffed buildings will incur maintenance penalties until you supply workers.
- Use City policies or education investments to increase the share of skilled workers and unlock higher wage premiums.

### Troubleshooting
- If prices appear stuck, disable diagnostics logging, exit to desktop, delete the generated log, then re-enable logging and retry.
- Conflicts may occur with other mods that fully replace pricing or labor systems. Try disabling overlapping mods if you encounter issues.
- Build failures complaining about missing `Colossal.*` or `UnityEngine.*` assemblies usually mean the CS2 mod toolchain paths aren’t configured. Re-open the in-game Modding Tools panel to refresh `CSII_*` environment variables and rebuild.
- Duplicate `TargetFrameworkAttribute` errors indicate another project or tool emits its own assembly info; ensure you’re building from a clean tree so the main mod and bootstrap projects stay separate.

### Contribution & Support
- Issues and pull requests welcome. Provide steps to reproduce along with diagnostic logs if possible.
- For balance feedback, include save details and the slider settings you changed from defaults. 