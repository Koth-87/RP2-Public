using UnityEngine;
using Verse;
using Planets.Utilities;
using Planets.Utilities.Logging;

namespace MapModeFramework
{
    [StaticConstructorOnStartup]
    public static class Materials
    {
        private const int selectorRenderQueue = 3560;
        private const int terrainOverlayRenderQueue = 3510;  // Map mode tile coloring
        private const int overlayRenderQueue = 3520;          // Draw-mode overlays (always above map mode)

        private const int numMatsGrowingPeriod = 13;
        private const int numMatsHilliness = 6; // Hilliness enum: Undefined(0), Flat(1), SmallHills(2), LargeHills(3), Mountainous(4), Impassable(5)

        public static readonly Material matWhiteOverlay;
        public static readonly Material matGreenOverlay;
        public static readonly Material matMouseRegion;
        public static readonly Material matSelectedRegion;
        public static readonly Material matFeaturesBorder;
        
        // Orographic effects materials (thread-safe, pre-generated)
        public static readonly Material matOrographicWindward;
        public static readonly Material matOrographicLeeward;
        public static readonly Material matOrographicNeutral;

        public static readonly Material matDrainageExorheic;
        public static readonly Material matDrainageEndorheicLake;
        public static readonly Material matDrainageFilledTerminal;
        public static readonly Material matDrainageFilledSpilling;
        
        // Ridge highlighting (golden glow overlay)
        public static readonly Material matRidgeGlowLow; // Band 4
        public static readonly Material matRidgeGlowHigh; // Band 5
        
        private static Material[] matsFertility;
        private static readonly Color[] FertilitySpectrum;
        private static Material[] matsTemperature;
        private static readonly Color[] TemperatureSpectrum;
        private static Material[] matsElevation;
        private static readonly Color[] ElevationSpectrum;
        private static Material[] matsRainfall;
        private static readonly Color[] RainfallSpectrum;
        private static Material[] matsGrowingPeriod;
        private static readonly Color[] GrowingPeriodSpectrum;

        public static Material[] matsCommonality;
        private static readonly Color[] CommonalitySpectrum;
        
        // === REALISTIC PLANETS ADDITIONS ===
        private static Material[] matsAridity;
        private static readonly Color[] AriditySpectrum;
        private static Material[] matsContinentality;
        private static readonly Color[] ContinentalitySpectrum;
        private static Material[] matsSeasonalAmplitude;
        private static readonly Color[] SeasonalAmplitudeSpectrum;
        
        // === NEW MAP MODES ===
        private static Material[] matsHilliness;
        private static readonly Color[] HillinessSpectrum;
        private static Material[] matsFlowAccumulation;
        private static readonly Color[] FlowAccumulationSpectrum;
        private static Material[] matsWetlands;
        private static readonly Color[] WetlandsSpectrum;
        private static Material[] matsDrainageBasins;
        private const int numMatsDrainageBasins = 100; // Fixed palette for basin visualization
        private static Material[] matsOrographic;
        
        private static Material[] matsMonsoon;
        private static readonly Color[] MonsoonSpectrum;
        
        private static Material[] matsWindArrowDirSpd;
        private const int NumWindDirMats = 24;   // 15° increments
        private const int NumWindSpdBands = 3;   // dim / mid / bright
        public static readonly Material matRidgeNormalArrow;
        
        private static int NumMatsPerMode;

        static Materials()
        {
            matWhiteOverlay = new Material(BaseContent.WhiteMat) { renderQueue = terrainOverlayRenderQueue };
            matGreenOverlay = new Material(SolidColorMaterials.SimpleSolidColorMaterial(Color.green)) { renderQueue = terrainOverlayRenderQueue };
            matMouseRegion = new Material(ShaderDatabase.WorldOverlayAdditive)
            {
                renderQueue = selectorRenderQueue,
                color = new Color(1f, 1f, 1f, 0.1f)
            };
            matSelectedRegion = new Material(ShaderDatabase.WorldOverlayAdditive)
            {
                renderQueue = selectorRenderQueue,
                color = new Color(1f, 1f, 1f, 0.1f)
            };
            matFeaturesBorder = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, new Color(1f, 1f, 1f, 0.1f), 3510);
            
