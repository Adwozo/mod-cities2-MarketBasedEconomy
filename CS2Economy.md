Cities: Skylines II Economy — Technical Notes and Mod Hook Points

Overview

This document explains how the vanilla game computes prices, taxes, fees, wages, company profitability, and workforce scaling across building types, with direct references to the decompiled sources in ref/Game. It also describes where this mod hooks into the price flow to make prices more market-driven.

Key concepts and data structures

- Resources and indices
  - Enum Resource is used across systems to represent goods/services. EconomyUtils.GetResourceIndex(Resource) maps a resource to a 0-based index used for arrays/buffers.
    - File: ref/Game/Economy/EconomyUtils.cs (function GetResourceIndex)
  - ResourceData and ResourcePrefabs define weights, pricing category, and metadata for each resource.
    - Files: ref/Game/Prefabs/ResourceData.cs, ref/Game/Economy/EconomyUtils.cs

- Trade costs and transport
  - Each company can maintain TradeCost buffers per resource (buy/sell cost), updated based on recent trades and transport costs.
    - Files: ref/Game/Companies/TradeCost.cs, ref/Game/Economy/EconomyUtils.cs (GetTradeCost, SetTradeCost, GetTransportCost, GetWeight)

Taxation and fees

- Tax rates: storage, ranges, and modifiers
  - TaxSystem keeps a packed array m_TaxRates (length ≈ 92) that holds:
    - [0]: base total tax
    - Per-area offsets: Residential, Commercial, Industrial, Office via GetTaxRate(TaxAreaType, taxRates) = taxRates[0] + taxRates[(int)areaType]
    - Residential job level offsets: indices [5..9]
    - Resource-specific offsets:
      - Commercial: at index 10 + EconomyUtils.GetResourceIndex(resource)
      - Industrial and Office: at index 51 + EconomyUtils.GetResourceIndex(resource)
  - Limits are enforced via EnsureAreaTaxRateLimits and EnsureJobLevelTaxRateLimits/EnsureResourceTaxRateLimits using TaxParameterData ranges.
  - District policies (e.g., LowCommercialTax) can modify effective tax via GetModifiedTaxRate.
  - Files:
    - ref/Game/Simulation/TaxSystem.cs (TaxRate property; GetTaxRate/GetModifiedTaxRate; EnsureAreaTaxRateLimits; EnsureJobLevelTaxRateLimits; EnsureResourceTaxRateLimits)
    - ref/Game/Prefabs/TaxParameterData.cs

- Tax collection
  - OnUpdate schedules three PayTaxJob runs per “tax day” for Residential, Commercial and Industrial taxpayers (IncomeSource.TaxResidential / TaxCommercial / TaxIndustrial). These jobs aggregate TaxPayer data and transfer money from entities to the city.
  - Individual payer liability: TaxSystem.GetTax(TaxPayer) = round(0.01 * averageTaxRate * untaxedIncome)
  - Files:
    - ref/Game/Simulation/TaxSystem.cs (OnUpdate, PayTaxJob setup, GetTax)

- Service fees (water/electricity/education/etc.)
  - ServiceFeeSystem collects fees from households, applies consumption/efficiency/happiness multipliers, and routes revenue to city buffers:
    - ServiceFeeSystem.GetConsumptionMultiplier / GetEfficiencyMultiplier / GetHappinessEffect
    - Service fees are cached per CollectedCityServiceFeeData and then transferred to the city using FeeToCityJob.
  - Files:
    - ref/Game/Simulation/ServiceFeeSystem.cs
    - ref/Game/City/CollectedCityServiceFeeData.cs
    - ref/Game/City/ServiceFee.cs, ServiceFeeParameterData, BuildingEfficiencyParameterData, CitizenHappinessParameterData

Household income and wages

- Wages, benefits, and household money
  - PayWageSystem iterates households each “wage day,” computing income and enqueuing payments:
    - Workers receive wages (scaled for commuters), funded by companies (negative payment enqueued to workplace) and added to household Resources(Money).
    - Non-working citizens: child allowance, pensions, unemployment benefits (capped by days), all added to household resources.
    - Residential tax smoothing: taxPayer.m_AverageTaxRate is interpolated toward current residential rate by a fraction proportional to income; untaxed income accumulates for later taxation in TaxSystem.
  - Files:
    - ref/Game/Simulation/PayWageSystem.cs (PayWageJob, PayJob)
    - ref/Game/Economy/EconomyUtils.cs (AddResources)

