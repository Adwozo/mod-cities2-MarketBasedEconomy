using System;
using System.Collections.Generic;
using Game.Economy;
using UnityEngine;

namespace MarketBasedEconomy.Analytics
{
    /// <summary>
    /// Lightweight IMGUI overlay that renders sampled wage and price data with filtering and time-window controls.
    /// </summary>
    public sealed class EconomyAnalyticsOverlay : MonoBehaviour
    {
        private const int kGraphWidth = 320;
        private const int kGraphHeight = 160;
        private const int kYAxisLabelWidth = 55;
        private const int kTimeWindowDefaultIndex = 2; // 5 minutes
        private const float kLiveListHeight = 260f;
        private const float kStatusMessageDuration = 3.5f;

        private static readonly Color32[] s_WageColors =
        {
            new Color32(220, 76, 70, 255),   // Level 0
            new Color32(255, 171, 64, 255),  // Level 1
            new Color32(102, 187, 106, 255), // Level 2
            new Color32(66, 165, 245, 255),  // Level 3
            new Color32(171, 71, 188, 255)   // Level 4
        };

        private static readonly string[] s_WageLevelNames =
        {
            "Elementary",
            "High School",
            "College",
            "University",
            "Graduate"
        };

        private static readonly Color32 s_PriceColor = new(255, 255, 255, 255);
        private static readonly Color32 s_GridColor = new(255, 255, 255, 35);
        private static readonly float[] s_TimeWindowDurations = { 30f, 120f, 300f, 600f, 0f };
        private static readonly string[] s_TimeWindowLabels = { "30s", "2m", "5m", "10m", "All" };
    private static readonly string[] s_TabLabels = { "Live", "Wages", "Prices" };

        private static EconomyAnalyticsOverlay s_Instance;

        private Texture2D m_WageTexture;
        private Texture2D m_PriceTexture;
        private Color32[] m_WagePixels;
        private Color32[] m_PricePixels;

        private readonly List<EconomyAnalyticsRecorder.WageSample> m_WageSamples = new();
        private readonly List<EconomyAnalyticsRecorder.PriceSample> m_PriceSamples = new();
        private readonly List<Resource> m_TrackedResources = new();
        private readonly List<Resource> m_FilteredResources = new();

        private Rect m_WindowRect = new(60f, 60f, 420f, 520f);
        private bool m_ShowOverlay;
    private int m_SelectedTab;
        private int m_SelectedWageLevelIndex;
        private int m_SelectedResourceIndex;
        private int m_TimeWindowIndex = kTimeWindowDefaultIndex;
        private string m_ResourceFilter = string.Empty;
        private Resource? m_LastSelectedResource;
    private Vector2 m_LiveScroll;
    private string m_StatusMessage;
    private float m_StatusMessageTimer;

        private float m_LastWageMin;
        private float m_LastWageMax;
        private float m_LastPriceMin;
        private float m_LastPriceMax;
        private EconomyAnalyticsRecorder.WageSample? m_LatestWageSample;
        private float m_LatestPriceValue;
        private bool m_HasLatestPrice;

        private GUIStyle m_RightAlignedLabel;

        public static EconomyAnalyticsOverlay Instance => s_Instance;
        public bool Visible => m_ShowOverlay;

        private static float CurrentWindowDuration(int index) => s_TimeWindowDurations[Mathf.Clamp(index, 0, s_TimeWindowDurations.Length - 1)];

