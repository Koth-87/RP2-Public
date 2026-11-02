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
                    features);
                foreach (object step in orchestrator.ComputeSteps())
                    yield return step;

                BakeQuantizer.QuantizeGameplayPackArrays(scratch);
                LogPackMismatch(target, scratch);
                target.CopyDiagnosticsFrom(scratch);
                Api.RealisticPlanetsApi.LogContract();
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
            int mismatches = 0;
            int n = saved.SeasonalAmp.Length < rebuilt.SeasonalAmp.Length ? saved.SeasonalAmp.Length : rebuilt.SeasonalAmp.Length;
            for (int i = 0; i < n; i++)
            {
                if (UnityEngine.Mathf.Abs(saved.SeasonalAmp[i] - rebuilt.SeasonalAmp[i]) > 0.015f)
                    mismatches++;
            }
            if (mismatches > 0 && saved.bakeRecord != null && saved.bakeRecord.modelVersion == ClimateModelVersion.Current)
                RPLog.W($"[Climate] Rebuilt seasonal amplitude differs on {mismatches} tiles.");
        }
    }
}
