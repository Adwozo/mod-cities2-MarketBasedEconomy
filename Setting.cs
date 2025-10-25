using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.UI.Localization;
using MarketBasedEconomy.Analytics;
using MarketBasedEconomy.Economy;
using Unity.Mathematics;
using UnityEngine;

namespace MarketBasedEconomy
{
    [FileLocation(nameof(MarketBasedEconomy))]
    [SettingsUIGroupOrder(kEconomyGroup, kKeybindingGroup)]
    [SettingsUIShowGroupName(kEconomyGroup, kKeybindingGroup)]
    [SettingsUIKeyboardAction(Mod.kToggleOverlayActionName, ActionType.Button, usages: new string[] { Usages.kDefaultUsage }, interactions: new string[] { "Press" })]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";

        public const string kEconomyGroup = "Economy";
        public const string kKeybindingGroup = "KeyBinding";
        [SettingsUISection(kSection, kEconomyGroup)]
        public bool EnableDiagnosticsLog
        {
            get => Diagnostics.DiagnosticsLogger.Enabled;
            set
            {
                Diagnostics.DiagnosticsLogger.Enabled = value;
                if (value)
                {
                    Diagnostics.DiagnosticsLogger.Initialize();
                }
            }
        }

        [SettingsUISection(kSection, kEconomyGroup)]
        public bool EnableCompanyTaxAdjustments
        {
            get => CompanyProfitAdjustmentSystem.FeatureEnabled;
            set => CompanyProfitAdjustmentSystem.FeatureEnabled = value;
        }
        [SettingsUISlider(min = 0f, max = 1f, step = 0.05f)]
        [SettingsUICustomFormat(fractionDigits = 2, separateThousands = false, maxValueWithFraction = 1f)]
        [SettingsUISection(kSection, kEconomyGroup)]
        public float ExternalMarketWeight
        {
            get => MarketEconomyManager.Instance.ExternalPriceInfluence;
            set => MarketEconomyManager.Instance.ExternalPriceInfluence = math.clamp(value, 0f, 1f);
        }

        [SettingsUISlider(min = 0.1f, max = 0.75f, step = 0.05f)]
        [SettingsUICustomFormat(fractionDigits = 2, separateThousands = false, maxValueWithFraction = 1f)]
        [SettingsUISection(kSection, kEconomyGroup)]
        public float MinimumUtilizationShare
        {
            get => WorkforceUtilizationManager.Instance.MinimumUtilizationShare;
            set => WorkforceUtilizationManager.Instance.MinimumUtilizationShare = math.clamp(value, 0.05f, 0.95f);
        }

        [SettingsUISlider(min = 0f, max = 1f, step = 0.05f)]
        [SettingsUICustomFormat(fractionDigits = 2, separateThousands = false, maxValueWithFraction = 1f)]
        [SettingsUISection(kSection, kEconomyGroup)]
        public float UnemploymentWagePenalty
        {
            get => LaborMarketManager.Instance.UnemploymentWagePenalty;
            set => LaborMarketManager.Instance.UnemploymentWagePenalty = math.max(0f, value);
        }

        [SettingsUISlider(min = 0f, max = 1.5f, step = 0.05f)]
        [SettingsUICustomFormat(fractionDigits = 2, separateThousands = false, maxValueWithFraction = 1.5f)]
        [SettingsUISection(kSection, kEconomyGroup)]
        public float SkillShortagePremium
        {
            get => LaborMarketManager.Instance.SkillShortagePremium;
            set => LaborMarketManager.Instance.SkillShortagePremium = math.max(0f, value);
        }

        [SettingsUISlider(min = 0f, max = 1f, step = 0.05f)]
        [SettingsUICustomFormat(fractionDigits = 2, separateThousands = false, maxValueWithFraction = 1f)]
        [SettingsUISection(kSection, kEconomyGroup)]
        public float EducationMismatchPremium
        {
            get => LaborMarketManager.Instance.EducationMismatchPremium;
            set => LaborMarketManager.Instance.EducationMismatchPremium = math.max(0f, value);
        }