        private void Awake()
        {
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            s_Instance = this;

            m_WageTexture = CreateTexture();
            m_PriceTexture = CreateTexture();
            m_WagePixels = new Color32[kGraphWidth * kGraphHeight];
            m_PricePixels = new Color32[kGraphWidth * kGraphHeight];

            ClearPixels(m_WagePixels);
            ClearPixels(m_PricePixels);
            ApplyTexture(m_WageTexture, m_WagePixels);
            ApplyTexture(m_PriceTexture, m_PricePixels);

        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        public void ToggleVisibility()
        {
            m_ShowOverlay = !m_ShowOverlay;
        }

        public void SetVisibility(bool visible)
        {
            m_ShowOverlay = visible;
        }

        private void Update()
        {
            if (m_StatusMessageTimer > 0f)
            {
                m_StatusMessageTimer -= Time.deltaTime;
                if (m_StatusMessageTimer <= 0f)
                {
                    m_StatusMessageTimer = 0f;
                    m_StatusMessage = null;
                }
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (!m_ShowOverlay)
            {
                return;
            }

            m_WindowRect = GUI.Window(GetInstanceID(), m_WindowRect, DrawWindow, "Market Economy Analytics");
        }

        private void EnsureStyles()
        {
            if (m_RightAlignedLabel == null)
            {
                m_RightAlignedLabel = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperRight
                };
            }
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            DrawHeader();
            GUILayout.Space(6f);
            DrawTabSelector();

            GUILayout.Space(8f);
            switch (Mathf.Clamp(m_SelectedTab, 0, s_TabLabels.Length - 1))
            {
                case 0:
                    DrawLiveSection();
                    break;
                case 1:
                    DrawWageTab();
                    break;
                case 2:
                    DrawPriceTab();
                    break;
            }

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, m_WindowRect.width, 24f));
        }

