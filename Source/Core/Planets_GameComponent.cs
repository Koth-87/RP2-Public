using System.Collections.Generic;
using System.Reflection;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Planets.Biomes.Core;
using Planets.Compatibility;
using Planets.WorldGen.Astronomy;
using Planets.WorldGen.Geography;
using Planets.WorldGen.Constants;
using Planets.Utilities.Logging;

namespace Planets.Core
{
    /// <summary>
    /// Persistent storage for world generation parameters chosen during world creation.
    /// 
    /// RimWorld attaches this GameComponent to each world and calls ExposeData() during
    /// save/load cycles. This allows the mod to remember what parameters the user selected
    /// (sea level, rainfall modifier, world preset, etc.).
    /// </summary>
    public class Planets_GameComponent : GameComponent
    {
        /// <summary>
        /// Axial tilt selected during world creation (affects seasonal temperature variation)
        /// </summary>
        public static AxialTilt axialTilt = AxialTilt.Normal;
        
        /// <summary>
        /// Sea level selected during world creation (affects ocean coverage)
        /// </summary>
        public static SeaLevel seaLevel = SeaLevel.Normal;
        
        /// <summary>
        /// World preset name (translation key) selected during world creation
        /// </summary>
        public static string worldPreset = "Planets.Vanilla";
        
        /// <summary>
        /// Subdivision count for My Little Planet integration
        /// </summary>
        public static int subcount = 10;
        
        /// <summary>
        /// Planet name selected during world creation
        /// </summary>
        public static string planetName = "";

        /// <summary>
        /// River density multiplier
        /// </summary>
        public static float riverDensity = 1f;

        /// <summary>
        /// Ancient road density multiplier
        /// </summary>
        public static float ancientRoadDensity = 1f;

        /// <summary>
        /// Faction road density multiplier
        /// </summary>
        public static float factionRoadDensity = 1f;

        /// <summary>True while this page is about to generate a world through Realistic Planets.</summary>
        public static bool generatingWithRP;

        // Snapshot of the generation choices for this game. The static fields above are the
        // world-creation page. InitNewGame copies them here, then resets the statics so the
        // next tutorial or quick test does not inherit this planet.
        private AxialTilt storedAxialTilt = AxialTilt.Normal;
        private SeaLevel storedSeaLevel = SeaLevel.Normal;
        private string storedWorldPreset = "Planets.Vanilla";
        private int storedSubcount = 10;
        private string storedPlanetName = "";
        private float storedRiverDensity = 1f;
        private float storedAncientRoadDensity = 1f;
        private float storedFactionRoadDensity = 1f;
        private bool generationParametersStored;

        /// <summary>Copy the live creation-page values onto this game before the statics are reset.</summary>
        public void CaptureGenerationParameters()
        {
            storedAxialTilt = axialTilt;
            storedSeaLevel = seaLevel;
            storedWorldPreset = worldPreset;
            storedSubcount = subcount;
            storedPlanetName = planetName;
            storedRiverDensity = riverDensity;
            storedAncientRoadDensity = ancientRoadDensity;
            storedFactionRoadDensity = factionRoadDensity;
            generationParametersStored = true;
        }

        /// <summary>Creation-page defaults. Does not change the snapshot already stored on a game.</summary>
        public static void ResetGenerationStatics()
        {
            axialTilt = AxialTilt.Normal;
            seaLevel = SeaLevel.Normal;
            worldPreset = "Planets.Vanilla";
            subcount = 10;
            planetName = "";
            riverDensity = 1f;
            ancientRoadDensity = 1f;
            factionRoadDensity = 1f;
        }

        private static Planets_GameComponent StoredParameters
        {
            get
            {
                Planets_GameComponent component = Current.Game?.GetComponent<Planets_GameComponent>();
                if (component == null || !component.generationParametersStored)
                    return null;
                return component;
            }
        }

        /// <summary>Axial tilt for the current game, or the creation-page value when no game has stored one.</summary>
        public static AxialTilt ActiveAxialTilt => StoredParameters?.storedAxialTilt ?? axialTilt;