            // Pre-generate orographic effect materials for thread safety (legacy - kept for compatibility)
            matOrographicWindward = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, 
                new Color(0.2f, 0.8f, 0.2f, 0.7f), terrainOverlayRenderQueue); // Green for windward
            matOrographicLeeward = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, 
                new Color(0.6f, 0.4f, 0.2f, 0.7f), terrainOverlayRenderQueue); // Brown for leeward
            matOrographicNeutral = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, 
                new Color(0.8f, 0.8f, 0.8f, 0.3f), terrainOverlayRenderQueue); // Light gray for neutral
                
            // Drainage classification materials
            matDrainageExorheic = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, 
                new Color(0.2f, 0.6f, 1.0f, 0.5f), terrainOverlayRenderQueue); // Blue (Ocean drainage)
            matDrainageEndorheicLake = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, 
                new Color(0.8f, 0.4f, 0.1f, 0.5f), terrainOverlayRenderQueue); // Orange (Endorheic Land)
            matDrainageFilledTerminal = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, 
                new Color(0.8f, 0.1f, 0.1f, 0.8f), terrainOverlayRenderQueue); // Red (Terminal Lake)
            matDrainageFilledSpilling = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, 
                new Color(0.1f, 0.8f, 0.2f, 0.8f), terrainOverlayRenderQueue); // Green (Spilling Lake)
            
            // Ridge glow materials - golden/yellow overlay for ridge tiles
            // Uses same pattern as other working overlay materials (MetaOverlay with WhiteTex)
            
            // Band 4 (Mountainous ridges): Golden yellow
            matRidgeGlowLow = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.WorldOverlayTransparent, 
                new Color(1f, 0.85f, 0.2f, 0.6f), overlayRenderQueue); 
                
            // Band 5 (Impassable peaks): Intense Red-Orange
            matRidgeGlowHigh = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.WorldOverlayTransparent, 
                new Color(1f, 0.4f, 0.1f, 0.8f), overlayRenderQueue);

            // Wind direction arrows: circular HSV (hue = direction) + saturation = speed.
            // East=Red, North=Yellow-Green, West=Cyan, South=Blue-Purple. Speed: low/mid/high saturation.
            matsWindArrowDirSpd = new Material[NumWindDirMats * NumWindSpdBands];
            float[] satValues = { 0.35f, 0.65f, 1.0f };
            for (int d = 0; d < NumWindDirMats; d++)
            {
                float hue = (float)d / NumWindDirMats;
                for (int s = 0; s < NumWindSpdBands; s++)
                {
                    Color c = Color.HSVToRGB(hue, satValues[s], 0.95f);
                    c.a = 0.9f;
                    matsWindArrowDirSpd[d * NumWindSpdBands + s] = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.WorldOverlayTransparent, c, overlayRenderQueue);
                }
            }
            matRidgeNormalArrow = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.WorldOverlayTransparent,
                new Color(0.95f, 0.75f, 0.2f, 0.9f), overlayRenderQueue);

            NumMatsPerMode = 50;

            FertilitySpectrum = new Color[2]
            {
                new Color(0f, 1f, 0f, 0f),
                new Color(0f, 1f, 0f, 0.5f)
            };
            // 14-step temperature spectrum: -50°C (coldest) to +50°C (hottest)
            // Smooth progression: Deep blue → Blue → Cyan → White → Yellow → Orange → Red
            // Each step represents ~7.7°C across the 100°C range
            TemperatureSpectrum = new Color[14]
            {
                new Color(0.2f, 0.1f, 0.5f),    // Very cold (-50°C): Deep purple-blue
                new Color(0.1f, 0.2f, 0.7f),    // Cold (-42°C): Dark blue
                new Color(0.0f, 0.3f, 0.9f),    // Cool (-35°C): Blue
                new Color(0.0f, 0.5f, 1.0f),    // Cool-moderate (-27°C): Bright blue
                new Color(0.2f, 0.7f, 1.0f),    // Moderate-cold (-19°C): Cyan-blue
                new Color(0.4f, 0.8f, 1.0f),    // Cool temperate (-12°C): Light cyan
                new Color(0.7f, 0.9f, 1.0f),    // Temperate (-4°C): Pale blue-white
                new Color(0.9f, 0.9f, 0.8f),    // Mild (4°C): Off-white/light gray
                new Color(1.0f, 0.95f, 0.6f),   // Warm (12°C): Light yellow
                new Color(1.0f, 0.8f, 0.3f),   // Hot (19°C): Yellow-orange
                new Color(1.0f, 0.6f, 0.1f),    // Very hot (27°C): Orange
                new Color(1.0f, 0.4f, 0.0f),    // Extreme hot (35°C): Red-orange
                new Color(1.0f, 0.2f, 0.0f),    // Scorching (42°C): Bright red
                new Color(0.8f, 0.0f, 0.0f)     // Maximum (50°C): Deep red
            };
            ElevationSpectrum = new Color[4]
            {
                new Color(0.224f, 0.18f, 0.15f),
                new Color(0.447f, 0.369f, 0.298f),
                new Color(0.6f, 0.6f, 0.6f),
                new Color(1f, 1f, 1f)
            };
            // Improved rainfall spectrum following natural dry→wet progression
            // Bands are interpreted via a 0-12000mm Earth-scale visualization range:
            // 0-100mm, 100-250mm, 250-500mm, 500-1000mm, 1000-2000mm,
            // 2000-3000mm, 3000-5000mm, 5000-8000mm, 8000-10000mm, 10000-12000mm+
            RainfallSpectrum = new Color[10]
            {
                new Color(0.9f, 0.85f, 0.7f),   // Hyper-arid desert (0-100mm)
                new Color(0.85f, 0.7f, 0.5f),   // Arid desert (100-250mm)
                new Color(0.8f, 0.6f, 0.4f),    // Semi-arid steppe (250-500mm)
                new Color(0.9f, 0.8f, 0.3f),    // Dry sub-humid / grassland (500-1000mm) 
                new Color(0.7f, 0.85f, 0.4f),   // Humid temperate (1000-2000mm)
                new Color(0.5f, 0.8f, 0.3f),    // Wet temperate / monsoon fringe (2000-3000mm)
                new Color(0.3f, 0.7f, 0.2f),    // Rainforest / very wet (3000-5000mm)
                new Color(0.2f, 0.6f, 0.3f),    // Mountain rainforest / orographic wet (5000-8000mm)
                new Color(0.1f, 0.5f, 0.4f),    // Extreme orographic (8000-10000mm)
                new Color(0.0f, 0.4f, 0.5f)     // Ultra-extreme orographic (10000-12000mm+)
            };
            GrowingPeriodSpectrum = new Color[13]
            {
                new Color(0.9f, 0.9f, 0.9f),    // Never (0/12): White/Light Grey (Winter)
                new Color(0.8f, 0.9f, 1.0f),    // 1/12: Very pale blue
                new Color(0.6f, 0.8f, 1.0f),    // 2/12: Pale blue
                new Color(0.4f, 0.9f, 0.8f),    // 3/12: Cyan-blue
                new Color(0.5f, 1.0f, 0.7f),    // 4/12: Pale Aquamarine
                new Color(0.6f, 0.9f, 0.5f),    // 5/12: Light yellow-green
                new Color(0.5f, 0.8f, 0.3f),    // 6/12: Light green
                new Color(0.4f, 0.7f, 0.2f),    // 7/12: Medium green
                new Color(0.3f, 0.6f, 0.1f),    // 8/12: Grass green
                new Color(0.2f, 0.5f, 0.1f),    // 9/12: Forest green
                new Color(0.1f, 0.4f, 0.1f),    // 10/12: Dark green
                new Color(0.0f, 0.3f, 0.0f),    // 11/12: Deep green
                new Color(0.0f, 0.4f, 0.2f)     // Year-round (12/12): Rich Jungle Green
            };
            CommonalitySpectrum = new Color[11]
            {
                new Color(1f, 1f, 1f), //White or 0
                new Color(1f, 1f, 0f),
                new Color(1f, 0.84f, 0f),
                new Color(1f, 0.64f, 0f),
                new Color(1f, 0.54f, 0f),
                new Color(1f, 0.49f, 0f),
                new Color(1f, 0.38f, 0.27f),
                new Color(1f, 0.27f, 0f),
                new Color(1f, 0.2f, 0f),
                new Color(1f, 0.1f, 0f),
                new Color(1f, 0f, 0f),
            };
            
            // === REALISTIC PLANETS COLOR SPECTRUMS ===
            // Extended for 0-6 aridity range (was 0-3)
            AriditySpectrum = new Color[10]
            {
                new Color(0.8f, 0.4f, 0.1f),    // Hyper-arid (brown) - AI 0.0
                new Color(0.9f, 0.6f, 0.2f),    // Arid (tan) - AI ~0.67
                new Color(1f, 0.8f, 0.4f),      // Semi-arid (light tan) - AI ~1.33
                new Color(0.7f, 0.9f, 0.3f),    // Sub-humid (yellow-green) - AI ~2.0
                new Color(0.4f, 0.8f, 0.2f),    // Humid (green) - AI ~2.67
                new Color(0.2f, 0.6f, 0.1f),    // Very humid (dark green) - AI ~3.33
                new Color(0.1f, 0.5f, 0.3f),    // Very humid+ (teal-green) - AI ~4.0
                new Color(0.1f, 0.4f, 0.5f),    // Extremely humid (cyan) - AI ~4.67
                new Color(0.0f, 0.3f, 0.7f),    // Extremely humid+ (deep cyan) - AI ~5.33
                new Color(0.0f, 0.2f, 0.9f)     // Maximum (deep blue) - AI 6.0+
            };
            ContinentalitySpectrum = new Color[6]
            {
                new Color(0.2f, 0.6f, 1f),      // Coastal (blue)
                new Color(0.4f, 0.8f, 1f),      // Near-coastal (light blue)
                new Color(0.8f, 0.9f, 0.6f),    // Intermediate (light yellow)
                new Color(1f, 0.8f, 0.4f),      // Continental (orange)
                new Color(0.9f, 0.5f, 0.2f),    // Deep continental (dark orange)
                new Color(0.8f, 0.3f, 0.1f)     // Extreme continental (red-brown)
            };
            SeasonalAmplitudeSpectrum = new Color[10]
            {
                new Color(0.9f, 0.9f, 1f),      // Minimal variation (white) - 0°C
                new Color(0.7f, 0.8f, 1f),      // Low variation (pale blue) - ~15°C
                new Color(0.5f, 0.7f, 0.9f),    // Moderate variation (blue) - ~30°C
                new Color(1f, 0.8f, 0.2f),      // High variation (yellow) - ~45°C
                new Color(1f, 0.5f, 0.1f),      // Very high variation (orange) - ~60°C
                new Color(0.8f, 0.2f, 0.2f),    // Extreme variation (red) - ~75-90°C
                new Color(0.7f, 0.1f, 0.1f),    // Very extreme (dark red) - ~105°C
                new Color(0.6f, 0.05f, 0.1f),   // Ultra extreme (deep red) - ~120°C
                new Color(0.5f, 0.0f, 0.15f),   // Maximum (dark red-purple) - ~140°C
                new Color(0.4f, 0.0f, 0.2f)     // Absolute maximum (dark purple-red) - ~160°C+
            };
            
            // === NEW MAP MODE COLOR SPECTRUMS ===
            HillinessSpectrum = new Color[6]
            {
                new Color(0.5f, 0.5f, 0.5f),    // Undefined (gray) - should rarely be used
                new Color(0.2f, 0.8f, 0.2f),    // Flat (green)
                new Color(0.6f, 0.9f, 0.4f),    // Small hills (light green)
                new Color(0.9f, 0.8f, 0.3f),    // Large hills (yellow)
                new Color(0.8f, 0.5f, 0.2f),    // Mountainous (orange)
                new Color(0.6f, 0.3f, 0.3f)     // Impassable (dark red)
            };
            FlowAccumulationSpectrum = new Color[8]
            {
                new Color(0.95f, 0.95f, 0.9f),  // Minimal flow (off-white)
                new Color(0.8f, 0.9f, 1f),      // Low flow (pale blue)
                new Color(0.6f, 0.8f, 1f),      // Moderate flow (light blue)
                new Color(0.4f, 0.7f, 0.9f),    // Higher flow (blue)
                new Color(0.2f, 0.5f, 0.8f),    // High flow (dark blue)
                new Color(0.1f, 0.4f, 0.7f),    // Very high flow (deeper blue)
                new Color(0.0f, 0.3f, 0.6f),    // Extreme flow (deep blue)
                new Color(0.0f, 0.2f, 0.5f)     // Maximum flow (navy)
            };
            WetlandsSpectrum = new Color[6]
            {
                new Color(0.9f, 0.85f, 0.7f),   // Dry (tan)
                new Color(0.7f, 0.8f, 0.5f),    // Slightly moist (light olive)
                new Color(0.5f, 0.7f, 0.3f),    // Moist (olive green)
                new Color(0.3f, 0.6f, 0.2f),    // Wet (green)
                new Color(0.2f, 0.5f, 0.3f),    // Very wet (dark green)
                new Color(0.1f, 0.3f, 0.5f)     // Saturated/swampy (blue-green)
            };
            // Monsoon boost 0-800mm (seasonal additional rainfall in monsoon zones)
            MonsoonSpectrum = new Color[6]
            {
                new Color(0.95f, 0.95f, 0.9f),  // No monsoon (0mm)
                new Color(0.7f, 0.85f, 0.6f),    // Weak (50-200mm)
                new Color(0.4f, 0.75f, 0.4f),    // Moderate (200-400mm)
                new Color(0.2f, 0.6f, 0.3f),     // Strong (400-600mm)
                new Color(0.1f, 0.5f, 0.4f),    // Very strong (600-800mm)
                new Color(0.05f, 0.35f, 0.5f)   // Extreme (800mm+)
            };
            
            GenerateMats(ref matsFertility, FertilitySpectrum, NumMatsPerMode);
            GenerateMats(ref matsTemperature, TemperatureSpectrum, NumMatsPerMode);
            GenerateMats(ref matsElevation, ElevationSpectrum, NumMatsPerMode);
            GenerateMats(ref matsRainfall, RainfallSpectrum, NumMatsPerMode);
            GenerateMats(ref matsGrowingPeriod, GrowingPeriodSpectrum, numMatsGrowingPeriod);
            GenerateMats(ref matsCommonality, CommonalitySpectrum, NumMatsPerMode);
            
            // Generate RP-specific materials
            GenerateMats(ref matsAridity, AriditySpectrum, NumMatsPerMode);
            GenerateMats(ref matsContinentality, ContinentalitySpectrum, NumMatsPerMode);
            GenerateMats(ref matsSeasonalAmplitude, SeasonalAmplitudeSpectrum, NumMatsPerMode);
            
            // Generate new map mode materials - use direct colors for discrete hilliness
            GenerateHillinessMats();
            
            // DEBUG: Log hilliness material generation
            RPLog.D($"[MMF] Generated {numMatsHilliness} hilliness materials from {HillinessSpectrum.Length} spectrum colors");
            for (int i = 0; i < matsHilliness.Length; i++)
            {
                RPLog.D($"[MMF] Hilliness[{i}]: {matsHilliness[i]?.color}");
            }
            GenerateMats(ref matsFlowAccumulation, FlowAccumulationSpectrum, NumMatsPerMode);
            GenerateMats(ref matsWetlands, WetlandsSpectrum, NumMatsPerMode);
            GenerateMats(ref matsMonsoon, MonsoonSpectrum, NumMatsPerMode, ShaderDatabase.WorldOverlayTransparent);
            GenerateDrainageBasinMats();
            GenerateOrographicMats();
        }

        private static void GenerateDrainageBasinMats()
        {
            matsDrainageBasins = new Material[numMatsDrainageBasins];
            for (int i = 0; i < numMatsDrainageBasins; i++)
            {
                // Generate distinct colors using golden ratio to spread hues
                float hue = (i * 0.618034f) % 1f;
                Color color = Color.HSVToRGB(hue, 0.7f, 0.8f);
                matsDrainageBasins[i] = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, color, 3510);
            }
        }

        private static void GenerateMats(ref Material[] mats, Color[] colorSpectrum, int numMats, Shader shader = null)
        {
            shader = shader ?? ShaderDatabase.MetaOverlay;
            mats = new Material[numMats];
            for (int i = 0; i < numMats; i++)
            {
                Color color = ColorsFromSpectrum.Get(colorSpectrum, (float)i / (float)numMats);
                Material mat = MaterialPool.MatFrom(BaseContent.WhiteTex, shader, color, 3510);
                mats[i] = mat;
            }
        }
        
        /// <summary>
        /// Generate hilliness materials using exact spectrum colors (no interpolation)
        /// </summary>
        private static void GenerateHillinessMats()
        {
            matsHilliness = new Material[numMatsHilliness];
            for (int i = 0; i < numMatsHilliness; i++)
            {
                // Use exact color from spectrum, no interpolation
                Color color = HillinessSpectrum[i];
                Material mat = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, color, 3510);
                matsHilliness[i] = mat;
            }
        }

        public static Material MatForFertilityOverlay(float fert)
        {
            int value = Mathf.FloorToInt(fert * (float)NumMatsPerMode);
            return matsFertility[Mathf.Clamp(value, 0, NumMatsPerMode - 1)];
        }

        public static Material MatForTemperature(float temp)
        {
            int value = Mathf.FloorToInt(Mathf.InverseLerp(-50f, 50f, temp) * (float)NumMatsPerMode);
            return matsTemperature[Mathf.Clamp(value, 0, NumMatsPerMode - 1)];
        }

        public static Material MatForElevation(float elev)
        {
            // REALISTIC PLANETS: Use Earth-scale 9km elevation range
            int value = Mathf.FloorToInt(Mathf.InverseLerp(0f, 9000f, elev) * (float)NumMatsPerMode);
            return matsElevation[Mathf.Clamp(value, 0, NumMatsPerMode - 1)];
        }

        public static Material MatForRainfallOverlay(float rain)
        {
            // REALISTIC PLANETS: Use Earth-scale 0-12000mm rainfall visualization range with
            // physically motivated bands:
            // 0-100, 100-250, 250-500, 500-1000, 1000-2000,
            // 2000-3000, 3000-5000, 5000-8000, 8000-10000, 10000-12000+ mm
            float r = Mathf.Clamp(rain, 0f, 12000f);
            float t; // normalized [0,1] across the 10 bands

            if (r <= 100f)
            {
                // 0-100mm → 0.0-0.1
                t = 0.0f + Mathf.InverseLerp(0f, 100f, r) * 0.1f;
            }
            else if (r <= 250f)
            {
                // 100-250mm → 0.1-0.2
                t = 0.1f + Mathf.InverseLerp(100f, 250f, r) * 0.1f;
            }
            else if (r <= 500f)
            {
                // 250-500mm → 0.2-0.3
                t = 0.2f + Mathf.InverseLerp(250f, 500f, r) * 0.1f;
            }
            else if (r <= 1000f)
            {
                // 500-1000mm → 0.3-0.4
                t = 0.3f + Mathf.InverseLerp(500f, 1000f, r) * 0.1f;
            }
            else if (r <= 2000f)
            {
                // 1000-2000mm → 0.4-0.5
                t = 0.4f + Mathf.InverseLerp(1000f, 2000f, r) * 0.1f;
            }
            else if (r <= 3000f)
            {
                // 2000-3000mm → 0.5-0.6
                t = 0.5f + Mathf.InverseLerp(2000f, 3000f, r) * 0.1f;
            }
            else if (r <= 5000f)
            {
                // 3000-5000mm → 0.6-0.7
                t = 0.6f + Mathf.InverseLerp(3000f, 5000f, r) * 0.1f;
            }
            else if (r <= 8000f)
            {
                // 5000-8000mm → 0.7-0.8
                t = 0.7f + Mathf.InverseLerp(5000f, 8000f, r) * 0.1f;
            }
            else if (r <= 10000f)
            {
                // 8000-10000mm → 0.8-0.9
                t = 0.8f + Mathf.InverseLerp(8000f, 10000f, r) * 0.1f;
            }
            else
            {
                // 10000-12000mm+ → 0.9-1.0
                t = 0.9f + Mathf.InverseLerp(10000f, 12000f, r) * 0.1f;
            }

            int value = Mathf.FloorToInt(t * (float)NumMatsPerMode);
            return matsRainfall[Mathf.Clamp(value, 0, NumMatsPerMode - 1)];
        }

        public static Material MatForGrowingPeriodOverlay(float growingPeriod)
        {
            // Input is in twelfths (0-12)
            int value = Mathf.FloorToInt(growingPeriod);
            return matsGrowingPeriod[Mathf.Clamp(value, 0, numMatsGrowingPeriod - 1)];
        }

        public static Material MatForCommonalityOverlay(float commonality)
        {
            int value = Mathf.FloorToInt(Mathf.InverseLerp(0f, 1f, commonality) * (float)NumMatsPerMode);
            return matsCommonality[Mathf.Clamp(value, 0, NumMatsPerMode - 1)];
        }
        
        // === REALISTIC PLANETS MATERIAL METHODS ===
        
        /// <summary>
        /// Get material for aridity index (0 = hyper-arid, 6+ = extremely humid)
        /// </summary>
        public static Material MatForAridityOverlay(float aridityIndex)
        {
            int value = Mathf.FloorToInt(Mathf.InverseLerp(0f, 6f, aridityIndex) * (float)NumMatsPerMode);
            return matsAridity[Mathf.Clamp(value, 0, NumMatsPerMode - 1)];
        }
        
        /// <summary>
        /// Get material for continentality (0.0 = coastal/maritime, 1.0 = deep continental)
        /// Uses non-linear mapping (pow 0.5) to show more nuance in the 0-0.5 range
        /// </summary>
        public static Material MatForContinentalityOverlay(float continentality)
        {
            // Normalized value 0-1
            float val = Mathf.Clamp01(continentality);
            
            // Apply power curve to expand lower values
            // 0.25 -> 0.5, 0.5 -> 0.71
            // This allocates ~70% of the color spectrum to the 0-0.5 range (coastal/near-coastal)
            val = Mathf.Pow(val, 0.5f);
            
            int value = Mathf.FloorToInt(val * (float)NumMatsPerMode);
            return matsContinentality[Mathf.Clamp(value, 0, NumMatsPerMode - 1)];
        }
        
        /// <summary>
        /// Get material for seasonal temperature amplitude (0°C = no variation, 160°C+ = extreme variation)
        /// Color scale: 0-90°C uses first 6 colors, 90-160°C uses extended darker colors
        /// </summary>
        public static Material MatForSeasonalAmplitudeOverlay(float amplitudeC)
        {
            // Extended range to visualize extreme axial tilt worlds with ~160°C full annual swings
            // Split mapping: 0-90°C maps to first 6 colors, 90-160°C maps to extended colors
            // First 6 colors cover 90/160 = 56.25% of the material range
            // Last 4 colors cover 70/160 = 43.75% of the material range
            float normalizedValue;
            if (amplitudeC <= 90f)
            {
                // Map 0-90°C to first 56.25% of materials (covering first 6 spectrum colors)
                normalizedValue = Mathf.InverseLerp(0f, 90f, amplitudeC) * 0.5625f;
            }
            else
            {
                // Map 90-160°C to remaining 43.75% of materials (covering last 4 spectrum colors)
                normalizedValue = 0.5625f + Mathf.InverseLerp(90f, 160f, amplitudeC) * 0.4375f;
            }
            
            int value = Mathf.FloorToInt(normalizedValue * (float)NumMatsPerMode);
            return matsSeasonalAmplitude[Mathf.Clamp(value, 0, NumMatsPerMode - 1)];
        }
        
        // === NEW MAP MODE MATERIAL METHODS ===
        
        public static Material MatForMoistureMm(float millimeters)
        {
            float log = Mathf.Log10(Mathf.Max(millimeters, 1f));
            return MatFromPalette(matsRainfall, Mathf.InverseLerp(0f, 4.3f, log));
        }

        public static Material MatForZeroToOne(float value)
        {
            return MatFromPalette(matsContinentality, value);
        }

        public static Material MatForSigned(float value, float halfRange)
        {
            float span = Mathf.Max(halfRange, 0.001f);
            return MatFromPalette(matsTemperature, Mathf.InverseLerp(-span, span, value));
        }

        public static Material MatForDailyRange(float degrees)
        {
            return MatFromPalette(matsSeasonalAmplitude, Mathf.InverseLerp(0f, 20f, degrees));
        }

        private static Material MatFromPalette(Material[] mats, float normalized)
        {
            if (mats == null || mats.Length == 0)
                return BaseContent.ClearMat;
            int value = Mathf.FloorToInt(Mathf.Clamp01(normalized) * mats.Length);
            if (value >= mats.Length)
                value = mats.Length - 1;
            return mats[value];
        }

        public static Material MatForHillinessOverlay(int hilliness)
        {
            int hillinessIndex = Mathf.Clamp(hilliness, 0, numMatsHilliness - 1);
            
            return matsHilliness[hillinessIndex];
        }
        
        /// <summary>
        /// Get material for flow accumulation (logarithmic scale for drainage areas in km²)
        /// Flow accumulation is already in km² (not tile count)
        /// </summary>
        public static Material MatForFlowAccumulationOverlay(float drainageKm2)
        {
            // Use logarithmic scale for better visualization of wide range of values
            // Range: ~800 km² (1 tile) to ~8,000,000 km² (10000 tiles / Amazon scale)
            float logValue = Mathf.Log10(Mathf.Max(1f, drainageKm2));
            // Scale to log10 range: ~2.9 (800 km²) to ~6.9 (8M km²) = 4 orders of magnitude
            int value = Mathf.FloorToInt(Mathf.InverseLerp(2.5f, 7f, logValue) * (float)NumMatsPerMode);
            return matsFlowAccumulation[Mathf.Clamp(value, 0, NumMatsPerMode - 1)];
        }
        
        /// <summary>
        /// Get material for wetland intensity (0 = dry, 1 = saturated wetland)
        /// </summary>
        public static Material MatForWetlandsOverlay(float wetlandIntensity)
        {
            int value = Mathf.FloorToInt(wetlandIntensity * (float)NumMatsPerMode);
            return matsWetlands[Mathf.Clamp(value, 0, NumMatsPerMode - 1)];
        }

        /// <summary>
        /// Get material for drainage basin ID (pre-generated palette to avoid threading issues)
        /// </summary>
        public static Material MatForDrainageBasin(int basinId)
        {
            if (basinId < 0) return BaseContent.ClearMat;
            return matsDrainageBasins[basinId % numMatsDrainageBasins];
        }

        /// <summary>
        /// Generate intensity-based orographic effect materials.
        /// Creates 50 materials: 25 for leeward (display 0.2–1.0, red) and 25 for windward (1.0–8.0, green).
        /// Neutral (1.0) is transparent. Covers full range so L/U indices up to ~2 map to distinct levels.
        /// </summary>
        private static void GenerateOrographicMats()
        {
            matsOrographic = new Material[NumMatsPerMode];
            
            const float minLeeward = 0.2f;  // Minimum leeward multiplier
            const float maxWindward = 8.0f; // Maximum windward multiplier
            const float neutralThreshold = 0.05f; // Neutral range: 0.95-1.05
            
            for (int i = 0; i < NumMatsPerMode; i++)
            {
                // Map index to multiplier range: 0 = minLeeward, middle = 1.0, NumMatsPerMode-1 = maxWindward
                // Split array: first half for leeward (0.2 to 1.0), second half for windward (1.0 to 8.0)
                float multiplier;
                Color color;
                float alpha;
                
                if (i < NumMatsPerMode / 2)
                {
                    // Leeward: map 0 to (NumMatsPerMode/2-1) to range minLeeward to 1.0
                    float t = (float)i / ((float)NumMatsPerMode / 2f - 1f);
                    multiplier = Mathf.Lerp(minLeeward, 1.0f, t);
                    
                    // Red color with alpha based on intensity
                    // Intensity = (1.0 - mult) / (1.0 - minLeeward)
                    float intensity = Mathf.Clamp01((1.0f - multiplier) / (1.0f - minLeeward));
                    
                    // Skip if too close to neutral (transparent)
                    if (multiplier > 1.0f - neutralThreshold)
                    {
                        alpha = 0f; // Fully transparent
                    }
                    else
                    {
                        alpha = Mathf.Lerp(0.25f, 0.9f, intensity);
                    }

                    // Red/orange ramp (not alpha-only) so weak/strong leeward regions are distinguishable.
                    color = Color.Lerp(
                        new Color(0.95f, 0.55f, 0.30f, 1f), // weak leeward: warm orange
                        new Color(0.70f, 0.05f, 0.05f, 1f), // strong leeward: deep crimson
                        intensity
                    );
                    color.a = alpha;
                }
                else
                {
                    // Windward: map NumMatsPerMode/2 to NumMatsPerMode-1 to range 1.0 to maxWindward
                    float t = ((float)i - (float)NumMatsPerMode / 2f) / ((float)NumMatsPerMode / 2f - 1f);
                    multiplier = Mathf.Lerp(1.0f, maxWindward, t);
                    
                    // Green color with alpha based on intensity
                    // Intensity = (mult - 1.0) / (maxWindward - 1.0)
                    float intensity = Mathf.Clamp01((multiplier - 1.0f) / (maxWindward - 1.0f));
                    
                    // Skip if too close to neutral (transparent)
                    if (multiplier < 1.0f + neutralThreshold)
                    {
                        alpha = 0f; // Fully transparent
                    }
                    else
                    {
                        alpha = Mathf.Lerp(0.20f, 0.85f, intensity);
                    }

                    // Green ramp for lift intensity.
                    color = Color.Lerp(
                        new Color(0.45f, 0.90f, 0.45f, 1f), // weak windward
                        new Color(0.05f, 0.55f, 0.10f, 1f), // strong windward
                        intensity
                    );
                    color.a = alpha;
                }
                
                matsOrographic[i] = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.WorldOverlayTransparent, color, overlayRenderQueue);
            }
        }

        /// <summary>
        /// Get material for orographic multiplier with intensity-based alpha fading.
        /// Range: 0.2 (strong leeward/red) to 8.0 (strong windward/green), with neutral (1.0) being transparent.
        /// </summary>
        public static Material MatForOrographicOverlay(float multiplier)
        {
            const float minLeeward = 0.2f;
            const float maxWindward = 8.0f;
            const float neutralThreshold = 0.05f;
            
            // Handle neutral range (no overlay)
            if (multiplier >= 1.0f - neutralThreshold && multiplier <= 1.0f + neutralThreshold)
            {
                return BaseContent.ClearMat;
            }
            
            // Map multiplier to array index
            int index;
            if (multiplier < 1.0f)
            {
                // Leeward: map 0.2-1.0 to indices 0 to NumMatsPerMode/2-1
                float t = Mathf.InverseLerp(minLeeward, 1.0f, multiplier);
                index = Mathf.FloorToInt(t * ((float)NumMatsPerMode / 2f - 1f));
            }
            else
            {
                // Windward: map 1.0-8.0 to indices NumMatsPerMode/2 to NumMatsPerMode-1
                float t = Mathf.InverseLerp(1.0f, maxWindward, multiplier);
                index = Mathf.FloorToInt(((float)NumMatsPerMode / 2f) + t * ((float)NumMatsPerMode / 2f - 1f));
            }
            
            return matsOrographic[Mathf.Clamp(index, 0, NumMatsPerMode - 1)];
        }

        /// <summary>
        /// U-only orographic overlay: windward intensity with gradient scaling.
        /// Uses InverseLerp(0.35, 2.0) so 0.35-2.0 maps to gradient; avoids saturated green everywhere.
        /// WorldLayer_OrographicUplift applies terrain filter (ridge/relief/local-climb) before calling.
        /// </summary>
        public static Material MatForOrographicUpliftOverlay(float windward)
        {
            const float minWindward = 0.35f;
            const float maxWindward = 2.0f;
            if (windward < minWindward) return BaseContent.ClearMat;
            float t = Mathf.InverseLerp(minWindward, maxWindward, windward);
            int half = NumMatsPerMode / 2;
            int index = 25 + Mathf.FloorToInt(t * (half - 1));
            return matsOrographic[Mathf.Clamp(index, 25, NumMatsPerMode - 1)];
        }

        /// <summary>
        /// L-only orographic overlay: leeward intensity 0..1. Display-only clamp; keep in sync with climate pipeline.
        /// Below threshold returns ClearMat (no draw).
        /// </summary>
        public static Material MatForRainShadowOverlay(float leeward)
        {
            const float threshold = 0.05f;
            if (leeward < threshold) return BaseContent.ClearMat;
            float t = Mathf.Clamp01(leeward);
            int half = NumMatsPerMode / 2;
            int index = Mathf.FloorToInt((1f - t) * (half - 1));
            return matsOrographic[Mathf.Clamp(index, 0, half - 1)];
        }

        /// <summary>
        /// Material for wind direction arrow: hue = compass direction (East=Red, West=Cyan, etc.), saturation = speed.
        /// worldDir = wind blowing toward (same as ClimateData.WindDirection). tileCenter for tangent frame.
        /// </summary>
        public static Material MatForWindArrowDirSpd(Vector3 worldDir, Vector3 tileCenter, float speedMs)
        {
            if (matsWindArrowDirSpd == null || matsWindArrowDirSpd.Length == 0) return matWhiteOverlay;
            Vector3 n = tileCenter.normalized;
            Vector3 east = Vector3.Cross(n, Vector3.up).normalized;
            if (east.sqrMagnitude < 1e-6f) east = Vector3.right;
            Vector3 north = Vector3.Cross(n, east).normalized;
            Vector3 tangent = worldDir - Vector3.Dot(worldDir, n) * n;
            float len = tangent.magnitude;
            if (len < 1e-5f) return matsWindArrowDirSpd[0];
            float dotE = Vector3.Dot(tangent, east);
            float dotN = Vector3.Dot(tangent, north);
            float angle = Mathf.Atan2(dotN, dotE);
            float t = angle / (2f * Mathf.PI);
            if (t < 0f) t += 1f;
            int d = Mathf.FloorToInt(t * NumWindDirMats) % NumWindDirMats;
            int s = speedMs < 3f ? 0 : speedMs < 8f ? 1 : 2;
            return matsWindArrowDirSpd[d * NumWindSpdBands + s];
        }

        /// <summary>
        /// Material for monsoon boost overlay (0-2000mm summer monsoon rainfall excess). Keep in sync with climate pipeline.
        /// </summary>
        public static Material MatForMonsoonOverlay(float boostMm)
        {
            if (matsMonsoon == null || matsMonsoon.Length == 0) return matWhiteOverlay;
            float t = Mathf.InverseLerp(0f, 2000f, Mathf.Clamp(boostMm, 0f, 2000f));
            int i = Mathf.FloorToInt(t * (matsMonsoon.Length - 1));
            return matsMonsoon[Mathf.Clamp(i, 0, matsMonsoon.Length - 1)];
        }
    }
}
