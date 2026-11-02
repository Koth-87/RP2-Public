using System.Collections;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;
using Planets.DebugTools;
using Planets.Utilities.Logging;
using Planets.WorldGen.Climate;

namespace Planets.DebugTools.Exports
{
    public static class MoistureVsDistanceExport
    {
        [DebugAction(DebugActionBase.CATEGORY_EXPORTS, "Export Moisture vs Distance", allowedGameStates = AllowedGameStates.WorldRenderedNow)]
        public static void Export()
        {
            if (!Climate.IsInitialized())
            {
                Messages.Message("Climate system not initialized - generate a world first", MessageTypeDefOf.RejectInput);
                return;
            }
            LongEventHandler.QueueLongEvent(Write(), "Exporting moisture", exception => RPLog.E($"Moisture export failed: {exception}"));
        }

        private static IEnumerable Write()
        {
            ClimateData climate = Find.World?.GetComponent<ClimateData>();
            int count = Find.WorldGrid.TilesCount;
            var sb = new StringBuilder();
            sb.AppendLine("# oceanDistKm is distance to the true ocean. Lakes are not ocean.");
            sb.AppendLine("# distanceAlongWindKm is downwind travel distance from the true ocean along the annual carrying wind.");
            sb.AppendLine("# inlandWaterStrength is the strongest enclosed body's shore influence, before the continentality weight.");
            sb.AppendLine("tileID,oceanDistKm,distanceAlongWindKm,inlandWaterDistKm,inlandWaterStrength,incoming,condensation,remaining");
            for (int i = 0; i < count; i++)
            {
                sb.Append(i).Append(',')
                    .Append(Value(climate?.OceanDistKm, i)).Append(',')
                    .Append(Dist(climate?.DistanceAlongWindKm, i)).Append(',')
                    .Append(Dist(climate?.InlandWaterDistKm, i)).Append(',')
                    .Append(Value(climate?.InlandWaterInfluence, i)).Append(',')
                    .Append(Value(climate?.AdvectedIncomingMoistureMm, i)).Append(',')
                    .Append(Value(climate?.AdvectedCondensationMm, i)).Append(',')
                    .Append(Value(climate?.AdvectedRemainingMoistureMm, i))
                    .AppendLine();
                if (i % 5000 == 0)
                    yield return null;
            }
            ExportWriter.WriteAll("MoistureVsDistance.csv", sb, false);
        }

        private static string Dist(float[] values, int tile)
        {
            if (values == null || tile < 0 || tile >= values.Length || values[tile] < 0f)
                return "";
            return values[tile].ToString("F1");
        }

        private static string Value(float[] values, int tile)
        {
            if (values == null || tile < 0 || tile >= values.Length)
                return "";
            return values[tile].ToString("F1");
        }
    }
}
