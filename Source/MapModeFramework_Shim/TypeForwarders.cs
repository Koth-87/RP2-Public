using System.Runtime.CompilerServices;
using MapModeFramework;

// Forward types that external mods expect to find in MapModeFramework.dll
// to their implementation in Realistic_Planets_2.dll

// Core and Components
[assembly: TypeForwardedTo(typeof(Core))]
[assembly: TypeForwardedTo(typeof(MapModeDef))]
[assembly: TypeForwardedTo(typeof(MapModeFrameworkDefOf))]
[assembly: TypeForwardedTo(typeof(MapModeComponent))]
[assembly: TypeForwardedTo(typeof(DrawSettings))]
[assembly: TypeForwardedTo(typeof(MapModeUI))]
[assembly: TypeForwardedTo(typeof(Settings))]
[assembly: TypeForwardedTo(typeof(CacheSetting))]
[assembly: TypeForwardedTo(typeof(Resources))]
[assembly: TypeForwardedTo(typeof(Materials))]
[assembly: TypeForwardedTo(typeof(TaskHandler))]
[assembly: TypeForwardedTo(typeof(EdgesCache))]
[assembly: TypeForwardedTo(typeof(RegionCache<>))]
[assembly: TypeForwardedTo(typeof(ModCompatibility))]
[assembly: TypeForwardedTo(typeof(TileUtilities))]

// Base Classes
[assembly: TypeForwardedTo(typeof(MapMode))]
[assembly: TypeForwardedTo(typeof(MapMode_Default))]
[assembly: TypeForwardedTo(typeof(MapMode_Cached))]
[assembly: TypeForwardedTo(typeof(MapMode_Terrain))]
[assembly: TypeForwardedTo(typeof(MapMode_Biome))]
[assembly: TypeForwardedTo(typeof(MapMode_AnimalCommonality))]
[assembly: TypeForwardedTo(typeof(MapMode_PlantCommonality))]
[assembly: TypeForwardedTo(typeof(MapMode_GrowingPeriod))]
[assembly: TypeForwardedTo(typeof(MapMode_RockTypes))]
[assembly: TypeForwardedTo(typeof(IMapMode_Window))]
[assembly: TypeForwardedTo(typeof(RegionProperties))]
[assembly: TypeForwardedTo(typeof(MapModeVisibility))]
[assembly: TypeForwardedTo(typeof(MapMode_Dropdown<>))]

// Regions
[assembly: TypeForwardedTo(typeof(MapMode_Region))]
[assembly: TypeForwardedTo(typeof(MapMode_GenericRegion<>))]
[assembly: TypeForwardedTo(typeof(MapMode_Features))]
[assembly: TypeForwardedTo(typeof(Region))]

// World Layers
[assembly: TypeForwardedTo(typeof(WorldLayer_MapMode))]
[assembly: TypeForwardedTo(typeof(WorldLayer_MapMode_Region))]
[assembly: TypeForwardedTo(typeof(WorldLayer_SelectionRegion))]
[assembly: TypeForwardedTo(typeof(WorldLayer_MouseRegion))]
[assembly: TypeForwardedTo(typeof(WorldLayer_SelectedRegion))]
[assembly: TypeForwardedTo(typeof(WorldLayer_MapMode_Terrain))]
[assembly: TypeForwardedTo(typeof(WorldLayer_Ridges))]
[assembly: TypeForwardedTo(typeof(WorldLayer_OrographicUplift))]
[assembly: TypeForwardedTo(typeof(WorldLayer_RainShadow))]
[assembly: TypeForwardedTo(typeof(WorldLayer_WindDirection))]
[assembly: TypeForwardedTo(typeof(WorldLayer_RidgeNormal))]
[assembly: TypeForwardedTo(typeof(WorldLayer_MonsoonWind))]
[assembly: TypeForwardedTo(typeof(WorldRegenHandler))]
