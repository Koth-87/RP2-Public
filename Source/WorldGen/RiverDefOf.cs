using RimWorld;
using Verse;

namespace Planets.WorldGen
{
    /// <summary>
    /// Static references to vanilla river definitions for hydrology-based river generation.
    /// These match vanilla river def names from Core/Defs/Misc/RiverDefs.xml
    /// </summary>
    [DefOf]
    public static class RiverDefOf
    {
        public static RiverDef Creek;
        public static RiverDef River;
        public static RiverDef LargeRiver;
        public static RiverDef HugeRiver;
        
        static RiverDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RiverDefOf));
        }
    }
}