        /// <summary>Sea level for the current game, or the creation-page value when no game has stored one.</summary>
        public static SeaLevel ActiveSeaLevel => StoredParameters?.storedSeaLevel ?? seaLevel;

        /// <summary>World preset name for the current game, or the creation-page value when no game has stored one.</summary>
        public static string ActiveWorldPreset => StoredParameters?.storedWorldPreset ?? worldPreset;

        /// <summary>Subdivision count (planet scale 5–11) for the current surface layer, stored game, or creation page.</summary>
        public static int ActiveSubcount
        {
            get
            {
                PlanetLayer surface = Find.WorldGrid?.Surface;
                if (surface != null)
                {
                    FieldInfo subField = typeof(PlanetLayer).GetField("subdivisions", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (subField?.GetValue(surface) is int surfaceSubdivisions && surfaceSubdivisions >= 5 && surfaceSubdivisions <= 11)
                        return surfaceSubdivisions;
                }
                return StoredParameters?.storedSubcount ?? subcount;
            }
        }

        public static void SyncEarthScaleFromSurface()
        {
            EarthScaleConstants.SetForSubdivisions(ActiveSubcount);
        }

        /// <summary>
        /// Def overrides that were active when this world was generated.
        /// Null means an older save that never stored them: leave the XML values alone.
        /// </summary>
        public Dictionary<string, DefOverrideData> defOverrides;

        private List<string> defOverrideKeys;
        private List<DefOverrideData> defOverrideValues;

        public Planets_GameComponent()
        {
        }

        public Planets_GameComponent(Game game)
        {
        }

        /// <summary>Remember the overrides used for the world just generated. Replaces any previous copy.</summary>
        public void RememberDefOverrides(Dictionary<string, DefOverrideData> overrides)
        {
            defOverrides = overrides ?? new Dictionary<string, DefOverrideData>();
        }

        public override void FinalizeInit()
        {
            // Scoring tools follow the current worker settings. Density values follow this world.
            if (Controller.Settings?.biomeConfig != null)
                BiomeWorkerManager.ApplyWorkers(Controller.Settings.biomeConfig);
            BiomeWorkerManager.ApplySavedOverrides(defOverrides);

            SyncEarthScaleFromSurface();
            DynamicAtmosphereCompat.PushTilt();
            Planets.Api.RealisticPlanetsApi.LogContract();
        }

        /// <summary>
        /// Serialize/deserialize world generation parameters.
        /// </summary>
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving && !generationParametersStored)
                CaptureGenerationParameters();

            // Same keys as older saves, which wrote the static fields directly.
            Scribe_Values.Look(ref storedAxialTilt, "axialTilt", AxialTilt.Normal, true);
            Scribe_Values.Look(ref storedSeaLevel, "seaLevel", SeaLevel.Normal, true);
            Scribe_Values.Look(ref storedSubcount, "subcount", 10, true);
            Scribe_Values.Look(ref storedPlanetName, "planetName", "", true);
            Scribe_Values.Look(ref storedRiverDensity, "riverDensity", 1f, true);
            Scribe_Values.Look(ref storedAncientRoadDensity, "ancientRoadDensity", 1f, true);
            Scribe_Values.Look(ref storedFactionRoadDensity, "factionRoadDensity", 1f, true);
            Scribe_Values.Look(ref storedWorldPreset, "worldPreset", "Planets.Vanilla", true);
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                generationParametersStored = true;
                axialTilt = storedAxialTilt;
                seaLevel = storedSeaLevel;
                worldPreset = storedWorldPreset;
                subcount = storedSubcount;
                planetName = storedPlanetName ?? "";
                riverDensity = storedRiverDensity;
                ancientRoadDensity = storedAncientRoadDensity;
                factionRoadDensity = storedFactionRoadDensity;
            }

            Scribe_Collections.Look(ref defOverrides, "defOverrides", LookMode.Value, LookMode.Deep, ref defOverrideKeys, ref defOverrideValues);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                defOverrideKeys = null;
                defOverrideValues = null;
            }
        }
    }
}
