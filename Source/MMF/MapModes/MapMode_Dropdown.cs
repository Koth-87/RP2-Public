using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapModeFramework
{
    /// <summary>
    /// Base map mode for dropdown-selectable definition overlays (biomes, animals, plants, rock types).
    /// </summary>
    public abstract class MapMode_Dropdown<T> : MapMode_Terrain where T : Def
    {
        public override string Name => string.Format("{0} ({1})", base.Name, selected.LabelCap);
        public T selected;

        protected MapMode_Dropdown() { }
        protected MapMode_Dropdown(MapModeDef def) : base(def) { }

        public override void OnButtonClick()
        {
            MapModeComponent mapModeComponent = MapModeComponent.Instance;
            var options = new List<FloatMenuOption>();
            foreach (T def in DefDatabase<T>.AllDefsListForReading.Where(ValidDef))
            {
                T captured = def;
                options.Add(new FloatMenuOption(captured.LabelCap, delegate
                {
                    selected = captured;
                    mapModeComponent.RequestMapModeSwitch(this);
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public virtual bool ValidDef(T def) => true;

        public virtual bool IsSelectedTile(int tile) => false;

        public override Material GetMaterial(int tile)
        {
            return IsSelectedTile(tile) ? Materials.matGreenOverlay : Materials.matWhiteOverlay;
        }
    }

    public class MapMode_Biome : MapMode_Dropdown<BiomeDef>
    {
        public MapMode_Biome() { }
        public MapMode_Biome(MapModeDef def) : base(def) { }

        public override bool ValidDef(BiomeDef def) => def.generatesNaturally;

        public override bool IsSelectedTile(int tile) => Find.WorldGrid[tile].PrimaryBiome == selected;
    }

    public class MapMode_AnimalCommonality : MapMode_Dropdown<PawnKindDef>
    {
        public MapMode_AnimalCommonality() { }
        public MapMode_AnimalCommonality(MapModeDef def) : base(def) { }

        public override bool ValidDef(PawnKindDef def)
        {
            int count = Core.allNaturalBiomes.Count;
            for (int i = 0; i < count; i++)
            {
                if (Core.allNaturalBiomes[i].AllWildAnimals.Contains(def))
                    return true;
            }
            return false;
        }

        public override bool IsSelectedTile(int tile)
        {
            BiomeDef biome = Find.WorldGrid[tile].PrimaryBiome;
            return biome != null && biome.CommonalityOfAnimal(selected) > 0f;
        }

        public override Material GetMaterial(int tile)
        {
            if (IsSelectedTile(tile))
            {
                BiomeDef biome = Find.WorldGrid[tile].PrimaryBiome;
                return Materials.MatForCommonalityOverlay(biome.CommonalityOfAnimal(selected));
            }
            return base.GetMaterial(tile);
        }

        public override string GetTileLabel(int tile)
        {
            if (IsSelectedTile(tile))
            {
                BiomeDef biome = Find.WorldGrid[tile].PrimaryBiome;
                return biome.CommonalityOfAnimal(selected).ToStringPercent();
            }
            return base.GetTileLabel(tile);
        }

        public override string GetTooltip(int tile)
        {
            if (IsSelectedTile(tile))
            {
                BiomeDef biome = Find.WorldGrid[tile].PrimaryBiome;
                return $"{selected.LabelCap}\n- {"MMF.Commonality".Translate()}: {biome.CommonalityOfAnimal(selected).ToStringPercent()}";
            }
            return base.GetTooltip(tile);
        }
    }

    public class MapMode_PlantCommonality : MapMode_Dropdown<ThingDef>
    {
        public MapMode_PlantCommonality() { }
        public MapMode_PlantCommonality(MapModeDef def) : base(def) { }

        public override bool ValidDef(ThingDef def)
        {
            int count = Core.allNaturalBiomes.Count;
            for (int i = 0; i < count; i++)
            {
                if (Core.allNaturalBiomes[i].AllWildPlants.Contains(def))
                    return true;
            }
            return false;
        }

        public override bool IsSelectedTile(int tile)
        {
            BiomeDef biome = Find.WorldGrid[tile].PrimaryBiome;
            return biome != null && biome.CommonalityOfPlant(selected) > 0f;
        }

        public override Material GetMaterial(int tile)
        {
            if (IsSelectedTile(tile))
            {
                BiomeDef biome = Find.WorldGrid[tile].PrimaryBiome;
                return Materials.MatForCommonalityOverlay(biome.CommonalityOfPlant(selected));
            }
            return base.GetMaterial(tile);
        }

        public override string GetTileLabel(int tile)
        {
            if (IsSelectedTile(tile))
            {
                BiomeDef biome = Find.WorldGrid[tile].PrimaryBiome;
                return biome.CommonalityOfPlant(selected).ToStringPercent();
            }
            return base.GetTileLabel(tile);
        }

        public override string GetTooltip(int tile)
        {
            if (IsSelectedTile(tile))
            {
                BiomeDef biome = Find.WorldGrid[tile].PrimaryBiome;
                return $"{selected.LabelCap}\n- {"MMF.Commonality".Translate()}: {biome.CommonalityOfPlant(selected).ToStringPercent()}";
            }
            return base.GetTooltip(tile);
        }
    }

    /// <summary>
    /// Displays natural stone types per tile. Precomputes a 64-bit bitmask on the main thread
    /// because <c>World.NaturalRockTypesIn</c> reuses a shared static list.
    /// </summary>
    public class MapMode_RockTypes : MapMode_Dropdown<ThingDef>
    {
        private List<ThingDef> rockDefs = new List<ThingDef>();
        private ulong[] masks;

        public MapMode_RockTypes() { }
        public MapMode_RockTypes(MapModeDef def) : base(def)
        {
            selected = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d => d.IsNonResourceNaturalRock);
        }

        public override bool ValidDef(ThingDef def) => def != null && def.IsNonResourceNaturalRock;

        public override void DoPreRegenerate()
        {
            base.DoPreRegenerate();
            BuildRockCache();
        }

        private void BuildRockCache()
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid?.Surface == null || Find.World == null)
            {
                masks = null;
                return;
            }
            rockDefs = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsNonResourceNaturalRock).Take(64).ToList();
            masks = new ulong[grid.TilesCount];
            PlanetLayer surface = grid.Surface;
            for (int i = 0; i < masks.Length; i++)
            {
                ulong mask = 0;
                IEnumerable<ThingDef> rocks = Find.World.NaturalRockTypesIn(new PlanetTile(i, surface));
                if (rocks == null)
                    continue;
                foreach (ThingDef rock in rocks)
                {
                    int index = rockDefs.IndexOf(rock);
                    if (index >= 0)
                        mask |= 1UL << index;
                }
                masks[i] = mask;
            }
        }

        public override bool IsSelectedTile(int tile)
        {
            if (selected == null || masks == null || tile < 0 || tile >= masks.Length)
                return false;
            int index = rockDefs.IndexOf(selected);
            if (index < 0)
                return false;
            return (masks[tile] & (1UL << index)) != 0;
        }
    }
}
