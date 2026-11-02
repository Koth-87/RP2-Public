using HarmonyLib;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace MapModeFramework
{
    [StaticConstructorOnStartup]
    public static class ModCompatibility
    {
        public static Dictionary<string, string> patchedMods;
        public static List<Assembly> assemblies;
        private static bool foundRimworldExplorationMode;

        static ModCompatibility()
        {
            patchedMods = new Dictionary<string, string>
            {
                { "regrowth.botr.core", "ReGrowthCore" },
                { "zal.wmbp", "BiomesKit" },
                { "thelastbulletbender.rwexploration", "RimworldExplorationMode" },
                { "m00nl1ght.geologicallandforms", "GeologicalLandforms" }
            };
            assemblies = new List<Assembly>();
        }

        public static void GetAssemblies()
        {
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading.Where(x => patchedMods.Keys.Contains(x.PackageId.ToLower())).ToList();
            foreach (ModContentPack mod in mods)
            {
                bool found = false;
                string modPackageId = mod.PackageId.ToLower();
                string assemblyName = patchedMods[modPackageId];
                foreach (Assembly loadedAssembly in mod.assemblies.loadedAssemblies)
                {
                    string name = loadedAssembly.GetName().Name;
                    if (name.Contains(assemblyName))
                    {
                        found = true;
                        assemblies.Add(loadedAssembly);
                        Core.Message("Assembly for " + mod.Name + " loaded. Will load compatibility patches.");
                        break;
                    }
                }
                if (!found)
                {
                    Core.Error($"Assembly ({assemblyName}) not found for running mod ({mod.Name}). Compatibility patches will not apply.");
                }
            }
            Core.Message($"Loading assemblies of patched mods done. Total assemblies loaded: {assemblies.Count}");
            ApplyPatches();
        }

        public static void ApplyPatches()
        {
            PatchWorldLayer("GeologicalLandforms", "WorldLayer_Landforms", settings => settings.drawHills);
            // ReGrowth 2 compatibility - Updated for 1.6 changes:
            // RG2 1.5 used BiomesKitWorldLayer, but 1.6 switched to patching vanilla WorldDrawLayer_Hills
            // Since RG2 1.6 patches the vanilla hills layer, we need to patch WorldDrawLayer_Hills for compatibility
            PatchReGrowth2Compatibility();
            PatchWMBPCompatibility();
            Assembly explorationMode = assemblies.FirstOrDefault(x => x.GetName().Name.Contains("RimworldExplorationMode"));
            foundRimworldExplorationMode = explorationMode != null;
            if (foundRimworldExplorationMode)
            {
                RimWorldExplorationMode.visibilityManagerType = explorationMode.GetTypes().FirstOrDefault(x => x.FullName.Contains("VisibilityManager"));
                RimWorldExplorationMode.worldFeatureManagerType = explorationMode.GetTypes().FirstOrDefault(x => x.FullName.Contains("WorldFeatureManager"));
            }
        }

        public static void PatchReGrowth2Compatibility()
        {
            Assembly rgAssembly = assemblies.FirstOrDefault(x => x.GetName().Name.Contains("ReGrowthCore"));
            if (rgAssembly == null)
            {
                Core.Warning("ReGrowthCore assembly not found. ReGrowth 2 render patch not applied.");
                return;
            }

            // Check for both 1.5 and 1.6 compatibility
            // 1.5 uses BiomesKitWorldLayer class
            Type biomesKitWorldLayer = rgAssembly.GetTypes().FirstOrDefault(x => x.FullName.Contains("BiomesKitWorldLayer"));
            if (biomesKitWorldLayer != null)
            {
                // RG2 1.5 compatibility
                WorldDrawLayerBase_Render_Patch.disableRendering.Add(biomesKitWorldLayer, settings => settings.drawHills);
                Core.Message("ReGrowth 2 1.5 compatibility: BiomesKitWorldLayer patched.");
                return;
            }

            // 1.6+ uses patches on vanilla WorldDrawLayer_Hills instead
            // Check if the RG2 1.6 patch class exists (Patch_WorldDrawLayer_Hills_Regenerate)
            Type rg2HillsPatch = rgAssembly.GetTypes().FirstOrDefault(x => x.FullName.Contains("Patch_WorldDrawLayer_Hills_Regenerate"));
            if (rg2HillsPatch != null)
            {
                // RG2 1.6+ compatibility - patch the vanilla WorldDrawLayer_Hills that RG2 also patches
                if (!WorldDrawLayerBase_Render_Patch.disableRendering.ContainsKey(typeof(WorldDrawLayer_Hills)))
                {
                    WorldDrawLayerBase_Render_Patch.disableRendering.Add(typeof(WorldDrawLayer_Hills), settings => settings.drawHills);
                    Core.Message("ReGrowth 2 1.6+ compatibility: WorldDrawLayer_Hills patched for RG2 integration.");
                }
                // Also hide ReGrowth's WorldDrawLayer_Beautification when drawHills is off (it draws the actual hills)
                Type rgBeautification = rgAssembly.GetTypes().FirstOrDefault(x => x.FullName != null && x.FullName.Contains("WorldDrawLayer_Beautification"));
                if (rgBeautification != null && !WorldDrawLayerBase_Render_Patch.disableRendering.ContainsKey(rgBeautification))
                {
                    WorldDrawLayerBase_Render_Patch.disableRendering.Add(rgBeautification, settings => settings.drawHills);
                    Core.Message("ReGrowth 2 1.6+ compatibility: WorldDrawLayer_Beautification patched for hills toggle.");
                }
                return;
            }

            Core.Error("BiomesKitWorldLayer [ReGrowthCore] not found. Render patch not applied.");
        }

        public static void PatchWMBPCompatibility()
        {
            Assembly wmbpAssembly = assemblies.FirstOrDefault(x => x.GetName().Name.Contains("BiomesKit"));
            if (wmbpAssembly == null)
            {
                return;
            }
            Type wmbpBeautification = wmbpAssembly.GetTypes().FirstOrDefault(x => x.FullName != null && x.FullName.Contains("WorldDrawLayer_Beautification"));
            if (wmbpBeautification != null && !WorldDrawLayerBase_Render_Patch.disableRendering.ContainsKey(wmbpBeautification))
            {
                WorldDrawLayerBase_Render_Patch.disableRendering.Add(wmbpBeautification, settings => settings.drawHills);
                Core.Message("WMBP compatibility: WorldDrawLayer_Beautification patched for hills toggle.");
            }
        }

        public static void PatchWorldLayer(string assemblyName, string worldLayerName, Func<DrawSettings, bool> setting)
        {
            Assembly assembly = assemblies.FirstOrDefault(x => x.GetName().Name.Contains(assemblyName));
            if (assembly == null)
            {
                Core.Warning($"Assembly named {assemblyName} not found. Render patch not applied.");
                return;
            }
            Type worldLayer = assembly.GetTypes().FirstOrDefault(x => x.FullName.Contains(worldLayerName));
            if (worldLayer != null)
            {
                WorldDrawLayerBase_Render_Patch.disableRendering.Add(worldLayer, setting);
            }
            else
            {
                Core.Error($"{worldLayerName} [{assemblyName}] not found. Render patch not applied.");
            }
        }

        public static bool[] explorationVisible;

        /// <summary>Copy exploration visibility on the main thread before a mesh task reads it.</summary>
        public static void SnapshotExplorationVisibility()
        {
            if (!foundRimworldExplorationMode || Find.WorldGrid == null)
            {
                explorationVisible = null;
                return;
            }
            int count = Find.WorldGrid.TilesCount;
            bool[] snapshot = new bool[count];
            for (int i = 0; i < count; i++)
                snapshot[i] = RimWorldExplorationMode.TileVisible(i);
            explorationVisible = snapshot;
        }

        public static bool DrawTile(int tile)
        {
            if (explorationVisible != null)
                return tile >= 0 && tile < explorationVisible.Length && explorationVisible[tile];
            if (foundRimworldExplorationMode)
                return RimWorldExplorationMode.TileVisible(tile);
            return true;
        }

        public static bool OverrideLabel(int tile, out string label)
        {
            label = string.Empty;
            if (!foundRimworldExplorationMode)
            {
                return false;
            }
            return !RimWorldExplorationMode.TileVisible(tile);
        }

        public static bool OverrideTooltip(int tile, out string tooltip)
        {
            tooltip = string.Empty;
            if (!foundRimworldExplorationMode)
            {
                return false;
            }
            if (!(MapModeComponent.Instance.currentMapMode is MapMode_Features mapModeFeatures))
            {
                return !RimWorldExplorationMode.TileVisible(tile);
            }
            Region region = mapModeFeatures.GetRegion(tile);
            int featureIndex = mapModeFeatures.RegionList.FindIndex(x => x.name == region?.name);
            List<bool> learnedFeatures = RimWorldExplorationMode.GetLearnedFeatures();
            if (learnedFeatures != null && featureIndex >= 0 && featureIndex < learnedFeatures.Count && learnedFeatures[featureIndex])
                return false;
            return true;
        }
    }

    [HarmonyPatch]
    public static class VisibilityManager_UpdateGraphics_Patch
    {
        private const string typeName = "RimworldExplorationMode.VisibilityManager";
        private const float RegenIntervalSeconds = 1f;
        private static float lastRegenTime = -999f;
        private static World lastWorld;

        public static bool Prepare()
        {
            return AccessTools.TypeByName(typeName) != null;
        }

        public static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                Core.Error("Type " + typeName + " not found. UpdateGraphics patch not loaded.");
                return null;
            }
            MethodInfo methodInfo = AccessTools.Method(type, "UpdateGraphics");
            if (methodInfo == null)
                Core.Error("Method UpdateGraphics not found for " + typeName + ". UpdateGraphics patch not loaded.");
            return methodInfo;
        }

        public static void Postfix()
        {
            World world = null;
            try { world = Find.World; } catch { }
            if (world == null)
                return;

            if (world != lastWorld)
            {
                lastWorld = world;
                lastRegenTime = -999f;
            }
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - lastRegenTime < RegenIntervalSeconds)
                return;

            try
            {
                MapModeComponent mapModeComponent = MapModeComponent.Instance;
                if (mapModeComponent?.currentMapMode?.WorldLayer == null)
                    return;
                lastRegenTime = now;
                mapModeComponent.RegenerateNow();
            }
            catch (Exception ex)
            {
                Core.ErrorOnce($"Exploration mode refresh failed: {ex.Message}", "ExplorationRefresh".GetHashCode());
            }
        }
    }

    public static class RimWorldExplorationMode
    {
        public static Type visibilityManagerType;
        public static Type worldFeatureManagerType;
        public static MethodInfo tileVisible;
        public static FieldInfo learnedFeatures;

        public static GameComponent visibilityManager;
        public static WorldComponent worldFeatureManager;

        public static bool TileVisible(int tile)
        {
            if (visibilityManagerType == null)
            {
                return true;
            }
            GameComponent gameComponent = Current.Game.GetComponent(visibilityManagerType);
            if (visibilityManager == null || (visibilityManager != null && visibilityManager != gameComponent))
            {
                visibilityManager = gameComponent;
                tileVisible = visibilityManagerType.GetMethod("TileVisible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null)
                    ?? visibilityManagerType.GetMethod("TileVisible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(PlanetTile) }, null);
            }
            if (tileVisible != null)
            {
                object arg = tileVisible.GetParameters()[0].ParameterType == typeof(PlanetTile)
                    ? (object)new PlanetTile(tile)
                    : tile;
                return (bool)tileVisible.Invoke(visibilityManager, new[] { arg });
            }
            return true;
        }

        public static List<bool> GetLearnedFeatures()
        {
            if (worldFeatureManagerType == null)
            {
                return null;
            }
            WorldComponent worldComponent = Find.World.GetComponent(worldFeatureManagerType);
            if (worldFeatureManager == null || worldFeatureManager != null && worldFeatureManager != worldComponent)
            {
                worldFeatureManager = worldComponent;
                learnedFeatures = worldFeatureManagerType.GetFields().FirstOrDefault(x => x.Name.Contains("learnedFeatures"));
            }
            if (learnedFeatures != null)
            {
                List<bool> learnedFeaturesList = (List<bool>)learnedFeatures.GetValue(worldFeatureManager);
                return learnedFeaturesList;
            }
            return null;
        }
    }
}