        [SettingsUIKeyboardBinding(BindingKeyboard.G, Mod.kToggleOverlayActionName, shift: true)]
        [SettingsUISection(kSection, kKeybindingGroup)]
        public ProxyBinding ToggleOverlayBinding { get; set; }

        [SettingsUISection(kSection, kKeybindingGroup)]
        public bool ResetBindings
        {
            set
            {
                Mod.log.Info("Reset key bindings");
                ResetKeyBindings();
            }
        }

        [SettingsUIButton]
        [SettingsUISection(kSection, kEconomyGroup)]
        public bool ResetEconomyDefaults
        {
            set
            {
                SetDefaults();
                ApplyAndSave();
            }
        }

        public Setting(IMod mod) : base(mod)
        {

        }

        public override void SetDefaults()
        {
            var marketManager = MarketEconomyManager.Instance;
            marketManager.MinimumPriceMultiplier = 0.5f;
            marketManager.MaximumPriceMultiplier = 2.5f;
            marketManager.Sensitivity = 0.65f;
            marketManager.ExternalPriceInfluence = 0.35f;

            var workforceManager = WorkforceUtilizationManager.Instance;
            workforceManager.MinimumUtilizationShare = 0.25f;

            var laborManager = LaborMarketManager.Instance;
            laborManager.UnemploymentWagePenalty = 0.6f;
            laborManager.SkillShortagePremium = 0.8f;
            laborManager.EducationMismatchPremium = 0.2f;

            Diagnostics.DiagnosticsLogger.Enabled = false;
            CompanyProfitAdjustmentSystem.FeatureEnabled = false;
            EconomyAnalyticsConfig.ResetToDefaults();
        }

        public void EnsureKeyBindingsRegistered()
        {
            if (!keyBindingRegistered)
            {
                RegisterKeyBindings();
            }

            ApplyKeyBindings();

            var toggleAction = GetAction(Mod.kToggleOverlayActionName);
            if (toggleAction != null)
            {
                toggleAction.shouldBeEnabled = true;
            }
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Market Based Economy" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kEconomyGroup), "Economy" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kKeybindingGroup), "Key bindings" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ExternalMarketWeight)), "External market weight" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ExternalMarketWeight)), "Blend factor between local supply-demand price and external trade price references." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.MinimumUtilizationShare)), "Minimum utilization" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.MinimumUtilizationShare)), "A fraction of the building employee capacity set as the company minimum staffed required." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UnemploymentWagePenalty)), "Unemployment wage penalty" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UnemploymentWagePenalty)), "Wage reduction factor applied when unemployment rises." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.SkillShortagePremium)), "Skill shortage wage premium" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.SkillShortagePremium)), "Wage increase factor applied when few skilled workers are available." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EducationMismatchPremium)), "Education mismatch wage premium" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EducationMismatchPremium)), "Additional wage boost when low-skill workers dominate the labor pool." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ToggleOverlayBinding)), "Toggle Analytics Overlay" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ToggleOverlayBinding)), "Keybinding to toggle the analytics overlay display" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ResetBindings)), "Reset key bindings" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ResetBindings)), "Reset all key bindings of the mod" },

                { m_Setting.GetBindingKeyLocaleID(Mod.kToggleOverlayActionName), "Toggle Overlay" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ResetEconomyDefaults)), "Reset economy defaults" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ResetEconomyDefaults)), "Restore all economy settings in this mod to their default values." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableDiagnosticsLog)), "Enable diagnostics log" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableDiagnosticsLog)), "Write detailed economy diagnostics to MarketEconomy.log for balancing and debugging." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableCompanyTaxAdjustments)), "Enable company tax adjustments (Experimental Large Performance Impact)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableCompanyTaxAdjustments)), "Apply the experimental profit-based tax recalculation (Experimental Large Performance Impact)." },
            };
        }

        public void Unload()
        {

        }
    }
}
