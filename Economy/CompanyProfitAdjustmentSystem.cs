using Game.Agents;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

using ExtractorCompanyComponent = Game.Companies.ExtractorCompany;

namespace MarketBasedEconomy.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ServiceCompanySystem))]
    [UpdateAfter(typeof(ProcessingCompanySystem))]
    [UpdateAfter(typeof(ExtractorCompanySystem))]
    public partial class CompanyProfitAdjustmentSystem : SystemBase
    {
    public static bool FeatureEnabled { get; set; } = false;

        private EntityQuery m_CompanyQuery;

        private BufferLookup<Employee> m_EmployeeLookup;
        private BufferLookup<Efficiency> m_BuildingEfficiencies;
        private BufferLookup<DistrictModifier> m_DistrictModifiers;

        private ComponentLookup<ServiceCompanyData> m_ServiceCompanyDatas;
        private ComponentLookup<IndustrialCompany> m_IndustrialCompanies;
        private ComponentLookup<ExtractorCompanyComponent> m_ExtractorCompanies;
        private ComponentLookup<IndustrialProcessData> m_ProcessDatas;
        private ComponentLookup<ResourceData> m_ResourceDatas;
        private ComponentLookup<Citizen> m_Citizens;
        private ComponentLookup<Building> m_Buildings;
        private ComponentLookup<CurrentDistrict> m_CurrentDistricts;

        private ResourceSystem m_ResourceSystem;
        private TaxSystem m_TaxSystem;

        private ResourcePrefabs m_ResourcePrefabs;
        private EconomyParameterData m_EconomyParameters;
        private NativeArray<int> m_TaxRates;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CompanyQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<CompanyData>(),
                    ComponentType.ReadOnly<PropertyRenter>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Employee>(),
                    ComponentType.ReadWrite<TaxPayer>()
                }
            });

            RequireForUpdate(m_CompanyQuery);
            RequireForUpdate<EconomyParameterData>();

            m_EmployeeLookup = GetBufferLookup<Employee>(true);
            m_BuildingEfficiencies = GetBufferLookup<Efficiency>(true);
            m_DistrictModifiers = GetBufferLookup<DistrictModifier>(true);

            m_ServiceCompanyDatas = GetComponentLookup<ServiceCompanyData>(true);
            m_IndustrialCompanies = GetComponentLookup<IndustrialCompany>(true);
            m_ExtractorCompanies = GetComponentLookup<ExtractorCompanyComponent>(true);
            m_ProcessDatas = GetComponentLookup<IndustrialProcessData>(true);
            m_ResourceDatas = GetComponentLookup<ResourceData>(true);
            m_Citizens = GetComponentLookup<Citizen>(true);
            m_Buildings = GetComponentLookup<Building>(true);
            m_CurrentDistricts = GetComponentLookup<CurrentDistrict>(true);

            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_TaxSystem = World.GetOrCreateSystemManaged<TaxSystem>();
        }

        protected override void OnUpdate()
        {
            if (!FeatureEnabled)
            {
                return;
            }

            m_EmployeeLookup.Update(this);
            m_BuildingEfficiencies.Update(this);
            m_DistrictModifiers.Update(this);

            m_ServiceCompanyDatas.Update(this);
            m_IndustrialCompanies.Update(this);
            m_ExtractorCompanies.Update(this);
            m_ProcessDatas.Update(this);
            m_ResourceDatas.Update(this);
            m_Citizens.Update(this);
            m_Buildings.Update(this);
            m_CurrentDistricts.Update(this);

            m_ResourcePrefabs = m_ResourceSystem.GetPrefabs();
            m_EconomyParameters = SystemAPI.GetSingleton<EconomyParameterData>();
            m_TaxRates = m_TaxSystem.GetTaxRates();

            var tracker = CompanyFinanceTracker.Instance;

            using var companies = m_CompanyQuery.ToEntityArray(Allocator.TempJob);
            tracker.Prune(companies);

            int totalInQuery = companies.Length;
            int processedCount = 0;
            int skippedNoEmployeeBuffer = 0;
            int skippedEmptyEmployees = 0;
            int skippedNoProcessData = 0;
            int skippedWrongCategory = 0;
            int skippedAbandonedProperty = 0;

            var resourcePrefabs = m_ResourcePrefabs;
            var employeeLookup = m_EmployeeLookup;
            var efficiencyLookup = m_BuildingEfficiencies;
            var serviceCompanyDatas = m_ServiceCompanyDatas;
            var industrialCompanies = m_IndustrialCompanies;
            var extractorCompanies = m_ExtractorCompanies;
            var processDatas = m_ProcessDatas;
            var resourceDatas = m_ResourceDatas;
            var citizens = m_Citizens;
            var buildings = m_Buildings;
            var currentDistricts = m_CurrentDistricts;
            var districtModifiers = m_DistrictModifiers;
            var economyParameters = m_EconomyParameters;
            var taxRates = m_TaxRates;

            Entities
                .WithName("CompanyProfitAdjustment")
                .WithStoreEntityQueryInField(ref m_CompanyQuery)
                .WithReadOnly(employeeLookup)
                .WithReadOnly(efficiencyLookup)
                .WithReadOnly(serviceCompanyDatas)
                .WithReadOnly(industrialCompanies)
                .WithReadOnly(extractorCompanies)
                .WithReadOnly(processDatas)
                .WithReadOnly(resourceDatas)
                .WithReadOnly(citizens)
                .WithReadOnly(buildings)
                .WithReadOnly(currentDistricts)
                .WithReadOnly(districtModifiers)
                .WithoutBurst()
                .ForEach((Entity entity, ref TaxPayer taxPayer, in PrefabRef prefabRef, in PropertyRenter propertyRenter) =>
                {
                    if (!employeeLookup.HasBuffer(entity))
                    {
                        skippedNoEmployeeBuffer++;
                        return;
                    }

                    DynamicBuffer<Employee> employees = employeeLookup[entity];
                    if (employees.Length == 0)
                    {
                        skippedEmptyEmployees++;
                        return;
                    }

                    Entity companyPrefab = prefabRef.m_Prefab;
                    if (!processDatas.HasComponent(companyPrefab))
                    {
                        skippedNoProcessData++;
                        return;
                    }

                    bool isIndustrial = industrialCompanies.HasComponent(entity) || extractorCompanies.HasComponent(entity);
                    bool isService = serviceCompanyDatas.HasComponent(companyPrefab);
                    if (!isIndustrial && !isService)
                    {
                        skippedWrongCategory++;
                        return;
                    }

                    float buildingEfficiency = 1f;
                    Entity property = propertyRenter.m_Property;
                    if (property != Entity.Null && efficiencyLookup.HasBuffer(property))
                    {
                        buildingEfficiency = BuildingUtils.GetEfficiency(efficiencyLookup[property]);
                    }

                    if (property != Entity.Null && buildings.HasComponent(property))
                    {
                        if (SystemAPI.HasComponent<Game.Buildings.Abandoned>(property))
                        {
                            skippedAbandonedProperty++;
                            return;
                        }
                    }

                    IndustrialProcessData process = processDatas[companyPrefab];

                    int profitPerDay = EconomyUtils.GetCompanyProfitPerDay(
                        buildingEfficiency,
                        isIndustrial,
                        employees,
                        process,
                        resourcePrefabs,
                        ref resourceDatas,
                        ref citizens,
                        ref economyParameters);

                    Diagnostics.DiagnosticsLogger.Log(
                        "CompanyProfit",
                        $"company={entity.Index}:{entity.Version} prefab={companyPrefab.Index}:{companyPrefab.Version} industrial={isIndustrial} service={isService} efficiency={buildingEfficiency:F2} profitPerDay={profitPerDay}");

                    int profitPerTick = profitPerDay / EconomyUtils.kCompanyUpdatesPerDay;
                    if (profitPerTick < 0)
                    {
                        profitPerTick = 0;
                    }

                    int rentPerTick = 0;
                    if (propertyRenter.m_Rent > 0)
                    {
                        float rentFloat = propertyRenter.m_Rent / (float)PropertyRenterSystem.kUpdatesPerDay;
                        rentPerTick = (int)math.round(rentFloat);
                    }

                    int netIncome = profitPerTick - rentPerTick;
                    if (netIncome < 0)
                    {
                        netIncome = 0;
                    }

                    CompanyFinanceState state = tracker.GetState(entity);
                    int previousUntaxed = state.LastUntaxedIncome;
                    int vanillaDelta = taxPayer.m_UntaxedIncome - previousUntaxed;
                    int adjustment = netIncome - vanillaDelta;

                    Diagnostics.DiagnosticsLogger.Log(
                        "CompanyProfit",
                        $"company={entity.Index}:{entity.Version} profitPerTick={profitPerTick} rentPerTick={rentPerTick} netIncome={netIncome} vanillaDelta={vanillaDelta} adjustment={adjustment} untaxedBefore={taxPayer.m_UntaxedIncome}");

                    if (adjustment != 0)
                    {
                        int updatedIncome = math.max(0, taxPayer.m_UntaxedIncome + adjustment);
                        taxPayer.m_UntaxedIncome = updatedIncome;
                    }

                    if (netIncome > 0)
                    {
                        int areaRate = GetEffectiveTaxRate(
                            entity,
                            property,
                            process,
                            isIndustrial,
                            resourcePrefabs,
                            resourceDatas,
                            currentDistricts,
                            districtModifiers,
                            taxRates);

                        float weight = netIncome / math.max(1f, netIncome + previousUntaxed);
                        int previousAverage = state.Initialised ? state.LastAverageTaxRate : areaRate;
                        int newAverage = (int)math.round(math.lerp(previousAverage, areaRate, weight));
                        taxPayer.m_AverageTaxRate = math.clamp(newAverage, 0, 100);

                        Diagnostics.DiagnosticsLogger.Log(
                            "CompanyProfit",
                            $"company={entity.Index}:{entity.Version} areaRate={areaRate} previousAverage={previousAverage} newAverage={taxPayer.m_AverageTaxRate} weight={weight:F2} previousUntaxed={previousUntaxed} untaxedNow={taxPayer.m_UntaxedIncome}");
                    }

                    state.SyncCaches(taxPayer.m_UntaxedIncome, taxPayer.m_AverageTaxRate);
                    tracker.SetState(entity, state);
                    processedCount++;
                })
                .Run();

            Diagnostics.DiagnosticsLogger.Log(
                "CompanyProfit",
                $"CompanyProfitAdjustment summary: total={totalInQuery}, processed={processedCount}, noEmployeeBuffer={skippedNoEmployeeBuffer}, emptyEmployees={skippedEmptyEmployees}, noProcessData={skippedNoProcessData}, wrongCategory={skippedWrongCategory}, abandonedProperty={skippedAbandonedProperty}");
        }

        private static int GetEffectiveTaxRate(
            Entity company,
            Entity property,
            IndustrialProcessData process,
            bool isIndustrial,
            ResourcePrefabs resourcePrefabs,
            ComponentLookup<ResourceData> resourceDatas,
            ComponentLookup<CurrentDistrict> districts,
            BufferLookup<DistrictModifier> districtModifiers,
            NativeArray<int> taxRates)
        {
            Resource outputResource = process.m_Output.m_Resource;

            if (isIndustrial)
            {
                if (outputResource != Resource.NoResource)
                {
                    Entity resourcePrefab = resourcePrefabs[outputResource];
                    if (resourceDatas.HasComponent(resourcePrefab) && resourceDatas[resourcePrefab].m_Weight == 0f)
                    {
                        return TaxSystem.GetOfficeTaxRate(outputResource, taxRates);
                    }
                }

                return TaxSystem.GetIndustrialTaxRate(outputResource, taxRates);
            }

            bool isOffice = false;
            if (outputResource != Resource.NoResource)
            {
                Entity resourcePrefab = resourcePrefabs[outputResource];
                if (resourceDatas.HasComponent(resourcePrefab))
                {
                    isOffice = resourceDatas[resourcePrefab].m_Weight == 0f;
                }
            }

            if (isOffice)
            {
                return TaxSystem.GetOfficeTaxRate(outputResource, taxRates);
            }

            if (property != Entity.Null && districts.HasComponent(property))
            {
                CurrentDistrict currentDistrict = districts[property];
                if (currentDistrict.m_District != Entity.Null && districtModifiers.HasBuffer(currentDistrict.m_District))
                {
                    return TaxSystem.GetModifiedCommercialTaxRate(outputResource, taxRates, currentDistrict.m_District, districtModifiers);
                }
            }

            return TaxSystem.GetCommercialTaxRate(outputResource, taxRates);
        }
    }
}

