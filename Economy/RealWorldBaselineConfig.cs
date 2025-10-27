using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MarketBasedEconomy.Economy
{
    public sealed class RealWorldBaselineConfig
    {
        public float PriceScale { get; set; } = 1f;

        public float BaseConsumptionScale { get; set; } = 1f;

        public float MinimumPrice { get; set; } = 0.5f;

        public Dictionary<string, ResourceBaseline> Resources { get; set; } = new Dictionary<string, ResourceBaseline>(StringComparer.OrdinalIgnoreCase);

        public CompanyBaselines Companies { get; set; } = new CompanyBaselines();

        public HouseholdBaselines Household { get; set; } = new HouseholdBaselines();

        public WageBaselines Wages { get; set; } = new WageBaselines();

        public RealWorldBaselineConfig Normalize()
        {
            Resources = Resources != null
                ? new Dictionary<string, ResourceBaseline>(Resources, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ResourceBaseline>(StringComparer.OrdinalIgnoreCase);
            Companies ??= new CompanyBaselines();
            Companies.Normalize();
            Household ??= new HouseholdBaselines();
            Wages ??= new WageBaselines();
            return this;
        }

        public static RealWorldBaselineConfig CreateDefault()
        {
            var config = new RealWorldBaselineConfig
            {
                PriceScale = 1f,
                BaseConsumptionScale = 1f,
                MinimumPrice = 0.5f,
                Resources = new Dictionary<string, ResourceBaseline>(StringComparer.OrdinalIgnoreCase)
                {
                    // Agriculture & Food
                    ["Grain"] = new ResourceBaseline { Price = 200f, OutputPerWorkerPerDay = 3f },
                    ["Vegetables"] = new ResourceBaseline { Price = 800f, OutputPerWorkerPerDay = 2f },
                    ["Livestock"] = new ResourceBaseline { Price = 2500f, OutputPerWorkerPerDay = 0.3f },
                    ["Fish"] = new ResourceBaseline { Price = 2000f, OutputPerWorkerPerDay = 1f },

                    // Processed foods and beverages
                    ["Food"] = new ResourceBaseline { Price = 1500f, OutputPerWorkerPerDay = 6f },
                    ["ConvenienceFood"] = new ResourceBaseline { Price = 2500f, OutputPerWorkerPerDay = 5f },
                    ["Meals"] = new ResourceBaseline { Price = 10000f, OutputPerWorkerPerDay = 0.2f },
                    ["Beverages"] = new ResourceBaseline { Price = 900f, OutputPerWorkerPerDay = 8f },

                    // Forestry and wood processing
                    ["Wood"] = new ResourceBaseline { Price = 80f, OutputPerWorkerPerDay = 18f },
                    ["Timber"] = new ResourceBaseline { Price = 220f, OutputPerWorkerPerDay = 12f },
                    ["Paper"] = new ResourceBaseline { Price = 700f, OutputPerWorkerPerDay = 8f },
                    ["Furniture"] = new ResourceBaseline { Price = 3000f, OutputPerWorkerPerDay = 1f },

                    // Mining, energy, basic materials
                    ["Ore"] = new ResourceBaseline { Price = 60f, OutputPerWorkerPerDay = 18f },
                    ["Coal"] = new ResourceBaseline { Price = 50f, OutputPerWorkerPerDay = 22f },
                    ["Stone"] = new ResourceBaseline { Price = 12f, OutputPerWorkerPerDay = 24f },
                    ["Oil"] = new ResourceBaseline { Price = 550f, OutputPerWorkerPerDay = 8f },
                    ["Petrochemicals"] = new ResourceBaseline { Price = 800f, OutputPerWorkerPerDay = 8f },
                    ["Plastics"] = new ResourceBaseline { Price = 1400f, OutputPerWorkerPerDay = 7f },
                    ["Metals"] = new ResourceBaseline { Price = 2000f, OutputPerWorkerPerDay = 6f },
                    ["Steel"] = new ResourceBaseline { Price = 800f, OutputPerWorkerPerDay = 5f },
                    ["Concrete"] = new ResourceBaseline { Price = 100f, OutputPerWorkerPerDay = 9f },
                    ["Minerals"] = new ResourceBaseline { Price = 400f, OutputPerWorkerPerDay = 10f }, // generalized industrial minerals
                    ["Chemicals"] = new ResourceBaseline { Price = 900f, OutputPerWorkerPerDay = 6f },
                    ["Pharmaceuticals"] = new ResourceBaseline { Price = 40000f, OutputPerWorkerPerDay = 0.1f },

                    // Textiles
                    ["Cotton"] = new ResourceBaseline { Price = 1800f, OutputPerWorkerPerDay = 2f },
                    ["Textiles"] = new ResourceBaseline { Price = 2500f, OutputPerWorkerPerDay = 4f },

                    // Manufacturing and high-tech
                    ["Machinery"] = new ResourceBaseline { Price = 6000f, OutputPerWorkerPerDay = 0.6f },
                    ["Vehicles"] = new ResourceBaseline { Price = 15000f, OutputPerWorkerPerDay = 0.2f },
                    ["Electronics"] = new ResourceBaseline { Price = 30000f, OutputPerWorkerPerDay = 0.2f },
                    ["Software"] = new ResourceBaseline { Price = 800f, OutputPerWorkerPerDay = 5f }, // treated as “per ton” placeholder; see note below

                    // Services (kept as “per ton” placeholders for engine compatibility)
                    ["Lodging"] = new ResourceBaseline { Price = 400f, OutputPerWorkerPerDay = 3f },
                    ["Telecom"] = new ResourceBaseline { Price = 600f, OutputPerWorkerPerDay = 4f },
                    ["Financial"] = new ResourceBaseline { Price = 1000f, OutputPerWorkerPerDay = 4f },
                    ["Media"] = new ResourceBaseline { Price = 500f, OutputPerWorkerPerDay = 5f },
                    ["Entertainment"] = new ResourceBaseline { Price = 500f, OutputPerWorkerPerDay = 4f },
                    ["Recreation"] = new ResourceBaseline { Price = 400f, OutputPerWorkerPerDay = 4f },

                    // Municipal
                    ["Garbage"] = new ResourceBaseline { Price = 40f, OutputPerWorkerPerDay = 25f }
                },
                Companies = new CompanyBaselines
                {
                    // Multipliers tune “work required per unit” relative to default
                    ServiceWorkPerUnitMultiplier = 1.15f,
                    IndustrialWorkPerUnitMultiplier = 1.05f,
                    ExtractorWorkPerUnitMultiplier = 0.9f,

                    // Default output is a mid-complex manufacturing baseline
                    DefaultOutputPerWorkerPerDay = 6f,

                    Prefabs = new Dictionary<string, CompanyPrefabOverride>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Commercial_FastFood"] = new CompanyPrefabOverride { WorkPerUnitMultiplier = 1.2f },
                        ["Commercial_SupermarketLarge"] = new CompanyPrefabOverride { WorkPerUnitMultiplier = 1.15f },
                        ["Industrial_BioRefinery"] = new CompanyPrefabOverride { WorkPerUnitMultiplier = 0.85f },
                        ["Industrial_SteelMill"] = new CompanyPrefabOverride { WorkPerUnitMultiplier = 0.9f },
                        ["Extractor_OilRig"] = new CompanyPrefabOverride { WorkPerUnitMultiplier = 0.8f }
                    }
                },
                Household = new HouseholdBaselines
                {
                    ResourceConsumption = 1f,
                    TouristConsumptionMultiplier = 1f,
                    ResidentialMinimumEarnings = 800,
                    FamilyAllowance = 300,
                    Pension = 800,
                    UnemploymentBenefit = 800
                },
                Wages = new WageBaselines
                {
                    Level0 = 1200,
                    Level1 = 2000,
                    Level2 = 2500,
                    Level3 = 3500,
                    Level4 = 5000
                }
            };

            config.Companies.Normalize();
            return config;
        }
    }

    public sealed class ResourceBaseline
    {
        public float? Price { get; set; }

        public float? ReferencePriceUsd { get; set; }

        public float? PriceMultiplier { get; set; }

        public float? BaseConsumption { get; set; }

        public float? BaseConsumptionPerCapitaKgPerDay { get; set; }

        public float? BaseConsumptionMultiplier { get; set; }

        public bool? IsTradable { get; set; }

        public bool? IsProduceable { get; set; }

        public bool? IsLeisure { get; set; }

        public float? Weight { get; set; }

        public float? ChildWeight { get; set; }

        public float? TeenWeight { get; set; }

        public float? AdultWeight { get; set; }

        public float? ElderlyWeight { get; set; }

        public float? CarConsumption { get; set; }

        public float? OutputPerWorkerPerDay { get; set; }
    }

    public sealed class CompanyBaselines
    {
        public float ServiceWorkPerUnitMultiplier { get; set; } = 1f;

        public float IndustrialWorkPerUnitMultiplier { get; set; } = 1f;

        public float ExtractorWorkPerUnitMultiplier { get; set; } = 1f;

        public float DefaultOutputPerWorkerPerDay { get; set; } = 6f;

        public Dictionary<string, CompanyPrefabOverride> Prefabs { get; set; } = new Dictionary<string, CompanyPrefabOverride>(StringComparer.OrdinalIgnoreCase);

        public CompanyBaselines Normalize()
        {
            Prefabs = Prefabs != null
                ? new Dictionary<string, CompanyPrefabOverride>(Prefabs, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, CompanyPrefabOverride>(StringComparer.OrdinalIgnoreCase);
            return this;
        }

        public bool TryGetOverride(string prefabName, out CompanyPrefabOverride overrideData)
        {
            if (Prefabs == null)
            {
                overrideData = null;
                return false;
            }

            return Prefabs.TryGetValue(prefabName, out overrideData);
        }
    }

    public sealed class CompanyPrefabOverride
    {
        public float? WorkPerUnit { get; set; }

        public float? WorkPerUnitMultiplier { get; set; }

        public float? MaxWorkersPerCell { get; set; }

        public int? MaxService { get; set; }

        public float? OutputPerWorkerPerDay { get; set; }

        public float? OutputPerWorkerMultiplier { get; set; }
    }

    public sealed class HouseholdBaselines
    {
        public float? ResourceConsumption { get; set; }

        public float? TouristConsumptionMultiplier { get; set; }

        public int? ResidentialMinimumEarnings { get; set; }

        public int? FamilyAllowance { get; set; }

        public int? Pension { get; set; }

        public int? UnemploymentBenefit { get; set; }
    }

    public sealed class WageBaselines
    {
        public int? Level0 { get; set; }

        public int? Level1 { get; set; }

        public int? Level2 { get; set; }

        public int? Level3 { get; set; }

        public int? Level4 { get; set; }
    }

    public static class RealWorldBaselineConfigLoader
    {
        private static readonly JsonSerializerSettings s_SerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }
        };

        public static RealWorldBaselineConfig LoadOrCreate(string path)
        {
            RealWorldBaselineConfig config = null;

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    config = JsonConvert.DeserializeObject<RealWorldBaselineConfig>(json, s_SerializerSettings);
                }
                catch (Exception ex)
                {
                    Mod.log.Warn($"Failed to read real-world baseline config at {path}: {ex}");
                }
            }

            if (config == null)
            {
                config = RealWorldBaselineConfig.CreateDefault();
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                        string json = JsonConvert.SerializeObject(config, Formatting.Indented, s_SerializerSettings);
                        File.WriteAllText(path, json);
                        Mod.log.Info($"Wrote default real-world baseline config to {path}");
                    }
                    catch (Exception ex)
                    {
                        Mod.log.Error($"Failed to write default real-world baseline config: {ex}");
                    }
                }
            }

            return config.Normalize();
        }
    }
}
