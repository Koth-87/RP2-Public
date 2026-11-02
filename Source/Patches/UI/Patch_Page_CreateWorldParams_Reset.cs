using HarmonyLib;
using RimWorld;
using Planets.UI.Pages;

namespace Planets.Patches.UI
{
    /// <summary>
    /// Vanilla Reset is what other mods call. RP-specific defaults run after it, before later postfixes.
    /// </summary>
    [HarmonyPatch(typeof(Page_CreateWorldParams), nameof(Page_CreateWorldParams.Reset))]
    [HarmonyPriority(Priority.First)]
    public static class Patch_Page_CreateWorldParams_Reset
    {
        [HarmonyPostfix]
        public static void Postfix(Page_CreateWorldParams __instance)
        {
            if (__instance is Page_CreateWorldParamsRP rpPage)
                rpPage.ResetRPState();
        }
    }
}
