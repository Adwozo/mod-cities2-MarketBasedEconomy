using System;
using System.Reflection;
using Game.Prefabs;

namespace MarketBasedEconomy.Economy
{
    internal static class EconomyParameterAccess
    {
        private const BindingFlags kBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MemberAccessor<float> s_ResourceConsumption = new("ResourceConsumption");
        private static readonly MemberAccessor<float> s_TouristConsumptionMultiplier = new("TouristConsumptionMultiplier");
        private static readonly MemberAccessor<int> s_ResidentialMinimumEarnings = new("ResidentialMinimumEarnings");
        private static readonly MemberAccessor<int> s_FamilyAllowance = new("FamilyAllowance");
        private static readonly MemberAccessor<int> s_Pension = new("Pension");
        private static readonly MemberAccessor<int> s_UnemploymentBenefit = new("UnemploymentBenefit");
        private static readonly MemberAccessor<int>[] s_Wages =
        {
            new MemberAccessor<int>("Wage0"),
            new MemberAccessor<int>("Wage1"),
            new MemberAccessor<int>("Wage2"),
            new MemberAccessor<int>("Wage3"),
            new MemberAccessor<int>("Wage4")
        };

        public static bool TryGetResourceConsumption(ref EconomyParameterData data, out float value) => s_ResourceConsumption.TryGetValue(ref data, out value);

        public static bool TrySetResourceConsumption(ref EconomyParameterData data, float value) => s_ResourceConsumption.TrySetValue(ref data, value);

        public static bool TryGetTouristConsumptionMultiplier(ref EconomyParameterData data, out float value) => s_TouristConsumptionMultiplier.TryGetValue(ref data, out value);

        public static bool TrySetTouristConsumptionMultiplier(ref EconomyParameterData data, float value) => s_TouristConsumptionMultiplier.TrySetValue(ref data, value);

        public static bool TryGetResidentialMinimumEarnings(ref EconomyParameterData data, out int value) => s_ResidentialMinimumEarnings.TryGetValue(ref data, out value);

        public static bool TrySetResidentialMinimumEarnings(ref EconomyParameterData data, int value) => s_ResidentialMinimumEarnings.TrySetValue(ref data, value);

        public static bool TryGetFamilyAllowance(ref EconomyParameterData data, out int value) => s_FamilyAllowance.TryGetValue(ref data, out value);

        public static bool TrySetFamilyAllowance(ref EconomyParameterData data, int value) => s_FamilyAllowance.TrySetValue(ref data, value);

        public static bool TryGetPension(ref EconomyParameterData data, out int value) => s_Pension.TryGetValue(ref data, out value);

        public static bool TrySetPension(ref EconomyParameterData data, int value) => s_Pension.TrySetValue(ref data, value);

        public static bool TryGetUnemploymentBenefit(ref EconomyParameterData data, out int value) => s_UnemploymentBenefit.TryGetValue(ref data, out value);

        public static bool TrySetUnemploymentBenefit(ref EconomyParameterData data, int value) => s_UnemploymentBenefit.TrySetValue(ref data, value);

        public static bool TryGetWage(ref EconomyParameterData data, int level, out int value)
        {
            if ((uint)level >= s_Wages.Length)
            {
                value = default;
                return false;
            }

            return s_Wages[level].TryGetValue(ref data, out value);
        }

        public static bool TrySetWage(ref EconomyParameterData data, int level, int value)
        {
            if ((uint)level >= s_Wages.Length)
            {
                return false;
            }

            return s_Wages[level].TrySetValue(ref data, value);
        }

        private sealed class MemberAccessor<T>
        {
            private readonly string m_LogicalName;
            private readonly FieldInfo m_Field;
            private readonly PropertyInfo m_Property;
            private bool m_WarnedNoSetter;

            public MemberAccessor(string logicalName)
            {
                m_LogicalName = logicalName;
                var type = typeof(EconomyParameterData);
                m_Field = FindField(type, logicalName);
                m_Property = FindProperty(type, logicalName);
                if (!IsAvailable)
                {
                    Mod.log.Warn($"RealWorldBaseline: economy parameter '{logicalName}' is not available in EconomyParameterData.");
                }
            }

            public bool IsAvailable => m_Field != null || m_Property != null;

            public bool TryGetValue(ref EconomyParameterData data, out T value)
            {
                if (!IsAvailable)
                {
                    value = default;
                    return false;
                }

                object boxed = data;
                object raw = m_Field != null ? m_Field.GetValue(boxed) : m_Property!.GetValue(boxed);
                if (raw is T typed)
                {
                    value = typed;
                    return true;
                }

                if (raw != null)
                {
                    try
                    {
                        value = (T)Convert.ChangeType(raw, typeof(T));
                        return true;
                    }
                    catch (Exception)
                    {
                    }
                }

                value = default;
                return false;
            }

            public bool TrySetValue(ref EconomyParameterData data, T value)
            {
                if (!IsAvailable)
                {
                    return false;
                }

                object boxed = data;
                try
                {
                    if (m_Field != null)
                    {
                        m_Field.SetValue(boxed, value);
                    }
                    else if (m_Property != null)
                    {
                        if (!m_Property.CanWrite)
                        {
                            if (!m_WarnedNoSetter)
                            {
                                Mod.log.Warn($"RealWorldBaseline: economy parameter '{m_LogicalName}' is read-only; unable to apply baseline override.");
                                m_WarnedNoSetter = true;
                            }
                            return false;
                        }

                        m_Property.SetValue(boxed, value);
                    }
                    else
                    {
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Mod.log.Warn(ex, $"RealWorldBaseline: failed to set economy parameter '{m_LogicalName}'.");
                    return false;
                }

                data = (EconomyParameterData)boxed;
                return true;
            }

            private static FieldInfo FindField(Type type, string logicalName)
            {
                string lowerCamel = char.ToLowerInvariant(logicalName[0]) + logicalName.Substring(1);
                string[] candidates =
                {
                    $"m_{logicalName}",
                    $"m_{lowerCamel}",
                    $"_{lowerCamel}",
                    lowerCamel,
                    logicalName
                };

                foreach (string candidate in candidates)
                {
                    FieldInfo field = type.GetField(candidate, kBindingFlags);
                    if (field != null && IsCompatible(field.FieldType))
                    {
                        return field;
                    }
                }

                return null;
            }

            private static PropertyInfo FindProperty(Type type, string logicalName)
            {
                string lowerCamel = char.ToLowerInvariant(logicalName[0]) + logicalName.Substring(1);
                string[] candidates =
                {
                    logicalName,
                    lowerCamel,
                    char.ToUpperInvariant(lowerCamel[0]) + lowerCamel.Substring(1)
                };

                foreach (string candidate in candidates)
                {
                    PropertyInfo property = type.GetProperty(candidate, kBindingFlags);
                    if (property != null && IsCompatible(property.PropertyType))
                    {
                        return property;
                    }
                }

                return null;
            }

            private static bool IsCompatible(Type type)
            {
                return type == typeof(T) || Nullable.GetUnderlyingType(type) == typeof(T) || typeof(T).IsAssignableFrom(type);
            }
        }
    }
}
