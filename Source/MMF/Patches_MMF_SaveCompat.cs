using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MapModeFramework
{
    /// <summary>
    /// Ensures saves from standalone Map Mode Framework (MMF) resolve MapModeComponent to RP2's type
    /// when loading without MMF. Without this, GetTypeInAnyAssembly can fail if the save references
    /// the old assembly (e.g. assembly-qualified name or type not found in loaded assemblies).
    /// </summary>
    [HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.GetBackCompatibleType))]
    public static class BackCompatibility_GetBackCompatibleType_MMF_Patch
    {
        private const string MapModeComponentTypeName = "MapModeFramework.MapModeComponent";

        public static void Postfix(Type baseType, string providedClassName, ref Type __result)
        {
            if (__result != null)
                return;
            if (baseType == null || !typeof(GameComponent).IsAssignableFrom(baseType))
                return;
            if (string.IsNullOrEmpty(providedClassName))
                return;
            // Match "MapModeFramework.MapModeComponent" or assembly-qualified "..., MapModeFramework, ..."
            if (providedClassName.StartsWith(MapModeComponentTypeName, StringComparison.Ordinal))
            {
                __result = typeof(MapModeComponent);
            }
        }
    }

    /// <summary>
    /// Invalidates Faction Territories cache when loading a save, ensuring territories are recalculated
    /// correctly when switching between MMF and RP2 or loading saves created without either mod.
    /// </summary>
    [HarmonyPatch(typeof(GameComponentUtility), nameof(GameComponentUtility.LoadedGame))]
    public static class GameComponentUtility_LoadedGame_MMF_Patch
    {
        public static void Postfix()
        {
            // Invalidate Faction Territories cache when loading a save
            // This ensures territories are recalculated with current world state
            try
            {
                Type territoryCacheType = AccessTools.TypeByName("FactionTerritories.TerritoryOwnershipCache");
                if (territoryCacheType != null)
                {
                    MethodInfo markDirtyMethod = AccessTools.Method(territoryCacheType, "MarkDirty");
                    if (markDirtyMethod != null)
                    {
                        markDirtyMethod.Invoke(null, null);
                        Core.Message("Invalidated Faction Territories cache on save load");
                    }
                }
            }
            catch
            {
                // Faction Territories mod not loaded or API changed - ignore silently
            }
        }
    }
}
