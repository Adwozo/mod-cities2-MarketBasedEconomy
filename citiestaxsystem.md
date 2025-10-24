# Cities II Tax System Notes

## 1. High-Level Flow
- `TaxPayer` components (households, commercial, industrial companies) accumulate `m_UntaxedIncome` during wage and profit calculations. Their `m_AverageTaxRate` is updated by the systems that credited that income (e.g. `PayWageSystem`, `ServiceCompanySystem`).
- `TaxSystem` runs `PayTaxJob` 32 times per in-game day (`kUpdatesPerDay`) to convert untaxed income into actual payments. The job withdraws money from the entity's resource buffer, pushes statistics events, and clears `m_UntaxedIncome`.
- Paid taxes are categorized by `IncomeSource` (`TaxResidential`, `TaxCommercial`, `TaxIndustrial`, `TaxOffice`). The job books the appropriate statistic (`ResidentialTaxableIncome`, `CommercialTaxableIncome`, etc.) so the UI and budget systems can estimate revenue.
- A global game mode multiplier (`ModeSettingData.m_TaxPaidMultiplier`) can down/upscale collected amounts without altering displayed rates.

## 2. Tax Rate Storage Model
- All rates live in a single persistent `NativeArray<int>` of length 92.
- Index layout:
  - `0`: Global base rate (`TaxRate` property). Defaults to 10%.
  - `1-4`: Area offsets for `Residential`, `Commercial`, `Industrial`, `Office`. Effective rate = base + offset.
  - `5-9`: Residential job-level offsets (levels 0-4). Effective job-level rate = residential rate + offset.
  - `10-50`: Commercial resource offsets. Index = `10 + EconomyUtils.GetResourceIndex(resource)`.
  - `51-91`: Industrial/Office resource offsets. Index = `51 + resourceIndex` (shared between the two areas; office uses the same slot as industrial).
- `TaxParameterData` (authored via `TaxParameterPrefab`) contains the min/max clamps for:
  - Total tax (`m_TotalTaxLimits`).
  - Area rates (`m_ResidentialTaxLimits`, `m_CommercialTaxLimits`, `m_IndustrialTaxLimits`, `m_OfficeTaxLimits`).
  - Residential job spread (`m_JobLevelTaxLimits`).
  - Resource adjustments (`m_ResourceTaxLimits`).
- `TaxSystem.EnsureAreaTaxRateLimits` and helper methods enforce these clamps after any change (including UI-triggered updates).

## 3. Collection Mechanics
1. **Income accrual**
   - Systems crediting households or companies add money to their resource buffers and bump `TaxPayer.m_UntaxedIncome` with the net taxable portion. They also lerp `m_AverageTaxRate` toward the current effective rate so the eventual tax equals `% * untaxed`.
2. **Scheduled taxation**
   - `TaxSystem.OnUpdate` pulls current tax parameters, gathers resource metadata, and schedules `PayTaxJob` for each payer group.
   - `PayTaxJob` computes `tax = round(0.01 * averageRate * untaxedIncome)`, scales it by the area multiplier, subtracts it, sends statistics events, then zeroes `m_UntaxedIncome`.
   - Industrial payers whose prefab output resource has zero weight are recategorized as office income so the UI/budget align with intangible services.
   - **Mod tweak:** A post-processing system recomputes commercial, industrial, and extractor `TaxPayer` values using profit (= vanilla profit per tick minus rent per tick). The system replaces the vanilla untaxed income delta and recomputes the blended average tax rate so taxation reflects profit rather than raw revenue.
3. **Statistics + UI**
   - `CityStatisticsSystem` receives the queued events and persists aggregated taxable income figures per category/resource index.
   - `TaxationUISystem` calls the `ITaxSystem` interface to expose current rates, legal ranges, resource lists, and estimated income/effects.

## 4. Configuration & Extensibility Points
- **Accessing the system**: `World.GetOrCreateSystemManaged<TaxSystem>()` (or via `ITaxSystem`) provides safe entry points: `TaxRate`, `SetTaxRate`, `SetResidentialTaxRate`, `SetCommercialTaxRate`, etc. Always call `taxSystem.Readers.Complete()` before mutating to avoid race conditions with scheduled jobs.
- **Adjusting clamps**: Create/override a `TaxParameterPrefab` to change min/max bounds. This prefab writes a `TaxParameterData` component during `LateInitialize`.
- **Resource eligibility**: `TaxableResource` components attach `TaxableResourceData` specifying which tax areas a prefab participates in. Without it, the resource defaults to being taxable in every area.
- **District policies**: `DistrictModifierType.LowCommercialTax` lowers commercial rates at runtime. Extend the policy pipeline to create new tax modifiers if needed.
- **Game-mode multipliers**: `ModeSettingData.m_TaxPaidMultiplier` (float3) scales collected taxes per payer group. Custom game modes can populate this to simulate tax holidays or surcharges without touching rates.
- **Planned hook (`GetTaxRateEffect`)**: Currently returns 0; modifying it (e.g. via Harmony) allows you to surface gameplay effects from rate changes to UI/budget systems.

## 5. Mod Integration Ideas
- **Custom progression**: Patch `TaxSystem.SetTaxRate`/`SetResidentialTaxRate` to route through your own logic (e.g. dynamic caps, non-linear adjustments) before falling back to the vanilla clamp logic.
- **Alternative tax formulas**: Override `TaxSystem.GetTax` or `PayTaxJob.PayTax` to introduce brackets, deductions, or progressive multipliers. Remember to update statistics so UI/budgets remain consistent.
- **Profit-based corporate taxation (implemented)**: `CompanyProfitAdjustmentSystem` recalculates company untaxed income each tick as `(profitPerDay / kCompanyUpdatesPerDay) - (rent / PropertyRenterSystem.kUpdatesPerDay)` and clamps negative values. Use this as a template for more elaborate profit adjustments (e.g. maintenance, loan interest).
- **Additional statistics**: After collecting taxes, queue your own `StatisticsEvent` entries if the mod tracks new income categories. Use the same `CityStatisticsSystem` queue obtained from `GetStatisticsEventQueue`.
- **UI synchronization**: If you add new taxable areas/resources, extend `TaxationUISystem` bindings (or mirror its logic) so the front-end reflects your additions. The UI expects the arrays/ranges described above.
- **Runtime clamp updates**: Inject a system that mutates `TaxParameterData` each simulation tick (e.g. based on milestones) by writing to the singleton entity hosting that component.

## 6. Implementation Checklist for the Mod
1. Fetch `ITaxSystem` during your manager/system initialization and cache it.
2. Call `Readers.Complete()` before modifying rates to stay job-safe.
3. When altering tax arrays directly, mirror vanilla index math to avoid corrupting unrelated slots.
4. If you alter the tax formula, update tooltips/estimation logic (`GetEstimatedTaxAmount`) accordingly so UI predictions stay accurate. When using the profit-based hook, double-check the UI still shows positive income (it uses `TaxSystem`’s estimators which assume revenue-based accumulation).
5. Document any new policies or multipliers so players understand how your mod's tax rules differ from the base game.


