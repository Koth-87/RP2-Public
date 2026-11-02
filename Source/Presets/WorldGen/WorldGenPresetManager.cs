using System.Collections.Generic;
using UnityEngine;
using Planets.Compatibility;
using Planets.Core;
using Planets.Core.Configuration;
using Planets.UI.Pages;
using Planets.Utilities.Serialization;
using Planets.WorldGen.Astronomy;

namespace Planets.Presets.WorldGen
{
    /// <summary>
    /// Manages saving, loading, deleting, and applying <see cref="WorldGenPreset"/> files.
    /// Backed by <see cref="XmlPresetStore{T}"/> in <c>RealisticPlanets_WorldGenPresets</c>.
    /// </summary>
    public static class WorldGenPresetManager
    {
        private static readonly XmlPresetStore<WorldGenPreset> Store = new XmlPresetStore<WorldGenPreset>(
            "RealisticPlanets_WorldGenPresets",
            "WorldGenPreset",
            p => p.name,
            p => p.savedDateTicks);

        public static List<WorldGenPreset> GetAllPresets(bool forceRefresh = false) => Store.GetAll(forceRefresh);

        public static bool SavePreset(WorldGenPreset preset) => Store.Save(preset);

        public static WorldGenPreset LoadPreset(string name) => Store.Load(name);

        public static bool DeletePreset(string name)
        {
            return Store.Delete(name, () =>
            {
                if (Controller.Settings?.defaultWorldGenPreset?.CurrentValue == name)
                {
                    Controller.Settings.defaultWorldGenPreset.CurrentValue = null;
                    Controller.Instance?.WriteSettings();
                }
            });
        }

        public static bool PresetExists(string name) => Store.Exists(name);

        public static WorldGenPreset CaptureFrom(Page_CreateWorldParamsRP page)
        {
            if (page == null)
                return null;

            var preset = new WorldGenPreset
            {
                planetName = Planets_GameComponent.planetName,
                seedString = page.seedString,
                subcount = Planets_GameComponent.subcount,
                riverDensity = Planets_GameComponent.riverDensity,
                ancientRoadDensity = Planets_GameComponent.ancientRoadDensity,
                factionRoadDensity = Planets_GameComponent.factionRoadDensity,
                planetCoverage = page.planetCoverage,
                seaLevel = Planets_GameComponent.seaLevel,
                rainfall = page.rainfall,
                temperature = page.temperature,
                population = page.population,
                pollution = page.pollution,
                landmarkDensity = page.landmarkDensity,
                axialTilt = Planets_GameComponent.axialTilt,
                mountains = page.Mountains,
                caveSystems = page.CaveSystems,
                insectTerritoryScale = page.InsectTerritoryScale
            };

            if (Controller.Settings?.climateConfig != null)
                preset.climateConfig.CopyFrom(Controller.Settings.climateConfig);
            if (Controller.Settings?.hydrologyConfig != null)
                preset.hydroConfig.CopyFrom(Controller.Settings.hydrologyConfig);

            return preset;
        }

        public static void ApplyTo(WorldGenPreset preset, Page_CreateWorldParamsRP page, bool isAutoLoad = false)
        {
            if (preset == null || page == null)
                return;

            if (!isAutoLoad && preset.includeSeedAndName)
            {
                Planets_GameComponent.planetName = preset.planetName ?? "";
                if (!string.IsNullOrEmpty(preset.seedString))
                    page.seedString = preset.seedString;
            }
            Planets_GameComponent.subcount = Mathf.Clamp(preset.subcount, 5, 11);
            Planets_GameComponent.riverDensity = preset.riverDensity;
            Planets_GameComponent.ancientRoadDensity = preset.ancientRoadDensity;
            Planets_GameComponent.factionRoadDensity = preset.factionRoadDensity;
            page.planetCoverage = preset.planetCoverage;
            Planets_GameComponent.seaLevel = preset.seaLevel;
            page.rainfall = preset.rainfall;
            page.temperature = preset.temperature;
            page.population = preset.population;
            page.pollution = preset.pollution;
            page.landmarkDensity = preset.landmarkDensity;
            Planets_GameComponent.axialTilt = preset.axialTilt;
            page.Mountains = preset.mountains;
            page.CaveSystems = preset.caveSystems;
            page.InsectTerritoryScale = preset.insectTerritoryScale;

            if (Controller.Settings?.climateConfig != null)
            {
                ClimateConfiguration live = Controller.Settings.climateConfig;
                bool earth = live.useEarthHeightmap.CurrentValue;
                string earthPath = live.earthHeightmapPath.CurrentValue;
                live.CopyFrom(preset.climateConfig);
                live.useEarthHeightmap.CurrentValue = earth;
                live.earthHeightmapPath.CurrentValue = earthPath;
            }
            if (Controller.Settings?.hydrologyConfig != null)
            {
                HydrologyConfiguration hydro = Controller.Settings.hydrologyConfig;
                HydrologyConfiguration kept = new HydrologyConfiguration();
                kept.CopyFrom(hydro);
                hydro.CopyFrom(preset.hydroConfig);
                hydro.CopyFeatureTogglesFrom(kept);
            }
            if (preset.hillinessBias != 0f
                && Controller.Settings?.climateConfig != null
                && Mathf.Approximately(Controller.Settings.climateConfig.mountainousness.CurrentValue, 1f))
            {
                Controller.Settings.climateConfig.mountainousness.CurrentValue =
                    Mathf.Clamp(1f + preset.hillinessBias, 0f, 2f);
            }

            Planets_GameComponent.worldPreset = "Planets.Custom";
            AxialTiltCurves.SetSeasonalCurve();

            if (GeologicalLandformsCompat.IsEnabled)
                GeologicalLandformsCompat.ApplyMountainCaveValues(preset.mountains, preset.caveSystems);
        }
    }
}
