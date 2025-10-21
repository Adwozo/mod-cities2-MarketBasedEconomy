using System;
using System.IO;
using UnityEngine;

namespace MarketBasedEconomy.Diagnostics
{
    internal static class DiagnosticsLogger
    {
        private static readonly object s_Lock = new();
        private static string s_LogFilePath;
        private static bool s_Initialized;

        public static bool Enabled { get; set; }

        public static void Initialize()
        {
            if (s_Initialized)
            {
                return;
            }

            try
            {
                string folder = Path.Combine(Application.persistentDataPath, nameof(MarketBasedEconomy));
                Directory.CreateDirectory(folder);
                s_LogFilePath = Path.Combine(folder, "MarketEconomy.log");
                File.WriteAllText(s_LogFilePath, $"[{DateTime.Now:O}] MarketBasedEconomy diagnostics log started{Environment.NewLine}");
                s_Initialized = true;
            }
            catch (Exception ex)
            {
                Mod.log.Warn(ex, "Unable to initialize diagnostics log file");
            }
        }

        public static void Log(string message)
        {
            if (!Enabled || !s_Initialized)
            {
                return;
            }

            try
            {
                lock (s_Lock)
                {
                    File.AppendAllText(s_LogFilePath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                Mod.log.Warn(ex, "Failed to write diagnostics log entry");
                Enabled = false;
            }
        }
    }
}

