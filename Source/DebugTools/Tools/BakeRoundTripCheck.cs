using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Planets.WorldGen.Climate;

namespace Planets.DebugTools.Tools
{
    /// <summary>
    /// Confirms the bake codecs are stable: applying them again changes nothing.
    /// A generated world should already be on those steps.
    /// </summary>
    public static class BakeRoundTripCheck
    {
        [DebugAction(DebugActionBase.CATEGORY_TOOLS, "RP2: bake round-trip check", allowedGameStates = AllowedGameStates.WorldRenderedNow)]
        public static void Check()
        {
            PlanetLayer layer = Find.WorldGrid?.Surface;
            ClimateData climate = Find.World?.GetComponent<ClimateData>();
            if (layer?.Tiles == null || climate == null)
            {
                Messages.Message("No Realistic Planets climate data on this world.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            int elevation = 0;
            int temperature = 0;
            int rainfall = 0;
            int swampiness = 0;
            int tiles = layer.Tiles.Count;
            for (int i = 0; i < tiles; i++)
            {
                Tile tile = layer.Tiles[i];
                if (tile == null)
                    continue;
                if (tile.elevation != BakeQuantizer.Elevation(tile.elevation, tile.WaterCovered))
                    elevation++;
                if (tile.temperature != BakeQuantizer.Temperature(tile.temperature))
                    temperature++;
                if (tile.rainfall != BakeQuantizer.Rainfall(tile.rainfall))
                    rainfall++;
                if (tile.swampiness != BakeQuantizer.Swampiness(tile.swampiness))
                    swampiness++;
            }

            int arrays = CountUnstable(climate.SeasonalAmp, BakeQuantizer.SeasonalAmp)
                + CountUnstable(climate.AridityIndex, BakeQuantizer.Thousandths)
                + CountUnstable(climate.Continentality, BakeQuantizer.UnitByte)
                + CountUnstable(climate.UpwindReliefIndex, BakeQuantizer.Thousandths)
                + CountUnstable(climate.LeewardShadowIndex, BakeQuantizer.Thousandths)
                + CountUnstable(climate.MonsoonBoost, BakeQuantizer.WholeMillimeters)
                + CountUnstable(climate.FlowAccumulationTopology, BakeQuantizer.FlowAccumulation);

            string text = "Bake round-trip. Tiles off-step: elevation " + elevation
                + ", temperature " + temperature
                + ", rainfall " + rainfall
                + ", swampiness " + swampiness
                + ". Gameplay values off-step: " + arrays + ".";
            Messages.Message(text, elevation + temperature + rainfall + swampiness + arrays == 0
                ? MessageTypeDefOf.TaskCompletion
                : MessageTypeDefOf.CautionInput, false);
        }

        private static int CountUnstable(float[] values, System.Func<float, float> encode)
        {
            if (values == null)
                return 0;
            int count = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != encode(values[i]))
                    count++;
            }
            return count;
        }
    }
}
