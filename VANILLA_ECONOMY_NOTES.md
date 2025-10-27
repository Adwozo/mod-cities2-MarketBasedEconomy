# Vanilla Pricing & Tax Reference

## Resource Pricing Data Pipeline
- `ref/Game/Prefabs/ResourcePrefab.cs` defines the authoring fields for every resource. The serialized field `float2 m_InitialPrice` stores the vanilla pricing tuple where **x = base industrial sale price** and **y = the commercial service markup that shops try to earn per unit**.
- `ref/Game/Prefabs/ResourceSystem.cs` copies those authoring values into the runtime `ResourceData` component (`resourceData.m_Price = prefab.m_InitialPrice`). The same pass also flags service resources through their `m_Weight` (services ship with weight 0, physical goods have a positive weight).
- `ref/Game/Prefabs/ResourceData.cs` exposes the runtime-facing structure. Relevant fields when rebalancing prices:
  - `m_Price.x`  ➜ industrial companies receive this much money when they offload a unit to another company or the virtual buyer.
  - `m_Price.y`  ➜ commercial companies expect to earn this profit on top of the purchase price when selling to households/tourists.
  - `m_Weight`   ➜ 0 indicates a non-shippable service; >0 counts toward cargo capacity and export cost.

### Resource catalogue
The `Game.Economy.Resource` enum (`ref/Game/Economy/Resource.cs`) lists every unit that ships with a price tuple. Grouping by the four broad categories the game uses:

```
Extractor / Farm Inputs: Grain, Vegetables, Livestock, Fish, Cotton, Coal, Oil, Ore, Stone, Minerals, Wood
Semi-finished Goods: Timber, Petrochemicals, Plastics, Metals, Steel, Concrete, Machinery, Chemicals, Pharmaceuticals, Paper, Textiles, Electronics
Finished Goods: ConvenienceFood, Food, Meals, Furniture, Vehicles, Beverages
Services & Intangibles (weight = 0): Lodging, Software, Telecom, Financial, Media, Entertainment, Recreation
Logistics & Misc: UnsortedMail, LocalMail, OutgoingMail, Garbage, Money (internal), Last (sentinel)
```

> **Important:** The actual vanilla numbers are serialized inside the Unity prefabs and are *not* present in this decompiled reference. To capture the live values you must query the `ResourceSystem` during gameplay.

### Capturing baseline prices at runtime
Drop a temporary system/behaviour inside your mod to log the vanilla tuple once the `ResourceSystem` has populated `ResourceData`:

```csharp
[BurstCompile]
public partial class DumpVanillaPricesSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<SimulationSystemGroup>();
    }

    protected override void OnUpdate()
    {
        var resourceSystem = World.GetOrCreateSystemManaged<Game.Prefabs.ResourceSystem>();
        var prefabs = resourceSystem.GetPrefabs();
        var lookup = GetComponentLookup<Game.Prefabs.ResourceData>(true);

        foreach (Game.Economy.Resource res in Enum.GetValues(typeof(Game.Economy.Resource)))
        {
            if (res == Game.Economy.Resource.NoResource || res == Game.Economy.Resource.Last || res == Game.Economy.Resource.All)
                continue;

            var entity = prefabs[res];
            if (entity == Entity.Null || !lookup.HasComponent(entity))
                continue;

            var data = lookup[entity];
            UnityEngine.Debug.Log($"[VanillaPrice] {res}: industrial={data.m_Price.x:F2}, service={data.m_Price.y:F2}, weight={data.m_Weight:F2}");
        }

        Enabled = false; // run once
    }
}
```

The resulting log gives you the authoritative vanilla price table you can copy back into balancing spreadsheets.

## Service Pricing & Household Consumption
- Zero-weight resources follow the service pipeline. In the stock `ResourceBuyerSystem`/`ResourceExporterSystem` pair, commercial companies convert goods into `Resource.Money` by booking `industrialPrice + serviceMarkup` per unit. See how your own `MarketProductSystem` branches on `resourceData.m_Weight == 0` and uses `vanillaUnitPrice = sanitizedIndustrialPrice + sanitizedServicePrice`.
- Households consume services by drawing down the money equivalent tracked in their `Resources` buffer. The global modifiers that control daily burn-down live in `EconomyParameterData` (`m_ResourceConsumptionMultiplier`, `m_ResourceConsumptionPerCitizen`). That struct is populated by `EconomyPrefab.LateInitialize`.

## Tax Collection Flow
The vanilla tax loop lives in `ref/Game/Simulation/TaxSystem.cs` and runs 32 mini-steps per in-game day.

### Accumulation
- Any entity with a `TaxPayer` component (households, commercial companies, processing companies) keeps an `m_UntaxedIncome` counter. Wage, sales, or rent systems increment this throughout the day.
- `TaxSystem.GetTax` converts accumulated income into payment when the update slot hits: `tax = round(0.01f * AverageTaxRate * UntaxedIncome)`.

### Payment job
- `TaxSystem.PayTaxJob.Execute` filters chunks to the correct `UpdateFrame`. For each taxpayer with a non-zero balance, it subtracts the amount (scaled by `m_TaxPaidMultiplier` for scenario modifiers) from the entity’s `Resource.Money` buffer and clears `m_UntaxedIncome`.
- The job also pushes a `StatisticsEvent` so the City Statistics UI can show taxable income per category (residential by job level, commercial/industrial/office by resource).

### Rate structure
- `TaxRates` (`ref/Game/City/TaxRates.cs`) is a dynamic buffer stored on a singleton. Vanilla uses a fixed `NativeArray<int>` of length 92:
  - index 0 = global base tax rate (defaults to 10%).
  - indices 1–4 = offsets for the four area types (Residential, Commercial, Industrial, Office).
  - indices 5–9 = residential job-level offsets (Simple → Manager).
  - indices 10–50 = per-resource offsets for commercial goods/services.
  - indices 51–91 = per-resource offsets for industrial & office products (shared block).
- `TaxParameterData` (`ref/Game/Prefabs/TaxParameterData.cs`) supplies min/max clamps for each slice. `TaxSystem.EnsureAreaTaxRateLimits` / `EnsureJobLevelTaxRateLimits` / `EnsureResourceTaxRateLimits` keep user edits inside those ranges.
- `TaxableResourceData` on each resource prefab flags the tax areas that resource belongs to. Only marked resources are iterated when calculating resource-specific offsets.

### Statistics impact
- Residential taxes: statistic `ResidentialTaxableIncome` parameterised by job level.
- Commercial: `CommercialTaxableIncome` parameterised by resource index.
- Industrial vs office: the job detects service-style industrial output by checking `ResourceData.m_Weight == 0` and pushes the income under `TaxOffice` / `OfficeTaxableIncome` instead.

## Practical Rebalance Steps
1. **Dump the vanilla tuples** with the helper system above and copy the results into a spreadsheet for safe keeping.
2. **Classify** which resources you plan to rebalance: goods (adjust `m_Price.x`) vs services (usually adjust the `m_Price.y` markup + household consumption multipliers).
3. **Review tax clamps** to make sure the min/max windows in `TaxParameterData` allow the policy range you want. If not, patch those limits first.
4. **Model tax sensitivity** by tweaking `TaxRates` offsets per group; remember residential job-level offsets can effectively invert the rate for high earners while keeping the headline rate stable.
5. **Verify in-game** by watching the `Statistics` panels—Taxable Income series should reflect your changes, and the log output from your own systems can confirm final sale and tax figures.