        private void DrawHeader()
        {
            if (EconomyAnalyticsConfig.AwaitingHotkeyCapture)
            {
                GUILayout.Label("Press any key to set the overlay hotkey (Esc to cancel)", GUI.skin.label);
            }
            else
            {
                string keyText = EconomyAnalyticsConfig.GetHotkeyDisplayName();
                string chord = EconomyAnalyticsConfig.RequireShift ? $"Shift + {keyText}" : keyText;
                string label = EconomyAnalyticsConfig.HotkeyEnabled
                    ? $"Toggle hotkey: {chord}"
                    : "Overlay hotkey disabled in settings";
                GUILayout.Label(label, GUI.skin.label);
            }

            if (!string.IsNullOrEmpty(m_StatusMessage) && m_StatusMessageTimer > 0f)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(m_StatusMessage, GUI.skin.box, GUILayout.ExpandWidth(false));
                GUILayout.EndHorizontal();
            }
        }

        private void DrawTabSelector()
        {
            int current = Mathf.Clamp(m_SelectedTab, 0, s_TabLabels.Length - 1);
            m_SelectedTab = Mathf.Clamp(GUILayout.Toolbar(current, s_TabLabels), 0, s_TabLabels.Length - 1);
        }

        private void DrawLiveSection()
        {
            GUILayout.Label("Live data", GUI.skin.label);

            EconomyAnalyticsRecorder.Instance.CopyWageSamples(m_WageSamples);
            if (m_WageSamples.Count > 0)
            {
                var latest = m_WageSamples[^1];
                GUILayout.Label($"Latest wages: {BuildWageSummary(latest, includeAllLevels: true)}");
            }
            else
            {
                GUILayout.Label("Waiting for wage samples...");
            }

            GUILayout.Space(10f);
            GUILayout.Label("Product snapshot", GUI.skin.label);
            DrawResourceFilterUI();

            EconomyAnalyticsRecorder.Instance.CopyTrackedResources(m_TrackedResources);
            if (m_TrackedResources.Count == 0)
            {
                GUILayout.Label("Waiting for product price samples...");
                return;
            }

            m_TrackedResources.Sort();
            RefreshFilteredResources();
            if (m_FilteredResources.Count == 0)
            {
                GUILayout.Label("No products match the current filter.");
                return;
            }

            m_LiveScroll = GUILayout.BeginScrollView(m_LiveScroll, GUILayout.Height(kLiveListHeight));
            foreach (var resource in m_FilteredResources)
            {
                string priceText = EconomyAnalyticsRecorder.Instance.TryGetLatestPrice(resource, out float price)
                    ? price.ToString("F2")
                    : "—";
                GUILayout.Label($"{resource}: {priceText}");
            }
            GUILayout.EndScrollView();
        }

        private void DrawWageTab()
        {
            DrawTimeWindowSelector();
            GUILayout.Space(6f);
            DrawWageLevelSelector();
            GUILayout.Space(6f);
            DrawWageSection();
        }

        private void DrawPriceTab()
        {
            DrawTimeWindowSelector();
            GUILayout.Space(6f);
            DrawPriceSection();
        }

        private void DrawTimeWindowSelector()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Time window:", GUILayout.Width(90f));
            for (int i = 0; i < s_TimeWindowLabels.Length; i++)
            {
                bool selected = m_TimeWindowIndex == i;
                bool toggled = GUILayout.Toggle(selected, s_TimeWindowLabels[i], GUI.skin.button, GUILayout.Width(52f));
                if (toggled && !selected)
                {
                    m_TimeWindowIndex = i;
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawWageLevelSelector()
        {
            int levelCount = s_WageLevelNames.Length;
            if (levelCount == 0)
            {
                return;
            }

            m_SelectedWageLevelIndex = Mathf.Clamp(m_SelectedWageLevelIndex, 0, levelCount - 1);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Education level:", GUILayout.Width(120f));

            if (GUILayout.Button("<", GUILayout.Width(28f)))
            {
                m_SelectedWageLevelIndex = (m_SelectedWageLevelIndex - 1 + levelCount) % levelCount;
            }

            GUILayout.Label(s_WageLevelNames[m_SelectedWageLevelIndex], GUILayout.ExpandWidth(true));

            if (GUILayout.Button(">", GUILayout.Width(28f)))
            {
                m_SelectedWageLevelIndex = (m_SelectedWageLevelIndex + 1) % levelCount;
            }

            GUILayout.EndHorizontal();
        }

        private string BuildWageSummary(EconomyAnalyticsRecorder.WageSample sample, bool includeAllLevels)
        {
            Span<float> values = stackalloc float[5]
            {
                sample.Level0,
                sample.Level1,
                sample.Level2,
                sample.Level3,
                sample.Level4
            };

            if (includeAllLevels)
            {
                List<string> parts = new(values.Length);
                for (int i = 0; i < values.Length && i < s_WageLevelNames.Length; i++)
                {
                    parts.Add($"{s_WageLevelNames[i]}: {values[i]:F0}");
                }

                return string.Join(" | ", parts);
            }

            int selected = Mathf.Clamp(m_SelectedWageLevelIndex, 0, s_WageLevelNames.Length - 1);
            return $"{s_WageLevelNames[selected]}: {values[selected]:F0}";
        }

        private void DrawWageSection()
        {
            string selectedLabel = s_WageLevelNames[Mathf.Clamp(m_SelectedWageLevelIndex, 0, s_WageLevelNames.Length - 1)];
            GUILayout.Label($"Average wage — {selectedLabel}", GUI.skin.label);

            EconomyAnalyticsRecorder.Instance.CopyWageSamples(m_WageSamples);



            if (!RefreshWageTexture())
            {
                GUILayout.Label("Waiting for wage samples...");
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Space(kYAxisLabelWidth);
            Rect rect = GUILayoutUtility.GetRect(kGraphWidth, kGraphHeight, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(rect, m_WageTexture);
            GUILayout.EndHorizontal();
            DrawYAxisLabels(rect, m_LastWageMin, m_LastWageMax);

            if (m_LatestWageSample.HasValue)
            {
                string summary = BuildWageSummary(m_LatestWageSample.Value, includeAllLevels: false);
                if (!string.IsNullOrEmpty(summary))
                {
                    GUILayout.Label($"Latest wage: {summary}");
                }
            }
        }

        private void DrawPriceSection()
        {
            GUILayout.Label("Product prices", GUI.skin.label);
            if (!DrawResourceSelectionUI(true, out Resource resource))
            {
                return;
            }

            EconomyAnalyticsRecorder.Instance.CopyPriceSamples(resource, m_PriceSamples);
            if (!RefreshPriceTexture())
            {
                GUILayout.Label("No price samples recorded yet.");
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Space(kYAxisLabelWidth);
            Rect rect = GUILayoutUtility.GetRect(kGraphWidth, kGraphHeight, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(rect, m_PriceTexture);
            GUILayout.EndHorizontal();
            DrawYAxisLabels(rect, m_LastPriceMin, m_LastPriceMax);

            if (m_HasLatestPrice)
            {
                GUILayout.Label($"Latest price: {m_LatestPriceValue:F2}");
            }
        }

        private void DrawResourceFilterUI()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter:", GUILayout.Width(50f));
            string newFilter = GUILayout.TextField(m_ResourceFilter ?? string.Empty, GUILayout.Width(180f));
            if (!string.Equals(newFilter, m_ResourceFilter, StringComparison.Ordinal))
            {
                m_ResourceFilter = newFilter;
            }
            if (GUILayout.Button("Clear", GUILayout.Width(60f)))
            {
                m_ResourceFilter = string.Empty;
            }
            GUILayout.EndHorizontal();
        }

        private bool DrawResourceSelectionUI(bool includeFilter, out Resource resource)
        {
            resource = default;

            EconomyAnalyticsRecorder.Instance.CopyTrackedResources(m_TrackedResources);
            if (m_TrackedResources.Count == 0)
            {
                GUILayout.Label("Waiting for product price samples...");
                return false;
            }

            m_TrackedResources.Sort();

            if (includeFilter)
            {
                DrawResourceFilterUI();
            }

            RefreshFilteredResources();
            if (m_FilteredResources.Count == 0)
            {
                GUILayout.Label("No products match the current filter.");
                return false;
            }

            int count = m_FilteredResources.Count;
            m_SelectedResourceIndex = Mathf.Clamp(m_SelectedResourceIndex, 0, count - 1);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(28f)))
            {
                m_SelectedResourceIndex = (m_SelectedResourceIndex - 1 + count) % count;
            }

            Resource current = m_FilteredResources[m_SelectedResourceIndex];
            GUILayout.Label($"Product: {current}", GUILayout.ExpandWidth(true));

            if (GUILayout.Button(">", GUILayout.Width(28f)))
            {
                m_SelectedResourceIndex = (m_SelectedResourceIndex + 1) % count;
                current = m_FilteredResources[m_SelectedResourceIndex];
            }
            GUILayout.EndHorizontal();

            resource = current;
            m_LastSelectedResource = current;
            return true;
        }

        private void RefreshFilteredResources()
        {
            m_FilteredResources.Clear();
            StringComparison comparison = StringComparison.OrdinalIgnoreCase;
            foreach (var resource in m_TrackedResources)
            {
                if (string.IsNullOrWhiteSpace(m_ResourceFilter) || resource.ToString().IndexOf(m_ResourceFilter, comparison) >= 0)
                {
                    m_FilteredResources.Add(resource);
                }
            }

            if (m_LastSelectedResource.HasValue)
            {
                int index = m_FilteredResources.IndexOf(m_LastSelectedResource.Value);
                if (index >= 0)
                {
                    m_SelectedResourceIndex = index;
                }
            }
        }

        private bool RefreshWageTexture()
        {
            ClearPixels(m_WagePixels);

            if (m_WageSamples.Count == 0)
            {
                m_LatestWageSample = null;
                m_LastWageMin = 0f;
                m_LastWageMax = 0f;
                ApplyTexture(m_WageTexture, m_WagePixels);
                return false;
            }

            int startIndex = GetStartIndex(m_WageSamples, CurrentWindowDuration(m_TimeWindowIndex));
            if (startIndex >= m_WageSamples.Count)
            {
                startIndex = m_WageSamples.Count - 1;
            }

            m_LatestWageSample = m_WageSamples[^1];

            int selectedLevel = Mathf.Clamp(m_SelectedWageLevelIndex, 0, s_WageLevelNames.Length - 1);
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = startIndex; i < m_WageSamples.Count; i++)
            {
                float value = GetWageValue(m_WageSamples[i], selectedLevel);
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }

            if (Mathf.Abs(max - min) < 0.01f)
            {
                max = min + 0.01f;
            }

            m_LastWageMin = min;
            m_LastWageMax = max;

            DrawGridLines(m_WagePixels);

            int sampleCount = m_WageSamples.Count - startIndex;
            if (sampleCount <= 0)
            {
                ApplyTexture(m_WageTexture, m_WagePixels);
                return false;
            }



            float firstTime = m_WageSamples[startIndex].Time;
            float lastTime = m_WageSamples[^1].Time;
            float timeRange = Mathf.Max(lastTime - firstTime, 0.001f);
            bool useIndexFallback = timeRange <= 0.0015f;
            float maxIndex = Mathf.Max(1, sampleCount - 1);

            bool hasPrev = false;
            int prevX = 0;
            int prevY = 0;
            int colorIndex = Mathf.Clamp(selectedLevel, 0, s_WageColors.Length - 1);
            Color32 lineColor = s_WageColors[colorIndex];

            for (int i = 0; i < sampleCount; i++)
            {
                var sample = m_WageSamples[startIndex + i];
                float value = GetWageValue(sample, selectedLevel);
                float normalized = useIndexFallback
                    ? i / maxIndex
                    : Mathf.Clamp01((sample.Time - firstTime) / timeRange);
                int x = Mathf.Clamp(Mathf.RoundToInt(normalized * (kGraphWidth - 1)), 0, kGraphWidth - 1);
                int y = ValueToPixelY(value, min, max);

                if (hasPrev)
                {
                    DrawLine(m_WagePixels, prevX, prevY, x, y, lineColor);
                }
                else
                {
                    SetPixel(m_WagePixels, x, y, lineColor);
                    hasPrev = true;
                }

                prevX = x;
                prevY = y;
            }

            if (hasPrev)
            {
                DrawMarker(m_WagePixels, prevX, prevY, lineColor);
            }

            ApplyTexture(m_WageTexture, m_WagePixels);
            return true;
        }

        private bool RefreshPriceTexture()
        {
            ClearPixels(m_PricePixels);

            if (m_PriceSamples.Count == 0)
            {
                m_HasLatestPrice = false;
                m_LastPriceMin = 0f;
                m_LastPriceMax = 0f;
                ApplyTexture(m_PriceTexture, m_PricePixels);
                return false;
            }

            int startIndex = GetStartIndex(m_PriceSamples, CurrentWindowDuration(m_TimeWindowIndex));
            if (startIndex >= m_PriceSamples.Count)
            {
                startIndex = m_PriceSamples.Count - 1;
            }

            m_HasLatestPrice = true;
            m_LatestPriceValue = m_PriceSamples[^1].Price;

            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = startIndex; i < m_PriceSamples.Count; i++)
            {
                float price = m_PriceSamples[i].Price;
                min = Mathf.Min(min, price);
                max = Mathf.Max(max, price);
            }

            if (Mathf.Abs(max - min) < 0.01f)
            {
                max = min + 0.01f;
            }

            m_LastPriceMin = min;
            m_LastPriceMax = max;

            DrawGridLines(m_PricePixels);

            int sampleCount = m_PriceSamples.Count - startIndex;
            if (sampleCount <= 0)
            {
                ApplyTexture(m_PriceTexture, m_PricePixels);
                return false;
            }

            float firstTime = m_PriceSamples[startIndex].Time;
            float lastTime = m_PriceSamples[^1].Time;
            float timeRange = Mathf.Max(lastTime - firstTime, 0.001f);
            bool useIndexFallback = timeRange <= 0.0015f;
            float maxIndex = Mathf.Max(1, sampleCount - 1);

            bool hasPrev = false;
            int prevX = 0;
            int prevY = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                var sample = m_PriceSamples[startIndex + i];
                float price = sample.Price;
                float normalized = useIndexFallback
                    ? i / maxIndex
                    : Mathf.Clamp01((sample.Time - firstTime) / timeRange);
                int x = Mathf.Clamp(Mathf.RoundToInt(normalized * (kGraphWidth - 1)), 0, kGraphWidth - 1);
                int y = ValueToPixelY(price, min, max);

                if (hasPrev)
                {
                    DrawLine(m_PricePixels, prevX, prevY, x, y, s_PriceColor);
                }
                else
                {
                    SetPixel(m_PricePixels, x, y, s_PriceColor);
                    hasPrev = true;
                }

                prevX = x;
                prevY = y;
            }

            if (hasPrev)
            {
                DrawMarker(m_PricePixels, prevX, prevY, s_PriceColor);
            }

            ApplyTexture(m_PriceTexture, m_PricePixels);
            return true;
        }

        private static float GetWageValue(EconomyAnalyticsRecorder.WageSample sample, int level)
        {
            return level switch
            {
                0 => sample.Level0,
                1 => sample.Level1,
                2 => sample.Level2,
                3 => sample.Level3,
                4 => sample.Level4,
                _ => sample.Level0
            };
        }

        private static Texture2D CreateTexture()
        {
            return new Texture2D(kGraphWidth, kGraphHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private static int GetStartIndex<T>(List<T> samples, float windowSeconds) where T : struct
        {
            if (windowSeconds <= 0f || samples.Count == 0)
            {
                return 0;
            }

            float minTime;
            if (typeof(T) == typeof(EconomyAnalyticsRecorder.WageSample))
            {
                float lastTime = ((EconomyAnalyticsRecorder.WageSample)(object)samples[^1]).Time;
                minTime = lastTime - windowSeconds;
                int index = 0;
                while (index < samples.Count && ((EconomyAnalyticsRecorder.WageSample)(object)samples[index]).Time < minTime)
                {
                    index++;
                }

                return index;
            }

            if (typeof(T) == typeof(EconomyAnalyticsRecorder.PriceSample))
            {
                float lastTime = ((EconomyAnalyticsRecorder.PriceSample)(object)samples[^1]).Time;
                minTime = lastTime - windowSeconds;
                int index = 0;
                while (index < samples.Count && ((EconomyAnalyticsRecorder.PriceSample)(object)samples[index]).Time < minTime)
                {
                    index++;
                }

                return index;
            }

            return 0;
        }

        private static void ClearPixels(Color32[] pixels)
        {
            var background = new Color32(18, 18, 18, 220);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = background;
            }
        }

        private static void ApplyTexture(Texture2D texture, Color32[] pixels)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        private static void SetPixel(Color32[] pixels, int x, int y, Color32 color)
        {
            if (x < 0 || x >= kGraphWidth || y < 0 || y >= kGraphHeight)
            {
                return;
            }

            int index = y * kGraphWidth + x;
            if (index >= 0 && index < pixels.Length)
            {
                pixels[index] = color;
            }
        }

        private static void DrawMarker(Color32[] pixels, int x, int y, Color32 color)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    SetPixel(pixels, x + dx, y + dy, color);
                }
            }
        }

        private static void DrawLine(Color32[] pixels, int x0, int y0, int x1, int y1, Color32 color)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                SetPixel(pixels, x0, y0, color);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }
                int err2 = err * 2;
                if (err2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (err2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private static int ValueToPixelY(float value, float min, float max)
        {
            float norm = Mathf.InverseLerp(min, max, value);
            return Mathf.Clamp(Mathf.RoundToInt((1f - norm) * (kGraphHeight - 1)), 0, kGraphHeight - 1);
        }

        private static void DrawGridLines(Color32[] pixels)
        {
            const int divisions = 4;
            for (int i = 1; i < divisions; i++)
            {
                int y = Mathf.RoundToInt((1f - i / (float)divisions) * (kGraphHeight - 1));
                DrawLine(pixels, 0, y, kGraphWidth - 1, y, s_GridColor);
            }
        }

        public void NotifyHotkeyRebound(KeyCode key)
        {
            m_StatusMessage = $"Overlay hotkey set to {EconomyAnalyticsConfig.GetKeyDisplayName(key)}";
            m_StatusMessageTimer = kStatusMessageDuration;
        }

        public void NotifyHotkeyCaptureCancelled()
        {
            m_StatusMessage = "Hotkey capture cancelled.";
            m_StatusMessageTimer = 2f;
        }

        private void DrawYAxisLabels(Rect rect, float min, float max)
        {
            if (rect.width <= 0f)
            {
                return;
            }

            float mid = (min + max) * 0.5f;
            GUI.Label(new Rect(rect.x - kYAxisLabelWidth, rect.y - 4f, kYAxisLabelWidth - 5f, 18f), max.ToString("F2"), m_RightAlignedLabel);
            GUI.Label(new Rect(rect.x - kYAxisLabelWidth, rect.y + rect.height * 0.5f - 9f, kYAxisLabelWidth - 5f, 18f), mid.ToString("F2"), m_RightAlignedLabel);
            GUI.Label(new Rect(rect.x - kYAxisLabelWidth, rect.y + rect.height - 16f, kYAxisLabelWidth - 5f, 18f), min.ToString("F2"), m_RightAlignedLabel);
        }

    }
}