Company buying/selling and price usage

- Resource buyers (households and companies)
  - ResourceBuyerSystem handles purchases for both consumers and business inputs. Core flow in BuyJob:
    - Determine base price:
      - If seller is commercial (SaleFlags.CommercialSeller): EconomyUtils.GetMarketPrice(resource, prefabs, ref resourceDatas)
      - Else (industrial seller): EconomyUtils.GetIndustrialPrice(resource, prefabs, ref resourceDatas)
    - Add buy/sell costs and transport cost per recent trades via TradeCost buffers.
    - If seller provides services (ServiceAvailable + ServiceCompanyData), multiply price by EconomyUtils.GetServicePriceMultiplier based on service availability vs. capacity.
    - Transfer money: buyer pays; seller receives if not a pure storage entity; update resource buffers accordingly.
    - Special case: buying a vehicle by a household can spawn a car.
  - Files:
    - ref/Game/Simulation/ResourceBuyerSystem.cs (HandleBuyersJob, BuyJob; calls GetMarketPrice/GetIndustrialPrice; updates TradeCost)
    - ref/Game/Economy/EconomyUtils.cs (GetMarketPrice, GetIndustrialPrice, GetTradeCost/SetTradeCost, GetTransportCost, GetServicePriceMultiplier)

- Resource producers and exports
  - ResourceProducerSystem aggregates ResourceProductionData from building prefab and installed upgrades, and if storage exceeds min(2000, storage capacity), adds a ResourceExporter with the current amount for outbound logistics.
  - Files:
    - ref/Game/Simulation/ResourceProducerSystem.cs (ResourceProducerJob)
    - ref/Game/Prefabs/ResourceProductionData.cs

Company profitability and workforce scaling

- Company total worth and profitability
  - EconomyUtils.GetCompanyTotalWorth(values company-held resources, owned vehicles/layouts, etc.)
  - CompanyProfitabilitySystem samples total worth daily and computes a byte-encoded profitability (delta worth per day, clamped). Used by AIs and potentially other systems.
  - Files:
    - ref/Game/Simulation/CompanyProfitabilitySystem.cs (CompanyProfitabilityJob)
    - ref/Game/Economy/EconomyUtils.cs (GetCompanyTotalWorth)

- Commercial AI workforce adjustment
  - CommercialAISystem adjusts WorkProvider.m_MaxWorkers based on service availability and capacity, and triggers property search or deletion if worth is too low.
  - Files:
    - ref/Game/Simulation/CommercialAISystem.cs (CommercialCompanyAITickJob)

- Industrial AI workforce adjustment
  - IndustrialAISystem adjusts workers based on storage levels, demand for the output resource (from CountCompanyDataSystem), and capacity fit; similarly triggers property search or deletion on persistently low worth.
  - Files:
    - ref/Game/Simulation/IndustrialAISystem.cs (CompanyAITickJob)
    - ref/Game/Simulation/CountCompanyDataSystem.cs (provides demand/production arrays)

Where this mod hooks in (market-based pricing)

- Hook point
  - ResourceBuyerSystem.BuyJob uses EconomyUtils.GetMarketPrice for commercial sales. This mod patches that call to adjust the base price using a supply/demand-derived multiplier before the rest of vanilla costs are applied.

- Implementation
  - Harmony reflection bridge applies a postfix to EconomyUtils.GetMarketPrice(Resource, ResourcePrefabs, ref ComponentLookup<ResourceData>), replacing the returned price via MarketEconomyManager.
  - Manager reads aggregate market signals (supply/demand) and computes a bounded multiplier with configurable sensitivity.
  - Files in this repository:
    - Mod.cs — loads the mod, initializes and applies patches
    - Harmony/HarmonyBridge.cs — reflection-based Harmony bridge to avoid a NuGet dependency; applies the postfix
    - Economy/MarketEconomyManager.cs — computes market-adjusted price; caches snapshots and parameters
    - Patches/EconomyPricePatch.cs — legacy HarmonyLib version (kept empty)

