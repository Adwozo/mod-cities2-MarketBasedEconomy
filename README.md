# Market Based Economy

Market Based Economy reshapes the Cities: Skylines II simulation so prices, wages, and staffing react to the way your city actually runs. The mod layers a lightweight market manager on top of the vanilla economy, rewrites how companies record sales, and nudges workplaces to keep a minimum share of their jobs filled. An experimental (disabled-by-default) corporate tax adjustment pass is included for future releases.

## Key Features
- **Dynamic product pricing** – Tracks supply and demand per resource, blending local production with external trade references to keep prices within configurable floors and ceilings.
- **Revenue-aware company sales** – Intercepts processing company updates to turn inventory movement into revenue that respects market prices while feeding back fresh supply/demand metrics.
- **Labor-driven wages** – Adjusts `EconomyParameterData` before vanilla wage payout so unemployment, skill shortages, and education mismatch can raise or lower wages.
- **Minimum workplace utilization** – Ensures commercial and industrial buildings maintain a baseline worker count based on building size, level, and process data, avoiding chronic understaffing.
- **Diagnostics pipeline** – Optional structured logging (`MarketEconomy.log`) captures market, wage, and company data for balancing.
- **Experimental profit taxation** – When enabled in settings, recalculates company untaxed income so taxation follows profit minus rent rather than raw turnover.

## Gameplay Effects
- Prices fluctuate smoothly instead of snapping, reacting to over/under supply without runaway spikes.
- Service-style resources (weight = 0) are treated as virtual goods so offices and high-tech services still receive revenue feedback.
- Wage pressure scales with job scarcity, rewarding cities that keep unemployment low and education balanced.
- Understaffed buildings have their max workforce raised toward a per-building minimum to keep production chains running.

## In-Game Settings
All options live under **Options ▶ Mods ▶ Market Based Economy** and save per profile:
- `External market weight` – How strongly external trade prices influence local prices (0–1).
- `Minimum utilization` – Minimum staffed share enforced for workplaces (0.1–0.75).
- `Unemployment wage penalty` – Wage reduction when unemployment rises.
- `Skill shortage wage premium` – Wage bump when skilled labor is scarce.
- `Education mismatch wage premium` – Extra wage pressure when most workers are low skilled.
- `Enable diagnostics log` – Writes verbose logs to help diagnose balancing issues.
- `Enable company tax adjustments (Experimental)` – Turns on the profit-based tax recalculation. Disabled by default because it currently impacts performance.
- `Reset economy defaults` – Restores the shipping values shown above.

## Performance & Diagnostics
- Core market, wage, and utilization systems are managed-code ECS passes that run once per simulation tick. They are optimized for typical city sizes but still log summary lines when diagnostics are enabled.
- The experimental tax adjustment system accesses extensive company data every tick and can slow the simulation. Keep it disabled unless you need the feature and can tolerate the overhead.
- Diagnostics logging is off by default. Enable it when tuning the mod or reporting issues; disable it for normal play.

## Installation & Compatibility
1. Place the mod folder inside your `Cities Skylines II` mod directory or subscribe via PDX Mods once released.
2. Launch the game, open **Options ▶ Mods ▶ Market Based Economy**, review the settings, and toggle diagnostics/tax features as desired.
3. Most vanilla economy systems remain intact; the mod schedules its systems ahead of wage payment, resource exporters, and the tax system to keep data consistent.

Known considerations:
- Mods that also override `EconomyParameterData`, `ServiceCompanySystem`, or `TaxSystem` may conflict. Ensure load order or patches are coordinated.
- This release ships with company tax adjustments disabled because the simulation pass is still being optimized.

## Reporting Issues
When reporting bugs or balance concerns, please attach the `MarketEconomy.log` file (if diagnostics logging was enabled) along with reproduction steps and any other active economy mods.

Enjoy a city economy that reacts to how you actually build and staff it!
