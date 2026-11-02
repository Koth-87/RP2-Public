using System.Linq;
using Verse;
using Planets.Presets.WorldGen;

namespace Planets.Utilities.UI
{
    /// <summary>
    /// Generates random world parameters for the "Random" button in world creation.
    /// Uses weighted distributions to create varied but balanced worlds.
    /// </summary>
    public static class RandomWorldParameters
    {
        /// <summary>
        /// Generate random cave system density for Geological Landforms.
        /// Bias toward middle values (0.5 = 50% = default).
        /// </summary>
        public static float RandomizeCaveSystems()
        {
            return Rand.ByCurve(new SimpleCurve
            {
                {0f, 0f},
                {0.25f, 0.1f},
                {0.5f, 1f},
                {0.75f, 0.1f},
                {1f, 0f}
            });
        }

        /// <summary>
        /// Generate random insect territory scale for VFE Insectoids.
        /// Bias toward 1.0 (100% = default), range 0-2.
        /// </summary>
        public static float RandomizeInsectScale()
        {
            return Rand.ByCurve(new SimpleCurve
            {
                {0f, 0f},
                {0.5f, 0.1f},
                {1f, 1f},
                {1.5f, 1f},
                {2f, 0f}
            });
        }

        /// <summary>
        /// Generate random pollution level for Biotech.
        /// Heavy bias toward low values (0.05 = 5% = default).
        /// </summary>
        public static float RandomizePollution()
        {
            return Rand.ByCurve(new SimpleCurve
            {
                {0f, 0f},
                {0.025f, 0.3f},
                {0.05f, 1f},
                {0.275f, 0.2f},
                {0.5f, 0.2f},
                {0.75f, 0.05f},
                {1f, 0f}
            });
        }

        /// <summary>
        /// Randomly select a world preset, excluding "Custom".
        /// Returns null if no valid presets available.
        /// </summary>
        public static WorldPreset GetRandomPreset(WorldPreset[] presets)
        {
            if (presets == null || presets.Length == 0)
                return null;

            var presetsExcludingCustom = presets.Where(p => p.Name != "Planets.Custom").ToArray();
            if (presetsExcludingCustom.Length == 0)
                return null;

            return presetsExcludingCustom[Rand.Range(0, presetsExcludingCustom.Length)];
        }
    }
}
