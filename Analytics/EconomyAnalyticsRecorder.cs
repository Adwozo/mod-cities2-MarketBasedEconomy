using System;
using System.Collections.Generic;
using Game.Economy;
using UnityEngine;

namespace MarketBasedEconomy.Analytics
{
    /// <summary>
    /// Aggregates sampled wage levels and market prices so the overlay can render charts.
    /// </summary>
    public sealed class EconomyAnalyticsRecorder
    {
        private const int kDefaultSampleCap = 2048;
        private const float kWageSampleInterval = 0.1f;  // Sample every 100ms instead of 1 second
        private const float kPriceSampleInterval = 0.1f;

        private static readonly Lazy<EconomyAnalyticsRecorder> s_Instance = new(() => new EconomyAnalyticsRecorder());

        private readonly object m_Lock = new();
        private readonly List<WageSample> m_WageSamples;
        private readonly Dictionary<Resource, List<PriceSample>> m_PriceSamples;
        private readonly List<Resource> m_TrackedResources;
        private readonly Dictionary<Resource, float> m_LastPriceSampleTimes;

        private int m_MaxSamples = kDefaultSampleCap;
        private float m_LastWageSampleTime = float.NegativeInfinity;

        private EconomyAnalyticsRecorder()
        {
            m_WageSamples = new List<WageSample>(kDefaultSampleCap);
            m_PriceSamples = new Dictionary<Resource, List<PriceSample>>();
            m_TrackedResources = new List<Resource>();
            m_LastPriceSampleTimes = new Dictionary<Resource, float>();
        }

        public static EconomyAnalyticsRecorder Instance => s_Instance.Value;

        public int MaxSamples
        {
            get => m_MaxSamples;
            set
            {
                lock (m_Lock)
                {
                    m_MaxSamples = Mathf.Max(32, value);
                    TrimSamples_NoLock();
                }
            }
        }

        public void Clear()
        {
            lock (m_Lock)
            {
                m_WageSamples.Clear();
                m_PriceSamples.Clear();
                m_TrackedResources.Clear();
                m_LastPriceSampleTimes.Clear();
                m_LastWageSampleTime = float.NegativeInfinity;
            }
        }

        public void RecordWageSample(int wage0, int wage1, int wage2, int wage3, int wage4)
        {
            float timestamp = Time.realtimeSinceStartup;
            lock (m_Lock)
            {
                bool shouldAppend = m_WageSamples.Count == 0 || timestamp - m_LastWageSampleTime >= kWageSampleInterval;
                if (shouldAppend)
                {
                    m_WageSamples.Add(new WageSample
                    {
                        Time = timestamp,
                        Level0 = wage0,
                        Level1 = wage1,
                        Level2 = wage2,
                        Level3 = wage3,
                        Level4 = wage4
                    });

                    TrimWageSamples_NoLock();
                    m_LastWageSampleTime = timestamp;
                }
                else if (m_WageSamples.Count > 0)
                {
                    int lastIndex = m_WageSamples.Count - 1;
                    m_WageSamples[lastIndex] = new WageSample
                    {
                        Time = timestamp,
                        Level0 = wage0,
                        Level1 = wage1,
                        Level2 = wage2,
                        Level3 = wage3,
                        Level4 = wage4
                    };
                }
            }
        }

        public void RecordPrice(Resource resource, float price)
        {
            if (resource == Resource.NoResource || resource == Resource.Money || float.IsNaN(price) || float.IsInfinity(price))
            {
                return;
            }

            float timestamp = Time.realtimeSinceStartup;
            lock (m_Lock)
            {
                if (!m_PriceSamples.TryGetValue(resource, out var samples))
                {
                    samples = new List<PriceSample>(m_MaxSamples);
                    m_PriceSamples[resource] = samples;
                    if (!m_TrackedResources.Contains(resource))
                    {
                        m_TrackedResources.Add(resource);
                    }
                }

                bool hasLastTimestamp = m_LastPriceSampleTimes.TryGetValue(resource, out float lastTimestamp);
                if (!hasLastTimestamp && samples.Count > 0)
                {
                    lastTimestamp = samples[^1].Time;
                    hasLastTimestamp = true;
                }

                bool shouldAppend = samples.Count == 0 || !hasLastTimestamp || timestamp - lastTimestamp >= kPriceSampleInterval;

                if (shouldAppend)
                {
                    samples.Add(new PriceSample
                    {
                        Time = timestamp,
                        Price = price
                    });

                    TrimPriceSamples_NoLock(samples);
                    m_LastPriceSampleTimes[resource] = timestamp;
                }
                else
                {
                    int lastIndex = samples.Count - 1;
                    samples[lastIndex] = new PriceSample
                    {
                        Time = timestamp,
                        Price = price
                    };
                }
            }
        }

        public void CopyWageSamples(List<WageSample> destination)
        {
            if (destination == null)
            {
                return;
            }

            lock (m_Lock)
            {
                destination.Clear();
                destination.AddRange(m_WageSamples);
            }
        }

        public void CopyPriceSamples(Resource resource, List<PriceSample> destination)
        {
            if (destination == null)
            {
                return;
            }

            lock (m_Lock)
            {
                destination.Clear();
                if (m_PriceSamples.TryGetValue(resource, out var samples))
                {
                    destination.AddRange(samples);
                }
            }
        }

        public void CopyTrackedResources(List<Resource> destination)
        {
            if (destination == null)
            {
                return;
            }

            lock (m_Lock)
            {
                destination.Clear();
                destination.AddRange(m_TrackedResources);
            }
        }

        public bool TryGetLatestPrice(Resource resource, out float price)
        {
            lock (m_Lock)
            {
                if (m_PriceSamples.TryGetValue(resource, out var samples) && samples.Count > 0)
                {
                    price = samples[^1].Price;
                    return true;
                }
            }

            price = 0f;
            return false;
        }

        private void TrimSamples_NoLock()
        {
            TrimWageSamples_NoLock();
            foreach (var samples in m_PriceSamples.Values)
            {
                TrimPriceSamples_NoLock(samples);
            }
        }

        private void TrimWageSamples_NoLock()
        {
            int excess = m_WageSamples.Count - m_MaxSamples;
            if (excess > 0)
            {
                m_WageSamples.RemoveRange(0, excess);
            }
        }

        private void TrimPriceSamples_NoLock(List<PriceSample> samples)
        {
            int excess = samples.Count - m_MaxSamples;
            if (excess > 0)
            {
                samples.RemoveRange(0, excess);
            }
        }

        public struct WageSample
        {
            public float Time;
            public float Level0;
            public float Level1;
            public float Level2;
            public float Level3;
            public float Level4;
        }

        public struct PriceSample
        {
            public float Time;
            public float Price;
        }
    }
}
