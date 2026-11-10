using System.Collections;
using System.Diagnostics;
using RimWorld.Planet;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.WorldGen.Climate
{
    /// <summary>
    /// Rebuilds non-saved diagnostic and map-mode overlay arrays from the world's <see cref="ClimateBakeRecord"/>
    /// and saved exact elevations after loading a save game. Gameplay tile values and the saved gameplay pack are never modified.
    /// </summary>
    public static class ClimateDiagnostics
    {
        public static string StalenessBanner;
        public static bool Building { get; private set; }

        public static void Clear()
        {
            StalenessBanner = null;
            Building = false;
        }

        public static void NoteLegacySave()
        {
            StalenessBanner = null;
        }

        public static void NoteLoadedBake(ClimateData data)
        {
            ClimateBakeRecord record = data?.bakeRecord;
            if (record == null)
            {
                StalenessBanner = null;
                return;
            }
            if (record.modelVersion != ClimateModelVersion.Current)
            {
                StalenessBanner = "This world's climate was baked by an older climate model. Overlays show the current model. Use Recalculate climate to update the world.";
                return;
            }
            int hash = ClimateBakeRecord.HashGeography(Find.WorldGrid?.Surface, data.ExactElevationMeters);
            if (record.inputHash != 0 && hash != record.inputHash)
            {
                StalenessBanner = "Geography changed since the climate was baked. Use Recalculate climate to update.";
                return;
            }
            StalenessBanner = null;
        }

        public static void QueueRebuild(ClimateData target)
        {
            if (target == null || Building || Find.WorldGrid?.Surface == null)
                return;
            Building = true;
            LongEventHandler.QueueLongEvent(Rebuild(target), "LoadingWorld", ex =>
            {
                Building = false;
                RPLog.E($"Climate overlay rebuild failed: {ex}");
            });
        }

        private static IEnumerable Rebuild(ClimateData target)
        {
            LongEventHandler.SetCurrentEventText("Building climate overlays…");
            yield return null;

            Stopwatch watch = Stopwatch.StartNew();
            PlanetLayer surface = Find.WorldGrid.Surface;
            Planets_GameComponent.SyncEarthScaleFromSurface();
            if (target.bakeRecord != null)
            {
                Planets_GameComponent.axialTilt = target.bakeRecord.axialTilt;
                Planets_GameComponent.seaLevel = target.bakeRecord.seaLevel;
                Planets.WorldGen.Astronomy.AxialTiltCurves.SetSeasonalCurve();
            }
            bool lifted = false;
            try
            {
                lifted = target.ApplyExactElevations(surface);
                int count = Find.WorldGrid.TilesCount;
                var features = Controller.Settings?.featureConfig;
                bool includeTier2 = features != null && features.IsTier2HydrologyEnabled;
                var scratch = new ClimateData(Find.World);
                scratch.InitializeArrays(count, includeTier2);
                scratch.PoleMean = target.PoleMean;
                scratch.EquatorMean = target.EquatorMean;
                scratch.BaseRain = target.BaseRain;
                scratch.EarthGeography = target.bakeRecord?.earthHeightmap == true;
                var orchestrator = new ClimateOrchestrator(
                    scratch,
                    surface,
                    target.bakeRecord?.climateSettings,
                    features,
                    target.bakeRecord?.hydrologySettings);
                foreach (object step in orchestrator.ComputeSteps())
                    yield return step;

                BakeQuantizer.QuantizeGameplayPackArrays(scratch);
                LogPackMismatch(target, scratch);
                target.CopyDiagnosticsFrom(scratch);
                Planets.API.RealisticPlanetsAPI.LogContract();
            }
            finally
            {
                if (lifted)
                    BakeQuantizer.QuantizeElevations(surface, null);
                watch.Stop();
                Building = false;
                if (Controller.DebugEnabled)
                    RPLog.D($"[Climate] Overlay rebuild took {watch.Elapsed.TotalSeconds:F1}s.");
            }
        }

        private static void LogPackMismatch(ClimateData saved, ClimateData rebuilt)
        {
            if (!Controller.DebugEnabled || saved.SeasonalAmp == null || rebuilt.SeasonalAmp == null)
                return;
            if (saved.bakeRecord == null || saved.bakeRecord.modelVersion != ClimateModelVersion.Current)
                return;

            int n = saved.SeasonalAmp.Length < rebuilt.SeasonalAmp.Length ? saved.SeasonalAmp.Length : rebuilt.SeasonalAmp.Length;
            PlanetLayer layer = Find.WorldGrid?.Surface;
            int mismatches = 0;
            int contDiff = 0;
            int aridDiff = 0;
            float maxAbs = 0f;
            const int detailCap = 400;

            for (int i = 0; i < n; i++)
            {
                float savedAmp = saved.SeasonalAmp[i];
                float rebuiltAmp = rebuilt.SeasonalAmp[i];
                float dAmp = rebuiltAmp - savedAmp;
                if (UnityEngine.Mathf.Abs(dAmp) <= 0.015f)
                    continue;

                mismatches++;
                float abs = UnityEngine.Mathf.Abs(dAmp);
                if (abs > maxAbs)
                    maxAbs = abs;

                float savedCont = At(saved.Continentality, i);
                float rebuiltCont = At(rebuilt.Continentality, i);
                float savedArid = At(saved.AridityIndex, i);
                float rebuiltArid = At(rebuilt.AridityIndex, i);
                if (UnityEngine.Mathf.Abs(rebuiltCont - savedCont) > 0.5f / 255f)
                    contDiff++;
                if (UnityEngine.Mathf.Abs(rebuiltArid - savedArid) > 0.0005f)
                    aridDiff++;

                if (mismatches > detailCap)
                    continue;

                Tile tile = layer != null && i < layer.Tiles.Count ? layer.Tiles[i] : null;
                float lat = Find.WorldGrid != null ? Find.WorldGrid.LongLatOf(i).y : 0f;
                float elev = tile != null ? tile.elevation : 0f;
                string biome = tile?.biome != null ? tile.biome.defName : "none";
                RPLog.D($"[Climate] Tile {i} lat {lat:F1} elev {elev:F1} biome {biome} amp {savedAmp:F2} -> {rebuiltAmp:F2} (d {dAmp:+0.00;-0.00}) cont {savedCont:F3} -> {rebuiltCont:F3} arid {savedArid:F3} -> {rebuiltArid:F3}");
            }

            if (mismatches == 0)
                return;

            RPLog.W($"[Climate] Rebuilt seasonal amplitude differs on {mismatches} tiles. Continentality also differs on {contDiff}, aridity on {aridDiff}. Largest swing change {maxAbs:F2}°C.");
            if (mismatches > detailCap)
                RPLog.W($"[Climate] Logged the first {detailCap} differing tiles.");
        }

        private static float At(float[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length ? values[index] : 0f;
        }
    }
}
