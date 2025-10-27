# RealEco Core Concept

## Purpose
RealEco rebuilds Cities: Skylines II's market simulation so prices, supply, and household demand operate on saner baselines. It accomplishes this by loading a tunable configuration at startup, rewriting prefab component data, and swapping in custom ECS systems that respect the new economic parameters. The result is a configurable economy overhaul that can rebalance prices and company behavior without rebuilding the entire mod for every tweak.

## Design Pillars
- **Externalized balance data** – Economy knobs live in `Config.xml`, not hard-coded values. Mod authors (or players) can edit multipliers, resource prices, company productivity, and demand curves without recompiling.
- **Prefab state reset** – Prefab components are forced back to vanilla defaults before new values are applied. This avoids stacking modifications when a save reloads or multiple mods touch the same entity.
- **Custom simulation loops** – Replacement systems handle household consumption, resource buying, and demand calculations so that runtime behavior matches the tuned data.
- **Feature toggles and diagnostics** – Settings flags determine which overhauls are active, while verbose logging makes it easy to audit each change.

## Configuration Pipeline
1. **Deserialize** – `ConfigXml` reads `Config.xml` into strongly typed DTOs covering economy parameters, prefab overrides, and demand sliders.
2. **Apply overrides** – `ConfigTool.ConfigurePrefab` walks each prefab/component the file mentions. It uses reflection for generic fields and dedicated logic for complex structs like `IndustrialProcess`.
3. **Re-run initialization** – After values change, `Initialize`/`LateInitialize` methods fire again to keep ECS data in sync with prefab fields.

## Prefab Reinitialization Loop
- `ResourcePrefabReinitializeSystem` copies values from `ResourcePrefab` components back into `ResourceData`. It also recalculates `ResourceSystem.m_BaseConsumptionSum`, then triggers company updates.
- `CompanyPrefabReinitializeSystem` recomputes `IndustrialProcessData.m_WorkPerUnit` and `ServiceCompanyData.m_WorkPerUnit` so profitability targets line up with the new economy settings.
- Systems run once per load and disable themselves, ensuring the rebalancing logic executes after all prefabs exist but before the main simulation advances.

## Simulation Replacements
- **HouseholdBehaviorSystem** – Rebuilt consumption logic queues purchase requests based on wealth, household composition, and updated prices instead of vanilla heuristics.
- **ResourceBuyerSystem** – Adjusts how companies satisfy queued demand, aligning deliveries with the revised production chain.
- **CommercialUISystem / demand tweaks** – Optional UI hooks surface the new demand math and prevent vanilla systems from undoing the mod's adjustments.
- Harmony patches schedule these systems through `UpdateSystem.UpdateAt` while optionally disabling the base game counterparts when a feature flag is enabled.

## Operational Flow
1. Mod loads configuration and applies prefab overrides.
2. Resource reinit runs, pushing updated price/consumption baselines into ECS data and recalculating global consumption sums.
3. Company reinit recalculates work-per-unit figures to preserve profitability targets with the new multipliers.
4. Custom simulation systems process households, companies, and demand using the tuned data, keeping runtime behavior coherent with the configuration.

## Takeaways for MarketBasedEconomy
- Treat RealEco's configuration loader as the blueprint for your own price rebalance pipeline.
- Always reset relevant prefab/component state before applying new multipliers to avoid compounding adjustments.
- Schedule reinitialization systems immediately after the config step so derived fields (`m_WorkPerUnit`, base consumption totals) stay consistent.
- When replacing vanilla logic, register custom systems via `UpdateSystem.UpdateAt` and guard them behind settings or diagnostics to simplify testing.
