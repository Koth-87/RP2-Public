using System;
using System.Collections.Generic;

namespace Planets.Biomes.Core
{
    /// <summary>
    /// Static registry of which scoring variables each of the 23 core RP2 biome workers uses.
    /// Used to initialize default variable toggles in the Biome Editor and identify biomes that use placement noise.
    /// </summary>
    public static class BiomeConditionRegistry
    {
        public const string Temperature = "Temperature";
        public const string Rainfall = "Rainfall";
        public const string Aridity = "Aridity";
        public const string Elevation = "Elevation";
        public const string DualElevation = "DualElevation";
        public const string Swampiness = "Swampiness";
        public const string Hilliness = "Hilliness";
        public const string Continentality = "Continentality";
        public const string Seasonality = "Seasonality";
        public const string Windward = "Windward";
        public const string Leeward = "Leeward";
        public const string FlowAccumulation = "FlowAccumulation";
        public const string SummerMax = "SummerMax";
        public const string WinterMin = "WinterMin";
        public const string MonsoonBoost = "MonsoonBoost";
        public const string HostRestriction = "HostRestriction";
        public const string Noise = "Noise";

        public static readonly HashSet<string> MinimumRequiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Temperature, Rainfall, Aridity, Elevation
        };

        private static Dictionary<string, HashSet<string>> _byDefName;

        private static void EnsureBuilt()
        {
            if (_byDefName != null)
                return;
            _byDefName = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            void Add(string defName, params string[] conditions)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string c in conditions)
                    set.Add(c);
                _byDefName[defName] = set;
            }

            // Vanilla (12)
            Add("TemperateForest", Temperature, Aridity, Elevation, Swampiness, Seasonality, Windward, MonsoonBoost, WinterMin);
            Add("BorealForest", Temperature, Aridity, Elevation, WinterMin, Seasonality, Windward, Swampiness);
            Add("AridShrubland", Temperature, Aridity, Elevation, Leeward, Continentality);
            Add("Desert", Temperature, Aridity, Elevation, DualElevation, SummerMax, Continentality, Leeward);
            Add("TropicalRainforest", Temperature, Aridity, Elevation, DualElevation, WinterMin, Windward, MonsoonBoost, Swampiness);
            Add("SeaIce", Temperature, SummerMax, WinterMin, Elevation);
            Add("IceSheet", Temperature, Rainfall, Elevation, DualElevation, SummerMax, WinterMin);
            Add("ExtremeDesert", Temperature, Aridity, Elevation, DualElevation, SummerMax, Continentality, Leeward);
            Add("ColdBog", Temperature, Aridity, Elevation, WinterMin, Swampiness, FlowAccumulation);
            Add("TemperateSwamp", Temperature, Aridity, Elevation, Swampiness, FlowAccumulation);
            Add("TropicalSwamp", Temperature, Aridity, Elevation, WinterMin, Swampiness, FlowAccumulation);
            Add("Tundra", Temperature, Rainfall, Elevation, DualElevation, SummerMax, Aridity, Continentality);

            // RP2 Custom (6)
            Add("RP_Permafrost", Temperature, Rainfall, Elevation, Continentality, Seasonality, Hilliness, Swampiness);
            Add("RP_ColdDesert", Temperature, Aridity, Elevation, Continentality, Leeward);
            Add("RP_Savanna", Temperature, Aridity, Elevation, WinterMin, Continentality, Seasonality, MonsoonBoost);
            Add("RP_Steppe", Temperature, Aridity, Elevation);
            Add("RP_Prairie", Temperature, Aridity, Elevation, DualElevation, Hilliness, Continentality);
            Add("RP_Oasis", Temperature, Aridity, Elevation, FlowAccumulation, Swampiness, HostRestriction, Noise);

            // Odyssey (5)
            Add("Grasslands", Temperature, Aridity, Elevation, DualElevation, Hilliness, Continentality);
            Add("GlacialPlain", Temperature, Rainfall, Elevation, DualElevation, WinterMin, Seasonality, HostRestriction, Noise);
            Add("LavaField", Temperature, Aridity, Elevation, SummerMax, HostRestriction, Noise);
            Add("Scarlands", Temperature, Aridity, Elevation, Seasonality, HostRestriction, Noise);
            Add("Glowforest", Temperature, Aridity, Elevation, Seasonality, Swampiness);
        }

        public static HashSet<string> GetUsedConditions(string biomeDefName)
        {
            if (string.IsNullOrEmpty(biomeDefName))
                return null;
            EnsureBuilt();
            return _byDefName.TryGetValue(biomeDefName, out HashSet<string> set) ? set : null;
        }

        public static bool UsesNoise(string biomeDefName)
        {
            HashSet<string> set = GetUsedConditions(biomeDefName);
            return set != null && set.Contains(Noise);
        }

        public static bool IsInRegistry(string biomeDefName)
        {
            EnsureBuilt();
            return !string.IsNullOrEmpty(biomeDefName) && _byDefName.ContainsKey(biomeDefName);
        }
    }
}
