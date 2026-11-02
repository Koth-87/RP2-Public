using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Planets.Utilities.Logging;

namespace Planets.Compatibility
{
    /// <summary>
    /// Central registry for mod compatibility modules.
    /// Manages lifecycle and access to all registered compatibility modules.
    /// </summary>
    public static class ModCompatibilityRegistry
    {
        private static readonly List<ICompatibilityModule> modules = new List<ICompatibilityModule>();
        private static bool verified = false;

        /// <summary>
        /// Register a compatibility module.
        /// Should be called during static initialization of the module.
        /// </summary>
        public static void Register(ICompatibilityModule module)
        {
            if (module == null) return;
            
            if (!modules.Contains(module))
            {
                modules.Add(module);
                // We don't log here to avoid spam during startup, detailed logging happens in VerifyModules
            }
        }

        /// <summary>
        /// Get all registered modules.
        /// </summary>
        public static List<ICompatibilityModule> GetModules()
        {
            return modules;
        }

        /// <summary>
        /// Get all active modules.
        /// </summary>
        public static List<ICompatibilityModule> GetActiveModules()
        {
            return modules.Where(m => m.IsActive).ToList();
        }

        /// <summary>
        /// Verify and initialize all registered modules.
        /// Called by Planets_Initializer.
        /// </summary>
        public static void VerifyAndInitialize()
        {
            if (verified) return;

            // Redirect mod biome workers to RP2 compat workers (must run after DefDatabase loaded)
            BiomeCompatWorkerRedirect.RunRedirects();

            // Auto-discover modules via reflection to ensure none are missed due to static constructor ordering
            DiscoverModules();

            int activeCount = 0;
            foreach (var module in modules)
            {
                try
                {
                    module.Initialize();
                    if (module.IsActive)
                    {
                        activeCount++;
                        if (Planets.Core.Controller.DebugEnabled)
                        {
                            RPLog.Init($"[Compat] Module Active: {module.ModName} ({module.ModuleID})");
                        }
                    }
                }
                catch (Exception e)
                {
                    RPLog.Error($"[Compat] Failed to initialize module {module.ModName}: {e}");
                }
            }
            
            if (Planets.Core.Controller.DebugEnabled)
            {
                RPLog.Init($"[Compat] Initialized {activeCount} active compatibility modules out of {modules.Count} registered.");
            }

            verified = true;
        }

        /// <summary>
        /// Render UI for all active modules in the Integrations tab.
        /// </summary>
        public static void RenderAllIntegrationUI(Listing_Standard list)
        {
            foreach (var module in GetActiveModules())
            {
                try
                {
                    module.RenderIntegrationUI(list);
                }
                catch (Exception e)
                {
                    RPLog.Error($"[Compat] Error rendering UI for {module.ModName}: {e}");
                }
            }
        }

        /// <summary>
        /// Expose data for every registered module. Active-only iteration drops settings
        /// for a module whose Initialize has not run yet.
        /// </summary>
        public static void ExposeAllData()
        {
            foreach (var module in GetModules())
            {
                try
                {
                    module.ExposeData();
                }
                catch (Exception e)
                {
                    RPLog.Error($"[Compat] Error saving data for {module.ModName}: {e}");
                }
            }
        }

        /// <summary>
        /// Reset defaults for all active modules.
        /// </summary>
        public static void ResetAllToDefaults()
        {
            foreach (var module in GetActiveModules())
            {
                try
                {
                    module.ResetToDefaults();
                }
                catch (Exception e)
                {
                    RPLog.Error($"[Compat] Error resetting defaults for {module.ModName}: {e}");
                }
            }
        }

        /// <summary>
        /// Get modules that should have a button on the World Creation page.
        /// </summary>
        public static List<ICompatibilityModule> GetWorldCreationButtonModules()
        {
            // Ensure modules are discovered if this is called before initialization
            if (!verified) VerifyAndInitialize();
            return modules.Where(m => m.IsActive && m.HasWorldCreationButton).ToList();
        }

        /// <summary>
        /// Use reflection to find all classes implementing ICompatibilityModule
        /// and register them if they haven't registered themselves.
        /// </summary>
        private static void DiscoverModules()
        {
            try
            {
                var interfaceType = typeof(ICompatibilityModule);
                var types = System.Reflection.Assembly.GetExecutingAssembly().GetTypes()
                    .Where(t => interfaceType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in types)
                {
                    // Check if we already have an instance of this type
                    if (modules.Any(m => m.GetType() == type))
                        continue;

                    try
                    {
                        // Access the singleton "Instance" property if it exists
                        var instanceProp = type.GetProperty("Instance", 
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        
                        ICompatibilityModule moduleInstance = null;
                        
                        if (instanceProp != null)
                        {
                            moduleInstance = instanceProp.GetValue(null) as ICompatibilityModule;
                        }
                        else
                        {
                            // Fallback: try to create instance directly (though all our modules use singletons)
                            moduleInstance = Activator.CreateInstance(type) as ICompatibilityModule;
                        }

                        if (moduleInstance != null)
                        {
                            Register(moduleInstance);
                            if (Planets.Core.Controller.DebugEnabled)
                            {
                                RPLog.Init($"[Compat] Auto-discovered module: {moduleInstance.ModName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        RPLog.Error($"[Compat] Failed to instantiate discovered module {type.Name}: {ex}");
                    }
                }
            }
            catch (Exception e)
            {
                RPLog.Error($"[Compat] Error during module discovery: {e}");
            }
        }
    }
}
