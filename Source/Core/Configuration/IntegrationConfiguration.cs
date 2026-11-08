using UnityEngine;
using Verse;
using Planets.Compatibility;

namespace Planets.Core.Configuration
{
    /// <summary>
    /// Integration configuration registry for mod compatibility.
    /// Delegates UI rendering, data persistence, and reset logic to individual compat patches.
    /// Adding a new integration only requires one method call per operation here.
    /// </summary>
    public class IntegrationConfiguration : IExposable
    {
        private Vector2 scrollPos = Vector2.zero;
        private float integrationTabHeight = 800f;

        /// <summary>Fraction of RP_Oasis tiles that must receive an Odyssey oasis landmark. 0 disables the requirement.</summary>
        public float odysseyMinOasisLandmarkCoverage = 0f;

        /// <summary>Copy Medieval Overhaul animals into Realistic Planets biomes.</summary>
        public bool medievalCreaturesInNewBiomes = true;

        /// <summary>Copy Medieval Overhaul plants into Realistic Planets biomes.</summary>
        public bool medievalPlantsInNewBiomes = true;

        /// <summary>Render all integration settings UI sections</summary>
        public void RenderUI(Rect canvas)
        {
            float viewWidth = canvas.width - 16f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, integrationTabHeight);

            Widgets.BeginScrollView(canvas, ref scrollPos, viewRect);

            Listing_Standard list = new Listing_Standard();
            list.maxOneColumn = true;
            list.ColumnWidth = viewWidth;
            list.Begin(viewRect);

            var active = ModCompatibilityRegistry.GetActiveModules();
            if (active.Count > 0)
            {
                GUI.color = Color.cyan;
                list.Label("Compatibility Status");
                GUI.color = Color.white;
                list.Gap(4f);
                for (int i = 0; i < active.Count; i++)
                    list.Label(active[i].ModName + ": active");
                list.Gap(12f);
            }

            ModCompatibilityRegistry.RenderAllIntegrationUI(list);

            list.End();
            integrationTabHeight = list.CurHeight + 12f;
            Widgets.EndScrollView();
        }

        /// <summary>Persist all integration settings to save file</summary>
        public void ExposeData()
        {
            // These keys are written here, not in the compat modules. Module ExposeData runs
            // only for modules already registered, and the Mod constructor saves settings
            // before any module has registered.
            Scribe_Values.Look(ref odysseyMinOasisLandmarkCoverage, "odysseyMinOasisLandmarkCoverage", 0f, true);
            Scribe_Values.Look(ref medievalCreaturesInNewBiomes, "medievalCreaturesInNewBiomes", true, true);
            Scribe_Values.Look(ref medievalPlantsInNewBiomes, "medievalPlantsInNewBiomes", true, true);
            ModCompatibilityRegistry.ExposeAllData();
        }

        /// <summary>Reset the integration controls on the mod settings page. Does not touch another mod's own window.</summary>
        public void ResetPageDefaults()
        {
            odysseyMinOasisLandmarkCoverage = 0f;
            medievalCreaturesInNewBiomes = true;
            medievalPlantsInNewBiomes = true;
        }
    }
}