- Behavioral effect
  - Only the base price returned by GetMarketPrice is altered; all subsequent vanilla adjustments (trade costs, transport, service capacity multiplier, etc.) remain intact. If Harmony is not present at runtime, the patch is skipped and vanilla pricing is used.

Data sources for market signals

- The manager leverages existing aggregate systems to infer supply/demand. Useful references:
  - ref/Game/Simulation/BudgetSystem.cs — tracks aggregated trades/wealth/company counts used to infer market pressure
  - ref/Game/Simulation/CountCompanyDataSystem.cs — provides per-resource production/demand arrays for industrial logic

End-to-end price flow (commercial sale)

1) Base price: EconomyUtils.GetMarketPrice(resource, ...) — patched by this mod to apply MarketEconomyManager.AdjustMarketPrice(resource, basePrice)
2) Add TradeCost buy cost for the seller; add transport cost-derived components; update moving averages for buyer/seller
3) If seller is a ServiceCompany, multiply by service availability multiplier (EconomyUtils.GetServicePriceMultiplier)
4) Transfer money/resources; possibly trigger side-effects (e.g., spawning vehicles)

Notes on building categories

- Commercial buildings
  - Provide services/goods directly to buyers; price path uses GetMarketPrice; workforce adjusts with CommercialAISystem

- Industrial buildings
  - Produce intermediate/final goods; sell to companies (industrial price path) or storage; workforce adjusts with IndustrialAISystem; export when storage is high via ResourceProducerSystem

- Office buildings
  - Taxation shares resource-specific offsets with industrial in TaxSystem (office resource range uses the same base slice [51 + index])

- City services
  - Collect fees via ServiceFeeSystem; consumption/efficiency/happiness respond to relative fee levels

Extending this mod

- Add additional patches by wiring more targets in HarmonyBridge.ApplyAll
  - Examples of potential hook points:
    - EconomyUtils.GetIndustrialPrice — for B2B pricing
    - EconomyUtils.SetTradeCost / Update trade cost logic inside ResourceBuyerSystem — for more sophisticated transport/learning curves
    - EconomyUtils.GetCompanyTotalWorth — to change valuation weightings (be mindful of AI effects)

Build and runtime notes

- Target framework: .NET Framework 4.8 (net48) to match the game’s Mono environment.
- No NuGet dependency on Harmony; Harmony/HarmonyBridge.cs discovers Harmony types at runtime if present.
- Output builds to bin/Debug/net48/MarketBasedEconomy.dll

File reference quick map

- Pricing and trade
  - ref/Game/Economy/EconomyUtils.cs — GetMarketPrice, GetIndustrialPrice, GetTradeCost/SetTradeCost, GetTransportCost, GetWeight, GetCompanyTotalWorth
  - ref/Game/Simulation/ResourceBuyerSystem.cs — uses pricing and maintains TradeCost buffers

- Taxes and wages
  - ref/Game/Simulation/TaxSystem.cs — rates, ranges, PayTaxJob scheduling
  - ref/Game/Simulation/PayWageSystem.cs — wages/benefits to households, residential tax smoothing

- Producers and services
  - ref/Game/Simulation/ResourceProducerSystem.cs — export triggers based on storage
  - ref/Game/Simulation/ServiceFeeSystem.cs — fee collection and effects

- Company AI and profitability
  - ref/Game/Simulation/CompanyProfitabilitySystem.cs — total worth delta
  - ref/Game/Simulation/CommercialAISystem.cs — workforce scaling, property seeking/deletion
  - ref/Game/Simulation/IndustrialAISystem.cs — workforce scaling, storage/demand aware
  - ref/Game/Simulation/CountCompanyDataSystem.cs — demand/production arrays used by Industrial AI

Mod internals (this repo)

- Mod.cs — mod lifecycle and patch entry
- Harmony/HarmonyBridge.cs — runtime Harmony integration and patch wiring
- Economy/MarketEconomyManager.cs — market multiplier computation from aggregate signals
- Properties/PublishProfiles — publishing configs

Troubleshooting

- Patch not applied (vanilla prices):
  - Ensure Harmony is present in the game environment; HarmonyBridge logs a message and skips if not found
  - Check log for signature mismatches if game updates change EconomyUtils.GetMarketPrice signature

- Build errors about target framework:
  - Confirm TargetFramework=net48 in MarketBasedEconomy.csproj
