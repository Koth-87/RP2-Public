using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Planets.Compatibility
{
    /// <summary>
    /// Interface for mod compatibility modules.
    /// Allows centralized management of initialization, settings, and UI integration.
    ///
    /// When another mod does work we also own, ship both a public flag or method they can
    /// check and a fallback for the build that does not know it yet. The fallback removes
    /// their patch, or writes our result back afterward, against the methods that version
    /// actually uses. Keep the fallback after they adopt the API, for installs that never
    /// update. RealisticAxialTiltCompat is the fallback for yearly temperature and plants.
    /// Their sky, shadows, and moon stay. Do not copy their source. Reimplement the behavior.
    /// </summary>
    public interface ICompatibilityModule
    {
        /// <summary>
        /// Unique identifier for the module (usually the mod package ID or name).
        /// </summary>
        string ModuleID { get; }
        
        /// <summary>
        /// Human-readable name of the compatible mod.
        /// </summary>
        string ModName { get; }

        /// <summary>
        /// Whether the compatible mod is active and this module is enabled.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Called when the module is initialized.
        /// Use this to perform startup checks or patch applications.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Render settings UI for this mod in the Integrations tab.
        /// </summary>
        void RenderIntegrationUI(Listing_Standard list);

        /// <summary>
        /// Save settings data.
        /// </summary>
        void ExposeData();

        /// <summary>
        /// Reset settings to defaults.
        /// </summary>
        void ResetToDefaults();

        /// <summary>
        /// Whether this mod adds a button to the World Creation page.
        /// </summary>
        bool HasWorldCreationButton { get; }

        /// <summary>
        /// Label for the World Creation button.
        /// </summary>
        string ButtonLabel { get; }

        /// <summary>
        /// Action to perform when the World Creation button is clicked.
        /// </summary>
        void OnWorldCreationButtonClicked(List<FactionDef> factions);
    }
}
