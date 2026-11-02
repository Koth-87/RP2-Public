using System;
using System.Diagnostics;
using Verse;
using Planets.Core;

namespace Planets.Utilities.Logging
{
    /// <summary>
    /// Centralized logging facade for Realistic Planets.
    /// All debug, warning, and error messages should go through this system.
    /// </summary>
    public static class RPLog
    {
        private static Stopwatch timer = new Stopwatch();
        private static bool timerStarted = false;
        
        /// <summary>
        /// Verbose log gate. Same rule as Controller.DebugEnabled: RimWorld Dev Mode and the mod Debug Mode checkbox.
        /// </summary>
        public static bool DebugEnabled => Controller.DebugEnabled;
        
        /// <summary>
        /// Start the elapsed time timer (call at worldgen start).
        /// </summary>
        public static void StartTimer()
        {
            timer.Restart();
            timerStarted = true;
        }
        
        /// <summary>
        /// Get elapsed time since timer started.
        /// </summary>
        private static string GetElapsedTime()
        {
            if (!timerStarted)
                return "";
            
            var elapsed = timer.Elapsed;
            return $"[+{elapsed.TotalSeconds:F1}s] ";
        }

        /// <summary>
        /// Debug message - only shows when debug is enabled.
        /// </summary>
        public static void D(string msg)
        {
            if (DebugEnabled)
                Log.Message("[RP] " + GetElapsedTime() + msg);
        }

        /// <summary>
        /// Warning message - only shows when debug is enabled.
        /// </summary>
        public static void W(string msg)
        {
            if (DebugEnabled)
                Log.Warning("[RP] " + GetElapsedTime() + msg);
        }

        /// <summary>
        /// Error message - always visible.
        /// </summary>
        public static void E(string msg)
        {
            Log.Error("[RP] " + GetElapsedTime() + msg);
        }

        /// <summary>
        /// Error message that only logs once per key.
        /// </summary>
        public static void ErrorOnce(string msg, int key)
        {
            Log.ErrorOnce("[RP] " + GetElapsedTime() + msg, key);
        }

        // Long-form aliases for consistency with newer code
        public static void Debug(string msg) => D(msg);
        public static void Warning(string msg) => W(msg);
        public static void Error(string msg) => E(msg);
        public static void Message(string msg) => D(msg);
        
        /// <summary>
        /// Initialization and worldgen messages.
        /// </summary>
        public static void Init(string msg)
        {
            D("[Init] " + msg);
        }

        /// <summary>
        /// Climate calculation and cache messages.
        /// </summary>
        public static void Climate(string msg)
        {
            D("[Climate] " + msg);
        }

        /// <summary>
        /// Export operation messages.
        /// </summary>
        public static void Export(string msg)
        {
            D("[Export] " + msg);
        }

        /// <summary>
        /// Mod compatibility messages.
        /// </summary>
        public static void Compat(string msg)
        {
            D("[Compat] " + msg);
        }

        /// <summary>
        /// Performance and profiling messages.
        /// </summary>
        public static void Perf(string msg)
        {
            D("[Perf] " + msg);
        }

        /// <summary>
        /// Astronomy/sun position debug (day-night bug tracking). Only when debug enabled.
        /// </summary>
        public static void Astronomy(string msg)
        {
            if (DebugEnabled)
                Log.Message("[RP] [Astronomy] " + GetElapsedTime() + msg);
        }

        /// <summary>
        /// Moon rendering and phase debug. Only when debug enabled.
        /// </summary>
        public static void Moon(string msg)
        {
            if (DebugEnabled)
                Log.Message("[RP] [Moon] " + GetElapsedTime() + msg);
        }

        /// <summary>
        /// True when astronomy debug logging should run (debug enabled and playing).
        /// </summary>
        public static bool ShouldLogAstronomy() => DebugEnabled;
    }
}
