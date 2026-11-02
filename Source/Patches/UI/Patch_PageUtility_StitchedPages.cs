using HarmonyLib;
using RimWorld;
using Verse;
using System.Collections.Generic;
using System.Linq;
using Planets.Core;
using Planets.UI.Pages;
using Planets.Utilities.Logging;

namespace Planets.Patches.UI
{
    /// <summary>
    /// WORLD CREATION PAGE REPLACEMENT
    /// ================================
    /// Intercepts RimWorld's page navigation chain and replaces vanilla's Page_CreateWorldParams
    /// with our CreateWorldParamsPage that adds RP-specific settings.
    /// 
    /// This runs early (VeryHigh priority + HarmonyBefore) to ensure we replace before other mods
    /// (especially Progression Storytellers) read and cache the pages list.
    /// </summary>
    [HarmonyPatch(typeof(PageUtility), "StitchedPages")]
    public static class Patch_PageUtility_StitchedPages
    {
        /// <summary>
        /// Prefix that replaces vanilla pages BEFORE Progression Storytellers caches them.
        /// This prevents Progression Storytellers from caching the vanilla page reference.
        /// Note: This is NEW in RP2 - old RP code only used Postfix.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        [HarmonyBefore("ferny.ProgressionStorytellers")]
        public static void Prefix(ref IEnumerable<Page> pages)
        {
            if (pages == null || TutorSystem.TutorialMode)
                return;
            
            var pageList = pages.ToList();
            bool replaced = false;
            
            for (int i = 0; i < pageList.Count; i++)
            {
                // Only replace exact vanilla type
                if (pageList[i] != null && pageList[i].GetType() == typeof(Page_CreateWorldParams))
                {
                    var rpPage = new Page_CreateWorldParamsRP();
                    pageList[i] = rpPage;
                    replaced = true;
                    
                    if (Controller.DebugEnabled)
                        RPLog.D($"Prefix: Replaced vanilla Page_CreateWorldParams at index {i}");
                }
            }
            
            if (replaced)
            {
                pages = pageList;
                
                if (Controller.DebugEnabled)
                    RPLog.D("Prefix: Updated pages list with RP2 replacements");
            }
        }
        
        /// <summary>
        /// Postfix that handles page chain fixup and direct instantiation cases.
        /// Kept for backward compatibility and as a fallback.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPriority(Priority.VeryHigh)]
        public static void Postfix(ref Page __result)
        {
            // Skip in tutorial mode
            if (__result == null || TutorSystem.TutorialMode)
                return;
            
            // Find all vanilla Page_CreateWorldParams in the chain and replace them
            var replacements = new List<(Page oldPage, Page newPage)>();
            
            Page current = __result;
            while (current != null)
            {
                // Only replace exact vanilla type (not our subclass or other mods' subclasses)
                if (current.GetType() == typeof(Page_CreateWorldParams))
                {
                    var rpPage = new Page_CreateWorldParamsRP
                    {
                        prev = current.prev,
                        next = current.next
                    };
                    
                    replacements.Add((current, rpPage));
                }
                
                current = current.next;
            }
            
            // Apply replacements to the chain
            foreach (var (oldPage, newPage) in replacements)
            {
                // Update prev page's next pointer
                if (oldPage.prev != null)
                    oldPage.prev.next = newPage;
                
                // Update next page's prev pointer
                if (oldPage.next != null)
                    oldPage.next.prev = newPage;
                
                // Update result if this was the first page
                if (oldPage == __result)
                    __result = newPage;
                
                if (Controller.DebugEnabled)
                {
                    RPLog.D($"Replaced {oldPage.GetType().Name} with {newPage.GetType().Name}");
                }
            }
            
            if (replacements.Count == 0 && Controller.DebugEnabled)
            {
                RPLog.D("No vanilla Page_CreateWorldParams found (already replaced or not present)");
            }
        }
    }
    
    /// <summary>
    /// DIRECT WINDOW ADDITION INTERCEPT
    /// =================================
    /// Catches cases where mods or vanilla bypass PageUtility and directly add pages to WindowStack.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), "Add")]
    public static class Patch_WindowStack_Add
    {
        [HarmonyPriority(Priority.VeryHigh)]
        public static void Prefix(ref Window window)
        {
            if (TutorSystem.TutorialMode)
                return;

            // Only intercept exact vanilla type
            if (window != null && window.GetType() == typeof(Page_CreateWorldParams))
            {
                var vanillaPage = window as Page_CreateWorldParams;
                
                var rpPage = new Page_CreateWorldParamsRP
                {
                    prev = vanillaPage.prev,
                    next = vanillaPage.next
                };
                
                // Update navigation chain
                if (vanillaPage.prev != null)
                    vanillaPage.prev.next = rpPage;
                if (vanillaPage.next != null)
                    vanillaPage.next.prev = rpPage;
                
                // Replace the window
                window = rpPage;
                
                if (Controller.DebugEnabled)
                {
                    RPLog.D("Intercepted direct Page_CreateWorldParams creation");
                }
            }
        }
    }
}
