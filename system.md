# Market-Based Economy Systems

## MarketEconomyManager (`Economy/MarketEconomyManager.cs`)
- Singleton orchestrator for market pricing across resources.
- Maintains per-resource supply/demand metrics and price state in `NativeHashMap`s.
- Integrates with game systems (`BudgetSystem`, `IndustrialDemandSystem`, `CommercialDemandSystem`, `CountCompanyDataSystem`, `ResourceSystem`) to compute up-to-date supply-demand ratios and trade snapshots.
- Exposes APIs to:
  - Adjust total price or specific price components with configurable floors/ceilings and external trade influence.
  - Register ad-hoc supply/demand events from gameplay systems.
  - Query sanitized supply/demand ratios for use in other systems.
- Applies smoothing and tolerances (`Sensitivity`, `DemandTolerance`, biases) to avoid runaway prices.
- Diagnostics routed through `DiagnosticsLogger` for traceability of market decisions.

## MarketProductSystem (`Economy/MarketProductSystem.cs`)
- `SystemBase` running in the simulation group before vanilla exporter/buyer systems.
- Iterates processing companies on a timed update cadence (32 slots per day) and converts produced goods into revenue.
- Differentiates zero-weight resources (treated as virtual services) vs weighted goods:
- Zero-weight: uses `MarketEconomyManager.AdjustPriceComponent` with the `Market` component to adjust the combined price before adding money.
  - Weighted: clamps price by supply/demand ratio from `MarketEconomyManager`.
- Logs revenue events and registers matching supply/demand volumes back to `MarketEconomyManager`.
- Caps sale batch size to avoid draining inventories in one update.

## MarketTransactionSystem (`Economy/MarketTransactionSystem.cs`)
- Lightweight `ISystem` that consumes `DynamicBuffer<MarketTransaction>` buffers from entities tagged with `MarketEconomyManager.MarketMetricsProxy`.
- Transfers buffered transactions into the manager via `RegisterSupply` / `RegisterDemand`, then clears the buffer.
- Provides a generic bridge for other systems to push market events without direct `MarketEconomyManager` dependencies.

## WageAdjustmentSystem (`Economy/WageAdjustmentSystem.cs`)
- Simulation system that updates `EconomyParameterData` prior to vanilla wage processing (`PayWageSystem`).
- Uses `LaborMarketManager` each frame to compute labor statistics and wage multipliers.
- Iterates all chunks containing `EconomyParameterData`, ensures baseline captured, and applies per-level wage adjustments before writing back.
- Aggregation happens in managed code (Burst disabled) because it uses manager APIs and diagnostics.

## LaborMarketManager (`Economy/LaborMarketManager.cs`)
- Singleton responsible for interpreting workforce data and deriving wage multipliers.
- Integrates with `CountHouseholdDataSystem` to gather city labor statistics (workforce size, employed citizens, education levels).
- Tracks baseline wage levels to allow reversible adjustments.
- Calculates a multiplier based on unemployment penalty, skill shortage premium, and education mismatch premium; clamps and logs the result.
- Supplies helper methods to apply multipliers, reset state, and restore baselines.

## WorkforceUtilizationManager (`Economy/WorkforceUtilizationManager.cs`)
- Singleton centered on enforcing minimum staffing for workplaces and recording utilization.
- Operates post-`WorkProviderSystem` update: queries all relevant work providers and adjusts `WorkProvider.m_MaxWorkers` to ensure floor staffing.
- Computes floors by inspecting associated property/building prefab data, industrial processes, and derived capacity (lot area, level, workers-per-cell).
- Uses `EndFrameBarrier` command buffer to safely write back modifications inside ECS constraints.
- Diagnostics available to trace enforced adjustments.

## WorkforceMaintenanceState (`Economy/WorkforceMaintenanceState.cs`)
- Marker component (`IComponentData`) used to gate systems (e.g., `WorkforceMaintenanceSystem`, not present in current repo) that may rely on maintenance state.

## Supporting Types
- `MarketTransaction` (buffer element) and `MarketTransactionType` enums provide a standardized payload for supply/demand events.
- `DiagnosticsLogger` handles opt-in file-based logging under the mod's persistent data directory.
- `EconomyOverrides` placeholder for finance-related hooks (currently empty).
