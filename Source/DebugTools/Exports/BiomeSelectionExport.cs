using System.Text;
using LudeonTK;
using RimWorld;
using Verse;
using Planets.WorldGen;

namespace Planets.DebugTools.Exports
{
    public static class BiomeSelectionExport
    {
        [DebugAction(DebugActionBase.CATEGORY_EXPORTS, "Export Biome Selection", allowedGameStates = AllowedGameStates.WorldRenderedNow)]
        public static void Export()
        {
            if (string.IsNullOrEmpty(WorldGenerationOrchestrator.LastSelectionReport))
            {
                Messages.Message("No biome selection summary yet. Generate a world first.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            var body = new StringBuilder(WorldGenerationOrchestrator.LastSelectionReport);
            ExportWriter.WriteAll("RP2_BiomeSelection.csv", body);
            Messages.Message("Wrote RP2_BiomeSelection.csv.", MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
